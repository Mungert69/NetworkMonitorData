using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Data;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Linq;
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Utils; // Assuming ResultObj is defined here
using NetworkMonitor.Utils.Helpers;
using System.Collections.Generic;

namespace NetworkMonitor.Data.Services
{
    public interface IProcessorBrokerService
    {
        Task<ResultObj> ChangeProcessorAppID(Tuple<string, string> appIDs);
        Task<ResultObj> UserUpdateProcessor(ProcessorObj processor);
        Task<ResultObj> GenAuthKey(ProcessorObj processor);
        Task<ResultObj> Init();
    }
    public class ProcessorBrokerService : IProcessorBrokerService
    {
        private readonly ILogger<ProcessorBrokerService> _logger;
        private readonly IRabbitRepo _rabbitRepo;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IProcessorState _processorState;
        private readonly SystemParams _systemParams;
        private readonly PingParams _pingParams;

        public ProcessorBrokerService(ILogger<ProcessorBrokerService> logger, IRabbitRepo rabbitRepo, IProcessorState processorState, IServiceScopeFactory scopeFactory, ISystemParamsHelper systemParamsHelper)
        {
            _logger = logger;
            _rabbitRepo = rabbitRepo;
            _processorState = processorState;
            _scopeFactory = scopeFactory;
            _systemParams = systemParamsHelper.GetSystemParams();
            _pingParams = systemParamsHelper.GetPingParams();
        }

        public async Task<ResultObj> Init()
        {
            var result = new ResultObj();
            result.Message = " Service : ProcessorBrookerService : ";
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();

                    _processorState.ProcessorList = await monitorContext.ProcessorObjs.ToListAsync();
                    result.Message += " Success : Got Processor List from Database .";
                }
            }
            catch (Exception e)
            {
                result.Message = $" Error : Failed to get Processor List from Database . Error was : {e.Message}";
                result.Success = false;
                return result;
            }
            try
            {
                await _rabbitRepo.PublishAsync("fullProcessorList", _processorState.ProcessorList);
                result.Message += " Published full list of processors. ";
            }

            catch (Exception e)
            {
                result.Message += $" Error : Failed to Publish FullProcessorList . Error was : {e.Message}";
                result.Success = false;
                return result;
            }
            result.Success = true;

            return result;

        }


        public async Task<ResultObj> ChangeProcessorAppID(Tuple<string, string> appIDs)
        {
            var result = new ResultObj();
            result.Message = " Service : ChangeProcessorAppID : ";
            result.Success = true;
            /*
                        try
                        {
                            using (var scope = _scopeFactory.CreateScope())
                            {
                                var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                                var monitorIPs = await monitorContext.MonitorIPs.Where(w => w.AppID == appIDs.Item1).ToListAsync();
                                if (monitorIPs != null)
                                {
                                    monitorIPs.ForEach(f => f.AppID = appIDs.Item2);
                                    await monitorContext.SaveChangesAsync();
                                }

                            }
                            result.Message = $" Success : MonitorIPs with AppID  {appIDs.Item1} swapped to {appIDs.Item2}.";
                            _logger.LogInformation(result.Message);
                        }
                        catch (Exception ex)
                        {
                            result.Success = false;
                            result.Message = $" Error : updating MonitorIPs AppIDs . Error was : {ex.Message}";
                            _logger.LogError(result.Message);
                        }
            */
            result.Message += " Success : No longer changing AppIDs .";
            return result;
        }

        private async Task ActivateTestUser(string location, string owner, MonitorContext monitorContext)
        {
            var parts = location.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
            string email = parts.Length > 0 ? parts[0].Trim() : string.Empty; // Safely trim and check existence

            var testUser = await monitorContext.TestUsers.Where(w => w.Email == email).FirstOrDefaultAsync();
            if (testUser == null) return;
            bool flag = false;
            if (testUser.UserID == null) { testUser.UserID = owner; flag = true; }
            if (testUser.ActivatedDate == null) { testUser.ActivatedDate = DateTime.UtcNow; flag = true; }
            if (flag) await monitorContext.SaveChangesAsync();

        }
        public async Task<ResultObj> GenAuthKey(ProcessorObj processor)
        {
            var result = new ResultObj();
            result.Message = " Service : SendAuthKey : ";
            ProcessorInitObj initObj = new ProcessorInitObj();
            initObj.TotalReset = true;
            initObj.Reset = false;
            initObj.AppID = processor.AppID;
            initObj.PingParams = _pingParams;
            initObj.MonitorIPs = new List<MonitorIP>();


            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    var existingProcessor = await monitorContext.ProcessorObjs.FirstOrDefaultAsync(p => p.AppID == processor.AppID);
                    processor.AuthKey = AesOperation.EncryptString(_systemParams.EmailEncryptKey, processor.AppID);
                    if (existingProcessor == null)
                    {
                        // New Processor
                        processor.DateCreated = DateTime.UtcNow;
                        processor.LastAccessDate = DateTime.UtcNow;
                        monitorContext.ProcessorObjs.Add(processor);
                        await _rabbitRepo.PublishAsync<ProcessorObj>("addProcessor", processor);
                        result.Message += $" Success : New processor with AppID {processor.AppID} added and notified.";
                    }
                    else
                    {

                        // Update Processor
                        existingProcessor.DisabledEndPointTypes = processor.DisabledEndPointTypes;
                        existingProcessor.IsEnabled = processor.IsEnabled;
                        existingProcessor.Location = processor.Location;
                        existingProcessor.MaxLoad = processor.MaxLoad;
                        await _rabbitRepo.PublishAsync<ProcessorObj>("updateProcessor", processor);
                        result.Message += $" Success : Processor with AppID {processor.AppID} updated and notified.";
                        initObj.MonitorIPs = await monitorContext.MonitorIPs.Where(w => w.AppID == processor.AppID && !w.Hidden).ToListAsync();

                    }
                    await monitorContext.SaveChangesAsync();

                    await ActivateTestUser(processor.Location, processor.Owner, monitorContext);

                    initObj.AuthKey = processor.AuthKey;
                    await _rabbitRepo.PublishAsync<ProcessorInitObj>($"processorAuthKey{processor.AppID}", initObj);
                    result.Message += $" Success : ProcessorInitObj with AuthKey sent to AppID {processor.AppID} .";


                }

                result.Success = true;
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $" Error : updating processor with AppID {processor.AppID}. Error was : {ex.Message}";
                _logger.LogError(result.Message);
                return result;
            }
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    initObj.MonitorIPs = await monitorContext.MonitorIPs.Where(w => w.AppID == processor.AppID && !w.Hidden).ToListAsync();
                    if (initObj.MonitorIPs == null) initObj.MonitorIPs = new List<MonitorIP>();
                    await _rabbitRepo.PublishAsync<ProcessorInitObj>("processorInit" + processor.AppID, initObj);
                    result.Message += " Success : Sent ProcessorInit event to appID " + processor.AppID + " . ";
                    result.Success = true;
                }
            }

            catch (Exception e)
            {
                result.Message += $" Error : Could not send ProcessorInit event to appID {processor.AppID} . Error was : {e.Message} . ";
                result.Success = false;
                _logger.LogError(result.Message);

            }

            return result;
        }

        public async Task<ResultObj> UserUpdateProcessor(ProcessorObj processor)
        {
            var result = new ResultObj();
            result.Message = " Service : UserUpdateProcessor : ";
            ProcessorInitObj initObj = new ProcessorInitObj();
            initObj.TotalReset = false;
            initObj.Reset = true;

            initObj.PingParams = _pingParams;


            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    var existingProcessor = await monitorContext.ProcessorObjs.FirstOrDefaultAsync(p => p.AppID == processor.AppID);
                    processor.AuthKey = AesOperation.EncryptString(_systemParams.EmailEncryptKey, processor.AppID);
                    if (existingProcessor == null)
                    {
                        // New Processor
                        processor.DateCreated = DateTime.UtcNow;
                        processor.LastAccessDate = DateTime.UtcNow;
                        monitorContext.ProcessorObjs.Add(processor);
                        await _rabbitRepo.PublishAsync<ProcessorObj>("addProcessor", processor);
                        result.Message += $" Success : New processor with AppID {processor.AppID} added and notified.";
                    }
                    else
                    {
                        // Update Processor
                        existingProcessor.DisabledEndPointTypes = processor.DisabledEndPointTypes;
                        existingProcessor.IsEnabled = processor.IsEnabled;
                        existingProcessor.Location = processor.Location;
                        existingProcessor.MaxLoad = processor.MaxLoad;
                        await _rabbitRepo.PublishAsync<ProcessorObj>("updateProcessor", processor);
                        result.Message += $" Success : Processor with AppID {processor.AppID} updated and notified.";
                    }
                    await _rabbitRepo.PublishAsync<ProcessorObj>($"processorAuthKey{processor.AppID}", processor);
                    result.Message += $" Success : Processor AuthKey sent to AppID {processor.AppID} .";

                    await monitorContext.SaveChangesAsync();
                }

                result.Success = true;
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $" Error : updating processor with AppID {processor.AppID}. Error was : {ex.Message}";
                _logger.LogError(result.Message);
                return result;
            }
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    initObj.MonitorIPs = await monitorContext.MonitorIPs.Where(w => w.AppID == processor.AppID && !w.Hidden).ToListAsync();
                    if (initObj.MonitorIPs == null) initObj.MonitorIPs = new List<MonitorIP>();
                    await _rabbitRepo.PublishAsync<ProcessorInitObj>("processorInit" + processor.AppID, initObj);
                    result.Message += " Success : Sent ProcessorInit event to appID " + processor.AppID + " . ";
                    result.Success = true;
                }
            }

            catch (Exception e)
            {
                result.Message += $" Error : Could not send ProcessorInit event to appID {processor.AppID} . Error was : {e.Message} . ";
                result.Success = false;
                _logger.LogError(result.Message);

            }

            return result;
        }

    }
}
