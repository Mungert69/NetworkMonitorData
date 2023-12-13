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
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Utils.Helpers;
using NetworkMonitor.Objects.Repository;
namespace NetworkMonitor.Data.Services
{
    public interface IRabbitListener
    {
        Task<ResultObj> UpdateMonitorPingInfos(Tuple<string, string> processorDataTuple);
        Task<ResultObj> SaveData();
        Task<ResultObj> DataCheck(MonitorDataInitObj serviceObj);
        Task<ResultObj> DataPurge();

        Task<ResultObj> RestorePingInfosForAllUsers();
        Task<ResultObj> CreateHostSummaryReport();

    }

    public class RabbitListener : RabbitListenerBase, IRabbitListener
    {
        protected IMonitorData _monitorData;
        protected IDatabaseQueueService _databaseService;
        protected IProcessorBrokerService _processorBrokerService;

        protected IPingInfoService _pingInfoService;
        protected IReportService _reportService;
        protected IMonitorIPService _monitorIPService;
        public RabbitListener(IMonitorData monitorData, IDatabaseQueueService databaseService, IPingInfoService pingInfoService, IMonitorIPService monitorIPService, IReportService reportService, ILogger<RabbitListenerBase> logger, ISystemParamsHelper systemParamsHelper, IProcessorBrokerService processorBrokerService) : base(logger, DeriveSystemUrl(systemParamsHelper))
        {

            _monitorData = monitorData;
            _databaseService = databaseService;
            _pingInfoService = pingInfoService;
            _reportService = reportService;
            _monitorIPService = monitorIPService;
            _processorBrokerService = processorBrokerService;
            Setup();
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
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "initData",
                FuncName = "initData",
                MessageTimeout = 60000
            });
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "createHostSummaryReport",
                FuncName = "createHostSummaryReport",
                MessageTimeout = 2160000
            });
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "changeProcessorAppID",
                FuncName = "changeProcessorAppID",
                MessageTimeout = 2160000
            });
            _rabbitMQObjs.Add(new RabbitMQObj()
            {
                ExchangeName = "userUpdateProcessor",
                FuncName = "userUpdateProcessor",
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
                if (rabbitMQObj.ConnectChannel != null)
                {
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
                                _logger.LogError(" Error : RabbitListener.DeclareConsumers.dataUpdateMonitorPingInfos " + ex.Message);
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
                                _logger.LogError(" Error : RabbitListener.DeclareConsumers.dataCheck " + ex.Message);
                            }
                        };
                            break;
                        case "updateUserPingInfos":
                            rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);
                            rabbitMQObj.Consumer.Received += async (model, ea) =>
                        {
                            try
                            {
                                var tResult = await UpdateUserPingInfos(ConvertToObject<PaymentTransaction>(model, ea));
                                result.Success = tResult.Success;
                                result.Message = tResult.Message;
                                result.Data = tResult.Data;
                                rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(" Error : RabbitListener.DeclareConsumers.updateUserPingInfos " + ex.Message);
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
                                _logger.LogError(" Error : RabbitListener.DeclareConsumers.dataPurge " + ex.Message);
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
                                _logger.LogError(" Error : RabbitListener.DeclareConsumers.dataPurge " + ex.Message);
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
                                _logger.LogError(" Error : RabbitListener.DeclareConsumers.saveData " + ex.Message);
                            }
                        };
                            break;
                        case "initData":
                            rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                            rabbitMQObj.Consumer.Received += async (model, ea) =>
                        {
                            try
                            {
                                result = await InitData(ConvertToObject<MonitorDataInitObj>(model, ea));
                                rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(" Error : RabbitListener.DeclareConsumers.initData " + ex.Message);
                            }
                        };
                            break;
                        case "createHostSummaryReport":
                            rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                            rabbitMQObj.Consumer.Received += async (model, ea) =>
                        {
                            try
                            {
                                result = await CreateHostSummaryReport();
                                rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(" Error : RabbitListener.DeclareConsumers.report " + ex.Message);
                            }
                        };
                            break;
                        case "changeProcessorAppID":
                            rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                            rabbitMQObj.Consumer.Received += async (model, ea) =>
                        {
                            try
                            {
                                result = await ChangeProcessorAppID(ConvertToObject<Tuple<string,string>>(model, ea));
                                rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(" Error : RabbitListener.DeclareConsumers.user.AddProcessor " + ex.Message);
                            }
                        };
                            break;
                        case "userUpdateProcessor":
                            rabbitMQObj.ConnectChannel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
                            rabbitMQObj.Consumer.Received += async (model, ea) =>
                        {
                            try
                            {
                                result = await UserUpdateProcessor(ConvertToObject<ProcessorObj>(model, ea));
                                rabbitMQObj.ConnectChannel.BasicAck(ea.DeliveryTag, false);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(" Error : RabbitListener.DeclareConsumers.userUpdateProcessor " + ex.Message);
                            }
                        };
                            break;

                    }
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
                _logger.LogInformation(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to set MonitorPingInfos : Error was : " + e.Message + " ";
                _logger.LogError("Error : Failed to set MonitorPingInfos : Error was : " + e.Message + " ");
            }
            returnResult.Message = result.Message;
            returnResult.Success = result.Success;
            returnResult.Data = (object)result.Data!;
            return returnResult;
        }
        public async Task<ResultObj> DataCheck(MonitorDataInitObj? serviceObj)
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : DataCheck : ";
            if (serviceObj == null)
            {
                result.Message += " Error : serviceObj  is Null ";
                return result;
            }
            try
            {
                result = await _monitorData.DataCheck(serviceObj);
                _logger.LogInformation(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.LogError(result.Message);
            }
            return result;
        }
        public async Task<TResultObj<string>> UpdateUserPingInfos(PaymentTransaction? paymentTransaction)
        {
            var result = new TResultObj<string>();
            result.Success = false;
            result.Message = "MessageAPI : UpdateUserPingInfos : ";
            if (paymentTransaction == null)
            {
                result.Message += " Error : paymentTranaction is Null ";
                return result;
            }
            try
            {
                Func<string,Task<TResultObj<string>>> func = _pingInfoService.RestorePingInfosForSingleUser;
                result = await _databaseService.RestorePingInfosForSingleUser(func, paymentTransaction.UserInfo.CustomerId!);
                paymentTransaction.Result = result;
                if (result.Success)
                {
                    paymentTransaction.PingInfosComplete = true;
                }
                else
                {
                    paymentTransaction.PingInfosComplete = false;
                }
                _logger.LogInformation(result.Message);
                _monitorData.RabbitRepo.Publish<PaymentTransaction>("pingInfosComplete", paymentTransaction);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.LogError(result.Message);
            }
            return result;
        }


        public async Task<ResultObj> DataPurge()
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : DataPurge : ";
            var results = new List<ResultObj>();
            try
            {
                Func<Task<ResultObj>> func = _monitorData.DataPurge;
                var resultPurge = await _databaseService.AddTaskToQueue(func);
                result.Message += resultPurge.Message;
                result.Success = resultPurge.Success;
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed dataPurge message : Error was : " + e.Message + " ";

            }
            try
            {
                Func<Task<ResultObj>> func = _monitorIPService.DisableMonitorIPs;
                var resultMonitorIPs = await _databaseService.AddTaskToQueue(func);
                result.Message += resultMonitorIPs.Message;
                result.Success = result.Success && resultMonitorIPs.Success;
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to disable MonitorIPs : Error was : " + e.Message + " ";

            }

            if (result.Success) _logger.LogInformation(result.Message);
            else _logger.LogError(result.Message);
            return result;
        }

        public async Task<ResultObj> RestorePingInfosForAllUsers()
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : RestorePingInfosForAllUsers : ";
            try
            {
                 Func<Task<ResultObj>> func = _pingInfoService.RestorePingInfosForAllUsers;
                result = await _databaseService.AddTaskToQueue(func);
                _logger.LogInformation(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.LogError(result.Message);
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
                result = await _databaseService.AddTaskToQueue(func);
                TimeSpan timeTakenInner = timerInner.Elapsed;
                // If time taken is greater than the time to wait, then we need to adjust the time to wait.
                int timeTakenInnerInt = (int)timeTakenInner.TotalSeconds;
                result.Message += " Completed in " + timeTakenInnerInt + " s";
                _logger.LogInformation(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to run SaveData : Error was : " + e.Message + " ";
                _logger.LogError("Error : Failed to run SaveData : Error was : " + e.Message + " ");
            }
            return result;
        }
        public async Task<ResultObj> InitData(MonitorDataInitObj? serviceObj)
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : InitData : ";
            if (serviceObj == null)
            {
                result.Message += " Error : serviceObj  is Null ";
                return result;
            }
            try
            {
                var resultBroker = await _processorBrokerService.Init();
                if (resultBroker.Success)
                {
                    _logger.LogInformation(resultBroker.Message);
                }
                else
                {
                    _logger.LogError(resultBroker.Message);
                }
                await Task.Delay(5000);
                var resultService = await _monitorData.InitService(serviceObj);
                if (result.Success)
                {
                    _logger.LogInformation(resultService.Message);
                }
                else
                {
                    _logger.LogError(resultService.Message);
                }
                result.Success = resultBroker.Success && resultService.Success;
                result.Message += resultBroker.Message + resultService.Message;

            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.LogError(result.Message);
            }
            return result;
        }

        public async Task<ResultObj> ChangeProcessorAppID(Tuple<string,string> appIDs)
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : UserAddProcessor : ";
            if (appIDs == null)
            {
                result.Message += " Error : appIDs  is Null ";
                return result;
            }
            try
            {
                result = await _processorBrokerService.ChangeProcessorAppID(appIDs);
                if (result.Success)
                {
                    _logger.LogInformation(result.Message);
                }
                else
                {
                    _logger.LogError(result.Message);
                }

            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.LogError(result.Message);
            }
            return result;
        }

        public async Task<ResultObj> UserUpdateProcessor(ProcessorObj? processorObj)
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : UserUpdateProcessor : ";
            if (processorObj == null)
            {
                result.Message += " Error : processorObj  is Null ";
                return result;
            }
             if (processorObj.AppID == null )
            {
                result.Message += " Error : processorObj.AppID  is Null ";
                return result;
            }
              if (processorObj.AppID == "")
            {
                result.Message += " Error : processorObj.AppID  is empty ";
                return result;
            }
            try
            {
                result = await _processorBrokerService.UserUpdateProcessor(processorObj);
                if (result.Success)
                {
                    _logger.LogInformation(result.Message);
                }
                else
                {
                    _logger.LogError(result.Message);
                }

            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.LogError(result.Message);
            }
            return result;
        }
        public async Task<ResultObj> CreateHostSummaryReport()
        {
            ResultObj result = new ResultObj();
            result.Success = false;
            result.Message = "MessageAPI : CreateHostSummaryReport : ";
            try
            {
                result = await _reportService.CreateHostSummaryReport();
                _logger.LogInformation(result.Message);
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Success = false;
                result.Message += "Error : Failed to receive message : Error was : " + e.Message + " ";
                _logger.LogError(result.Message);
            }
            return result;
        }

    }
}
