using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Data;
using NetworkMonitor.Data.Repo;
using NetworkMonitor.Data.Services;
using NetworkMonitor.DTOs;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
namespace NetworkMonitor.Utils.Helpers
{
    public class PingInfoHelper
    {
        private Dictionary<ushort, string> _statusLookup;
        private Dictionary<string, ushort> _reverseStatusLookup; // Reverse dictionary
        private readonly MonitorContext _monitorContext;
        private int _targetPingInfoCount;

        public PingInfoHelper(MonitorContext monitorContext, int targetPingInfoCount=20)
        {
            _monitorContext = monitorContext;
            _targetPingInfoCount=targetPingInfoCount;
            _statusLookup = new Dictionary<ushort, string>();
            _reverseStatusLookup = new Dictionary<string, ushort>();
        }
        public async Task SetStatusList()
        {
            _statusLookup = await _monitorContext.StatusList.AsNoTracking().ToDictionaryAsync(s => s.ID, s => s.Status ?? "");
            _reverseStatusLookup = _statusLookup.ToDictionary(kvp => kvp.Value, kvp => kvp.Key); // Populate the reverse dictionary
        }
        public List<PingInfoDTO> MapPingInfosToDTO(List<PingInfo> pingInfos)
        {
            List<PingInfoDTO> pingInfosDTO = pingInfos.Select(p => new PingInfoDTO
            {
                ResponseTime = p.RoundTripTimeInt,
                DateSent = p.DateSent,
                Status = p.Status
            }).ToList();
            return pingInfosDTO;
        }
        private void AddStatusToList(int intCount, PingInfo f, List<StatusItem> addStatusList, ILogger logger)
        {
            ushort count;
            if (intCount >= ushort.MaxValue)
            {
                logger.LogCritical("Fatal : StatusList table is full can not process PingInfo in PingInfoHelper.UpdatePingInfosFromStatusList");
                f.StatusID = ushort.MaxValue;
                f.Status = "StatusList table is full";
                // Ensure the status with ID of ushort.MaxValue exists in the dictionary
                if (!_statusLookup.ContainsKey(ushort.MaxValue))
                {
                    _statusLookup[ushort.MaxValue] = "StatusList table is full";
                    _reverseStatusLookup["StatusList table is full"] = ushort.MaxValue;
                    addStatusList.Add(new StatusItem { ID = ushort.MaxValue, Status = "StatusList table is full" });
                }
            }
            else
            {
                count = (ushort)intCount;
                var newStatusItem = new StatusItem() { Status = f.Status, ID = count };
                _statusLookup[f.StatusID] = f.Status!;
                _reverseStatusLookup[f.Status!] = f.StatusID;
                addStatusList.Add(newStatusItem);
                f.StatusID = count;
            }
        }
        private async Task<ResultObj> UpdatePingInfosFromStatusList(List<PingInfo> pingInfos, ILogger logger, bool isLookStatusID)
        {
            var result = new ResultObj();
            result.Message = " SERVICE : UpdatePingInfosFromStatusList ";
            try
            {

                var addStatusList = new List<StatusItem>();
                int intCount = 0;
                if (_statusLookup?.Any() == true)
                {
                    intCount = _statusLookup.Keys.Max();
                }

                foreach (var f in pingInfos)
                {
                    try
                    {
                        if (isLookStatusID)
                        {
                            if (_statusLookup?.ContainsKey(f.StatusID) == true)
                            {
                                f.Status = _statusLookup[f.StatusID];
                            }
                            else
                            {
                                intCount++;
                                AddStatusToList(intCount, f, addStatusList, logger);
                            }
                        }
                        else
                        {
                            f.Status ??= ""; // Ensure f.Status is not null
                            if (_reverseStatusLookup?.TryGetValue(f.Status, out ushort key) == true)
                            {
                                f.StatusID = key;
                            }
                            else
                            {
                                intCount++;
                                AddStatusToList(intCount, f, addStatusList, logger);
                            }
                        }
                        result.Success = true;
                    }
                    catch (Exception ex)
                    {
                        result.Message += $"Error processing PingInfo {f.ID} with StatusID {f.StatusID}: {ex.Message}";
                        result.Success = false;
                        f.StatusID = 0;
                    }
                }

                if (result.Success)
                {
                    if (_monitorContext != null)
                    {
                        _monitorContext.StatusList.AddRange(addStatusList);
                        await _monitorContext.SaveChangesAsync();
                    }

                }
            }
            catch (DbUpdateException ex)
            {
                result.Success = false;
                result.Message += $"Database update error: {ex.Message}";
            }
            catch (InvalidOperationException ex)
            {
                result.Success = false;
                result.Message += $"Invalid operation: {ex.Message}";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message += $"Unexpected error: {ex.Message}";
            }
            return result;
        }

        private async Task<List<PingInfo>> GetPingInfosWithStatus(int monitorPingInfoID, DateTime startDate, DateTime endDate)
        {
            var startInt = MapDateToInt(startDate);
            var endInt = MapDateToInt(endDate);
            var pingInfos = await _monitorContext.PingInfos.AsNoTracking()
              .Where(p => p.MonitorPingInfoID == monitorPingInfoID && p.DateSentInt >= startInt && p.DateSentInt <= endInt)
              .ToListAsync();
            return MapPingInfoStatuses(pingInfos);
        }
        private async Task<List<PingInfo>> GetPingInfosWithStatus(int monitorPingInfoID, DateTime? startDate = null, DateTime? endDate = null)
        {
            var pingInfos = await _monitorContext.PingInfos.AsNoTracking()
              .Where(p => p.MonitorPingInfoID == monitorPingInfoID)
              .ToListAsync();
            // Filter by StartDate and EndDate if provided
            if (startDate.HasValue)
            {
                pingInfos = pingInfos.Where(p => p.DateSent >= startDate.Value).ToList();
            }
            if (endDate.HasValue)
            {
                pingInfos = pingInfos.Where(p => p.DateSent <= endDate.Value).ToList();
            }
            return MapPingInfoStatuses(pingInfos);
        }

        private async Task<List<PingInfo>> GetPingInfosWithStatus(int monitorPingInfoID)
        {
            var pingInfos = await _monitorContext.PingInfos.AsNoTracking()
              .Where(p => p.MonitorPingInfoID == monitorPingInfoID)
              .ToListAsync();
            return MapPingInfoStatuses(pingInfos);
        }
        public List<PingInfo> MapPingInfoStatuses(List<PingInfo> pingInfos)
        {
            foreach (var pingInfo in pingInfos)
            {
                try { if (pingInfo.StatusID == 0) pingInfo.Status = "Null";
                else pingInfo.Status = _statusLookup[pingInfo.StatusID];}catch {
                    pingInfo.Status="N/A";
                }
               
                if (pingInfo.RoundTripTime == UInt16.MaxValue) pingInfo.RoundTripTimeInt = -1;
                else pingInfo.RoundTripTimeInt = (int)pingInfo.RoundTripTime!;
            }
            return pingInfos;
        }
        public uint MapDateToInt(DateTime date)
        {
            DateTime epoch = new DateTime(2022, 1, 1);
            TimeSpan span = date.Subtract(epoch);
            return (uint)span.TotalSeconds;
        }
        public async Task<List<PingInfoDTO>> ProcessPingInfoDTOs(int monitorPingInfoID, DateTime startDate, DateTime endDate)
        {
            await SetStatusList();
            var pingInfos = await GetPingInfosWithStatus(monitorPingInfoID, startDate, endDate);
            return MapPingInfosToDTO(pingInfos);
        }


        public async Task<List<PingInfoDTO>> ProcessPingInfoDTOs(int monitorPingInfoID, DateTime? startDate = null, DateTime? endDate = null)
        {
            await SetStatusList();
            var pingInfos = await GetPingInfosWithStatus(monitorPingInfoID, startDate, endDate);
            return MapPingInfosToDTO(pingInfos);
        }
        public async Task<List<PingInfo>> ProcessPingInfos(int monitorPingInfoID, DateTime? startDate, DateTime? endDate)
        {
            await SetStatusList();
            var pingInfos = await GetPingInfosWithStatus(monitorPingInfoID, startDate, endDate);
            return pingInfos;
        }
        public async Task<List<PingInfo>> ProcessPingInfos(int monitorPingInfoID)
        {
            await SetStatusList();
            var pingInfos = await GetPingInfosWithStatus(monitorPingInfoID);
            return pingInfos;
        }

        public async Task<List<PingInfo>> ProcessPingInfos(List<PingInfo> pingInfos)
        {
            await SetStatusList();
            return MapPingInfoStatuses(pingInfos);
        }
        public async Task<ResultObj> UpdateStatusAndPingInfos(List<PingInfo> pingInfos, ILogger logger, bool isLookStatusID)
        {
            await SetStatusList();
            return await UpdatePingInfosFromStatusList(pingInfos, logger, isLookStatusID);
        }

        public async Task<TResultObj<HostResponseObj>> GetMonitorPingInfoDTOByFilter(TResultObj<HostResponseObj> result, DateRangeQuery query, string authUserId, IDataFileService fileService, IUserRepo userRepo)
        {

            // CHeck User and MonitorPingInfoId are valid checking against MonitorIPs
            if (!ValidateUser.VerifyUserExists(userRepo, query.User!, true, authUserId))
            {
                result.Success = false;
                result.Data = null;
                result.Message += "Error : User " + query.User!.UserID + " is not in database call AddUserApi .";
                return result;
            }

            IQueryable<MonitorPingInfo>? matchingMonitorPingInfosQuery = null;
            var matchingMonitorPingInfos = new List<MonitorPingInfo>();
            if (query.MonitorPingInfoID.HasValue)
            {
                var mpi = await _monitorContext.MonitorPingInfos
                    .FirstOrDefaultAsync(m => m.ID == query.MonitorPingInfoID.Value && m.UserID == query.User!.UserID!);
                if (mpi != null && (mpi.DateStarted > query.EndDate || mpi.DateEnded < query.StartDate))
                {
                    result.Success = false;
                    result.Data = null;
                    result.Message += $"Error : You have selected a date range that does not overlap the MonitorPingInfo with ID {mpi.ID} date range ({mpi.DateStarted} to {mpi.DateEnded}). Either query using the address and date range instead or choose another MonitorPingInfo ID that has an overlapping date range .";
                    return result;
                }
                if (mpi == null)
                {
                    result.Success = false;
                    result.Data = null;
                    result.Message += $"Error : No MonitorPingInfo with ID {query.MonitorPingInfoID} ";
                    return result;
                }
                matchingMonitorPingInfos.Add(mpi);
            }
            else if (!string.IsNullOrEmpty(query.Address))
            {
                matchingMonitorPingInfosQuery = _monitorContext.MonitorPingInfos
                     .Where(m =>
                        EF.Functions.Like(m.Address, $"%{query.Address}%") && m.UserID == query.User!.UserID);


            }
            else if (query.MonitorIPID != null)
            {
                matchingMonitorPingInfosQuery = _monitorContext.MonitorPingInfos
                     .Where(m =>
                       m.MonitorIPID == query.MonitorIPID && m.UserID == query.User!.UserID);
            }
            if (matchingMonitorPingInfosQuery != null)
            {
                if (query.StartDate != null && query.EndDate != null)
                {
                    matchingMonitorPingInfosQuery = matchingMonitorPingInfosQuery.Where(m => m.DateStarted <= query.EndDate && m.DateEnded >= query.StartDate);
                }
                else if (query.StartDate != null)
                {
                    matchingMonitorPingInfosQuery = matchingMonitorPingInfosQuery.Where(m => m.DateEnded >= query.StartDate || m.DateStarted >= query.StartDate);
                }
                else if (query.EndDate != null)
                {
                    matchingMonitorPingInfosQuery = matchingMonitorPingInfosQuery.Where(m => m.DateStarted <= query.EndDate || m.DateEnded <= query.EndDate);
                }

                matchingMonitorPingInfos = await matchingMonitorPingInfosQuery.ToListAsync();


                if (matchingMonitorPingInfos.Count > 1 && !string.IsNullOrEmpty(query.Address))
                {
                    var multiMonitoIPs = _monitorContext.MonitorIPs
                     .Where(m =>
                        EF.Functions.Like(m.Address, $"%{query.Address}%") && m.UserID == query.User!.UserID).ToList();
                    if (multiMonitoIPs != null && multiMonitoIPs.Count > 1)
                    {
                        var idsAndAddresses = multiMonitoIPs
                       .Select(m => $"(MonitoIPID: {m.ID}, Address: {m.Address}, End Point Type: {m.EndPointType}, Port : {m.Port} , AppID : {m.AppID})")
                       .Aggregate((current, next) => current + ", " + next);

                        result.Message += $"Warning: Multiple MonitorIPIDs found for the given address. Found: {idsAndAddresses}. Please call the API again using the corresponding MonitorID.";
                        return result;
                    }

                }



            }

            if (matchingMonitorPingInfos == null || matchingMonitorPingInfos.Count==0)
            {
                result.Success = false;
                result.Message += " Warning : No response data found for the given criteria.";
                return result;
            }

            var pingInfos=new List<PingInfo>();
            foreach (var monitorPingInfo in matchingMonitorPingInfos)
            {
                var tempPingInfos=await _monitorContext.PingInfos
                        .Where(p => p.MonitorPingInfoID == monitorPingInfo.ID).ToListAsync();
                if (monitorPingInfo.IsArchived)
                {
                    var pingInfo = tempPingInfos
                        .FirstOrDefault();
                    if (pingInfo != null)
                    {
                        var aPingInfos = new List<PingInfo>();
                        aPingInfos.Add(pingInfo);
                        await SetStatusList();
                        MapPingInfoStatuses(aPingInfos);
                        result.Success = false;
                        result.Message += $"Error : {aPingInfos[0].Status}";
                        return result;
                    }
                }
                
                 pingInfos.AddRange(tempPingInfos);
            }

          
            var pingInfosDTO = MapPingInfosToDTO(pingInfos);
              var firstMonitorPingInfo=matchingMonitorPingInfos.First();
           
            var hostResponseObj = MonitorPingInfoHelper.CreateNewMonitorPingInfoFromPingInfos(firstMonitorPingInfo, pingInfosDTO);
            hostResponseObj.PingInfosDTO = pingInfosDTO;

            result.DataFileUrl = fileService.SaveDataToFile<HostResponseObj>(hostResponseObj, firstMonitorPingInfo.ID);
            var countOrigPI = pingInfos.Count();
            pingInfos = PingInfoProcessor.ReducePingInfosToTarget(pingInfos, _targetPingInfoCount);
            var countReducedPI = pingInfos.Count();
           pingInfos = MapPingInfoStatuses(pingInfos);
            pingInfosDTO = MapPingInfosToDTO(pingInfos);
            hostResponseObj.PingInfosDTO = pingInfosDTO;
            result.Data = hostResponseObj;
            result.Success = true;
            if (countReducedPI < countOrigPI)
            result.Message += $" Warning : Data has been compressed Displaying {countReducedPI} response times from {countOrigPI}. Reduce time range to less than 20 mins to get full detail. ";
            result.Message += $"Success : Got Response data for Host {firstMonitorPingInfo.Address} with MontiorPingInfoID {firstMonitorPingInfo.ID} . You can download the full data from {result.DataFileUrl} . This link is only valid for one hour . ";
            return result;

        }


        public async Task<TResultObj<HostResponseObj, SentUserData>> GetMonitorPingInfoDTO(TResultObj<HostResponseObj, SentUserData> result, SentUserData sentData, string? authUserId, IDataFileService fileService, IUserRepo userRepo)
        {
            UserInfo user = sentData.User!;
            result.SentData = sentData;
            //int dataSetId = sentData.DataSetId;
            int? monitorPingInfoID = sentData.MonitorPingInfoID;
            if (monitorPingInfoID == null)
            {
                throw new ArgumentException("Invalid Parameter : monitorPingInfoID of type int is required.");
            }
            // CHeck User and MonitorPingInfoId are valid checking against MonitorIPs
            if (!ValidateUser.VerifyUserExists(userRepo, user, true, authUserId))
            {
                result.Success = false;
                result.Data = null;
                result.Message += "Error : User " + user.UserID + " is not in database call AddUserApi .";
                return result;
            }

            MonitorPingInfo? monitorPingInfo;
            monitorPingInfo = await _monitorContext.MonitorPingInfos.Where(m => m.ID == monitorPingInfoID && m.UserID == user.UserID!).FirstOrDefaultAsync();
            if (monitorPingInfo == null)
            {
                result.Success = false;
                result.Data = null;
                if (authUserId == "default")
                    result.Message += "Error : Incorrect MonitorPingInfoID used. Call GetMonitorPingInfosByHostAddressDefault Api endpoint to get correct MonitorPingInfoID. ";
                else result.Message += "Error : Incorrect MonitorPingInfoID used. Call GetMonitorPingInfosByHostAddressAuth Api endpoint to get correct MonitorPingInfoID. ";
                return result;
            }
            //var pingInfoHelper = new PingInfoHelper(_monitorContext);
            var pingInfos = await ProcessPingInfos(monitorPingInfo.ID);
            if (pingInfos.Count() > 0)
            {
                if (pingInfos[0].Status?.Contains("your subscription") == true)
                {
                    result.Success = false;
                    result.Message += $"Error : {pingInfos[0].Status}";
                    return result;
                }
            }
            var pingInfosDTO = MapPingInfosToDTO(pingInfos);
            var hostResponseObj = MonitorPingInfoHelper.CopyAllFieldsFromMonitorPingInfo(monitorPingInfo);
            hostResponseObj.PingInfosDTO = pingInfosDTO;
            result.DataFileUrl = fileService.SaveDataToFile<HostResponseObj>(hostResponseObj, monitorPingInfo.ID);
            var countOrigPI = pingInfos.Count;
            pingInfos = PingInfoProcessor.ReducePingInfosToTarget(pingInfos);
            pingInfos = MapPingInfoStatuses(pingInfos);
            var countReducedPI = pingInfos.Count;
            pingInfosDTO = MapPingInfosToDTO(pingInfos);
            hostResponseObj.PingInfosDTO = pingInfosDTO;

            result.Data = hostResponseObj;
            result.Success = true;
            if (countReducedPI < countOrigPI)
                result.Message += $" Warning : Data has been compressed Displaying {countReducedPI} response times from {countOrigPI} . Reduce time range to less than 20 mins to get full detail.";

            result.Message += $"Success : Got Response data for Host {monitorPingInfo.Address} with MontiorPingInfoID {monitorPingInfo.ID} . You can download the full data from {result.DataFileUrl} . This link is only valid for one hour . ";
            return result;
        }



    }
}
