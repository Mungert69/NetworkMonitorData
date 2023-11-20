using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
namespace NetworkMonitor.Data.Services
{
    public interface IReportService
    {
        Task<ResultObj> CreateHostSummaryReport();
    }
    public class ReportService : IReportService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private ILogger _logger;
        private IRabbitRepo _rabbitRepo;
        public ReportService(IServiceScopeFactory scopeFactory, ILogger<ReportService> logger, IRabbitRepo rabbitRepo)
        {
            _scopeFactory = scopeFactory;
            _rabbitRepo = rabbitRepo;
            _logger = logger;
        }
        public async Task<ResultObj> CreateHostSummaryReport()
        {
            var result = new ResultObj();
            result.Message = "SERVICE : ReportService.CreateHostSummaryReport : ";
            result.Success = false;
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();

                    var users = await monitorContext.UserInfos.Where(u =>  u.UserID != "default" && !u.DisableEmail).ToListAsync();
                    foreach (var userInfo in users)
                    {
                        UserInfo? user = new UserInfo();
                        StringBuilder reportBuilder = new StringBuilder();
                        var userList = new List<UserInfo>();


                        user = await monitorContext.UserInfos.Where(u => u.UserID == userInfo.UserID).FirstOrDefaultAsync();

                        if (user == null)
                        {
                            result.Success = false;
                            result.Message += $" Error : Can't find user {userInfo.UserID} . ";
                            return result;
                        }


                        var monitorIPs = await monitorContext.MonitorIPs.Where(w =>  w.UserID == user.UserID && !w.Hidden && w.Address!="https://your-website-address.here" ).ToListAsync();
                        if (monitorIPs != null && monitorIPs.Count > 0)
                        {
                            reportBuilder.AppendLine("<h3>Hello there! Here's your comprehensive weekly report:</h3>");

                            foreach (var monitorIP in monitorIPs)
                            {
                                reportBuilder.Append(GetReportForHost(monitorIP, monitorContext));

                            }
                            reportBuilder.AppendLine("<h3>That's it for this week! Stay tuned for more insights next time.</h3>");
                            reportBuilder.AppendLine("<p>Remember, monitoring is key to maintaining a robust online presence.</p>");
                            reportBuilder.AppendLine($"<p>.. This reporting feature is in beta. Please provide feedback by replying to this email . Please quote you UserID {userInfo.UserID}...</p>");
                             result.Success = true;
                        result.Message += $"Success : Got Reports for user {userInfo.UserID}  . ";
                       
                        }
                        else
                        {
                            result.Success = false;
                            result.Message += $" Error : There are no hosts for the user {userInfo.UserID} . ";
                        }


                        if (result.Success)
                        {
                            try
                            {
                                await _rabbitRepo.PublishAsync<HostReportObj>("sendHostReport", new HostReportObj() { User = user!, Report = reportBuilder.ToString() });
                                result.Message += " Success : published event sentHostReport";
                            }
                            catch (Exception e)
                            {
                                result.Message += "Error : publish event sentHostReport : Error was : " + e.Message;
                                result.Success = false;
                                _logger.LogError("Error : publish event sentHostReport  : Error was : " + e.ToString());

                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Message += $"Error : Failed to Get Reports : Error was : " + e.Message;
                result.Success = false;
                _logger.LogError($"Error : Failed to Get Reports : Error was : " + e.ToString());
            }


            return result;
        }


        private string GetReportForHost(MonitorIP monitorIP, MonitorContext monitorContext)
        {
            StringBuilder reportBuilder = new StringBuilder();
            try
            {
                var endDate = DateTime.UtcNow;
                var startDate = endDate.AddDays(-7);
                reportBuilder.AppendLine($"<h2>Report for {monitorIP.Address} : {monitorIP.EndPointType}</h2>");
                reportBuilder.AppendLine($"<p>Covering Dates: {startDate.ToShortDateString()} to {endDate.ToShortDateString()}.</p>");

                var monitorPingInfos = monitorContext.MonitorPingInfos
                                          .Where(mpi => mpi.MonitorIPID == monitorIP.ID && mpi.DateStarted >= startDate && mpi.DateEnded <= endDate)
                                          .ToList();
                if (monitorIP.Enabled && monitorPingInfos != null && monitorPingInfos.Count > 0)
                {
                    // Data aggregation and calculations
                    var averageResponseTime = monitorPingInfos.Average(mpi => mpi.RoundTripTimeAverage);
                    var packetsLostPercentage = monitorPingInfos.Average(mpi => mpi.PacketsLostPercentage);
                    var uptimePercentage = 100 - packetsLostPercentage;

                    // Identifying incidents
                    var incidentCount = monitorPingInfos.Count(mpi => mpi.PacketsLost > 0);
                    //var totalDowntime = monitorPingInfos.Sum(mpi => mpi.PacketsLost);

                    // Check if the server was down the whole time
                    bool serverDownWholeTime = uptimePercentage == 0;

                    reportBuilder.AppendLine($"<p>- Average Response Time: {(serverDownWholeTime ? "N/A" : averageResponseTime.ToString("F0"))} ms.</p>");
                    reportBuilder.AppendLine($"<p>- Uptime: {uptimePercentage.ToString("F2")}%.</p>");
                    reportBuilder.AppendLine($"<p>- Number of Incidents: {incidentCount}</p>");

                    // User-friendly summaries and insights
                    reportBuilder.AppendLine($"<p>Some insights from the week:</p>");

                    // Uptime
                    var uptimeCategory = "";
                    if (serverDownWholeTime)
                    {
                        uptimeCategory = "ZeroUptime";
                    }
                    else if (uptimePercentage > 98)
                    {
                        uptimeCategory = "GoodUptime";
                    }
                    else if (uptimePercentage > 80) // Assuming that below 80% is considered 'BadUptime'
                    {
                        uptimeCategory = "PoorUptime";
                    }
                    else
                    {
                        uptimeCategory = "BadUptime";
                    }

                    reportBuilder.AppendLine($"<p>- Uptime: {GetRandomPhrase(uptimeCategory)}</p>");

                    // Response Time
                    var responseTimeCategory = serverDownWholeTime ? "N/A" : (averageResponseTime < 500 ? "GoodResponseTime" : "PoorResponseTime");
                    reportBuilder.AppendLine($"<p>- Response Time: {GetRandomPhrase(responseTimeCategory)}</p>");

                    // Stability
                    var stabilityCategory = serverDownWholeTime ? "Down" : (incidentCount > 0 ? "Unstable" : "Stable");
                    reportBuilder.AppendLine($"<p>- Stability: {GetRandomPhrase(stabilityCategory)}</p>");

                    // Inside the GetReportForHost method

                    string performanceCategory = DeterminePerformanceCategory(serverDownWholeTime, uptimePercentage, averageResponseTime, incidentCount);
                    reportBuilder.AppendLine($"<p>- Overall Performance: {GetRandomPhrase(performanceCategory)}</p>");

                }
                else
                {
                    if (monitorIP.Enabled) reportBuilder.AppendLine("There is not data for this host during this time period . ");

                    else reportBuilder.AppendLine("<p>The host was not enabled so not data was collected for this week. </p>");

                }
                // Visual data analysis (if applicable)
                // Note: Include graphs or charts if your system supports it
                // reportBuilder.AppendLine($"[Insert graphical analysis here if applicable]");

            }
            catch (Exception ex)
            {
                // Handle exceptions
                _logger.LogError($"Error generating report for host {monitorIP.Address}: {ex.Message}");
                reportBuilder.AppendLine($"<p>Oops! We ran into an issue while generating your report. Don't worry, our team is on it: {ex.Message}</p>");
            }
            return reportBuilder.ToString();
        }
        private string DeterminePerformanceCategory(bool serverDownWholeTime, double uptimePercentage, double averageResponseTime, int incidentCount)
        {
            if (serverDownWholeTime) return "PoorPerformance";
            // Define stricter thresholds and weights
            const double strictUptimeThreshold = 98; // Higher threshold for good uptime
            const double strictResponseTimeThreshold = 500; // Lower threshold for good response time
            const int strictStabilityThreshold = 0; // Allowing for minimal incidents

            int score = 0;

            // Uptime score
            score += serverDownWholeTime ? 0 : (uptimePercentage > strictUptimeThreshold ? 3 : (uptimePercentage > 80 ? 2 : 1));

            // Response time score
            score += averageResponseTime < strictResponseTimeThreshold ? 2 : (averageResponseTime < 1000 ? 1 : 0);


            // Stability score
            score += incidentCount <= strictStabilityThreshold ? 2 : (incidentCount <= 10 ? 1 : 0);

            // Determine overall performance category based on the total score
            if (score >= 7) return "ExcellentPerformance";
            if (score >= 4) return "GoodPerformance";
            if (score >= 2) return "FairPerformance";
            return "PoorPerformance";
        }

        private Dictionary<string, List<string>> reportPhrases = new Dictionary<string, List<string>>
{
    // Uptime
    {"GoodUptime", new List<string>
        {
            "Superb uptime! Your network's reliability is top-notch.",
            "Excellent uptime! Your system's reliability and consistent performance are noteworthy.",
            "Great job! Your network is consistently up and running.",
            "Excellent uptime! Your system is performing reliably."
        }
    },
    {"PoorUptime", new List<string>
        {
            "Uptime could be better. Time to check for potential issues.",
            "Moderate uptime. There's some room for improvement.",
            "Your uptime is okay, but strive for higher consistency.",
            "Decent uptime, yet there's scope for better performance."
        }
    },
     {"BadUptime", new List<string>
        {
            "Uptime is unsatisfactory, indicating potential system issues.",
            "Poor uptime observed, which could impact user experience negatively.",
            "Significant downtime detected, suggesting the need for system checks.",
            "Unsatisfactory uptime - your network's reliability is compromised."
        }
    },
    {"ZeroUptime", new List<string>
        {
            "The server was down all week. Immediate action is needed.",
            "No uptime recorded. Check your system's health urgently.",
            "Complete downtime. It's crucial to investigate the cause."
        }
    },

    // Response Time
    {"GoodResponseTime", new List<string>
        {
            "Excellent response times, ensuring a smooth user experience.",
            "Quick response times! Your server is highly responsive.",
            "Great response times, contributing to optimal performance.",
            "Swift responses from your server, enhancing user satisfaction."
        }
    },
    {"PoorResponseTime", new List<string>
        {
            "Longer response times detected. Consider optimizing your server.",
            "Response times are slower than ideal, affecting user experience.",
            "Response times could be improved for better performance.",
            "Consider reviewing your server's load, as response times are high."
        }
    },

    
    // Stability
    {"Stable", new List<string>
        {
            "Your network showed excellent stability this week.",
            "Solid stability - your system remains reliably up and running.",
            "Steady and stable - your network is performing well.",
            "Your network stability is commendable this week."
        }
    },
    {"Unstable", new List<string>
        {
            "Some fluctuations in network stability observed.",
            "Network stability was somewhat inconsistent - worth a check.",
            "Periodic instability detected - consider investigating further.",
            "Occasional network hiccups noted - stability could be improved."
        }
    },
    {"Down", new List<string>
        {
            "The network was consistently down - urgent review needed.",
            "Consistent network downtime observed - critical attention required.",
            "Your network faced serious stability issues - immediate action needed."
        }
    },

    // Excellent Performance
    {"ExcellentPerformance", new List<string>
        {
            "Peak performance achieved, with every aspect of the network functioning flawlessly.",
            "Optimal efficiency, with the network operating at its best possible level.",
            "Exceptional standards of reliability and speed, with no issues detected.",
            "Network performance is at its zenith, showcasing perfect operational capacity.",
            "Unparalleled network efficiency, exceeding all performance metrics.",
            "Flawless operation with maximum reliability, setting a benchmark in network performance."
        }
    },
    // Good Performance
    {"GoodPerformance", new List<string>
        {
            "Solid performance, with efficient and reliable network operation.",
            "Network performs well overall, though minor improvements could be made.",
            "Reliable and robust performance, with occasional areas for enhancement.",
            "Network efficiency is commendable, yet there's a small scope for optimization.",
            "Consistently good operation with notable reliability and uptime.",
            "Admirable performance, balancing efficiency and stability effectively."
        }
    },
    // Fair Performance
    {"FairPerformance", new List<string>
        {
            "Network is operational, but efficiency and reliability are just adequate.",
            "Performance is stable, yet several aspects need attention and improvement.",
            "Functional but not optimal, with clear areas for performance enhancements.",
            "Network maintains basic functionality, but lacks in consistency and speed.",
            "Operational performance meeting minimum standards, but improvements are necessary.",
            "Satisfactory in basic functions, yet lagging in advanced performance aspects."
        }
    },
    // Poor Performance
    {"PoorPerformance", new List<string>
        {
            "Network is non-functional, with critical issues needing immediate resolution.",
            "Severe performance degradation, rendering the network practically inoperable.",
            "Network faces significant operational challenges, requiring urgent attention.",
            "Complete breakdown in performance, with the network not working as intended.",
            "Critical performance failure, with essential functions not operational.",
            "Extremely poor performance, with the network failing to meet basic operational requirements."
        }
    }

};





        private Random random = new Random();

        private string GetRandomPhrase(string category)
        {
            if (reportPhrases.ContainsKey(category))
            {
                var phrases = reportPhrases[category];
                int index = random.Next(phrases.Count);
                return phrases[index];
            }
            return "";
        }

    }
}
