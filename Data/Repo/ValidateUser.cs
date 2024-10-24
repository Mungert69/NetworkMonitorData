using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NetworkMonitor.Data;
using NetworkMonitor.Objects;
using Microsoft.EntityFrameworkCore;
using System.Security;
using System.Security.Claims;
using NetworkMonitor.Data.Repo;

namespace NetworkMonitor.Data.Repo
{
    public class ValidateUser

    {
        private static string defaultUser = "default";
        // static method with input parameter of MonitorContext and UserInfo. Checks if userID is in the database.
        public async static Task<bool> VerifyUserExists(IUserRepo userRepo, UserInfo user, bool ignoreDefault, string? userId)
        {
         
            // Entry to this condition can be with userId or (email and sub).
            if (user.UserID == null) user.UserID = userId;
           
            if (user.Sub == null)   user.Sub = userId;


            bool valid = false;
            List<UserInfo> users = userRepo.CachedUsers.Where(u => u.UserID == user.UserID).ToList();
            // Return true if user is in database.
            if (users.Count() > 0)
            {
                valid = true;
                // This is to guard against editing the default user.
                if (user.UserID!.Equals(defaultUser) && ignoreDefault == false)
                {
                    valid = false;
                }
                else
                {
                    // Return the user info from database.
                    user = users.First();
                    if (user.Enabled == false)
                    {
                        valid = false;
                    }
                }

            }
            return valid;
        }
        // static method with input parameter of MonitorContext, UserInfo and MonitorIP.ID. Checks if MonitorIP ID and UserID is in the database.
        public async static Task<bool> VerifyMonitorIPExists(MonitorContext monitorContext, DelHost host)
        {
            bool valid = false;
            // Note we dont exclude hidden as this allows old data to be displayed
            // Set valid to true if monitorIPs contains a element with ID=id and UserID=user.UserID.
            if (await monitorContext.MonitorIPs.AnyAsync(m => m.ID == host.Index && m.UserID == host.UserID))
            {
                valid = true;
            }
            return valid;
        }
    }
}