using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Data;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Linq;
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using NetworkMonitor.Objects.ServiceMessage; // Assuming ResultObj is defined here

namespace NetworkMonitor.Data.Services
{
    public interface IProcessorBrokerService
    {
        Task<ResultObj> NewProcessor(ProcessorObj processor);
        Task<ResultObj> ProcessorStateChange(ProcessorObj processor);
        Task Init();
    }
    public class ProcessorBrokerService : IProcessorBrokerService
    {
        private readonly ILogger<ProcessorBrokerService> _logger;
        private readonly IRabbitRepo _rabbitRepo;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IProcessorState _processorState;

        public ProcessorBrokerService(ILogger<ProcessorBrokerService> logger, IRabbitRepo rabbitRepo, IProcessorState processorState, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _rabbitRepo = rabbitRepo;
            _processorState = processorState;
            _scopeFactory = scopeFactory;
        }

        public  async Task Init()
        {
            var message = " Service : ProcessorBrookerService : ";
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();

                    _processorState.ProcessorList = await monitorContext.ProcessorObjs.ToListAsync();
                    message+=" Success : Got Processor List from Database .";
                }
            }
            catch (Exception e){
                _logger.LogError($" Error : Failed to get Processor List from Database . Error was : {e.Message}");
                return ;
            }
            _logger.LogInformation(message);



        }

        public async Task<ResultObj> NewProcessor(ProcessorObj processor)
        {
            var result = new ResultObj();
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    processor.DateCreated = DateTime.UtcNow;
                    monitorContext.ProcessorObjs.Add(processor);
                    await monitorContext.SaveChangesAsync();
                }

                await _rabbitRepo.PublishAsync("addProcessor", processor);

                result.Success = true;
                result.Message = $" Success : New processor {processor.AppID} added and notified.";
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $" Error : adding new processor. Error was : {ex.Message}";
                _logger.LogError(result.Message);
            }
            return result;
        }

        public async Task<ResultObj> ProcessorStateChange(ProcessorObj processor)
        {
            var result = new ResultObj();
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    var updateProcessor = await monitorContext.ProcessorObjs.FirstOrDefaultAsync(p => p.ID == processor.ID);
                    if (updateProcessor != null)
                    {
                        updateProcessor.DisabledEndPointTypes = processor.DisabledEndPointTypes;
                        updateProcessor.IsEnabled = processor.IsEnabled;
                        updateProcessor.Location = processor.Location;
                        updateProcessor.MaxLoad = processor.MaxLoad;
                        await monitorContext.SaveChangesAsync();
                    }
                }

                await _rabbitRepo.PublishAsync("updateProcessor", processor);

                result.Success = true;
                result.Message = $" Success : Processor {processor.AppID} state updated and notified.";
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $" Error : updating processor state . Error was : {ex.Message}";
                _logger.LogError(result.Message);
            }
            return result;
        }

        // Additional methods for other processor-related operations
    }
}
