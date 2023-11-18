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
            UserInfo userInfo=new UserInfo(){
                UserID="84ab1c49-e8f2-4bb0-b347-06a3713c4798"
            }
            StringBuilder reportBuilder = new StringBuilder();
            var userList = new List<UserInfo>();
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    var user = await monitorContext.UserInfos.Where(u => u.UserID == userInfo.UserID).FirstOrDefaultAsync();
                    if (user == null)
                    {
                        result.Success = false;
                        result.Message += $" Error : Can't find user {userInfo.UserID} . ";
                        return result;
                    }

           
                    var monitorIPs = await monitorContext.MonitorIPs.Where(w => w.UserID == user.UserID).ToListAsync();
                    if (monitorIPs.Count > 0)
                    {
                        reportBuilder.AppendLine("Hello there! Here's your comprehensive weekly report:");

                        foreach (var monitorIP in monitorIPs)
                        {
                            reportBuilder.Append(GetReportForHost(monitorIP, monitorContext));

                        }
                        reportBuilder.AppendLine($"That's it for this week! Stay tuned for more insights next time. Remember, monitoring is key to maintaining a robust online presence.");
         

                    }
                    else
                    {
                        result.Success = false;
                        result.Message += $" Error : There are no hosts for the user {userInfo.UserID} . ";
                        return result;
                    }

                }
                result.Success = true;
                result.Message += $"Success : Got Reports for user {userInfo.UserID}  . ";
            }
            catch (Exception e)
            {
                result.Data = null;
                result.Message += $"Error : Got Reports for user {userInfo.UserID}: Error was : " + e.Message;
                result.Success = false;
                _logger.LogError($"Error : Got Reports for user {userInfo.UserID}  : Error was : " + e.ToString());
            }
            if (result.Success)
            {
                try
                {
                    await _rabbitRepo.PublishAsync<string>("sendReport", reportBuilder.ToString());
                    result.Message += " Success : published event sentReport";
                }
                catch (Exception e)
                {
                    result.Message += "Error : publish event sentReport : Error was : " + e.Message;
                    result.Success = false;
                    _logger.LogError("Error : publish event sentReport  : Error was : " + e.ToString());

                }
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
                var monitorPingInfos = monitorContext.MonitorPingInfos
                                          .Where(mpi => mpi.MonitorIPID == monitorIP.ID && mpi.DateStarted >= startDate && mpi.DateEnded <= endDate)
                                          .ToList();

                // Data aggregation and calculations
                var averageResponseTime = monitorPingInfos.Average(mpi => mpi.RoundTripTimeAverage);
                var packetsLostPercentage = monitorPingInfos.Average(mpi => mpi.PacketsLostPercentage);
                var uptimePercentage = 100 - packetsLostPercentage;

                // Identifying incidents
                var incidentCount = monitorPingInfos.Count(mpi => mpi.PacketsLost > 0);
                var totalDowntime = monitorPingInfos.Sum(mpi => mpi.PacketsLost > 0 ? (mpi.DateEnded.HasValue ? (mpi.DateEnded.Value - mpi.DateStarted).TotalMinutes : 0) : 0);

                // Build the report
                  reportBuilder.AppendLine($"Report for {monitorIP.Address}:");
                reportBuilder.AppendLine($"Covering Dates: {startDate.ToShortDateString()} to {endDate.ToShortDateString()}.");
                reportBuilder.AppendLine($"Let's dive into the numbers:");
                reportBuilder.AppendLine($"- Average Response Time: {averageResponseTime} ms. This is how fast your server responded on average – a crucial metric for user experience.");
                reportBuilder.AppendLine($"- Uptime: We're looking at {uptimePercentage}% uptime. That's {(uptimePercentage > 95 ? "fantastic" : "decent, but there's room for improvement")}.");
                reportBuilder.AppendLine($"- Total Downtime: {totalDowntime} minutes. It's the total time your server was unreachable. Less is always more here.");
                reportBuilder.AppendLine($"- Number of Incidents: {incidentCount}. Each incident is a potential hiccup in your service.");

                // User-friendly summaries and insights
                reportBuilder.AppendLine($"Some insights from the week:");
                reportBuilder.AppendLine($"- Stability: The server showed {(incidentCount > 0 ? "some fluctuations" : "solid stability")}. Notably, there were {incidentCount} incidents that might need your attention.");
                reportBuilder.AppendLine($"- Performance: The overall uptime of {uptimePercentage}% suggests {(uptimePercentage > 95 ? "top-notch performance" : "areas to keep an eye on")}. Consistent uptime is key for user trust and satisfaction.");

                // More Analysis with Comments on Server Performance
                // Note: Adjust this section based on the specific analytics and observations relevant to the server
                reportBuilder.AppendLine($"- Response Time Analysis: Your average response time was {averageResponseTime} ms. This {(averageResponseTime < 200 ? "excellent response time keeps users happy" : "higher response time might affect user experience negatively")}.");
                reportBuilder.AppendLine($"- Downtime Details: You had a total downtime of {totalDowntime} minutes. This might seem small, but every minute counts when it comes to availability.");

                // Visual data analysis (if applicable)
                // Note: Include graphs or charts if your system supports it
                // reportBuilder.AppendLine($"[Insert graphical analysis here if applicable]");

                   }
            catch (Exception ex)
            {
                // Handle exceptions
                _logger.LogError($"Error generating report for host {monitorIP.Address}: {ex.Message}");
                reportBuilder.AppendLine($"Oops! We ran into an issue while generating your report. Don't worry, our team is on it: {ex.Message}");
            }
            return reportBuilder.ToString();
        }




    }
}
