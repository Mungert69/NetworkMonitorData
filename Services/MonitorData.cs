using System;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Data;
using NetworkMonitor.Utils;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Utils.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Threading;
namespace NetworkMonitor.Data.Services
{
    public class MonitorData : IMonitorData
    {
        private ILogger _logger;
        private CancellationToken _token;
        private SystemParams _systemParams;

        private IPingInfoService _pingInfoService;
        private IConfiguration _config;
        private PingParams _pingParams;
        private readonly IServiceScopeFactory _scopeFactory;

        private bool _purgeReady;
        private bool _saveReady;
        private bool _awake;

        private IRabbitRepo _rabbitRepo;
        public IRabbitRepo RabbitRepo { get => _rabbitRepo; }
        private ISystemParamsHelper _systemParamsHelper;
        private IDatabaseQueueService _databaseService;

        private IProcessorState _processorState;
        public SystemParams SystemParams { get => _systemParams; set => _systemParams = value; }
        public PingParams PingParams { get => _pingParams; set => _pingParams = value; }

        public MonitorData(IConfiguration config, ILogger<MonitorData> logger, IServiceScopeFactory scopeFactory, CancellationTokenSource cancellationTokenSource, IDatabaseQueueService databaseService, IRabbitRepo rabbitRepo, ISystemParamsHelper systemParamsHelper, IPingInfoService pingInfoService, IProcessorState processorState)
        {
            _config = config;
            _databaseService = databaseService;
            _pingInfoService = pingInfoService;
            _rabbitRepo = rabbitRepo;
            _token = cancellationTokenSource.Token;
            _token.Register(() => OnStopping());
            _scopeFactory = scopeFactory;
            _logger = logger;
            _systemParamsHelper = systemParamsHelper;
            _processorState = processorState;
            _systemParams = _systemParamsHelper.GetSystemParams();
            _pingParams = _systemParamsHelper.GetPingParams();
        }
        private void OnStopping()
        {
            Console.WriteLine("SERVICE SHUTDOWN : starting shutdown of MonitorData");
            try
            {
                //ResultObj result = SaveData();
                ResultObj result = _databaseService.ShutdownTaskQueue().Result;
                result.Message += " OnStopping call complete.";
                //_daprClient.ShutdownSidecarAsync();
                _logger.LogWarning("SERVICE SHUTDOWN : Result : " + result.Message);
            }
            catch (Exception e)
            {
                _logger.LogCritical("Error : Failed to shutdown gracefully : Error Was : " + e.Message);
                Console.WriteLine();
            }
        }
        public async Task Init()
        {

            MonitorDataInitObj serviceObj = new MonitorDataInitObj();
            serviceObj.InitTotalResetAlertMessage = false;
            serviceObj.InitTotalResetProcesser = false;
            serviceObj.InitResetProcessor = false;
            serviceObj.InitUpdateAlertMessage = true;
            await InitService(serviceObj);

        }
        public async Task<ResultObj> InitService(MonitorDataInitObj serviceObj)
        {
            var result = new ResultObj();
            result.Message = $" SERVICE : InitService : with InitTotalResetAlertMessage={serviceObj.InitTotalResetAlertMessage}  InitTotalResetProcesser={serviceObj.InitTotalResetProcesser} InitResetProcessor={serviceObj.InitResetProcessor}  InitUpdateAlertMessage={serviceObj.InitUpdateAlertMessage}";
            result.Success = true;
            _awake = false;
            var userInfos = new List<UserInfo>();
            PingParams = new PingParams();

            try
            {

                SystemParams = _systemParamsHelper.GetSystemParams();
                PingParams = _systemParamsHelper.GetPingParams();
                if (serviceObj.IsTestMode) PingParams.Timeout = 10000;
                /*_processorState.ProcessorList = new List<ProcessorObj>();
                _config.GetSection("ProcessorList").Bind(_processorState.ProcessorList);
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();

                    monitorContext.ProcessorObjs.AddRange(_processorState.ProcessorList);
                    await monitorContext.SaveChangesAsync();
               
                }*/
                _logger.LogDebug("SystemParams: " + JsonUtils.WriteJsonObjectToString(SystemParams));
                _logger.LogDebug("PingParams: " + JsonUtils.WriteJsonObjectToString(PingParams));
                var message = " Got ProcessorList, SystemParams and PingParams from appsettings. ";
                result.Message += message;
                _logger.LogDebug(message);

            }
            catch (Exception e)
            {
                result.Success = false;
                var message = " Error : Unable to set SystemParms . Error was : " + e.ToString();
                result.Message += message;
                _logger.LogCritical(message);
            }
            ProcessorInitObj initObj = new ProcessorInitObj();
            AlertServiceInitObj alertObj = new AlertServiceInitObj();

            //NetConnects = new List<NetConnect>();
            using (var scope = _scopeFactory.CreateScope())
            {
                MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                _processorState.MonitorIPs = await monitorContext.MonitorIPs.AsNoTracking().Include(e => e.UserInfo).Where(w => w.Hidden == false).ToListAsync();
                ListUtils.RemoveNestedMonitorIPs(_processorState.MonitorIPs);
                _processorState.SetAllLoads();
                _logger.LogDebug("DATA : Retreived MonitorIPs " + _processorState.MonitorIPs.Count + " from database.");
                userInfos = await monitorContext.UserInfos.Where(w => w.Enabled == true).ToListAsync();
                _logger.LogDebug("DATA : Retreived UserInfos " + userInfos.Count + " from database.");
                var message = " Got MonitorIPs and UserInfos from Database. ";
                result.Message += message;
                _logger.LogDebug(message);
            }
            initObj.PingParams = _pingParams;
            initObj.Reset = serviceObj.InitResetProcessor;
            initObj.TotalReset = serviceObj.InitTotalResetProcesser;
            _logger.LogDebug("SavedMonitorIPs: " + JsonUtils.WriteJsonObjectToString(initObj.MonitorIPs));
            _logger.LogDebug("PingParmas: " + JsonUtils.WriteJsonObjectToString(initObj.PingParams));
            try
            {
                var message = "";
                foreach (var processorObj in _processorState.ProcessorList)
                {
                    initObj.MonitorIPs = _processorState.MonitorIPs.Where(w => w.AppID == processorObj.AppID).ToList();
                    await _rabbitRepo.PublishAsync<ProcessorInitObj>("processorInit" + processorObj.AppID, initObj);
                    message += " Sent ProcessorInit event to appID " + processorObj.AppID + " . ";
                }
                _logger.LogDebug(message);
                result.Message += message;
            }
            catch (Exception e)
            {
                result.Success = false;
                var message = " Error : Can not publish event  processorInit Error was : " + e.Message.ToString();
                result.Message += message;
                _logger.LogError(message);
            }

            try
            {
                alertObj.TotalReset = serviceObj.InitTotalResetAlertMessage;
                alertObj.UpdateUserInfos = serviceObj.InitUpdateAlertMessage;
                ListUtils.RemoveNestedMonitorIPs(userInfos);
                alertObj.UserInfos = userInfos;
                await _rabbitRepo.PublishAsync<AlertServiceInitObj>("alertMessageInit", alertObj);
                var message = " Sent alertMessageInit event. ";
                result.Message += message;
                _logger.LogDebug(message);
            }
            catch (Exception e)
            {
                result.Success = false;
                var message = " Error : Can not publish event  alertMessageInit Error was : " + e.Message.ToString();
                result.Message += message;
                _logger.LogError(message);
            }

            try
            {
                serviceObj = new MonitorDataInitObj();
                serviceObj.IsDataReady = true;
                serviceObj.IsDataMessage = true;
                await _rabbitRepo.PublishAsync<MonitorDataInitObj>("monitorDataReady", serviceObj);
                var message = " Published event MonitorDataInitObj.IsMonitorDataReady = true";
                result.Message += message;
                _logger.LogDebug(message);

            }
            catch (Exception e)
            {
                result.Success = false;
                var message = " Error : Can not publish event monitorDataReady Error was : " + e.Message.ToString();
                result.Message += message;
                _logger.LogError(message);
            }

            _awake = true;
            if (result.Success)
            {
                _logger.LogInformation(result.Message);
            }
            else
            {
                _logger.LogError(result.Message);
            }
            return result;
        }




        public async Task<ResultObj> DataCheck(MonitorDataInitObj checkObj)
        {
            var result = new ResultObj();
            result.Message = " Service : DataCheck ";
            try
            {
                MonitorDataInitObj publishObj = new MonitorDataInitObj();
                if (checkObj.IsDataMessage)
                {
                    if (_awake)
                    {
                        publishObj = new MonitorDataInitObj();
                        publishObj.IsDataMessage = true;
                        publishObj.IsDataReady = true;
                        await _rabbitRepo.PublishAsync<MonitorDataInitObj>("monitorDataReady", publishObj);
                        result.Message += "Received DataCheck so Published event DataReady";
                        result.Success = true;
                        _logger.LogInformation(result.Message);
                        return result;
                    }
                    else
                    {
                        _logger.LogError(" Error : Received DataCheck Data Ready? But Data is not awake.");

                    }

                }

                if (checkObj.IsDataSaveMessage && _saveReady)
                {
                    if (_saveReady)
                    {
                        publishObj = new MonitorDataInitObj();
                        publishObj.IsDataSaveMessage = true;
                        publishObj.IsDataSaveReady = true;
                        await _rabbitRepo.PublishAsync<MonitorDataInitObj>("monitorDataReady", publishObj);
                        result.Message += "Received DataCheck so Published event DataSaveReady";
                        result.Success = true;
                        _logger.LogInformation(result.Message);
                        return result;
                    }
                    else
                    {
                        _logger.LogError(" Error : Received DataCheck DataSave Ready? But Data is still in Saving state.");

                    }

                }

                if (checkObj.IsDataPurgeMessage)
                {
                    if (_purgeReady)
                    {
                        publishObj = new MonitorDataInitObj();
                        publishObj.IsDataPurgeMessage = true;
                        publishObj.IsDataPurgeReady = true;
                        await _rabbitRepo.PublishAsync<MonitorDataInitObj>("monitorDataReady", publishObj);
                        result.Message += "Received DataCheck so Published event DataPurgeReady";
                        result.Success = true;
                        _logger.LogInformation(result.Message);
                        return result;
                    }
                    else
                    {
                        _logger.LogError(" Error : Received DataCheck DataPurge Ready? But Data is still in Purging state.");

                    }

                }

                result.Message += " Message type not set.";
                _logger.LogError(result.Message);
                result.Success = false;
                return result;


            }
            catch (Exception e)
            {
                result.Message += " Failed to publish messages in DataCheck event . Error was : " + e.ToString();
                result.Success = false;
                _logger.LogError(result.Message);
            }
            return result;
        }

        private async Task<ResultObj> FilterPingInfosBasedOnAccountType(bool filterDefaultUser)
        {
            ResultObj result = new ResultObj();
            result.Message = "MessageAPI : FilterPingInfosBasedOnAccountType : ";
            result.Success = false;



            try
            {
                await _pingInfoService.FilterPingInfosBasedOnAccountType(filterDefaultUser);
                result.Success = true;
                result.Message += "Successfully filtered PingInfos based on Account Type.";
            }
            catch (Exception e)
            {
                result.Message += "Error : Failed to filter PingInfos based on Account Type : Error was : " + e.Message;
                _logger.LogError("Error : Failed to filter PingInfos based on Account Type. : Error was : " + e.ToString());
            }

            return result;
        }

        public async Task<ResultObj> DataPurge()
        {
            var result = new ResultObj();
            result.Message = " Service : DataPurge ";
            try
            {
                MonitorDataInitObj serviceObj = new MonitorDataInitObj();
                serviceObj.IsDataPurgeReady = false;
                serviceObj.IsDataPurgeMessage = true;
                _purgeReady = false;
                await _rabbitRepo.PublishAsync<MonitorDataInitObj>("monitorDataReady", serviceObj);
                result.Message += "Received DataPurge so Published event monitorDataReady.IsDataPurgeReady = false";
                await FilterPingInfosBasedOnAccountType(true);
                serviceObj.IsDataPurgeReady = true;

                await _rabbitRepo.PublishAsync<MonitorDataInitObj>("monitorDataReady", serviceObj);
                result.Message += "Finished DataPurge so Published event monitorDataReady.IsDataPurgeReady = true";
                result.Success = true;
                _logger.LogInformation(result.Message);
            }
            catch (Exception e)
            {
                result.Message += " Failed to run DataPurge . Error was : " + e.ToString();
                result.Success = false;
                _logger.LogError(result.Message);
            }
            finally
            {
                _purgeReady = true;
            }
            return result;
        }

        public async Task<ResultObj> SaveData()
        {

            _saveReady = false;
            ResultObj result = new ResultObj();
            result.Message = "SERVICE : MonitorData.SaveData : ";
            result.Success = false;
            result.Message += "Info : Starting MonitorData.Save ";
            MonitorDataInitObj publishObj = new MonitorDataInitObj();
            publishObj.IsDataSaveMessage = true;
            try
            {

                publishObj.IsDataSaveReady = false;

                await _rabbitRepo.PublishAsync<MonitorDataInitObj>("monitorDataReady", publishObj);
                _logger.LogInformation("Received DataSave so Published event monitorDataReady.IsataSaveReady = false");
            }
            catch (Exception e)
            {
                _logger.LogError("Error : Can not publish event  monitorDataReady.IsDataSaveReady = false" + e.Message.ToString());
            }
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    int maxDataSetID = 0;
                    try { maxDataSetID = await monitorContext.MonitorPingInfos.MaxAsync(m => m.DataSetID); }
                    catch (Exception e)
                    {
                        result.Message += "Warning : Failed to get max DataDataID : Error was : " + e.Message + " ";
                        _logger.LogWarning("Warning : Failed to get max DataDataID : Error was : " + e.Message);
                    }
                    maxDataSetID++;
                    int i = 0;
                    var currentMonitorPingInfos = await monitorContext.MonitorPingInfos.Where(w => w.DataSetID == 0).ToListAsync();
                    currentMonitorPingInfos.ForEach(f =>
                    {
                        f.DataSetID = maxDataSetID;
                        i++;
                    });
                    monitorContext.SaveChanges();
                    result.Message += "Success : DB Updated. Updated " + i + " records to DB ";
                    result.Success = true;
                }
                try
                {
                    MonitorDataInitObj serviceObj = new MonitorDataInitObj();
                    serviceObj.InitTotalResetAlertMessage = false;
                    serviceObj.InitTotalResetProcesser = false;
                    serviceObj.InitResetProcessor = true;
                    serviceObj.InitUpdateAlertMessage = true;
                    await _rabbitRepo.PublishAsync<MonitorDataInitObj>("initData", serviceObj);
                    result.Success = true;
                    result.Message += "Published event initData";
                }
                catch (Exception e)
                {
                    result.Success = false;
                    result.Message += "Error : Can not publish event  initData" + e.Message.ToString();
                    _logger.LogError(result.Message);
                }
            }
            catch (Exception e)
            {
                result.Message += "Error : DB Update Failed. Error was : " + e.Message + " ";
                result.Success = false;
                _logger.LogError("Error : DB Update Failed. Error was : " + e.Message + " Inner Exception :" + e.Message.ToString());
            }
            finally
            {
                _saveReady = true;
                try
                {
                    publishObj.IsDataSaveReady = true;

                    await _rabbitRepo.PublishAsync<MonitorDataInitObj>("monitorDataReady", publishObj);
                    _logger.LogInformation("Finished DataSave so Published event monitorDataReady.IsDataSaveReady = true");
                }
                catch (Exception e)
                {
                    _logger.LogError("Error : Can not publish event  monitorDataReady.IsDataSaveReady=true " + e.Message.ToString());
                }
                //if (_monitorPingProcessor.Abort) _monitorPingProcessor.Abort = false;
                result.Message += "Info : Finished MonitorData.SaveData ";
                _logger.LogInformation(result.Message);
            }


            return result;

        }


    }

}