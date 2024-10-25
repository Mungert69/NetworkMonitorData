using System;
using SkiaSharp;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.Entity;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Data;
using NetworkMonitor.Utils;
using NetworkMonitor.Utils.Helpers;
using NetworkMonitor.Data.Repo;
using NetworkMonitor.DTOs;

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
        private SystemParams _systemParams;
        private IProcessorState _processorState;
        private IDataFileService _fileService;
        private IUserRepo _userRepo;

        public ReportService(IServiceScopeFactory scopeFactory, ILogger<ReportService> logger, IRabbitRepo rabbitRepo, ISystemParamsHelper systemParamsHelper, IProcessorState processorState, IDataFileService fileService, IUserRepo userRepo)
        {
            _scopeFactory = scopeFactory;
            _rabbitRepo = rabbitRepo;
            _logger = logger;
            _systemParams = systemParamsHelper.GetSystemParams();
            _processorState = processorState;
            _fileService = fileService;
            _userRepo = userRepo;
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
                    var uri = _systemParams.ThisSystemUrl.ExternalUrl;



                    var users = await monitorContext.UserInfos.Where(u => u.UserID != "default" && !u.DisableEmail).ToListAsync();
                    foreach (var userInfo in users)
                    {
                        var emailInfo = new EmailInfo() { Email = userInfo.Email!, EmailType = "HostReport" };
                        UserInfo? user = new UserInfo();
                        StringBuilder reportBuilder = new StringBuilder();
                        var userList = new List<UserInfo>();


                        user = await monitorContext.UserInfos.Where(u => u.UserID == userInfo.UserID).FirstOrDefaultAsync();

                        if (user == null)
                        {
                            result.Success = false;
                            result.Message += $" Error : Can't find user {userInfo.UserID} . ";
                            continue;
                        }


                        var monitorIPs = await monitorContext.MonitorIPs.Where(w => w.UserID == user.UserID && !w.Hidden && w.Address != "https://your-website-address.here").ToListAsync();
                        if (monitorIPs != null && monitorIPs.Count > 0)
                        {
                            reportBuilder.AppendLine("<h3>Hello there! Here's your comprehensive weekly report:</h3>");

                            foreach (var monitorIP in monitorIPs)
                            {
                                reportBuilder.Append(await GetReportForHost(monitorIP, monitorContext, userInfo));

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
                                await _rabbitRepo.PublishAsync<HostReportObj>("sendHostReport", new HostReportObj() { UserInfo = user!, Report = reportBuilder.ToString(), HeaderImageUri = uri, ID = emailInfo.ID });
                                result.Message += " Success : published event sentHostReport";
                            }
                            catch (Exception e)
                            {
                                result.Message += "Error : publish event sentHostReport : Error was : " + e.Message;
                                result.Success = false;
                                _logger.LogError("Error : publish event sentHostReport  : Error was : " + e.ToString());

                            }
                            try
                            {
                                monitorContext.EmailInfos.Add(emailInfo);
                                await monitorContext.SaveChangesAsync();
                                result.Message += " Success : Added new EmailInfo to database .";

                            }
                            catch (Exception e)
                            {
                                result.Message += "Error : failed to add new EmailInfo to database : Error was : " + e.Message;
                                result.Success = false;
                                _logger.LogError("Error :  failed to add new EmailInfo to database   : Error was : " + e.ToString());

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


        private async Task<string> GetReportForHost(MonitorIP monitorIP, MonitorContext monitorContext, UserInfo userInfo)
        {
            StringBuilder reportBuilder = new StringBuilder();
            try
            {
                var endDate = DateTime.UtcNow;
                var startDate = endDate.AddDays(-7);
                string portStr = monitorIP.Port != 0 ? $" : Port {monitorIP.Port}" : "";
                reportBuilder.AppendLine($"<h3>Report for Host at {monitorIP.Address} : Endpoint {monitorIP.EndPointType}{portStr} : Agent Location {_processorState.LocationFromID(monitorIP.AppID)} </h3>");
                reportBuilder.AppendLine($"<p>Covering Dates: {startDate.ToShortDateString()} to {endDate.ToShortDateString()}.</p>");

                var monitorPingInfos = monitorContext.MonitorPingInfos
                                        .Where(mpi => mpi.MonitorIPID == monitorIP.ID && mpi.DateStarted >= startDate && mpi.DateEnded <= endDate)
                                        .ToList();

                var query = new DateRangeQuery
                {
                    MonitorIPID = monitorIP.ID,
                    StartDate = startDate,
                    EndDate = endDate,
                    User = userInfo
                };

                var pingInfoHelper = new PingInfoHelper(monitorContext,100);
                var result = await pingInfoHelper.GetMonitorPingInfoDTOByFilter(new TResultObj<HostResponseObj>(), query, monitorIP.UserID, _fileService, _userRepo);
                var hostResponseObj = result.Data;
                var pingInfos = new List<PingInfoDTO>();
                if (hostResponseObj != null) pingInfos = hostResponseObj.PingInfosDTO;


                if (monitorIP.Enabled && monitorPingInfos != null && monitorPingInfos.Count > 0)
                {
                    // Data aggregation and calculations
                    var averageResponseTime = monitorPingInfos.Average(mpi => mpi.RoundTripTimeAverage);
                    var packetsLostPercentage = monitorPingInfos.Average(mpi => mpi.PacketsLostPercentage);
                    var uptimePercentage = 100 - packetsLostPercentage;
                    var incidentCount = monitorPingInfos.Count(mpi => mpi.PacketsLost > 0);

                    reportBuilder.AppendLine($"<p>- Average Response Time: {(uptimePercentage == 0 ? "N/A" : averageResponseTime.ToString("F0"))} ms.</p>");
                    reportBuilder.AppendLine($"<p>- Uptime: {uptimePercentage.ToString("F2")}%.</p>");
                    reportBuilder.AppendLine($"<p>- Number of Incidents: {incidentCount}</p>");

                    // User-friendly summaries and insights
                    reportBuilder.AppendLine($"<p>Some insights from the week:</p>");
                    reportBuilder.AppendLine($"<p>- Uptime: {GetRandomPhrase(DeterminePerformanceCategory(false, uptimePercentage, averageResponseTime, incidentCount))}</p>");
                    reportBuilder.AppendLine($"<p>- Response Time: {GetRandomPhrase(averageResponseTime < 500 ? "GoodResponseTime" : "PoorResponseTime")}</p>");
                    reportBuilder.AppendLine($"<p>- Stability: {GetRandomPhrase(incidentCount > 0 ? "Unstable" : "Stable")}</p>");

                    // Generate response time graph and embed it in the report
                    // Add a timestamp to the file name to avoid caching issues
                    var fileName = $"{monitorIP.Address}_{monitorIP.EndPointType}_{monitorIP.Port}_{monitorIP.AppID}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";

                    string responseTimeGraphUrl = GenerateResponseTimeGraph(pingInfos, fileName);
                    if (!string.IsNullOrEmpty(responseTimeGraphUrl))
                    {
                        reportBuilder.AppendLine("<h3>Response Time Graph</h3>");
                        reportBuilder.AppendLine($"<img src='{responseTimeGraphUrl}' alt='Response Time Graph' />");
                    }

                }
                else
                {
                    reportBuilder.AppendLine("<p>No data for this host during this time period.</p>");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error generating report for host {monitorIP.Address}: {ex.Message}");
                reportBuilder.AppendLine($"<p>Oops! We ran into an issue while generating your report: {ex.Message}</p>");
            }

            return reportBuilder.ToString();
        }
private string GenerateResponseTimeGraph(List<PingInfoDTO> pingInfos, string fileName)
{

    
    // Prepare data for graph (response times and dates)
    var responseTimes = pingInfos.Select(p => p.ResponseTime).ToList();
    var dates = pingInfos.Select(p => p.DateSent.ToString("MM/dd HH:mm")).ToList();

    if (!responseTimes.Any() || !dates.Any())
    {
        _logger.LogError("No data points available to plot the graph.");
        return string.Empty;
    }

    // Define graph dimensions
    int width = 600;
    int height = 400;
    int margin = 50;

    // Find the maximum and minimum response times for dynamic scaling
    float maxResponseTime = responseTimes.Max();
    float minResponseTime = Math.Min(0, responseTimes.Min()); // Ensure Y-axis starts from 0
    float yScale = (height - (2 * margin)) / (maxResponseTime - minResponseTime); // Y scaling

    using (var surface = SKSurface.Create(new SKImageInfo(width, height)))
    {
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        // Draw gridlines and axes
        var gridPaint = new SKPaint
        {
            Color = SKColors.LightGray,
            StrokeWidth = 1,
            IsAntialias = true
        };

        // Horizontal gridlines (Y-axis)
        for (int i = 0; i <= 5; i++)
        {
            float y = margin + i * ((height - 2 * margin) / 5);
            canvas.DrawLine(margin, y, width - margin, y, gridPaint);
        }

        // Vertical gridlines (X-axis)
        for (int i = 0; i <= 5; i++)
        {
            float x = margin + i * ((width - 2 * margin) / 5);
            canvas.DrawLine(x, margin, x, height - margin, gridPaint);
        }

        // Draw axes
        var axisPaint = new SKPaint
        {
            Color = SKColors.Black,
            StrokeWidth = 2,
            IsAntialias = true
        };
        canvas.DrawLine(margin, margin, margin, height - margin, axisPaint); // Y-axis
        canvas.DrawLine(margin, height - margin, width - margin, height - margin, axisPaint); // X-axis

        // Y-axis label and values
        axisPaint.TextSize = 16;
        axisPaint.Color = new SKColor(0x60, 0x74, 0x66); // Primary theme color (visible green)
        for (int i = 0; i <= 5; i++)
        {
            float yValue = minResponseTime + (i * (maxResponseTime - minResponseTime) / 5);
            float yPosition = height - margin - (i * ((height - 2 * margin) / 5));
            canvas.DrawText($"{yValue:F0}", 10, yPosition + 5, axisPaint); // Adjust the position for visibility
        }

        // Draw response time data as a line chart
        var linePaint = new SKPaint
        {
            Color = new SKColor(0x60, 0x74, 0x66),// Use primary color for the line
            StrokeWidth = 1,
            IsAntialias = true
        };

        for (int i = 1; i < responseTimes.Count; i++)
        {
            float startX = margin + (i - 1) * ((width - 2 * margin) / (responseTimes.Count - 1));
            float startY = height - margin - ((responseTimes[i - 1] - minResponseTime) * yScale); // Adjust scaling
            float endX = margin + i * ((width - 2 * margin) / (responseTimes.Count - 1));
            float endY = height - margin - ((responseTimes[i] - minResponseTime) * yScale);

            canvas.DrawLine(startX, startY, endX, endY, linePaint);
        }

        // Add labels for dates, reduce their frequency to avoid clashing
        var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 14,
            IsAntialias = true
        };

        int labelInterval = Math.Max(1, dates.Count / 4); // Reduce the number of labels shown
        for (int i = 0; i < dates.Count; i += labelInterval)
        {
            float x = margin + i * ((width - 2 * margin) / (dates.Count - 1));
            canvas.DrawText(dates[i], x, height - 20, textPaint); // Place date labels
        }

        // Draw points at each data location for visibility
        linePaint.Color = new SKColor(0x60, 0x74, 0x66); // Primary theme green for regular points
        linePaint.StrokeWidth = 5;
        for (int i = 0; i < responseTimes.Count; i++)
        {
            float x = margin + i * ((width - 2 * margin) / (responseTimes.Count - 1));
            float y = height - margin - ((responseTimes[i] - minResponseTime) * yScale);

            // If response time is -1, mark it with error color, otherwise primary green
            if (responseTimes[i] == -1)
            {
                linePaint.Color = new SKColor(0xeb, 0x51, 0x60); // Error color
            }
            else
            {
                linePaint.Color = new SKColor(0x60, 0x74, 0x66); // Primary theme green
            }

            canvas.DrawCircle(x, y, 5, linePaint); // Draw a small circle at each data point
        }

        // Save the graph as an image
        using (var image = surface.Snapshot())
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            byte[] imageBytes = data.ToArray();
            string imageUrl = _fileService.SaveImageFile(imageBytes, fileName);

            return imageUrl; // Return the public URL for the saved image
        }
    }
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
