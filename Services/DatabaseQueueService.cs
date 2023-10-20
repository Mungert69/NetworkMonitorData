using System;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Data;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Utils.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using NetworkMonitorService.Objects.ServiceMessage;
using System.Threading.Tasks;
using System.Threading;
namespace NetworkMonitor.Data.Services
{
    public interface IDatabaseQueueService
    {

        Task<TResultObj<ProcessorDataObj>> AddProcessorDataStringToQueue(Tuple<string, string> processorDataTuple);


        Task<ResultObj> AddSaveDataToQueue(Func<Task<ResultObj>> func);
    }
    public class DatabaseQueueService : IDatabaseQueueService
    {
        private IConfiguration _config;
        private ILogger _logger;
        private IServiceScopeFactory _scopeFactory;
        private TaskQueue taskQueue = new TaskQueue();

        private List<string> _queuedProcessorJobIds = new List<string>();
        public DatabaseQueueService(IConfiguration config, ILogger<DatabaseQueueService> logger, IServiceScopeFactory scopeFactory)
        {
            _config = config;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public Task<TResultObj<ProcessorDataObj>> AddProcessorDataStringToQueue(Tuple<string, string> processorDataTuple)
        {
            if (_queuedProcessorJobIds.Contains(processorDataTuple.Item2))
            {
                return Task.Run(() =>
                    {
                        var result = new TResultObj<ProcessorDataObj>();
                        result.Success = false;
                        result.Message = " Backlog Error discarding ProcessorData from queue for AppID " + processorDataTuple.Item2 + " The queue already has data from the Processor.";
                        result.Data = null;
                        _logger.LogError(result.Message);
                        return result;
                    });

            }
            _queuedProcessorJobIds.Add(processorDataTuple.Item2);
            Func<Tuple<string, string>, Task<TResultObj<ProcessorDataObj>>> func = CommitProcessorDataTuple;
            return taskQueue.EnqueueTuple<TResultObj<ProcessorDataObj>>(func, processorDataTuple);
        }
        public Task<ResultObj> AddSaveDataToQueue(Func<Task<ResultObj>> func)
        {
            return taskQueue.Enqueue<ResultObj>(func);
        }

        private async Task<TResultObj<ProcessorDataObj>> CommitProcessorDataTuple(Tuple<string, string> processorDataTuple)
        {
           
                var result = new TResultObj<ProcessorDataObj>();
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        string timerStr = " TIMER started : ";
                        var timer = new Stopwatch();
                        timer.Start();
                        List<RemovePingInfo> removePingInfos = new List<RemovePingInfo>();
                        var processorDataObj = ProcessorDataBuilder.ExtractFromZString<ProcessorDataObj>(processorDataTuple.Item1);
                        timerStr += " Unziped at " + timer.Elapsed.TotalMilliseconds + " . ";
                        result.Message += " Processing data for Processor AppID=" + processorDataObj.AppID + " ";

                        if (processorDataObj.MonitorPingInfos != null)
                        {
                            result.Message += " Found " + processorDataObj.MonitorPingInfos.Count() + " MonitorPingInfos in message . And " + processorDataObj.PingInfos.Count() + " PingInfos in message . ";
                        }
                        var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();     
                        var pingInfoHelper=new PingInfoHelper(monitorContext);    
                        ResultObj pingInfoUpdateResult=await pingInfoHelper.UpdateStatusAndPingInfos(processorDataObj.PingInfos, _logger,false);
                        timerStr += " StatusList updated at " + timer.Elapsed.TotalMilliseconds + " . ";
                        var returnProcessorDataObj = await ServiceDataBuilder.Merge(processorDataObj, monitorContext);
                        //Thread.Sleep(125000);
                        timerStr += " Database updated at " + timer.Elapsed.TotalMilliseconds + " . ";
                        result.Data = returnProcessorDataObj;
                        result.Message += timerStr + " Sucess : Saved data to Database : ";
                        var test = await monitorContext.MonitorPingInfos.Where(w => w.Enabled == true && w.DataSetID == 0).FirstOrDefaultAsync();
                        if (test != null && await monitorContext.PingInfos.Where(w => w.MonitorPingInfoID == test.ID).FirstOrDefaultAsync() != null)
                        {
                            result.Message += "DEBUG : DatabaseService got PingInfos count=" + monitorContext.PingInfos.Where(w => w.MonitorPingInfoID == test.ID).Count() + " ";
                        }
                        else
                        {
                            result.Message += "DEBUG : DatabaseService No PingInfos in first enabled MonitorPingInfo. ";
                        }
                        result.Success = true;
                        result.Message += " Timer stopped at " + timer.Elapsed.TotalMilliseconds;
                        timer.Stop();
                    }
                }
                catch (Exception e)
                {
                    result.Success = false;
                    result.Message += "Error : failed to save data to Database. Error was : " + e.Message.ToString();
                    _logger.LogError(result.Message);
                }
                finally
                {

                    _queuedProcessorJobIds.Remove(processorDataTuple.Item2);
                }
                return result;
            
        }
    }
}