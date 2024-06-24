using NetworkMonitor.Objects;
using Microsoft.EntityFrameworkCore;
using NetworkMonitor.Data;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Objects.Repository;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using NetworkMonitor.Utils.Helpers;
namespace NetworkMonitor.Utils;

public class MonitorIPUpdateHelper { 
       public static async Task AddUpdateMonitorIP(MonitorIP monIP, MonitorIP dataMonIP, List<UpdateMonitorIP> updateMonitorIPs, string userId, MonitorContext monitorContext,string emailEncryptKey , IProcessorState processorState, int timeout)
        {
            var newMonIP = new UpdateMonitorIP(dataMonIP);
            var oldMonIP = new UpdateMonitorIP(monIP);
            monIP.Address = newMonIP.Address!.ToLower();
            monIP.Enabled = newMonIP.Enabled;
            monIP.Hidden = newMonIP.Hidden;
            monIP.Port = newMonIP.Port;
            monIP.Username = newMonIP.Username;
            monIP.Password = EncryptHelper.EncryptedPassword(emailEncryptKey, newMonIP.Password);
            monIP.EndPointType = newMonIP.EndPointType!.ToLower();
            if (newMonIP.Timeout > timeout)
            {
                newMonIP.Timeout = timeout;
            }
            monIP.Timeout = newMonIP.Timeout;
            monIP.UserID = userId;
            if (monIP.AppID != newMonIP.AppID)
            {
                oldMonIP.IsSwapping = true;
                oldMonIP.Delete = true;
                processorState.RemoveLoad(oldMonIP.AppID);
                updateMonitorIPs.Add(oldMonIP);
                newMonIP.IsSwapping = true;
                processorState.AddLoad(newMonIP.AppID);
                newMonIP.MonitorPingInfo = await monitorContext.MonitorPingInfos.Where(w => w.DataSetID == 0 && w.MonitorIPID == monIP.ID).FirstOrDefaultAsync();
            }
            monIP.AppID = newMonIP.AppID;
            updateMonitorIPs.Add(newMonIP);

        }

        
     
}