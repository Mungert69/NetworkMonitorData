using NetworkMonitor.Objects;
using NetworkMonitor.DTOs;
using System.Collections.Generic;
using System.Linq;
using System;

namespace NetworkMonitor.Utils.Helpers
{
public class MonitorPingInfoHelper
{
    public static HostResponseObj CreateNewMonitorPingInfoFromPingInfos(MonitorPingInfo monitorPingInfo, List<PingInfoDTO> pingInfosDTO)
    {
        HostResponseObj hostResponseObj = new HostResponseObj();

        // Copy properties from MonitorPingInfo to HostResponseObj
        hostResponseObj.ID = monitorPingInfo.ID;
        hostResponseObj.Address = monitorPingInfo.Address;
        hostResponseObj.Port = monitorPingInfo.Port;
        hostResponseObj.UserID = monitorPingInfo.UserID;
        hostResponseObj.EndPointType = monitorPingInfo.EndPointType;
        hostResponseObj.Enabled = monitorPingInfo.Enabled;
        hostResponseObj.AddUserEmail = monitorPingInfo.AddUserEmail;
        hostResponseObj.IsEmailVerified = monitorPingInfo.IsEmailVerified;
        hostResponseObj.MessageForUser =monitorPingInfo.MessageForUser ;
        hostResponseObj.MonitorStatus=new StatusObj(){
            Message=""
        };
      
       
    if (pingInfosDTO != null && pingInfosDTO.Any())
    {
        hostResponseObj.DateStarted = pingInfosDTO.Min(p => p.DateSent);
        hostResponseObj.DateEnded = pingInfosDTO.Max(p => p.DateSent);

        hostResponseObj.PacketsSent = pingInfosDTO.Count;

        var validPings = pingInfosDTO.Where(p => p.ResponseTime != -1).ToList();
        var invalidPingsCount = pingInfosDTO.Count - validPings.Count;

        hostResponseObj.PacketsRecieved = validPings.Count;
        hostResponseObj.PacketsLost = invalidPingsCount;

        if (hostResponseObj.PacketsSent > 0)
            hostResponseObj.PacketsLostPercentage = (float)invalidPingsCount / hostResponseObj.PacketsSent * 100;

        if (validPings.Any())
        {
            hostResponseObj.RoundTripTimeAverage = (float)validPings.Average(p => p.ResponseTime);
            hostResponseObj.RoundTripTimeMaximum = validPings.Max(p => p.ResponseTime);
            hostResponseObj.RoundTripTimeMinimum = validPings.Min(p => p.ResponseTime);
            hostResponseObj.RoundTripTimeTotal = validPings.Sum(p => p.ResponseTime);

            // Calculate standard deviation of response times
            double avgResponseTime = validPings.Average(p => p.ResponseTime);
            double sumOfSquaresOfDifferences = validPings.Select(p => Math.Pow(p.ResponseTime - avgResponseTime, 2)).Sum();
            hostResponseObj.RoundTripTimeStandardDeviation = (float)Math.Sqrt(sumOfSquaresOfDifferences / validPings.Count);
        }

        hostResponseObj.Status = pingInfosDTO.Last().Status;

        // Calculate the number of successful pings and failed pings
        hostResponseObj.SuccessfulPings = validPings.Count;
        hostResponseObj.FailedPings = invalidPingsCount;

    }

        return hostResponseObj;
    }

    
public static HostResponseObj CopyAllFieldsFromMonitorPingInfo(MonitorPingInfo monitorPingInfo)
{
    HostResponseObj hostResponseObj = new HostResponseObj();

    // Copy properties from MonitorPingInfo to HostResponseObj
    hostResponseObj.ID = monitorPingInfo.ID;
    hostResponseObj.Address = monitorPingInfo.Address;
    hostResponseObj.Port = monitorPingInfo.Port;
    hostResponseObj.UserID = monitorPingInfo.UserID;
    hostResponseObj.EndPointType = monitorPingInfo.EndPointType;
    hostResponseObj.Enabled = monitorPingInfo.Enabled;
    hostResponseObj.AddUserEmail = monitorPingInfo.AddUserEmail;
    hostResponseObj.IsEmailVerified = monitorPingInfo.IsEmailVerified;
    hostResponseObj.MessageForUser = monitorPingInfo.MessageForUser;
    hostResponseObj.MonitorStatus = monitorPingInfo.MonitorStatus;
        hostResponseObj.DateStarted = monitorPingInfo.DateStarted;
        hostResponseObj.DateEnded = monitorPingInfo.DateEnded;
        hostResponseObj.PacketsSent = monitorPingInfo.PacketsSent;
        hostResponseObj.PacketsRecieved = monitorPingInfo.PacketsRecieved;
        hostResponseObj.PacketsLost = monitorPingInfo.PacketsLost;
        hostResponseObj.PacketsLostPercentage = monitorPingInfo.PacketsLostPercentage;
        hostResponseObj.RoundTripTimeAverage = monitorPingInfo.RoundTripTimeAverage;
        hostResponseObj.RoundTripTimeMaximum = monitorPingInfo.RoundTripTimeMaximum;
        hostResponseObj.RoundTripTimeMinimum = monitorPingInfo.RoundTripTimeMinimum;
        hostResponseObj.RoundTripTimeTotal = monitorPingInfo.RoundTripTimeTotal;
        hostResponseObj.Status = monitorPingInfo.Status;
    

    return hostResponseObj;
}

}
}
