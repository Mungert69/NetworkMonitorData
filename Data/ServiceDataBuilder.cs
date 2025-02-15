using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Objects;
using NetworkMonitor.Utils;
using Microsoft.EntityFrameworkCore;

namespace NetworkMonitor.Data
{
    public class ServiceDataBuilder
    {
        public static async Task<ProcessorDataObj?> Merge(byte[] processorDataBytes, MonitorContext monitorContext)
        {
            return await Merge(ProcessorDataBuilder.ExtractFromZ<ProcessorDataObj>(processorDataBytes), monitorContext);
        }
        public static async Task<ProcessorDataObj?> Merge(ProcessorDataObj? processorDataObj, MonitorContext monitorContext)
        {

            if (processorDataObj == null) return null;
            var removePingInfos = new List<RemovePingInfo>();
            var addMonitorPingInfos = new List<MonitorPingInfo>();
            List<int> monitorIPIDs = await monitorContext.MonitorIPs.Where(w => w.AppID == processorDataObj.AppID).Select(s => s.ID).ToListAsync();

            // Fetch necessary data first
            var swapMonitorPingInfoIDs = processorDataObj.SwapMonitorPingInfos?.Select(f => f.ID).ToList() ?? new List<int>();

            if ( swapMonitorPingInfoIDs.Count > 0)
            {
                var existingMonitorPingInfos = (await monitorContext.MonitorPingInfos
  .Where(w => w.DataSetID == 0)
  .ToListAsync()) // Fetch all matching rows into memory
  .Where(w => swapMonitorPingInfoIDs.Contains(w.MonitorIPID)) // Client-side filtering
  .ToList();
                foreach (var f in processorDataObj.SwapMonitorPingInfos!)
                {
                    var m = existingMonitorPingInfos.FirstOrDefault(e => e.MonitorIPID == f.ID);
                    if (m != null)
                    {
                        m.AppID = processorDataObj.AppID;

                    }
                }
                await monitorContext.SaveChangesAsync();

            }
            uint minDateSentInt = uint.MaxValue;
            if (processorDataObj.PingInfos != null && processorDataObj.PingInfos.Count > 0)
            {
                minDateSentInt = processorDataObj.PingInfos.Min(m => m.DateSentInt);
            }

            var monitorPingInfos = await monitorContext.MonitorPingInfos
                .Where(w => w.DataSetID == 0 && w.AppID == processorDataObj.AppID)
                .Include(i => i.MonitorStatus)
                .Include(i => i.PingInfos.Where(w => w.DateSentInt > minDateSentInt))
                .ToListAsync();

            if (monitorPingInfos == null || monitorPingInfos.Count() == 0)
            {
                monitorPingInfos = new List<MonitorPingInfo>();
            }
            var pingInfoComparer = new PingInfoComparer();
            //var monitorIPIDs= origMonitorIPIDs.Except(swapMonitorPingInfoIDs);
            processorDataObj.MonitorPingInfos.Where(w => monitorIPIDs.Contains(w.MonitorIPID)).ToList().ForEach(p =>
                {
                    // Use the MonitorIPID as the key as MonitorPingInfoID needs to change to database given value.
                    var monitorPingInfo = monitorPingInfos.Where(w => w.MonitorIPID == p.MonitorIPID).FirstOrDefault();
                    List<PingInfo> pingInfos = new List<PingInfo>();
                    if (processorDataObj.PingInfos != null) pingInfos = processorDataObj.PingInfos.Where(w => w.MonitorPingInfoID == p.MonitorIPID).ToList();
                    pingInfos.ForEach(pi =>
                       {
                           removePingInfos.Add(new RemovePingInfo() { ID = pi.ID, MonitorPingInfoID = p.MonitorIPID });
                           pi.MonitorPingInfoID = 0;
                           pi.ID = 0;
                       });
                    if (monitorPingInfo != null)
                    {
                        monitorPingInfo.Address = p.Address;
                        monitorPingInfo.DateStarted = p.DateStarted;
                        monitorPingInfo.DateEnded = p.DateEnded;
                        monitorPingInfo.Enabled = p.Enabled;
                        monitorPingInfo.EndPointType = p.EndPointType;
                        monitorPingInfo.MonitorIPID = p.MonitorIPID;
                        monitorPingInfo.MonitorStatus.AlertFlag = p.MonitorStatus.AlertFlag;
                        monitorPingInfo.MonitorStatus.AlertSent = p.MonitorStatus.AlertSent;
                        monitorPingInfo.MonitorStatus.DownCount = p.MonitorStatus.DownCount;
                        monitorPingInfo.MonitorStatus.EventTime = p.MonitorStatus.EventTime;
                        monitorPingInfo.MonitorStatus.IsUp = p.MonitorStatus.IsUp;
                        monitorPingInfo.MonitorStatus.Message = p.MonitorStatus.Message;
                        monitorPingInfo.UserID = p.UserID;
                        monitorPingInfo.Timeout = p.Timeout;
                        monitorPingInfo.Status = p.Status;
                        monitorPingInfo.RoundTripTimeTotal = p.RoundTripTimeTotal;
                        monitorPingInfo.RoundTripTimeMinimum = p.RoundTripTimeMinimum;
                        monitorPingInfo.RoundTripTimeMaximum = p.RoundTripTimeMaximum;
                        monitorPingInfo.RoundTripTimeAverage = p.RoundTripTimeAverage;
                        monitorPingInfo.PacketsRecieved = p.PacketsRecieved;
                        monitorPingInfo.PacketsLostPercentage = p.PacketsLostPercentage;
                        monitorPingInfo.PacketsLost = p.PacketsLost;
                        monitorPingInfo.PacketsSent = p.PacketsSent;
                        var addPingInfos = pingInfos.Except(monitorPingInfo.PingInfos, pingInfoComparer).ToList();
                        monitorPingInfo.PingInfos.AddRange(addPingInfos);
                    }
                    else
                    {
                        if (!swapMonitorPingInfoIDs.Contains(p.MonitorIPID))
                        {
                            p.PingInfos = new List<PingInfo>(pingInfos);
                            p.MonitorStatus.MonitorPingInfoID = 0;
                            p.MonitorStatus.ID = 0;
                            p.ID = 0;
                            if (p.PingInfos != null) p.PacketsSent = (int)p.PingInfos.Count();
                            else p.PacketsSent = 0;
                            addMonitorPingInfos.Add(p);
                        }

                    }
                });
            if (processorDataObj.RemoveMonitorPingInfoIDs != null && processorDataObj.RemoveMonitorPingInfoIDs.Count() != 0)
            {
                var removeMonitorPingInfos = new List<MonitorPingInfo>();
                processorDataObj.RemoveMonitorPingInfoIDs.ForEach(f =>
                {
                    var addToList = monitorPingInfos.Where(w => w.MonitorIPID == f).FirstOrDefault();
                    if (addToList != null) removeMonitorPingInfos.Add(addToList);
                });
                monitorContext.MonitorPingInfos.RemoveRange(removeMonitorPingInfos);
            }
            monitorContext.MonitorPingInfos.AddRange(addMonitorPingInfos);
            await monitorContext.SaveChangesAsync();
            var returnProcessorDataObj = new ProcessorDataObj()
            {
                RemovePingInfos = removePingInfos,
                SwapMonitorPingInfos = processorDataObj.SwapMonitorPingInfos ?? [],
                RemoveMonitorPingInfoIDs = processorDataObj.RemoveMonitorPingInfoIDs ?? [],
                AppID = processorDataObj.AppID,
                PingInfos = processorDataObj.PingInfos!,
                MonitorPingInfos = await monitorContext.MonitorPingInfos.Where(w => w.DataSetID == 0 && w.AppID == processorDataObj.AppID).ToListAsync()
            };
            return returnProcessorDataObj;

        }
    }
}