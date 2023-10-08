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
    void InitService();
    PingParams PingParams { get; set; }
    SystemParams SystemParams { get; set; }
   

    bool Awake { get; set; }
    IRabbitRepo RabbitRepo { get; }

    Task<ResultObj> WakeUp();
    Task<ResultObj> DataCheck();
        Task<ResultObj> DataPurge();

  }
}