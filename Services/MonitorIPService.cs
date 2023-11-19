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
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
namespace NetworkMonitor.Data.Services
{
    public interface IMonitorIPService
    {
        Task<ResultObj> DisableMonitorIPs();
    }
    public class MonitorIPService : IMonitorIPService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private ILogger _logger;
        private IRabbitRepo _rabbitRepo;
        public MonitorIPService(IServiceScopeFactory scopeFactory, ILogger<MonitorIPService> logger, IRabbitRepo rabbitRepo)
        {
            _scopeFactory = scopeFactory;
            _rabbitRepo = rabbitRepo;
            _logger = logger;
        }
        public async Task<ResultObj> DisableMonitorIPs()
        {
            var result = new ResultObj();
            result.Message = "SERVICE : MonitorIPService.DisableMonitorIPs : ";
            result.Success = false;
            var userList = new List<UserInfo>();
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    var users = await monitorContext.UserInfos.Where(u => u.UserID!="default" && u.AccountType == "Free" && u.DisableEmail==false && u.LastLoginDate < DateTime.Now.AddMonths(-3)).ToListAsync();
                    foreach (var user in users)
                    {
                        var monitorIPs = await monitorContext.MonitorIPs.Where(w => w.Enabled && w.UserID == user.UserID).ToListAsync();
                        if (monitorIPs.Count > 0)
                        {
                            monitorIPs.ForEach(f => f.Enabled = false);
                            await monitorContext.SaveChangesAsync();
                            userList.Add(user);
                        }
                    }
                }
                result.Success = true;
                result.Message += "Success : Disabled MonitorIPs based on Account Type. ";
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Message += "Error : Failed to Disable MonitorIPs based on Account Type : Error was : " + e.Message;
                result.Success = false;
                _logger.LogError("Error : Failed to Disable MonitorIPs based on Account Type  : Error was : " + e.ToString());
            }
            if (result.Success)
            {
                try
                {
                    await _rabbitRepo.PublishAsync<List<UserInfo>>("userHostExpire", userList);
                    result.Message += " Success : published event userHostExpire";
                }
                catch (Exception e)
                {
                    result.Message += "Error : publish event userHostExpire : Error was : " + e.Message;
                    result.Success = false;
                    _logger.LogError("Error : publish event userHostExpire  : Error was : " + e.ToString());

                }
            }

            return result;
        }


    }
}
