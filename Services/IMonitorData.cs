using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Objects.Repository;
namespace NetworkMonitor.Data.Services
{
  public interface IMonitorData
  {
    Task Init();
    Task<ResultObj> InitService(MonitorDataInitObj serviceObj);
    PingParams PingParams { get; set; }
    SystemParams SystemParams { get; set; }
   
    IRabbitRepo RabbitRepo { get; }

    Task<ResultObj> DataCheck(MonitorDataInitObj checkObj);
        Task<ResultObj> DataPurge();
        Task<ResultObj> SaveData();

  }
}