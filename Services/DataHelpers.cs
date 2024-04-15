using System.Text;
using NetworkMonitor.Objects;
using System.Collections.Generic;
namespace NetworkMonitor.Data.Services;
public class DataHelpers { 

      public static string DisableAndbuildHostList(List<MonitorIP> monitorIPs)
        {
            var hostListBuilder = new StringBuilder().Append("(");

            monitorIPs.ForEach(f =>
            {
                f.Enabled = false;
                hostListBuilder.Append(f.Address + ", ");
            });

            // Remove the last comma if the StringBuilder is not empty
            if (hostListBuilder.Length > 2)
            {
                hostListBuilder.Length--;  // Reduces the length by 1, effectively removing the last comma
                hostListBuilder.Length--;
            }

            return hostListBuilder.Append(")").ToString();
        }

   public static string BuildHostList(List<MonitorIP> monitorIPs)
        {
            var hostListBuilder = new StringBuilder().Append("(");

            monitorIPs.ForEach(f =>
            {
                hostListBuilder.Append(f.Address + ", ");
            });

            // Remove the last comma if the StringBuilder is not empty
            if (hostListBuilder.Length > 2)
            {
                hostListBuilder.Length--;  // Reduces the length by 1, effectively removing the last comma
                hostListBuilder.Length--;
            }

            return hostListBuilder.Append(")").ToString();
        }
 

}