using NetworkMonitor.Objects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkMonitor.Data.Services
{
    public class PingInfoProcessor
    {

        public static List<PingInfo> ReducePingInfosToTarget(List<PingInfo> pingInfos, int targetCount = 20)
        {
            if (pingInfos == null || !pingInfos.Any()) return new List<PingInfo>();

            while (pingInfos.Count > targetCount)
            {
                pingInfos = CombinePingInfos(pingInfos);
            }

            return pingInfos;
        }
        public static List<PingInfo> CombinePingInfos(List<PingInfo> pingInfos)
        {
            if (pingInfos == null || !pingInfos.Any()) return new List<PingInfo>();

            if (pingInfos.Count % 2 != 0)
            {
                // Combine the last two PingInfos into a single one
                pingInfos[^2] = CombinePoints(new List<PingInfo> { pingInfos[^2], pingInfos[^1] })!;
                pingInfos.RemoveAt(pingInfos.Count - 1);
            }

            var sortedPingInfos = pingInfos.OrderBy(p => p.DateSentInt).ToList();
            var combinedPingInfos = new List<PingInfo>();

            for (int i = 0; i < sortedPingInfos.Count; i += 2)
            {
                var combinedPoint = CombinePoints(new List<PingInfo> { sortedPingInfos[i], sortedPingInfos[i + 1] });
                if (combinedPoint!=null) combinedPingInfos.Add(combinedPoint);
            }

            return combinedPingInfos;
        }




        private static PingInfo? CombinePoints(List<PingInfo> pointsToCombine)
        {
            if (pointsToCombine == null || pointsToCombine.Count != 2) return null;

            var point1 = pointsToCombine[0];
            var point2 = pointsToCombine[1];

            ushort averageRoundTripTime;
            uint averageDateSentInt;
            ushort? statusID;

            if (point1.RoundTripTime == UInt16.MaxValue && point2.RoundTripTime == UInt16.MaxValue)
            {
                // Both are timeouts
                averageRoundTripTime = UInt16.MaxValue;
                averageDateSentInt = (uint)((point1.DateSentInt + point2.DateSentInt) / 2);
                statusID = point1.StatusID;
            }
            else if (point1.RoundTripTime == UInt16.MaxValue)
            {
                // Only point1 is a timeout
                averageRoundTripTime = UInt16.MaxValue;
                averageDateSentInt = point1.DateSentInt;
                statusID = point1.StatusID;
            }
            else if (point2.RoundTripTime == UInt16.MaxValue)
            {
                // Only point2 is a timeout
                averageRoundTripTime = UInt16.MaxValue;
                averageDateSentInt = point2.DateSentInt;
                statusID = point2.StatusID;
            }
            else
            {
                // No timeouts
                averageRoundTripTime = (ushort)((point1.RoundTripTime!.Value + point2.RoundTripTime!.Value) / 2);
                averageDateSentInt = (uint)((point1.DateSentInt + point2.DateSentInt) / 2);
                statusID = point1.StatusID;
            }
          
            return new PingInfo
            {
                ID = point1.ID,
                StatusID = statusID.Value,
                MonitorPingInfoID = point1.MonitorPingInfoID,
                RoundTripTime = averageRoundTripTime,
                DateSentInt = averageDateSentInt
            };
        }

    }
}
