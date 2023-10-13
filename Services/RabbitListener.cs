using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Objects;
using NetworkMonitor.Data.Services;
using System.Collections.Generic;
using System;
using System.Text;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NetworkMonitor.Utils;
using MetroLog;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Utils.Helpers;
namespace NetworkMonitor.Objects.Repository
{
    public interface IRabbitListener
    {
        Task<ResultObj> UpdateMonitorPingInfos(Tuple<string, string> processorDataTuple);
        Task<ResultObj> SaveData();
        Task<ResultObj> DataCheck(MonitorDataInitObj serviceObj);
        Task<ResultObj> DataPurge();
        Task<ResultObj> RestorePingInfosForAllUsers();

    }

    public class RabbitListener : RabbitListenerBase, IRabbitListener
    {
        protected IMonitorData _monitorData;
        protected IDatabaseQueueService _databaseService;

        protected IPingInfoService _pingInfoService;
        public RabbitListener(IMonitorData monitorData, IDatabaseQueueService databaseService, IPingInfoService pingInfoService, INetLoggerFactory loggerFactory, ISystemParamsHelper systemParamsHelper) : base(DeriveLogger(loggerFactory), DeriveSystemUrl(systemParamsHelper))
        {

            _monitorData = monitorData;
            _databaseService = databaseService;
            _pingInfoService = pingInfoService;
            Setup();
        }

        private static ILogger DeriveLogger(INetLoggerFactory loggerFactory)
        {
            return loggerFactory.GetLogger("RabbitListener");
        }

        private static SystemUrl DeriveSystemUrl(ISystemParamsHelper systemParamsHelper)
        {
            return systemParamsHelper.GetSystemParams().ThisSystemUrl;
        }
        protected override void InitRabbitMQObjs()
        {
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "dataUpdateMonitorPingInfos",
                FuncName = "dataUpdateMonitorPingInfos",
                MessageTimeout = 60000
            });

            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "dataCheck",
                FuncName = "dataCheck",
                MessageTimeout = 60000
            });
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "updateUserPingInfos",
                FuncName = "updateUserPingInfos",
                MessageTimeout = 2160000
            });
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "restorePingInfosForAllUsers",
                FuncName = "restorePingInfosForAllUsers",
                MessageTimeout = 2160000
            });
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "dataPurge",
                FuncName = "dataPurge",
                MessageTimeout = 2160000
            });
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "saveData",
                FuncName = "saveData",
                MessageTimeout = 2160000
            });



        }
        protected override ResultObj DeclareConsumers()
        {
            var result = new ResultObj();
            try
            {
                _rabbitMQObjs.ForEach(rabbitMQObj =>
            {
                rabbitMQObj.Consumer = new EventingBasicConsumer(rabbitMQObj.ConnectChannel);
                switch (rabbitMQObj.FuncName)
                {
                    case "dataUpdateMonitorPingInfos":
                        rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);
                        rabbitMQObj.Consumer.Received += async (model, ea) =>
                    {
                        try
                        {
                            result = await UpdateMonitorPingInfos(ConvertToObject<Tuple<string, string>>(model, ea));
                            rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(" Error : RabbitListener.DeclareConsumers.dataUpdateMonitorPingInfos " + ex.Message);
                        }
                    };
                        break;
                    case "dataCheck":
                        rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                        rabbitMQObj.Consumer.Received += async (model, ea) =>
                    {
                        try
                        {
                            result = await DataCheck(ConvertToObject<MonitorDataInitObj>(model, ea));
                            rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(" Error : RabbitListener.DeclareConsumers.dataCheck " + ex.Message);
                        }
                    };
                        break;
                    case "updateUserPingInfos":
                        rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);
                        rabbitMQObj.Consumer.Received += async (model, ea) =>
                    {
                        try
                        {
                            result = await UpdateUserPingInfos(ConvertToObject<PaymentTransaction>(model, ea));
                            rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(" Error : RabbitListener.DeclareConsumers.updateUserPingInfos " + ex.Message);
                        }
                    };
                        break;
                    case "restorePingInfosForAllUsers":
                        rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                        rabbitMQObj.Consumer.Received += async (model, ea) =>
                    {
                        try
                        {
                            result = await RestorePingInfosForAllUsers();
                            rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(" Error : RabbitListener.DeclareConsumers.dataPurge " + ex.Message);
                        }
                    };
                        break;
                    case "dataPurge":
                        rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                        rabbitMQObj.Consumer.Received += async (model, ea) =>
                    {
                        try
                        {
                            result = await DataPurge();
                            rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(" Error : RabbitListener.DeclareConsumers.dataPurge " + ex.Message);
                        }
                    };
                        break;
                    case "saveData":
                        rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                        rabbitMQObj.Consumer.Received += async (model, ea) =>
                    {
                        try
                        {
                            result = await SaveData();
                            rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(" Error : RabbitListener.DeclareConsumers.saveData " + ex.Message);
                        }
                    };
                        break;


                }
            });
                result.Success = true;
                result.Message += " Success : Declared all consumers ";
            }
            catch (Exception e)
            {
                string message = " Error : failed to declate consumers. Error was : " + e.ToString() + " . ";
                result.Message += message;
                Console.WriteLine(result.Message);
                result.Success = false;
            }
            return result;
        }
        public async Task<ResultObj> UpdateMonitorPingInfos(Tuple<string, string> processorDataTuple)
        {
            var returnResult = new ResultObj();
            TResultObj<ProcessorDataObj> result = new TResultObj<ProcessorDataObj>();
            result.Success = false;
            result.Message = "MessageAPI : dataUpdateMonitorPingInfos : ";
            try
            {
                result = await _databaseService.AddProcessorDataStringToQueue(processorDataTuple);
                var returnProcessorDataObj = result.Data;
                if (returnProcessorDataObj != null)
                {
                    _monitorData.RabbitRepo.Publish<ProcessorDataObj>("removePingInfos" + returnProcessorDataObj.AppID, returnProcessorDataObj);
                }
                _logger.Info(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to set MonitorPingInfos : Error was : " + e.Message + " ";
                _logger.Error("Error : Failed to set MonitorPingInfos : Error was : " + e.Message + " ");
            }
            returnResult.Message = result.Message;
            returnResult.Success = result.Success;
            returnResult.Data = (Object)result.Data;
            return returnResult;
        }
        public async Task<ResultObj> DataCheck(MonitorDataInitObj serviceObj)
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : MonitorCheck : ";
            try
            {
                result = await _monitorData.DataCheck(serviceObj);
                _logger.Info(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.Error(result.Message);
            }
            return result;
        }
        public async Task<ResultObj> UpdateUserPingInfos(PaymentTransaction paymentTransaction)
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : UpdateUserPingInfos : ";
            try
            {
                result = await _pingInfoService.RestorePingInfosForSingleUser("", paymentTransaction.UserInfo.CustomerId);
                paymentTransaction.Result = result;
                if (result.Success)
                {
                    paymentTransaction.PingInfosComplete = true;
                }
                else
                {
                    paymentTransaction.PingInfosComplete = false;
                }
                _logger.Info(result.Message);
                _monitorData.RabbitRepo.Publish<PaymentTransaction>("pingInfosComplete", paymentTransaction);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.Error(result.Message);
            }
            return result;
        }


        public async Task<ResultObj> DataPurge()
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : DataPurge : ";
            try
            {
                result = await _monitorData.DataPurge();
                _logger.Info(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.Error(result.Message);
            }
            return result;
        }

 public async Task<ResultObj> RestorePingInfosForAllUsers()
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : RestorePingInfosForAllUsers : ";
            try
            {
                result = await _pingInfoService.RestorePingInfosForAllUsers();
                _logger.Info(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.Error(result.Message);
            }
            return result;
        }

        public async Task<ResultObj> SaveData()
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : SaveData : ";
            try
            {
                Stopwatch timerInner = new Stopwatch();
                timerInner.Start();
                Func<Task<ResultObj>> func = _monitorData.SaveData;
                result = await _databaseService.AddSaveDataToQueue(func);
                TimeSpan timeTakenInner = timerInner.Elapsed;
                // If time taken is greater than the time to wait, then we need to adjust the time to wait.
                int timeTakenInnerInt = (int)timeTakenInner.TotalSeconds;
                result.Message += " Completed in " + timeTakenInnerInt + " s";
                _logger.Info(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to run SaveData : Error was : " + e.Message + " ";
                _logger.Error("Error : Failed to run SaveData : Error was : " + e.Message + " ");
            }
            return result;
        }

    }
}
