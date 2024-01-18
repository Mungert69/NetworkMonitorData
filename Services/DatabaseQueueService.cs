using System;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Utils;
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
using System.Reflection;
namespace NetworkMonitor.Data.Services
{
    public interface IDatabaseQueueService
    {

        Task<TResultObj<ProcessorDataObj>> AddProcessorDataStringToQueue(Tuple<string, string> processorDataTuple);
        Task<TResultObj<string>> RestorePingInfosForSingleUser(Func<string, Task<TResultObj<string>>> func, string data);

        Task<ResultObj> AddTaskToQueue(Func<Task<ResultObj>> func);
        Task<ResultObj> ShutdownTaskQueue();
    }
    public class DatabaseQueueService : IDatabaseQueueService
    {
        private IConfiguration _config;
        private ILogger _logger;
        private IServiceScopeFactory _scopeFactory;
        private TaskQueue taskQueue = new TaskQueue();

        private string _encryptKey;
        private List<string> _queuedProcessorJobIds = new List<string>();
        public DatabaseQueueService(IConfiguration config, ILogger<DatabaseQueueService> logger, IServiceScopeFactory scopeFactory, ISystemParamsHelper systemParamsHelper)
        {
            _config = config;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _encryptKey = systemParamsHelper.GetSystemParams().EmailEncryptKey;
        }

        public async Task<ResultObj> ShutdownTaskQueue()
        {
            var result = new ResultObj();
            try
            {
                taskQueue.StopAcceptingTasks();
                await taskQueue.WaitForAllTasksToComplete();
                result.Message = " Success Task Queue is empty .";
                result.Success = true;
            }
            catch (Exception e)
            {
                result.Message = $" Errror : Could not empty Task Queue . Error was : {e.Message}";
                result.Success = false;
            }
            return result;

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
        public Task<ResultObj> AddTaskToQueue(Func<Task<ResultObj>> func)
        {
            return taskQueue.Enqueue<ResultObj>(func);
        }

        public Task<TResultObj<string>> RestorePingInfosForSingleUser(Func<string, Task<TResultObj<string>>> func, string data)
        {
            return taskQueue.EnqueueString<TResultObj<string>>(func, data);
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

                    if (processorDataObj == null)
                    {
                        result.Success = false;
                        result.Message = " Error : Failed CommitProcessorDataBytes processorDataObj is null.";
                        _logger.LogError(result.Message);
                        return result;
                    }
                    if (processorDataObj.AppID == null)
                    {
                        result.Success = false;
                        result.Message = " Error : Failed CommitProcessorDataBytes processorDataObj.AppID is null.";
                        _logger.LogError(result.Message);
                        return result;
                    }
                    if (processorDataObj.AuthKey == null)
                    {
                        result.Success = false;
                        result.Message = $" Error : Failed CommitProcessorDataBytes processorDataObj.AppKey is null for AppID {processorDataObj.AppID}";
                        _logger.LogError(result.Message);
                        return result;
                    }
                    if (EncryptionHelper.IsBadKey(_encryptKey, processorDataObj.AuthKey, processorDataObj.AppID))
                    {
                        result.Success = false;
                        result.Message = $" Error : Failed CommitProcessorDataBytes bad AuthKey for AppID {processorDataObj.AppID}";
                        var message=$" Key should be : {AesOperation.EncryptString(_encryptKey,processorDataObj.AppID)} . ";
                        _logger.LogError(result.Message+ " :: "+ message);
                        return result;
                    }
                    if (processorDataObj.MonitorPingInfos.Where(w => w.AppID != processorDataObj.AppID).Count() > 0)
                    {
                        result.Success = false;
                        result.Message = $" Error : Failed CommitProcessorDataBytes invalid AppID in data for AppID {processorDataObj.AppID}";
                        _logger.LogError(result.Message);
                        return result;
                    }
                    timerStr += " Unziped at " + timer.Elapsed.TotalMilliseconds + " . ";
                    result.Message += " Processing data for Processor AppID=" + processorDataObj.AppID + " ";

                    if (processorDataObj.MonitorPingInfos != null)
                    {
                        result.Message += " Found " + processorDataObj.MonitorPingInfos.Count() + " MonitorPingInfos in message . And " + processorDataObj.PingInfos.Count() + " PingInfos in message . ";
                    }
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    var pingInfoHelper = new PingInfoHelper(monitorContext);
                    ResultObj pingInfoUpdateResult = await pingInfoHelper.UpdateStatusAndPingInfos(processorDataObj.PingInfos, _logger, false);
                    timerStr += " StatusList updated at " + timer.Elapsed.TotalMilliseconds + " . ";
                    var returnProcessorDataObj = await ServiceDataBuilder.Merge(processorDataObj, monitorContext);
                    //Thread.Sleep(125000);
                    timerStr += " Database updated at " + timer.Elapsed.TotalMilliseconds + " . ";
                    result.Data = returnProcessorDataObj;
                    result.Message += timerStr + " Sucess : Saved data to Database : ";
                    var test = await monitorContext.MonitorPingInfos.Where(w => w.Enabled == true && w.DataSetID == 0).FirstOrDefaultAsync();
                    if (test != null && await monitorContext.PingInfos.AnyAsyncWhere(w => w.MonitorPingInfoID == test.ID) )
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