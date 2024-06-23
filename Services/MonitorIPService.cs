using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Data;
using NetworkMonitor.Data.Repo;
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
        Task<ResultObj> SaveMonitorIPsWithUser(ProcessorDataObj processorDataObj);
    }
    public class MonitorIPService : IMonitorIPService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private ILogger _logger;
        private IRabbitRepo _rabbitRepo;
        private IUserRepo _userRepo;
        private SystemParams _systemParams;
        private PingParams _pingParams;
        private IProcessorState _processorState;
        public MonitorIPService(IServiceScopeFactory scopeFactory, ILogger<MonitorIPService> logger, IUserRepo userRepo, IRabbitRepo rabbitRepo, ISystemParamsHelper systemParamsHelper, IProcessorState processorState)
        {
            _scopeFactory = scopeFactory;
            _rabbitRepo = rabbitRepo;
            _logger = logger;
            _userRepo = userRepo;
            _systemParams = systemParamsHelper.GetSystemParams();
            _pingParams = systemParamsHelper.GetPingParams();

            _processorState = processorState;

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
                    var users = await monitorContext.UserInfos.Where(u => u.UserID != "default" && u.AccountType == "Free" && u.DisableEmail == false && u.LastLoginDate < DateTime.Now.AddMonths(-_systemParams.ExpireMonths)).ToListAsync();
                    foreach (var user in users)
                    {
                        var emailInfo = new EmailInfo() { Email = user.Email!, EmailType = "UserHostExpire" };
                        var monitorIPs = await monitorContext.MonitorIPs.Where(w => w.Enabled && w.UserID == user.UserID).ToListAsync();
                        if (monitorIPs.Count > 0)
                        {

                            string hostList = DataHelpers.DisableAndbuildHostList(monitorIPs);
                            monitorContext.EmailInfos.Add(emailInfo);
                            await monitorContext.SaveChangesAsync();
                            if (!user.DisableEmail) emailList.Add(new GenericEmailObj() { UserInfo = user, HeaderImageUri = uri, ID = emailInfo.ID, ExtraMessage = hostList });
                        }
                    }
                    // Fetch all MonitorIPs for the default user that haven't been verified for over 3 months
                    var allMonitorIPs = await monitorContext.MonitorIPs
                        .Where(w => w.Enabled && w.UserID == "default" && !string.IsNullOrEmpty(w.AddUserEmail) && w.DateAdded < DateTime.UtcNow.AddMonths(-_systemParams.ExpireMonths))
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
                        string hostList = DataHelpers.DisableAndbuildHostList(monitorIPsDefault);

                        // Create email info object
                        var emailInfo = new EmailInfo() { Email = addUserEmail, EmailType = "UserHostExpire" };
                        monitorContext.EmailInfos.Add(emailInfo);

                        // Save changes if there are IPs to disable
                        if (monitorIPsDefault.Any())
                        {
                            await monitorContext.SaveChangesAsync();

                            // Assuming you have a logic to check if the user wants to receive emails or similar logic applied
                            emailList.Add(new GenericEmailObj() { UserInfo = new UserInfo { UserID = "default", Email = addUserEmail, Email_verified = isEmailVerified, DisableEmail = !isEmailVerified }, HeaderImageUri = uri, ID = emailInfo.ID, ExtraMessage = hostList });
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

        public async Task<ResultObj> SaveMonitorIPsWithUser(ProcessorDataObj processorDataObj)
        {
            ResultObj result = new ResultObj();
            result.Message = "Save Host Data : ";
            result.Success = false;
            List<MonitorIP> newData = processorDataObj.MonitorIPs;
            string authKey = processorDataObj.AuthKey;
            var userId = "";
            var appId = processorDataObj.AppID;
             if (EncryptHelper.IsBadKey(_systemParams.EmailEncryptKey, authKey, appId))
                    {
                      result.Message += $" Error : Processor AuthKey not valid for AppID {appId}. ";
                        result.Success = false;
                        return result;
                    }
            try
            {
                if (newData == null)
                {
                    result.Message += "Info : Nothing to save";
                    result.Success = true;
                    return result;
                }
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    // Two different ways of setting userId because database contains UserInfo object whereas data from api call does not.
                    var firstMon = newData.FirstOrDefault();
                    if (firstMon == null || firstMon.UserID == null)
                    {
                        result.Message += " Error : Data is empty. ";
                        result.Success = false;
                        return result;
                    }
                    
                    userId = firstMon.UserID;
                    appId = firstMon.AppID;
                    
                    var data = await monitorContext.MonitorIPs.Include(e => e.UserInfo).Where(w => w.UserInfo!.UserID == userId && w.Hidden == false).ToListAsync();
                    //ListUtils.RemoveNestedMonitorIPs(data);
                    var userInfo = await _userRepo.GetUserFromID(userId);
                    if (userInfo == null)
                    {
                        result.Message += " Error : User in data does not exist. ";
                        result.Success = false;
                        return result;
                    }
                    var countUserIDs = data.Count(w => w.UserID == userId);
                    if (countUserIDs != data.Count())
                    {
                        result.Message += " Error : UserID is not the same for all data. ";
                        result.Success = false;
                        return result;
                    }
                    var appIDNotSame = data.Any(w => w.AppID != appId);
                    if (!appIDNotSame)
                    {
                        result.Message += " Error : AppID is not the same for all data. ";
                        result.Success = false;
                        return result;
                    }
                     var processorObj = _processorState.ProcessorList.Where(w => w.AppID == appId).FirstOrDefault();
                      if (processorObj==null) { 
                        result.Message += " Error : Processor with AppID not found. ";
                        result.Success = false;
                        return result;
                    }
                    if (processorObj.AuthKey != authKey) { 
                        result.Message += " Error : Processor AuthKey does not match. ";
                        result.Success = false;
                        return result;
                    }
                    var updateMonitorIPs = new List<UpdateMonitorIP>();
                    if (newData.Count <= userInfo.HostLimit)
                    {
                        foreach (MonitorIP monIP in data)
                        {
                            var dataMonIP = newData.FirstOrDefault(m => m.ID == monIP.ID);
                            if (dataMonIP != null)
                            {
                                await MonitorIPUpdateHelper.AddUpdateMonitorIP(monIP, dataMonIP, updateMonitorIPs, userId, monitorContext, _systemParams.EmailEncryptKey, _processorState, _pingParams.Timeout);
                            }
                            //user = monIP.UserInfo;
                        }
                        await monitorContext.SaveChangesAsync();
                        ProcessorQueueDicObj queueDicObj = new ProcessorQueueDicObj();
                        //queueDicObj.MonitorIPs = newData;
                        queueDicObj.UserId = userId!;
                        
                        queueDicObj.MonitorIPs = updateMonitorIPs.Where(w => w.AppID == processorObj.AppID).ToList();
                        if (queueDicObj.MonitorIPs.Count > 0)
                        {
                            await _rabbitRepo.PublishAsync<ProcessorQueueDicObj>("processorQueueDic" + processorObj.AppID, queueDicObj);
                            _logger.LogInformation("Sent event ProcessorQueueDic for AppID  " + processorObj.AppID);
                        }

                        //result.Data = data;
                        if (userInfo.Email_verified)
                        {
                            result.Success = true;
                            result.Message += "Success : Data will become live in around 2 minutes ";
                        }
                        else
                        {
                            result.Success = false;
                            result.Message += "Warning : Data has been saved but you will receive not email alerts until you verify you email.";
                        }
                    }
                    else
                    {
                        result.Message += "Error : User " + userInfo.Name + " has reached the host limit of " + userInfo.HostLimit + " . Delete a host before saving again.";
                        result.Success = false;
                        //result.Data = data;
                    }
                }
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Message += "Error : DB Update Failed : Error was : " + e.Message + " ";
                result.Success = false;
                _logger.LogError("Error :  DB Update Failed: Error was : " + e.Message + " Inner Exception : " + e.Message.ToString());
            }
            finally
            {
            }
            return result;
        }


    }
}
