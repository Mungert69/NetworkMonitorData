
using System;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Utils;
using NetworkMonitor.Objects.Repository;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Utils.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;

namespace NetworkMonitor.Data.Repo;

public interface IUserRepo
{

    Task<UserInfo?> GetUserFromID(string userId);
    Task<UserInfo?> GetUserFromIDDB(string userId);
    Task<ResultObj> AddAuthUserInfo(UserAuthInfo userAuthInfo);
    Task<ResultObj> AddUser(UserInfo user);
    Task<ResultObj> UpdateApiUser(UserInfo user);
    Task<TResultObj<string>> UpdateUserSubscription(UserInfo user);
    Task<TResultObj<string>> UpdateUserCustomerId(UserInfo user);
    Task<ResultObj> SetUserDisabled(string userId);
    Task<ResultObj> Subscription(string email, string userId, bool subscribe);
    Task<ResultObj> VerifyEmail(string email, string userId);
    Task<ResultObj> UpgradeAccounts();
    List<UserInfo> CachedUsers { get; }
    Task<ResultObj> LogEmailOpen(Guid id);
    Task UpdateTokensUsed(string userId, int tokensUsed);
    Task<int> GetTokenCount(string userId);
    Task ResetTokensUsed();
    Task FillTokensUsed();
    Task RefreshUsers();
}
public class UserRepo : IUserRepo
{

    private readonly IServiceScopeFactory _scopeFactory;
    private List<UserInfo> _cachedUsers;
    private ILogger _logger;
    private IRabbitRepo _rabbitRepo;
    private IProcessorState _processorState;
    private ISystemParamsHelper _systemParamsHelper;

    public List<UserInfo> CachedUsers
    {
        get
        {
            if (_cachedUsers == null || _cachedUsers.Count == 0) _cachedUsers = GetAllDBUsersDBNoTracking().Result;
            return _cachedUsers;
        }
    }

    public async Task RefreshUsers()
    {
        _cachedUsers = await GetAllDBUsersDBNoTracking();
    }

    public UserRepo(ILogger<UserRepo> logger, IServiceScopeFactory scopeFactory, ISystemParamsHelper systemParamsHelper, IRabbitRepo rabbitRepo, IProcessorState processorState)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _rabbitRepo = rabbitRepo;
        _processorState = processorState;
        _systemParamsHelper = systemParamsHelper;

    }


    public void UpdateCachedUserInfo(UserInfo newUserInfo)
    {
        var userInfo = CachedUsers.Where(w => w.UserID == newUserInfo.UserID).FirstOrDefault();
        if (userInfo == null)
        {
            _logger.LogError($" Error ; Unabled to update cachedUser . Could not find user with Id {newUserInfo.UserID}");
            return;
        }
        userInfo.SetFields(newUserInfo);

    }
    public async Task<List<UserInfo>> GetAllDBUsersDBNoTracking()
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
            var users = await monitorContext.UserInfos.AsNoTracking().ToListAsync();
            return users;
        }

    }


    public async Task<UserInfo?> GetUserFromID(string userId)
    {

        return CachedUsers.Where(w => w.UserID == userId).FirstOrDefault();

    }
    public async Task<UserInfo?> GetUserFromIDDB(string userId)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
            return await monitorContext.UserInfos.Where(w => w.UserID == userId && w.Enabled).FirstOrDefaultAsync();
        }

    }

    public async Task<int> GetTokenCount(string userId)
    {

        return CachedUsers.Where(w => w.UserID == userId).Select(s => s.TokensUsed).FirstOrDefault();
    }
    public async Task UpdateTokensUsed(string userId, int tokensUsed)
    {
        var user = CachedUsers.Where(w => w.UserID == userId).FirstOrDefault();
        if (user != null)
        {
            user.TokensUsed -= tokensUsed;

        }
        using (var scope = _scopeFactory.CreateScope())
        {
            MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
            var dbUser = await monitorContext.UserInfos.Where(w => w.UserID == userId).FirstOrDefaultAsync();
            if (dbUser != null)
            {
                dbUser.TokensUsed -= tokensUsed;
                monitorContext.SaveChanges();
            }
        }
    }
    private void ResetTokens(List<UserInfo> userInfos)
    {
        foreach (var user in userInfos)
        {
            ResetTokenForUser(user);
        }
    }
    private void ResetTokenForUser(UserInfo user)
    {
        var accountType = AccountTypeFactory.GetAccountTypeByName(user.AccountType);

        if (accountType != null)
        {
            user.TokensUsed = accountType.TokenLimit;
        }
    }
    public async Task ResetTokensUsed()
    {
        var users = CachedUsers;

        ResetTokens(users);
        using (var scope = _scopeFactory.CreateScope())
        {
            MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
            var dbUsers = await monitorContext.UserInfos.Where(w => w.Enabled).ToListAsync();
            ResetTokens(dbUsers);
            monitorContext.SaveChanges();

        }
    }

    public async Task FillTokensUsed()
    {
        var users = CachedUsers;

        FillTokens(users);
        using (var scope = _scopeFactory.CreateScope())
        {
            MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
            var dbUsers = await monitorContext.UserInfos.Where(w => w.Enabled).ToListAsync();
            FillTokens(dbUsers);
            monitorContext.SaveChanges();

        }
    }

    private void FillTokens(List<UserInfo> userInfos)
    {
        var accountTypes = AccountTypeFactory.GetAccountTypes(); // Get the account type configurations

        foreach (var user in userInfos)
        {
            var accountType = accountTypes.FirstOrDefault(a => a.Name == user.AccountType);

            if (accountType != null)
            {
                int newTokenCount = Math.Min(user.TokensUsed + accountType.DailyTokens, accountType.TokenLimit);
                user.TokensUsed = newTokenCount;
            }
        }
    }





    public async Task<ResultObj> AddAuthUserInfo(UserAuthInfo userAuthInfo)
    {
        // Always hash new and updated entries with SHA3-256

        try
        {
            string hashedRefreshTokenSha3 = HashHelper.ComputeSha3_256Hash(userAuthInfo.ResfreshToken);

            using (var scope = _scopeFactory.CreateScope())
            {
                MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();

                // Check for existing entry with both SHA-256 and SHA3-256 hashes of the RefreshToken
                var existingUserAuthInfo = await monitorContext.UserAuthInfos
                    .FirstOrDefaultAsync(u => (u.ResfreshToken == HashHelper.ComputeSha256Hash(userAuthInfo.ResfreshToken) ||
                                               u.ResfreshToken == hashedRefreshTokenSha3) &&
                                               u.FusionAppID == userAuthInfo.FusionAppID &&
                                               u.ClientAppName == userAuthInfo.ClientAppName);

                if (existingUserAuthInfo != null)
                {
                    // Update existing entry
                    existingUserAuthInfo.UserID = userAuthInfo.UserID;
                    existingUserAuthInfo.DateUpdated = userAuthInfo.DateUpdated;
                    existingUserAuthInfo.IsAuthenticated = userAuthInfo.IsAuthenticated;
                    existingUserAuthInfo.IsSha3Hash = true; // Set IsSha3Hash to true for updated entries
                    existingUserAuthInfo.ResfreshToken = hashedRefreshTokenSha3; // Update the token to SHA3-256
                                                                                 // Add any other fields that need updating
                }
                else
                {
                    // Add new entry with SHA3-256 hashed token
                    userAuthInfo.ResfreshToken = hashedRefreshTokenSha3;
                    userAuthInfo.IsSha3Hash = true; // Set IsSha3Hash to true for new entries
                    monitorContext.UserAuthInfos.Add(userAuthInfo);
                }
                await monitorContext.SaveChangesAsync();
            }

            return new ResultObj
            {
                Success = true,
                Message = $"Success: Updated UserAuthInfos with UserID {userAuthInfo.UserID}. Hashed RefreshToken with SHA3-256"
            };
        }
        catch (Exception ex)
        {
            return new ResultObj
            {
                Success = false,
                Message = $"Error: Occurred while adding UserAuthInfo: {ex.Message}"
            };
        }
    }
    private async Task<ResultObj> UpgradeActivatedTestUser(UserInfo user, MonitorContext monitorContext)
    {
        var result = new ResultObj();
        string userId = user.UserID;

        if (!await monitorContext.ProcessorObjs.AnyAsync(w => w.Owner == userId && w.IsEnabled))
        {
            result.Message = " Info : User is not present in ProcessorObjs table (they have not authorised an agent yet) or agent is disabled.";
            result.Success = false;
            return result;
        }

        var userInfo = await monitorContext.UserInfos.Where(w => w.UserID == userId).FirstOrDefaultAsync();
        if (userInfo == null)
        {
            result.Message = $" Warning : User with ID {userId} is not present in DataBase .";
            result.Success = false;
            return result;
        }

        if (userInfo.AccountType == "Free")
        {
            userInfo.AccountType = "Standard";
            userInfo.CancelAt = DateTime.UtcNow.AddMonths(3);
            userInfo.HostLimit = 50;
            ResetTokenForUser(user);
            await monitorContext.SaveChangesAsync();
            UpdateCachedUserInfo(userInfo);
            result.Message = " Success : User account has been upgraded from Free to Standard.";
            result.Success = true;
            var alertMessage = new AlertMessage();
            alertMessage.UserInfo = user;
            alertMessage.SendTrustPilot = true;
            alertMessage.Subject = "Complimentary Plan Upgrade";
            alertMessage.Message = "You have received a complimentary account upgrade in appreciation of your valuable feedback and participation in the beta phase of the Free Network Monitor Agent. This upgrade is our way of saying thank you for helping us improve the Free Network Monitor Agent app. Your insights are instrumental in ensuring the highest quality and performance of our network monitoring solutions. Please send all feedback to support@freenetworkmonitor.click.";
            await _rabbitRepo.PublishAsync<AlertMessage>("alertMessage", alertMessage);

            return result;
        }
        else
        {
            result.Message = $" Success : User already has an upgraded account.";
            result.Success = false;
            return result;
        }

    }
    public async Task<ResultObj> AddUser(UserInfo user)
    {
        ResultObj result = new ResultObj();
        result.Message = "SERVICE : MonitorPingService.AddUser" + " : ";
        result.Success = false;
        if (user.UserID == null)
        {
            result.Message += "Failed : UserID is null.";
            result.Success = false;
            return result;
        }
        /*try
        {
            await MigrateUser(user);
        }
        catch (Exception e)
        {
            result.Message += "Error :  Failed to migrate user: Error was : " + e.Message + " ";
            result.Success = false;
            result.Data = null;
            _logger.LogError("Error :  Failed to migrate : Error was : " + e.Message + " Inner Exception : " + e.Message.ToString());
            return result;
        }*/

        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();


                if (await monitorContext.UserInfos.Where(u => u.UserID == user.Sub).CountAsync() == 0)
                {

                    AlertMessage alertMessage;
                    user.UserID = user.Sub;
                    user.HostLimit = _systemParamsHelper.GetPingParams().HostLimit;
                    if (user.DateCreated == DateTime.MinValue) { user.DateCreated = DateTime.UtcNow; }
                    if (user.Updated_at == DateTime.MinValue) { user.Updated_at = DateTime.UtcNow; }
                    user.LastLoginDate = DateTime.UtcNow;
                    user.MonitorIPs = new List<MonitorIP>();
                    ResetTokenForUser(user);
                    await monitorContext.UserInfos.AddAsync(user);
                    CachedUsers.Add(user);

                    alertMessage = new AlertMessage();
                    alertMessage.UserInfo = user;
                    alertMessage.SendTrustPilot = true;
                    alertMessage.Subject = "Verify your email";
                    alertMessage.Message = "You have created an account at Free Network Monitor. In order to save hosts you need to verify your email address. Be sure to add this address to your contacts or whitelist and mark it as not spam .";
                    alertMessage.VerifyLink = true;
                    await _rabbitRepo.PublishAsync<AlertMessage>("alertMessage", alertMessage);

                    await monitorContext.SaveChangesAsync();
                    UserInfo? saveUser = monitorContext.UserInfos.FirstOrDefault(u => u.UserID == user.Sub);
                    if (saveUser != null)
                    {
                        // change the user from default to the new user when emails match.
                        var moveMonitorIPs = await monitorContext.MonitorIPs.Where(w => w.UserInfoUserID == "default" && w.AddUserEmail == saveUser.Email).Include(i => i.UserInfo).ToListAsync();
                        moveMonitorIPs.ForEach(f =>
                        {
                            f.UserID = saveUser.UserID;
                            f.UserInfoUserID = saveUser.UserID;
                        });
                    }
                    await monitorContext.SaveChangesAsync();
                    result.Message += "Success : New User Added ";
                    result.Data = user;
                    //var resultMessage = await AddHostWithUser(user);
                    //result.Message += resultMessage.Message;
                    // Send a messsage to admin new user
                    alertMessage = new AlertMessage();
                    alertMessage.UserInfo = user;
                    string copyEmail = user.Email ?? "Missing";
                    // Send the new user message to system email address 
                    user.Email = _systemParamsHelper.GetSystemParams().SystemEmail;
                    alertMessage.SendTrustPilot = true;
                    alertMessage.Subject = "New user for FreeNetworkMonitor";
                    alertMessage.Message = "A new user has logged into Free Network Monitor: email address " + copyEmail + " user id " + user.UserID + " nickname " + user.Nickname + " name " + user.Name;
                    alertMessage.VerifyLink = false;
                    await _rabbitRepo.PublishAsync<AlertMessage>("alertMessage", alertMessage);
                    user.Email = copyEmail;
                }
                else
                {
                    UserInfo? saveUser = await monitorContext.UserInfos.FirstOrDefaultAsync(u => u.UserID == user.Sub);
                    if (saveUser != null)
                    {
                        List<MonitorIP> updateMonitorIPs;
                        if (saveUser.CancelAt < DateTime.UtcNow)
                        {
                            saveUser.HostLimit = _systemParamsHelper.GetPingParams().HostLimit;
                            saveUser.AccountType = "Free";
                            updateMonitorIPs = await monitorContext.MonitorIPs.Where(w => w.UserID == user.UserID && w.Enabled == true).Include(i => i.UserInfo).ToListAsync();
                            if (updateMonitorIPs.Count > _systemParamsHelper.GetPingParams().HostLimit)
                            {
                                updateMonitorIPs.RemoveRange(0, _systemParamsHelper.GetPingParams().HostLimit);
                                updateMonitorIPs.ForEach(fo => fo.Enabled = false);
                            }
                            //await monitorContext.SaveChangesAsync();
                            saveUser.CustomerId = "";
                            saveUser.CancelAt = null;
                            saveUser.Updated_at = DateTime.UtcNow;
                        }
                        if (user.Email_verified) saveUser.Email_verified = true;
                        // Only update if fields are empty , this prevents old token cookie data from overwritting fields call updateUserApi to update these.
                        if (saveUser.Picture.IsNullOrEmpty() && !user.Picture.IsNullOrEmpty()) saveUser.Picture = user.Picture;
                        if (saveUser.Name.IsNullOrEmpty() && !user.Name.IsNullOrEmpty()) saveUser.Name = user.Name;
                        if (saveUser.Given_name.IsNullOrEmpty() && !user.Given_name.IsNullOrEmpty()) saveUser.Given_name = user.Given_name;
                        if (saveUser.Family_name.IsNullOrEmpty() && !user.Family_name.IsNullOrEmpty()) saveUser.Family_name = user.Family_name;
                        saveUser.LastLoginDate = DateTime.UtcNow;
                        saveUser.MonitorIPs = new List<MonitorIP>();
                        await monitorContext.SaveChangesAsync();
                        UpdateCachedUserInfo(saveUser);
                        // change the user from default to the new user when emails match.
                        var moveMonitorIPs = await monitorContext.MonitorIPs.Where(w => w.UserInfoUserID == "default" && w.AddUserEmail == saveUser.Email).Include(i => i.UserInfo).ToListAsync();
                        moveMonitorIPs.ForEach(f =>
                        {
                            f.UserID = saveUser.UserID;
                            f.UserInfoUserID = saveUser.UserID;
                        });
                        await monitorContext.SaveChangesAsync();
                        result.Message += "Info : Updated user login time";
                        result.Data = saveUser;
                        user = saveUser;
                    }
                }

                result.Success = true;
                try
                {
                    if (user.UserID != null)
                    {
                        var upgradeResult = await UpgradeActivatedTestUser(user, monitorContext);
                        result.Message += upgradeResult.Message;
                    }
                }
                catch (Exception e)
                {
                    result.Message += "Error :  Failed to upgrade activated test user: Error was : " + e.Message + " ";
                    result.Success = false;
                    _logger.LogError("Error :  Failed to upgrade activated test user : Error was : " + e.Message + " Inner Exception : " + e.Message.ToString());

                }
                try
                {
                    user.MonitorIPs = new List<MonitorIP>();
                    await _rabbitRepo.PublishAsync<UserInfo>("updateUserInfoAlertMessage", user);
                    result.Message += " Info : Published updateUserInfoAlertMessage message";
                }
                catch (Exception e)
                {
                    result.Message += " Error :  Failed to publish updateUserInfoAlertMessage message : Error was : " + e.Message + " ";
                    result.Success = false;
                }
                try
                {
                    await _rabbitRepo.PublishAsync<RegisteredUser>("registerUser", new RegisteredUser() { UserId = user.UserID!, ExternalUrl = _systemParamsHelper.GetSystemParams().ThisSystemUrl.ExternalUrl, UserEmail = user.Email! });
                    result.Message += " Info : Published registerUser message";
                }
                catch (Exception e)
                {
                    result.Message += " Error :  Failed to publish registerUser message : Error was : " + e.Message + " ";
                    result.Success = false;
                }
                try
                {
                    if (user.UserID != null && _processorState.HasUserGotProcessor(user.UserID))
                    {
                        foreach (var processor in _processorState.UserProcessorListAll(user.UserID))
                        {
                            var processorUserEventObj = new ProcessorUserEventObj();
                            processorUserEventObj.IsLoggedInWebsite = true;
                            await _rabbitRepo.PublishAsync<ProcessorUserEventObj>("processorUserEvent" + processor.AppID, processorUserEventObj);
                            result.Message += $" Info : Published processorUserEvent message for procesor AppID {processor.AppID}";

                        }
                    }
                }
                catch (Exception e)
                {
                    result.Message += " Error :  Failed to publish processorUserEvent message : Error was : " + e.Message + " ";
                    result.Success = false;
                }
            }
        }
        catch (Exception e)
        {
            result.Message += "Error :  Failed to add User : Error was : " + e.Message + " ";
            result.Success = false;
            result.Data = null;
            _logger.LogError("Error :  Failed to add User : Error was : " + e.Message + " Inner Exception : " + e.Message.ToString());
        }
        finally
        {
        }
        return result;
    }
    public async Task<ResultObj> UpdateApiUser(UserInfo user)
    {
        ResultObj result = new ResultObj();
        result.Message = "SERVICE : Update User : ";
        result.Success = false;
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                UserInfo? dbUser = await monitorContext.UserInfos.FirstOrDefaultAsync(u => u.UserID == user.Sub);
                if (dbUser != null)
                {
                    dbUser.Name = user.Name;
                    dbUser.Picture = user.Picture;
                    dbUser.DisableEmail = user.DisableEmail;
                    monitorContext.SaveChanges();
                    UpdateCachedUserInfo(dbUser);
                    await _rabbitRepo.PublishAsync<UserInfo>("updateUserInfoAlertMessage", user);
                    result.Message += "Success : User updated";
                    result.Data = null;
                    result.Success = true;
                }
                else
                {
                    result.Message += "Error : Error updateing user " + user.UserID;
                    result.Data = null;
                    result.Success = false;
                }
            }
        }
        catch (Exception e)
        {
            result.Message += "Error :  Failed to  update user : Error was : " + e.Message + " ";
            result.Success = false;
            _logger.LogError("Error :  Failed to update event : Error was : " + e.Message + " Inner Exception : " + e.Message.ToString());
            result.Data = null;
        }
        return result;
    }
    public async Task<TResultObj<string>> UpdateUserSubscription(UserInfo user)
    {
        var result = new TResultObj<string>();
        result.Message = "SERVICE : Update User Subscription : ";
        result.Success = false;
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                var dbUser = await monitorContext.UserInfos.FirstOrDefaultAsync(u => u.CustomerId == user.CustomerId);
                if (dbUser != null)
                {
                    dbUser.AccountType = user.AccountType;
                    dbUser.HostLimit = user.HostLimit;
                    dbUser.CancelAt = user.CancelAt;
                    dbUser.CustomerId = user.CustomerId;
                    await monitorContext.SaveChangesAsync();
                    UpdateCachedUserInfo(dbUser);
                    await _rabbitRepo.PublishAsync<UserInfo>("updateUserInfoAlertMessage", dbUser);
                    result.Message += "Success : User Subcription updated to " + user.AccountType + " . ";
                    result.Data = "";
                    result.Success = true;
                }
                else
                {
                    result.Message += "Warning : Can't find CustomerID " + user.CustomerId + " . ";
                    result.Data = " Customer not present on this server : " + _systemParamsHelper.GetSystemParams().ThisSystemUrl.ExternalUrl + " . ";
                    result.Success = false;
                }
            }
        }
        catch (Exception e)
        {
            result.Message += "Error :  Failed to  update user : Error was : " + e.Message + " ";
            result.Success = false;
            _logger.LogError("Error :  Failed to update event : Error was : " + e.ToString());
            result.Data = null;
        }
        return result;
    }
    public async Task<TResultObj<string>> UpdateUserCustomerId(UserInfo user)
    {
        var result = new TResultObj<string>();
        result.Message = " SERVICE : Create User CustomerId : ";
        result.Success = false;
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                UserInfo? dbUser = await monitorContext.UserInfos.FirstOrDefaultAsync(u => u.UserID == user.UserID);
                if (dbUser != null)
                {
                    dbUser.CustomerId = user.CustomerId;
                    dbUser.Updated_at = DateTime.UtcNow;
                    monitorContext.SaveChanges();
                    UpdateCachedUserInfo(dbUser);
                    //_rabbitRepo.Publish<UserInfo>("updateUserInfoAlertMessage", dbUser);
                    result.Message += " Success : userId " + dbUser.UserID + " customerId updated to " + user.CustomerId + " . ";
                    result.Data = "";
                    result.Success = true;
                }
                else
                {
                    result.Message += " Warning no user with UserID '" + user.UserID + "' found";
                    result.Data = " User Not Present on this server : " + _systemParamsHelper.GetSystemParams().ThisSystemUrl.ExternalUrl;
                    result.Success = false;
                }
            }
        }
        catch (Exception e)
        {
            result.Message += "Error :  creating user Subcription : Error was : " + e.Message + " ";
            result.Success = false;
            _logger.LogError("Error :  creating user Subcription : Error was : " + e.ToString());
            result.Data = null;
        }
        return result;
    }

    public async Task<ResultObj> SetUserDisabled(string userId)
    {
        ResultObj result = new ResultObj();
        result.Message = "SERVICE : MonitorPingService.SetUserDisabled : ";
        result.Success = false;
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                var userInfo = monitorContext.UserInfos.FirstOrDefault(u => u.UserID == userId);
                if (userInfo != null)
                {
                    userInfo.Enabled = false;
                    userInfo.Updated_at = DateTime.UtcNow;
                    await monitorContext.SaveChangesAsync();
                    await _rabbitRepo.PublishAsync<UserInfo>("updateUserInfoAlertMessage", userInfo);
                    List<MonitorIP> monitorIPs = await monitorContext.MonitorIPs.Where(m => m.UserID == userId).ToListAsync();
                    foreach (MonitorIP monitorIP in monitorIPs)
                    {
                        monitorIP.Hidden = true;
                    }
                    await monitorContext.SaveChangesAsync();
                    UpdateCachedUserInfo(userInfo);
                    result.Success = true;
                    result.Message += "Success : User " + userId + " disabled";
                }
                else
                {
                    result.Success = false;
                    result.Message += "Error : user does not exist in database.";
                }
            }
        }
        catch (Exception e)
        {
            result.Data = null;
            result.Message += "Error : DB Update Failed in MonitorPinService. : Error was : " + e.Message + " for user " + userId;
            result.Success = false;
            _logger.LogError("Error : DB Update Failed in MonitorPinService. : Error was : " + e.Message + " for user " + userId + " Inner Exception : " + e.Message.ToString());
        }
        return result;
    }
    public async Task<ResultObj> Subscription(string email, string userId, bool subscribe)
    {
        ResultObj result = new ResultObj();
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                MonitorContext context = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                var userInfo = await context.UserInfos.FirstOrDefaultAsync(u => u.UserID == userId && u.UserID != "default");
                List<MonitorIP> monitorIPs = await context.MonitorIPs.Where(m => m.AddUserEmail == email).ToListAsync();
                if (userInfo != null)
                {
                    userInfo.DisableEmail = !subscribe;
                    userInfo.Updated_at = DateTime.UtcNow;
                    await context.SaveChangesAsync();
                    UpdateCachedUserInfo(userInfo);
                    await _rabbitRepo.PublishAsync<UserInfo>("updateUserInfoAlertMessage", userInfo);
                    result.Success = true;
                    if (!subscribe)
                    {
                        result.Message += " You have successfully unsubscribed  " + email + " from receiving alerts created for User " + userInfo.UserID + " ";
                    }
                    else
                    {
                        result.Message += " You have successfully re-subscribed   " + email + " to receive alerts created for User " + userInfo.UserID + " ";
                    }
                }
                if (monitorIPs != null)
                {
                    foreach (MonitorIP monitorIP in monitorIPs)
                    {
                        monitorIP.IsEmailVerified = subscribe;
                    }
                    await context.SaveChangesAsync();
                    if (!subscribe)
                    {
                        result.Message += " You have successfully unsubscribed  " + email + " from receiving alerts created from Api ";
                    }
                    else
                    {
                        result.Message += " You have successfully re-subscribed   " + email + " to receive alerts created from Api ";
                    }
                    result.Success = true;
                }
                if (userInfo == null && monitorIPs == null)
                {
                    result.Success = false;
                    result.Message += "There are no longer any records of either your user or email. Login to https://freenetworkmonitor.click to create a user or use ChatGpt Network Monitor Plugin to add hosts.";
                }
            }
            _logger.LogInformation(result.Message);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = "Service : Error updating " + email + " email address subcription : Error Was : " + ex.Message;
            _logger.LogError(result.Message);
        }
        return result;
    }

    public async Task<ResultObj> VerifyEmail(string email, string userId)
    {
        ResultObj result = new ResultObj();
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                MonitorContext context = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                var userInfo = await context.UserInfos.FirstOrDefaultAsync(u => u.UserID == userId);
                var monitorIPs = await context.MonitorIPs.Where(m => m.AddUserEmail == email).ToListAsync();
                if (userInfo != null)
                {
                    userInfo.Email_verified = true;
                    userInfo.Updated_at = DateTime.UtcNow;
                    await context.SaveChangesAsync();
                    UpdateCachedUserInfo(userInfo);
                    await _rabbitRepo.PublishAsync<UserInfo>("updateUserInfoAlertMessage", userInfo);
                    result.Success = true;
                    result.Message += " You have successfully verified subscribed user email address " + email;
                }
                if (monitorIPs != null)
                {
                    monitorIPs.ForEach(f => f.IsEmailVerified = true);
                    await context.SaveChangesAsync();
                    result.Success = true;
                    result.Message += " You have successfully verified Api registered email address  " + email;
                }
                if (!result.Success)
                {
                    result.Message += "Failed to verify  " + email + " user or email does not exist in database";
                }
            }
            _logger.LogInformation(result.Message);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = "Service : Error verifing " + email + " : Error Was : " + ex.Message;
            _logger.LogError(result.Message);
        }
        return result;
    }
    public async Task<ResultObj> UpgradeAccounts()
    {
        var result = new ResultObj();
        result.Message = "SERVICE : MonitorPingService.UpgradeAccounts : ";
        result.Success = false;
        var uri = _systemParamsHelper.GetSystemParams().ThisSystemUrl.ExternalUrl;

        var emailList = new List<GenericEmailObj>();
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                var userEmails = await monitorContext.EmailInfos.Where(w => w.EmailType == "UserHostExpire").ToListAsync();

                var users = await monitorContext.UserInfos.Where(u => u.AccountType == "Free" && !u.DisableEmail).ToListAsync();
                foreach (var user in users)
                {
                    if (userEmails.Where(w => w.Email == user.Email).FirstOrDefault() != null)
                    {
                        user.AccountType = "Standard";
                        user.CancelAt = DateTime.UtcNow.AddMonths(6);
                        user.HostLimit = 50;
                        var emailInfo = new EmailInfo() { Email = user.Email!, EmailType = "UserUpgrade" };
                        monitorContext.EmailInfos.Add(emailInfo);
                        emailList.Add(new GenericEmailObj() { UserInfo = user, HeaderImageUri = uri, ID = emailInfo.ID });
                    }

                }
                await monitorContext.SaveChangesAsync();
                users.ForEach(f => UpdateCachedUserInfo(f));

            }
            result.Success = true;
            result.Message += $"Success : {emailList.Count} Accounts upgraded. ";
        }
        catch (Exception e)
        {
            result.Data = null;
            result.Message += "Error : to upgrade accounts  : Error was : " + e.Message;
            result.Success = false;
            _logger.LogError("Error : to upgrade accounts : Error was : " + e.ToString());
        }
        if (result.Success)
        {
            try
            {
                await _rabbitRepo.PublishAsync<List<GenericEmailObj>>("userUpgrade", emailList);
                result.Message += " Success : published event userUpgrade";
            }
            catch (Exception e)
            {
                result.Message += "Error : publish event userUpgrade : Error was : " + e.Message;
                result.Success = false;
                _logger.LogError("Error : publish event userUpgrade  : Error was : " + e.ToString());

            }

        }

        return result;
    }

    public async Task<ResultObj> LogEmailOpen(Guid id)
    {
        ResultObj result = new ResultObj();
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                MonitorContext context = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                var emailInfo = await context.EmailInfos.Where(w => w.ID == id).FirstOrDefaultAsync();
                if (emailInfo != null)
                {
                    emailInfo.IsOpen = true;
                    emailInfo.DateOpened = DateTime.UtcNow;
                    await context.SaveChangesAsync();
                    result.Success = true;
                    result.Message += " Success : Updated email with Guid " + id;
                    var userInfos = await context.UserInfos.Where(w => w.Email == emailInfo.Email).ToListAsync();
                    if (userInfos.Count != 0)
                    {
                        userInfos.ForEach(f => f.Email_verified = true);
                    }
                    if (userInfos.Count > 1) _logger.LogWarning($" Warning : While updating EmailInfos found more than one email with address {emailInfo.Email} in UserInfos . ");
                    await context.SaveChangesAsync();

                    result.Message += $" Success : Set Email verified for Email {emailInfo.Email} .";
                    _logger.LogInformation(result.Message);
                }

                else
                {
                    result.Success = false;
                    result.Message = " Error : Failed to find email with Guid  " + id + "  email does not exist in database";
                    _logger.LogError(result.Message);
                }
            }

        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = "Service : Error updating email with Guid " + id + " : Error Was : " + ex.Message;
            _logger.LogError(result.Message);
        }
        return result;
    }

}