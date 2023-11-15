using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Data;
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
        public PingInfoHelper(MonitorContext monitorContext)
        {
            _monitorContext = monitorContext;
            _statusLookup = new Dictionary<ushort, string>();
            _reverseStatusLookup = new Dictionary<string, ushort>();
        }
        public async Task SetStatusList()
        {
            _statusLookup = await _monitorContext.StatusList.AsNoTracking().ToDictionaryAsync(s => s.ID, s => s.Status);
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
                _statusLookup[f.StatusID] = f.Status;
                _reverseStatusLookup[f.Status] = f.StatusID;
                addStatusList.Add(newStatusItem);
                f.StatusID = count;
            }
        }
        private async Task<ResultObj> UpdatePingInfosFromStatusList(List<PingInfo> pingInfos, ILogger logger, bool isLookStatusID)
        {
             var result = new ResultObj();
                result.Message=" SERVICE : UpdatePingInfosFromStatusList ";
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
                        result.Message+=$"Error processing PingInfo {f.ID} with StatusID {f.StatusID}: {ex.Message}";
                        result.Success = false;
                        f.StatusID=0;
                    }
                }

                if (result.Success)
                {
                    _monitorContext?.StatusList.AddRange(addStatusList);
                    await _monitorContext?.SaveChangesAsync();
                }
            }
            catch (DbUpdateException ex)
            {
                result.Success = false;
                result.Message+=$"Database update error: {ex.Message}";
            }
            catch (InvalidOperationException ex)
            {
                result.Success = false;
                result.Message+=$"Invalid operation: {ex.Message}";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message+=$"Unexpected error: {ex.Message}";
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
                if (pingInfo.StatusID == 0) pingInfo.Status = "Null";
                else pingInfo.Status = _statusLookup[pingInfo.StatusID];
                if (pingInfo.RoundTripTime == UInt16.MaxValue) pingInfo.RoundTripTimeInt = -1;
                else pingInfo.RoundTripTimeInt = (int)pingInfo.RoundTripTime;
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
    }
}
