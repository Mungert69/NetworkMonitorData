using System;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Data;
using NetworkMonitor.Utils;
using MetroLog;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Utils.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Threading;
namespace NetworkMonitor.Data.Services
{
    public class MonitorData : IMonitorData
    {
        private ILogger _logger;
        private CancellationToken _token;
        private SystemParams _systemParams;
        private bool _isLogChatGPT;
        private IConfiguration _config;
        private PingParams _pingParams;
        private readonly IServiceScopeFactory _scopeFactory;

        private bool _awake;

        private RabbitListener _rabbitListener;
        private IRabbitRepo _rabbitRepo;
        public IRabbitRepo RabbitRepo { get => _rabbitRepo; }
        private ISystemParamsHelper _systemParamsHelper;
        private IDatabaseQueueService _databaseService;

        private IPingInfoService _pingInfoService;
               public SystemParams SystemParams { get => _systemParams; set => _systemParams = value; }
        public bool Awake { get => _awake; set => _awake = value; }
        public PingParams PingParams { get => _pingParams; set => _pingParams = value; }

        public MonitorData(IConfiguration config, INetLoggerFactory loggerFactory, IServiceScopeFactory scopeFactory, CancellationTokenSource cancellationTokenSource, IDatabaseQueueService databaseService, IRabbitRepo rabbitRepo, ISystemParamsHelper systemParamsHelper, IPingInfoService pingInfoService)
        {
            _config = config;
            _databaseService = databaseService;
            _rabbitRepo = rabbitRepo;
            _token = cancellationTokenSource.Token;
            _token.Register(() => OnStopping());
            _scopeFactory = scopeFactory;
            _logger = loggerFactory.GetLogger("MonitorData");
            _systemParamsHelper = systemParamsHelper;
        }
        private void OnStopping()
        {
            Console.WriteLine("SERVICE SHUTDOWN : starting shutdown of MonitorData");
            try
            {
                //ResultObj result = SaveData();
                ResultObj result = new ResultObj();
                result.Success = true;
                result.Message = " OnStopping call complete.";
                //_daprClient.ShutdownSidecarAsync();
                _logger.Warn("SERVICE SHUTDOWN : Result : " + result.Message);
            }
            catch (Exception e)
            {
                _logger.Fatal("Error : Failed to run SaveDate before shutdown : Error Was : " + e.Message);
                Console.WriteLine();
            }
        }
        public Task Init()
        {
            return Task.Run(() =>
              {
                
                  InitService();
              });
        }
        public void InitService()
        {
            PingParams = new PingParams();
            try
            {

                SystemParams = _systemParamsHelper.GetSystemParams();
                PingParams = _systemParamsHelper.GetPingParams();
                _logger.Debug("SystemParams: " + JsonUtils.writeJsonObjectToString(SystemParams));
                _logger.Debug("PingParams: " + JsonUtils.writeJsonObjectToString(PingParams));
            }
            catch (Exception e)
            {
                _logger.Fatal(" Error : Unable to set SystemParms . Error was : " + e.ToString());
            }
          
            try
            {
                var serviceObj=new MonitorDataInitObj();
                serviceObj.IsServiceReady = true;
                _rabbitRepo.Publish<MonitorDataInitObj>("monitorDataReady", serviceObj);
                _logger.Info("Published event MonitorDataInitObj.IsMonitorDataReady = true");
            }
            catch (Exception e)
            {
                _logger.Error("Error : Can not publish event monitorDataReady Error was : " + e.Message.ToString());
            }
        }

        
        
       
        public async Task<ResultObj> WakeUp()
        {
            ResultObj result = new ResultObj();
            result.Message = "SERVICE : MonitorData.WakeUp() ";
            try
            {
                if (_awake)
                {
                    result.Message += "Received WakeUp but MonitorData Save is currently running";
                    result.Success = false;
                }
                else
                {
                    MonitorDataInitObj serviceObj = new MonitorDataInitObj();
                    serviceObj.IsServiceReady = true;
                    await _rabbitRepo.PublishAsync<MonitorDataInitObj>("monitorDataReady", serviceObj);
                    result.Message += "Received WakeUp so Published event monitorDataReady = true";
                    result.Success = true;
                }
                _logger.Info(result.Message);
            }
            catch (Exception e)
            {
                result.Message += "Error : failed to Published event monitorDataReady = true. Error was : " + e.ToString();
                result.Success = false;
            }
            return result;
        }
        public async Task<ResultObj> DataCheck()
        {
            var result = new ResultObj();
            result.Message = " Service : DataCheck ";
            try
            {
                MonitorDataInitObj serviceObj = new MonitorDataInitObj();
                serviceObj.IsMonitorCheckDataReady = true;
                await _rabbitRepo.PublishAsync<MonitorDataInitObj>("monitorCheckDataReady", serviceObj);
                result.Message += "Received MonitorCheck so Published event monitorCheckDataReady = true";
                result.Success = true;
                _logger.Info(result.Message);
            }
            catch (Exception e)
            {
                result.Message += " Failed to publish monitorCheckServiceReady event . Error was : " + e.ToString();
                result.Success = false;
                _logger.Error(result.Message);
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
                serviceObj.IsMonitorCheckDataReady = true;
                await _rabbitRepo.PublishAsync<MonitorDataInitObj>("monitorDataReady", serviceObj);
                result.Message += "Received MonitorCheck so Published event monitorCheckDataReady = true";
             
                result.Message += "TODO implement data purge from PingInfoService";
                result.Success = true;
                _logger.Info(result.Message);
            }
            catch (Exception e)
            {
                result.Message += " Failed to run DataPurge . Error was : " + e.ToString();
                result.Success = false;
                _logger.Error(result.Message);
            }
            return result;
        }


       
    }

}