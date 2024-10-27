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
using System.IO;
namespace NetworkMonitor.Data.Services
{
    public interface IPingInfoService
    {
        Task FilterPingInfosBasedOnAccountType(bool filterDefaultUser);
        Task<ResultObj> FilterReducePingInfos(int filterTimeMonths, bool filterDefaultUser);
        Task<ResultObj> RestorePingInfosForAllUsers();
        Task<TResultObj<string>> RestorePingInfosForSingleUser(string customerId);

        Task<TResultObj<string>> RestorePingInfosForSingleUser(string userId, string? customerId = null);
        Task<TResultObj<int>> ImportPingInfosFromFile(string filePath);
        Task<ResultObj> ImportMonitorPingInfosFromFile(UserInfo user, int monitorPingInfoID);
    }
    public class PingInfoService : IPingInfoService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private ILogger _logger;
        private IFileRepo _fileRepo;
        public PingInfoService(IServiceScopeFactory scopeFactory, ILogger<PingInfoService> logger, IFileRepo fileRepo)
        {
            _scopeFactory = scopeFactory;
            _fileRepo = fileRepo;
            _logger = logger;
        }
        public async Task<TResultObj<string>> RestorePingInfosForSingleUser(string customerId)
        {
            return await RestorePingInfosForSingleUser("", customerId);
        }
        public async Task<TResultObj<string>> RestorePingInfosForSingleUser(string userId, string? customerId = null)
        {
            var result = new TResultObj<string>();
            result.Message = "SERVICE : PingInfoService.RestorePingInfosForSingleUser() ";
            result.Success = false;
            int successfulImports = 0;
            int unsuccessfulImports = 0;
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    UserInfo? user;
                    if (customerId != null)
                    {
                        user = await monitorContext.UserInfos.AsNoTracking().FirstOrDefaultAsync(u => u.CustomerId == customerId);
                    }
                    else
                    {
                        user = await monitorContext.UserInfos.AsNoTracking().FirstOrDefaultAsync(u => u.UserID == userId);
                    }
                    // Get the user by userId
                    if (user == null)
                    {
                        result.Message += "Error : User not found.";
                        return result;
                    }
                    var userDirectory = $"./data/{user.UserID}";
                    EnsureDirectoryExists(userDirectory); // Ensure user directory exists

                    // Get the threshold date based on the user's account type
                    DateTime thresholdDate = GetThresholdDate(user.AccountType!);
                    int userSuccessfulImports = 0;
                    int userUnsuccessfulImports = 0;
                    int userSkipped = 0;
                    var importedMonitorPingInfoIDs = new List<(int, int)>();
                    // Get the MonitorPingInfos for the user that are newer than the threshold date
                    var userMonitorPingInfos = await monitorContext.MonitorPingInfos.AsNoTracking()
                        .Where(m => m.UserID == user.UserID && m.DateStarted >= thresholdDate && m.IsArchived)
                        .ToListAsync();
                    foreach (var monitorPingInfo in userMonitorPingInfos)
                    {
                        if (monitorPingInfo.IsArchived)
                        {
                            // Construct the file path for each MonitorPingInfo
                            var filePath = $"{userDirectory}/{monitorPingInfo.ID}.br";
                            // Restore PingInfos from the file
                            TResultObj<int> restoreResult = await ImportPingInfosFromFile(filePath);
                            if (restoreResult.Success)
                            {
                                userSuccessfulImports++;
                                successfulImports++;
                                importedMonitorPingInfoIDs.Add((monitorPingInfo.ID, restoreResult.Data));
                            }
                            else
                            {
                                userUnsuccessfulImports++;
                                unsuccessfulImports++;
                            }
                        }
                        else
                        {
                            userSkipped++;
                            successfulImports++;
                        }
                    }
                    _logger.LogInformation($"Restored PingInfos for user {user.UserID}. Successful imports: {userSuccessfulImports}, Unsuccessful imports: {userUnsuccessfulImports}, Skipped : {userSkipped}. Imported MonitorPingInfo IDs: {string.Join(", ", importedMonitorPingInfoIDs.Select(t => $"ID: {t.Item1} - PCount: {t.Item2}"))}");
                    result.Message += $"Info : Restored PingInfos for user {user.UserID}. Total successful imports: {successfulImports}, Total unsuccessful imports: {unsuccessfulImports}.";
                    result.Success = true;
                    _logger.LogInformation(result.Message);
                }
            }
            catch (Exception e)
            {
                result.Message += "Error : Failed to restore PingInfos for user. Error was : " + e.Message + " ";
                result.Success = false;
                _logger.LogError("Error : Failed to restore PingInfos for user. Error was : " + e.Message + " Inner Exception :" + e.InnerException?.Message);
            }
            return result;
        }
        public async Task<ResultObj> RestorePingInfosForAllUsers()
        {
            ResultObj result = new ResultObj();
            result.Message = "SERVICE : PingInfoService.RestorePingInfosForAllUsers() ";
            result.Success = false;
            int successfulImports = 0;
            int unsuccessfulImports = 0;
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    // Get all users
                    var users = await monitorContext.UserInfos.AsNoTracking().ToListAsync();
                    foreach (var user in users)
                    {
                        var userDirectory = $"./data/{user.UserID}";
                        EnsureDirectoryExists(userDirectory); // Ensure user directory exists

                        int userSuccessfulImports = 0;
                        int userUnsuccessfulImports = 0;
                        int userSkipped = 0;
                        var importedMonitorPingInfoIDs = new List<(int, int)>();
                        // For each user, get their MonitorPingInfos
                        var userMonitorPingInfos = await monitorContext.MonitorPingInfos.AsNoTracking()
                            .Where(m => m.UserID == user.UserID)
                            .ToListAsync();
                        foreach (var monitorPingInfo in userMonitorPingInfos)
                        {
                            // Construct the file path for each MonitorPingInfo
                            // Restore PingInfos from the file
                            if (monitorPingInfo.IsArchived)
                            {
                                var filePath = $"{userDirectory}/{monitorPingInfo.ID}.br";
                                TResultObj<int> restoreResult = await ImportPingInfosFromFile(filePath);
                                if (restoreResult.Success)
                                {
                                    userSuccessfulImports++;
                                    successfulImports++;
                                    importedMonitorPingInfoIDs.Add((monitorPingInfo.ID, restoreResult.Data));
                                }
                                else
                                {
                                    userUnsuccessfulImports++;
                                    unsuccessfulImports++;
                                }
                            }
                            else
                            {
                                userSkipped++;
                                successfulImports++;
                            }
                        }
                        _logger.LogInformation($"Restored PingInfos for user {user.UserID}. Successful imports: {userSuccessfulImports}, Unsuccessful imports: {userUnsuccessfulImports}, Skipped : {userSkipped}. Imported MonitorPingInfo IDs: {string.Join(", ", importedMonitorPingInfoIDs.Select(t => $"ID: {t.Item1} - PCount: {t.Item2}"))}");
                    }
                    result.Message += $"Info : Restored PingInfos for all users. Total successful imports: {successfulImports}, Total unsuccessful imports: {unsuccessfulImports}.";
                    result.Success = true;
                    _logger.LogInformation(result.Message);
                }
            }
            catch (Exception e)
            {
                result.Message += "Error : Failed to restore PingInfos for all users. Error was : " + e.Message + " ";
                result.Success = false;
                _logger.LogError("Error : Failed to restore PingInfos for all users. Error was : " + e.Message + " Inner Exception :" + e.InnerException?.Message);
            }
            return result;
        }
        public async Task<TResultObj<int>> ImportPingInfosFromFile(string filePath)
        {
            var result = new TResultObj<int>();
            result.Message = "SERVICE : PingInfoService.ImportPingInfosFromFile ";
            result.Success = false;
            if (!_fileRepo.IsFileExists(filePath))
            {
                //result.Message += " No MonitorPingInfo file : " + filePath;
                result.Success = false;
                result.Data = 0;
                return result;
            }
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    // Load the archived PingInfos from the file
                    var monitorPingInfo = await _fileRepo.GetStateJsonZAsync<MonitorPingInfo>(filePath);
                    if (monitorPingInfo == null)
                    {
                        result.Success = false;
                        result.Data = 0;
                        return result;
                    }
                    var pingInfosFromFile = monitorPingInfo.PingInfos;
                    int pingInfoCount = 0;
                    if (pingInfosFromFile != null && pingInfosFromFile.Any())
                    {
                        var updateMonitorPingInfo = await monitorContext.MonitorPingInfos.FindAsync(monitorPingInfo.ID);
                        if (updateMonitorPingInfo != null && updateMonitorPingInfo.PingInfos.Count <= 1)
                        {
                            pingInfosFromFile.ForEach(f => f.ID = 0);
                            if (updateMonitorPingInfo.PingInfos.Count == 1) updateMonitorPingInfo.PingInfos.RemoveAt(0);
                            updateMonitorPingInfo.IsArchived = false;
                            pingInfoCount = pingInfosFromFile.Count;
                            await monitorContext.PingInfos.AddRangeAsync(pingInfosFromFile);
                            await monitorContext.SaveChangesAsync();
                        }
                    }
                    result.Success = true;
                    result.Data = monitorPingInfo.PingInfos.Count;
                }
            }
            catch (Exception e)
            {
                result.Message += "Error : Failed to import PingInfos from file. Error was : " + e.Message + " ";
                result.Success = false;
                _logger.LogError("Error : Failed to import PingInfos from file. Error was : " + e.Message + " Inner Exception :" + e.InnerException?.Message);
            }
            return result;
        }

        private void EnsureDirectoryExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        public async Task<ResultObj> ImportMonitorPingInfosFromFile(UserInfo user, int monitorPingInfoID)
        {
            ResultObj result = new ResultObj();
            var userDirectory = $"./data/{user.UserID}";
            EnsureDirectoryExists(userDirectory); // Ensure user directory exists
            var filePath = $"{userDirectory}/{monitorPingInfoID}.br";

            result.Message = "SERVICE : PingInfoService.ImportPingInfosFromFile() ";
            result.Success = false;
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    // Load the archived MonitorPingInfo from the file
                    var monitorPingInfo = await _fileRepo.GetStateJsonZAsync<MonitorPingInfo>(filePath);
                    if (monitorPingInfo != null)
                    {
                        // Check if the MonitorPingInfo already exists in the database
                        var existingMonitorPingInfo = await monitorContext.MonitorPingInfos
                            .FirstOrDefaultAsync(m => m.ID == monitorPingInfo.ID);
                        if (existingMonitorPingInfo == null)
                        {
                            // Add the MonitorPingInfo to the database
                            monitorContext.MonitorPingInfos.Add(monitorPingInfo);
                        }
                        else
                        {
                            // Update the existing MonitorPingInfo with the data from the file
                            monitorContext.Entry(existingMonitorPingInfo).CurrentValues.SetValues(monitorPingInfo);
                        }
                        await monitorContext.SaveChangesAsync();
                    }
                    result.Message += "Info : Imported PingInfos from file. ";
                    result.Success = true;
                    _logger.LogInformation(result.Message);
                }
            }
            catch (Exception e)
            {
                result.Message += "Error : Failed to import PingInfos from file. Error was : " + e.Message + " ";
                result.Success = false;
                _logger.LogError("Error : Failed to import PingInfos from file. Error was : " + e.Message + " Inner Exception :" + e.Message.ToString());
            }
            return result;
        }
        public async Task<ResultObj> FilterReducePingInfos(int filterTimeMonths, bool filterDefaultUser)
        {
            ResultObj result = new ResultObj();
            result.Message = "SERVICE : MonitorService.FilterPingInfos() ";
            result.Success = false;
            _logger.LogInformation("Starting FilterReducePingInfos process...");
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    var pageSize = 100;
                    var page = 0;
                    var filterDate = DateTime.Now.AddMonths(-filterTimeMonths);
                    IQueryable<MonitorPingInfo> query = monitorContext.MonitorPingInfos
               .Where(i => i.DateStarted < filterDate && i.PingInfos.Count > 1) // Only select MonitorPingInfos with multiple PingInfos
               .OrderBy(i => i.ID)
               .Include(i => i.PingInfos);
                    query = filterDefaultUser ? query.Where(i => i.UserID == "default") : query.Where(i => i.UserID != "default");
                    var monitorPingInfos = await query.Skip(page * pageSize).Take(pageSize).ToListAsync();
                    while (monitorPingInfos.Any())
                    {
                        foreach (var f in monitorPingInfos)
                        {
                            var countBefore = f.PingInfos.Count;
                            var pingInfos = PingInfoProcessor.CombinePingInfos(f.PingInfos);
                            f.PingInfos.Clear();
                            f.PingInfos.AddRange(pingInfos);
                            var countAfter = f.PingInfos.Count;
                            _logger.LogInformation($"Processed MonitorPingInfo ID: {f.ID}. PingInfos before: {countBefore}, after: {countAfter}.");
                        }
                        await monitorContext.SaveChangesAsync();
                        page++;
                        _logger.LogInformation($"Processed page {page}. Moving to next page...");
                        monitorPingInfos = await query.Skip(page * pageSize).Take(pageSize).ToListAsync();
                    }
                    result.Message += "Info : Filtered PingInfos. ";
                    result.Success = true;
                    _logger.LogInformation(result.Message);
                }
            }
            catch (Exception e)
            {
                result.Message += "Error : Failed to Filter PingInfos. Error was : " + e.Message + " ";
                result.Success = false;
                _logger.LogError("Error : Failed to Filter PingInfos. Error was : " + e.Message + " Inner Exception :" + e.Message.ToString());
            }
            _logger.LogInformation("FilterReducePingInfos process completed.");
            return result;
        }
        private async Task FilterPingInfosForUser(UserInfo user, MonitorContext monitorContext)
        {
            DateTime thresholdDate = GetThresholdDate(user.AccountType!);
            //thresholdDate=DateTime.UtcNow;
            _logger.LogInformation("Filtering user " + user.UserID);
            var userDirectory = $"./data/{user.UserID}";
            EnsureDirectoryExists(userDirectory); // Ensure user directory exists

            const int batchSize = 100; // Adjust this value based on your needs
            int skip = 0;
            bool hasMoreRecords = true;
            while (hasMoreRecords)
            {
                var lastPingInfos = new List<PingInfo>();
                var batchMonitorPingInfos = await monitorContext.MonitorPingInfos.AsNoTracking()
                    .Where(m => m.UserID == user.UserID && m.DateStarted < thresholdDate && !m.IsArchived)
                    .OrderBy(m => m.ID)
                    .Skip(skip)
                    .Take(batchSize)
                    .ToListAsync();
                foreach (var monitorPingInfo in batchMonitorPingInfos)
                {
                    var filePath = $"{userDirectory}/{monitorPingInfo.ID}.br";
                    // Directly delete old PingInfos except the last one
                    var lastPingInfoId = await monitorContext.PingInfos.AsNoTracking()
                        .Where(p => p.MonitorPingInfoID == monitorPingInfo.ID)
                        .OrderByDescending(p => p.DateSentInt)
                        .Select(p => p.ID)
                        .FirstOrDefaultAsync();
                    // Save PingInfos to a compressed file before deleting
                    monitorPingInfo.PingInfos = await monitorContext.PingInfos.AsNoTracking().Where(p => p.MonitorPingInfoID == monitorPingInfo.ID).ToListAsync();
                    if (monitorPingInfo.PingInfos != null && monitorPingInfo.PingInfos.Count > 1)
                    {
                        if (!_fileRepo.IsFileExists(filePath))
                        {
                            _fileRepo.CheckFileExists(filePath, _logger);
                            await _fileRepo.SaveStateJsonZAsync<MonitorPingInfo>(filePath, monitorPingInfo);
                        }
                        await monitorContext.Database.ExecuteSqlInterpolatedAsync(
                            $"DELETE FROM PingInfos WHERE MonitorPingInfoID = {monitorPingInfo.ID} AND ID != {lastPingInfoId}");
                        // Update the last PingInfo's status
                        var lastPingInfo = monitorPingInfo.PingInfos.Where(w => w.ID == lastPingInfoId).FirstOrDefault();
                        if (lastPingInfo != null)
                        {
                            string duration = GetDurationString(user.AccountType!);
                            if (user.UserID == "default")
                            {
                                lastPingInfo.Status = $"This version of Network Monitor is limited to viewing data no older than {duration} . Upgrade your {user.AccountType} plan to view this data. Either install the Auth Network Monitor Plugin, login and upgrade your subscription or visit {App.Constants.FrontendUrl}/subscription, login and upgrade your subscription. You will then be able to view this data. Make sure to login with the same email address you have used to add hosts in this plugin. ";
                            }
                            else
                            {
                                lastPingInfo.Status = $"Your subscription plan limits you viewing data no older than {duration}. Upgrade your {user.AccountType} plan to view this data. Call the api endpoint GetProductsAuth to get details of upgrade options. ";
                            }
                            lastPingInfo.RoundTripTime = UInt16.MaxValue;
                            lastPingInfo.RoundTripTimeInt = -1;
                            lastPingInfos.Add(lastPingInfo);
                        }
                        else
                        {
                            _logger.LogError($" Error : Can't find last PingInfo with ID {lastPingInfoId} ");
                        }
                    }
                }
                await UpdateBatch(monitorContext, lastPingInfos);
                hasMoreRecords = batchMonitorPingInfos.Count == batchSize;
                skip += batchSize;
            }
        }


        private async Task UpdateBatch(MonitorContext monitorContext, List<PingInfo> lastPingInfos)
        {
            await new PingInfoHelper(monitorContext).UpdateStatusAndPingInfos(lastPingInfos, _logger, false);
            // Update the lastPingInfos in the database
            foreach (var pingInfo in lastPingInfos)
            {
                var existingPingInfo = await monitorContext.PingInfos.FindAsync(pingInfo.ID);
                var updateMonitorPingInfo = await monitorContext.MonitorPingInfos.Where(m => m.ID == pingInfo.MonitorPingInfoID).FirstOrDefaultAsync();
                if (existingPingInfo != null)
                {
                    existingPingInfo.Status = pingInfo.Status;
                    existingPingInfo.StatusID = pingInfo.StatusID;
                }
                if (updateMonitorPingInfo != null)
                {
                    updateMonitorPingInfo.IsArchived = true;
                }
            }
            await monitorContext.SaveChangesAsync();
        }
        public async Task FilterPingInfosBasedOnAccountType(bool filterDefaultUser)
        {
            var result = new ResultObj();
            result.Message = "SERVICE : PingInfoFilterService.FilterPingInfosBasedOnAccountType : ";
            result.Success = false;
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    var users = await monitorContext.UserInfos.AsNoTracking().ToListAsync();
                    foreach (var user in users)
                    {
                        await FilterPingInfosForUser(user, monitorContext);
                    }

                }
                result.Success = true;
                result.Message += "Success : Filtered PingInfos based on Account Type. ";
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Message += "Error : Failed to filter PingInfos based on Account Type : Error was : " + e.Message;
                result.Success = false;
                _logger.LogError("Error : DB Update Failed. : Error was : " + e.ToString());
            }
        }
        private string GetDurationString(string accountType)
        {
            switch (accountType)
            {
                case "Free":
                    return "1 month";
                case "Standard":
                    return "6 months";
                case "Standard-Old":
                    return "6 months";
                case "Professional":
                    return "2 years";
                case "Professional-Old":
                    return "2 years";
                case "Enterprise":
                    return "unlimited time"; // or whatever is appropriate for Enterprise
                case "God":
                    return "This is the God account"; 
                default:
                    return "invalid account type";
            }
        }
        private DateTime GetThresholdDate(string accountType)
        {
            switch (accountType)
            {
                case "Free":
                    return DateTime.UtcNow.AddMonths(-1);
                case "Standard":
                    return DateTime.UtcNow.AddMonths(-6);
                case "Standard-Old":
                    return DateTime.UtcNow.AddMonths(-6);
                case "Professional":
                    return DateTime.UtcNow.AddYears(-2);
                case "Professional-Old":
                    return DateTime.UtcNow.AddYears(-2);
                case "Enterprise":
                    return DateTime.MinValue; // No filtering for Enterprise
                case "God":
                    return DateTime.MinValue; // No filtering for God
                default:
                    return DateTime.MinValue; // No filtering if account type not found
            }
        }
    }
}
