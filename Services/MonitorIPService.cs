using System;
using System.Text;
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
using Microsoft.IdentityModel.Tokens;
using Org.BouncyCastle.Bcpg;
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
        private SystemParams _systemParams;
        public MonitorIPService(IServiceScopeFactory scopeFactory, ILogger<MonitorIPService> logger, IRabbitRepo rabbitRepo, ISystemParamsHelper systemParamsHelper)
        {
            _scopeFactory = scopeFactory;
            _rabbitRepo = rabbitRepo;
            _logger = logger;
            _systemParams = systemParamsHelper.GetSystemParams();

        }
        private string DisableAndbuildHostList(List<MonitorIP> monitorIPs)
        {
            var hostListBuilder = new StringBuilder().Append("(");

            monitorIPs.ForEach(f =>
            {
                f.Enabled = false;
                hostListBuilder.Append(f.Address + ",");
            });

            // Remove the last comma if the StringBuilder is not empty
            if (hostListBuilder.Length > 2)
            {
                hostListBuilder.Length--;  // Reduces the length by 1, effectively removing the last comma
                hostListBuilder.Length--;
            }

            return hostListBuilder.Append(")").ToString();
        }
        public async Task<ResultObj> DisableMonitorIPs()
        {
            var result = new ResultObj();
            result.Message = "SERVICE : MonitorIPService.DisableMonitorIPs : ";
            result.Success = false;
            var uri = _systemParams.ThisSystemUrl.ExternalUrl;

            var emailList = new List<GenericEmailObj>();
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    var users = await monitorContext.UserInfos.Where(u => u.UserID != "default" && u.AccountType == "Free" && u.DisableEmail == false && u.LastLoginDate < DateTime.Now.AddMonths(-3)).ToListAsync();
                    foreach (var user in users)
                    {
                        var emailInfo = new EmailInfo() { Email = user.Email!, EmailType = "UserHostExpire" };
                        var monitorIPs = await monitorContext.MonitorIPs.Where(w => w.Enabled && w.UserID == user.UserID).ToListAsync();
                        if (monitorIPs.Count > 0)
                        {

                            string hostList = DisableAndbuildHostList(monitorIPs);
                            monitorContext.EmailInfos.Add(emailInfo);
                            await monitorContext.SaveChangesAsync();
                            if (!user.DisableEmail) emailList.Add(new GenericEmailObj() { UserInfo = user, HeaderImageUri = uri, ID = emailInfo.ID, ExtraMessage = hostList });
                        }
                    }
                    // Fetch all MonitorIPs for the default user that haven't been verified for over 3 months
                    var allMonitorIPs = await monitorContext.MonitorIPs
                        .Where(w => w.Enabled && w.UserID == "default" && !string.IsNullOrEmpty(w.AddUserEmail) && w.DateAdded < DateTime.UtcNow.AddMonths(-3))
                        .ToListAsync();

                    // Group by AddUserEmail to process each unique email once
                    var groupedByAddUserEmail = allMonitorIPs.GroupBy(ip => ip.AddUserEmail);

                    foreach (var group in groupedByAddUserEmail)
                    {
                        if (string.IsNullOrEmpty(group.Key))
                            continue;
                        string addUserEmail = group.Key;
                        var monitorIPsDefault = group.ToList();
                        // Check if any of the MonitorIPs in this group has IsEmailVerified set to true
                        bool isEmailVerified = monitorIPsDefault.Any(mip => mip.IsEmailVerified);

                        // Assuming DisableAndbuildHostList is a method that disables the hosts and returns a string list of hosts
                        string hostList = DisableAndbuildHostList(monitorIPsDefault);

                        // Create email info object
                        var emailInfo = new EmailInfo() { Email = addUserEmail, EmailType = "UserHostExpire" };
                        monitorContext.EmailInfos.Add(emailInfo);

                        // Save changes if there are IPs to disable
                        if (monitorIPsDefault.Any())
                        {
                            await monitorContext.SaveChangesAsync();

                            // Assuming you have a logic to check if the user wants to receive emails or similar logic applied
                            emailList.Add(new GenericEmailObj() { UserInfo = new UserInfo { UserID = "default", Email = addUserEmail, Email_verified=isEmailVerified , DisableEmail=!isEmailVerified}, HeaderImageUri = uri, ID = emailInfo.ID, ExtraMessage = hostList });
                        }
                    }

                    await monitorContext.SaveChangesAsync();

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
                    await _rabbitRepo.PublishAsync<List<GenericEmailObj>>("userHostExpire", emailList);
                    result.Message += $" Success : published event userHostExpire with {emailList.Count} Objects";
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
