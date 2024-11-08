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
        private IDataLLMService _dataLLMService;
        private TimeSpan _timeSpan;





        public ReportService(IServiceScopeFactory scopeFactory, ILogger<ReportService> logger, IRabbitRepo rabbitRepo, ISystemParamsHelper systemParamsHelper, IProcessorState processorState, IDataFileService fileService, IUserRepo userRepo, IDataLLMService dataLLMService)
        {
            _scopeFactory = scopeFactory;
            _rabbitRepo = rabbitRepo;
            _logger = logger;
            _systemParams = systemParamsHelper.GetSystemParams();
            _processorState = processorState;
            _fileService = fileService;
            _dataLLMService = dataLLMService;
            _userRepo = userRepo;
            _timeSpan = TimeSpan.FromHours(_systemParams.SendReportsTimeSpan);
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
                    var userHostCount = await monitorContext.UserInfos
     .Where(u => u.UserID != "default" && !u.DisableEmail) // Filter users based on criteria
     .Select(u => u.MonitorIPs.Count(m => m.Enabled))      // Count enabled MonitorIPs per user
     .SumAsync();                                          // Sum counts across all users

                    TimeSpan waitTime = TimeSpan.FromTicks(_timeSpan.Ticks / userHostCount);
                    _logger.LogInformation($"Info: Processing {userHostCount} hosts.");


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
                        bool llmStarted = false;
                        var serviceObj = new LLMServiceObj()
                        {
                            RequestSessionId = Guid.NewGuid().ToString(),
                            UserInfo = user,
                            SourceLlm = "data",
                            DestinationLlm = "data",
                            IsSystemLlm = true
                        };

                        var monitorIPs = await monitorContext.MonitorIPs.Where(w => w.UserID == user.UserID && !w.Hidden && w.Address != "https://your-website-address.here").ToListAsync();
                        if (monitorIPs != null && monitorIPs.Count > 0)
                        {
                            reportBuilder.AppendLine("<div class=\"report-container\" style=\"font-family: Arial, sans-serif; max-width: 800px; margin: auto; padding: 10px; color: #333;\">");
                            reportBuilder.AppendLine("<h3 style=\"text-align: center; color: #6239AB;\">Weekly Network Performance Report</h3>");
                            reportBuilder.AppendLine($"<p style=\"color: #607466;\">Hi, {userInfo.Name}! Here's your network report for the past week:</p>");

                            foreach (var monitorIP in monitorIPs)
                            {
                                _logger.LogInformation($"Warning: Waiting for {waitTime.TotalMinutes} minutes.");

                                reportBuilder.Append(await GetReportForHost(monitorIP, monitorContext, userInfo, serviceObj));
                                await Task.Delay(waitTime);
                            }
                            reportBuilder.AppendLine("<h3 style=\"color: #6239AB;\">That's it for this week! Stay tuned for more insights next time.</h3>");
                            reportBuilder.AppendLine("<p style=\"color: #607466;\">Remember, monitoring is key to maintaining a robust online presence.</p>");
                            result.Success = true;
                            result.Message += $"Success : Got Reports for user {userInfo.UserID}  . ";

                        }
                        else
                        {
                            result.Success = false;
                            result.Message += $" Error : There are no hosts for the user {userInfo.UserID} . ";
                        }

                        // End of HTML report
                        reportBuilder.AppendLine("<footer style=\"margin-top: 20px; text-align: center;\">");
                        reportBuilder.AppendLine("<p style=\"font-size: 12px; color: #888;\">Generated by Network Monitor</p>");
                        reportBuilder.AppendLine("</footer>");
                        reportBuilder.AppendLine("</div>");

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

        private async Task<ResultObj> GetLLMReportForHost(string input, LLMServiceObj serviceObj)
        {

            var result = new ResultObj();
            result.Success = false;
            var resultStart = new TResultObj<LLMServiceObj>();
            try
            {

                resultStart = await _dataLLMService.SystemLlmStart(serviceObj);
                if (resultStart != null && resultStart.Success && resultStart.Data != null)
                {
                    serviceObj = resultStart.Data;
                    _logger.LogInformation(resultStart.Message);
                }
                else
                {
                    result.Message = resultStart!.Message;
                    _logger.LogError(resultStart.Message);
                    return result;
                }

            }
            catch (Exception e)
            {
                result.Message = $" Error : could not start llm . Error was : {e.Message}";
                _logger.LogError(result.Message);
                return result;

            }

            serviceObj.UserInput = "Produce a Report from this report data, ONLY REPLY WITH THE REPORT : " + input;
            result = await _dataLLMService.LLMInput(serviceObj);
            try
            {

                var resultStop = await _dataLLMService.SystemLlmStop(serviceObj);
                if (resultStop.Success) _logger.LogInformation(result.Message);
                else
                {
                    _logger.LogError(result.Message);
                }
            }
            catch (Exception e)
            {
                _logger.LogError($" Error : could not stop llm . Error was : {e.Message}");
            }
            return result;
        }

        private async Task<string> GetReportForHost(MonitorIP monitorIP, MonitorContext monitorContext, UserInfo userInfo, LLMServiceObj serviceObj)
        {
            var llmResult = new ResultObj();
            bool errorFlag = false;
            StringBuilder reportBuilder = new StringBuilder();
            StringBuilder responseDataBuilder = new StringBuilder();
            try
            {
                var endDate = DateTime.UtcNow;
                var startDate = endDate.AddDays(-7);
                string portStr = monitorIP.Port != 0 ? $" : Port {monitorIP.Port}" : "";
                reportBuilder.AppendLine($"<h3 style=\"color: #6239AB;\">Report for Host at {monitorIP.Address} : Endpoint {monitorIP.EndPointType}{portStr} : Agent Location {_processorState.LocationFromID(monitorIP.AppID)}</h3>");
                reportBuilder.AppendLine($"<p style=\"color: #607466;\">Covering Dates: {startDate.ToShortDateString()} to {endDate.ToShortDateString()}.</p>");

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

                var pingInfoHelper = new PingInfoHelper(monitorContext, 84);
                var result = await pingInfoHelper.GetMonitorPingInfoDTOByFilter(new TResultObj<HostResponseObj>(), query, monitorIP.UserID!, _fileService, _userRepo);
                var hostResponseObj = result.Data;
                var pingInfos = new List<PingInfoDTO>();
                if (hostResponseObj != null) pingInfos = hostResponseObj.PingInfosDTO;


                if (monitorIP.Enabled && monitorPingInfos != null && pingInfos != null && pingInfos.Count > 0)
                {
                    // Data aggregation and calculations
                    var averageResponseTime = monitorPingInfos.Average(mpi => mpi.RoundTripTimeAverage);
                    var packetsLostPercentage = monitorPingInfos.Average(mpi => mpi.PacketsLostPercentage);
                    var uptimePercentage = 100 - packetsLostPercentage;
                    var incidentCount = monitorPingInfos.Count(mpi => mpi.PacketsLost > 0);
                    bool serverDownWholeTime = false;
                    if (packetsLostPercentage == 100) serverDownWholeTime = true;
                    // Additional performance metrics
                    var maxResponseTime = monitorPingInfos.Max(mpi => mpi.RoundTripTimeMaximum);
                    var minResponseTime = monitorPingInfos.Min(mpi => mpi.RoundTripTimeMinimum);
                    var stdDevResponseTime = Math.Sqrt(monitorPingInfos.Average(mpi => Math.Pow(mpi.RoundTripTimeAverage - averageResponseTime, 2)));
                    var successfulPings = monitorPingInfos.Sum(mpi => mpi.PacketsRecieved);
                    var failedPings = monitorPingInfos.Sum(mpi => mpi.PacketsLost);

                    reportBuilder.AppendLine($"<p style=\"color: #607466;\">- Average Response Time: {(uptimePercentage == 0 ? "N/A" : averageResponseTime.ToString("F0"))} ms</p>");
                    reportBuilder.AppendLine($"<p style=\"color: #607466;\">- Maximum Response Time: {monitorPingInfos.Max(mpi => mpi.RoundTripTimeMaximum)} ms</p>");
                    reportBuilder.AppendLine($"<p style=\"color: #607466;\">- Minimum Response Time: {monitorPingInfos.Min(mpi => mpi.RoundTripTimeMinimum)} ms</p>");
                    reportBuilder.AppendLine($"<p style=\"color: #607466;\">- Standard Deviation of Response Times: {Math.Sqrt(monitorPingInfos.Average(mpi => Math.Pow(mpi.RoundTripTimeAverage - averageResponseTime, 2))):F2} ms</p>");
                    reportBuilder.AppendLine($"<p style=\"color: #607466;\">- Uptime: {uptimePercentage:F2}%</p>");
                    reportBuilder.AppendLine($"<p style=\"color: {(incidentCount > 0 ? "#d4a10d" : "#607466")};\">- Number of Incidents: {incidentCount}</p>");
                    // Insight and performance categories
                    var insightsColor = "#607466"; // Default to primary color

                    string uptimeCategoryKey = DetermineUptimeCategory(uptimePercentage, serverDownWholeTime);
                    string responseTimeCategoryKey = DetermineResponseTimeCategory(averageResponseTime, monitorIP.EndPointType!, monitorIP.Port);
                    string stabilityCategoryKey = DetermineStabilityCategory(incidentCount);
                    string overallPerformanceKey = DeterminePerformanceCategory(serverDownWholeTime, uptimePercentage, averageResponseTime, incidentCount, monitorIP.EndPointType!, monitorIP.Port);

                    reportBuilder.AppendLine("<p style=\"color: #6239AB;\">Weekly Insights</p>");
                    reportBuilder.AppendLine($"<p style=\"color: {insightsColor};\">- Uptime: {GetRandomPhrase(uptimeCategoryKey)}</p>");
                    reportBuilder.AppendLine($"<p style=\"color: {insightsColor};\">- Response Time: {GetRandomPhrase(responseTimeCategoryKey)}</p>");
                    reportBuilder.AppendLine($"<p style=\"color: {insightsColor};\">- Stability: {GetRandomPhrase(stabilityCategoryKey)}</p>");
                    reportBuilder.AppendLine($"<p style=\"color: {insightsColor};\">- Overall Performance: {GetRandomPhrase(overallPerformanceKey)}</p>");
                    // Generate response time graph and embed it in the report
                    // Add a timestamp to the file name to avoid caching issues
                    var fileName = $"{monitorIP.Address}_{monitorIP.EndPointType}_{monitorIP.Port}_{monitorIP.AppID}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";

                    string responseTimeGraphUrl = GenerateResponseTimeGraph(pingInfos, fileName);
                    if (!string.IsNullOrEmpty(responseTimeGraphUrl))
                    {
                        reportBuilder.AppendLine("<h3 style=\"color: #6239AB;\">Response Time Graph</h3>");
                        reportBuilder.AppendLine($"<img src='{responseTimeGraphUrl}' alt='Response Time Graph' style=\"border: 1px solid #607466; max-width: 100%;\"/>");
                    }
                    responseDataBuilder.AppendLine("{");
                    responseDataBuilder.AppendLine("\"intro\": \"Analyze the response time data for patterns such as spikes, high values, and timeouts.\",");
                    responseDataBuilder.AppendLine("\"response_data\": [");

                    for (int i = 0; i < pingInfos.Count; i++)
                    {
                        var pingInfo = pingInfos[i];
                        responseDataBuilder.Append($"{{\"DateSent\":\"{pingInfo.DateSent}\", \"ResponseTime\":{pingInfo.ResponseTime}}}");

                        // Add a comma after each item except the last one
                        if (i < pingInfos.Count - 1)
                        {
                            responseDataBuilder.AppendLine(",");
                        }
                        else
                        {
                            responseDataBuilder.AppendLine(); // No comma for the last item
                        }
                    }

                    responseDataBuilder.AppendLine("]");
                    responseDataBuilder.AppendLine("}");

                    var reportSoFar = reportBuilder.ToString() + responseDataBuilder.ToString();
                    llmResult = await GetLLMReportForHost(reportSoFar, serviceObj);
                    //reportBuilder.AppendLine($"<p> AI Report :{llmResult}");


                }
                else
                {
                    reportBuilder.AppendLine($"<p style=\"color: #d4a10d;\">No data for this host during this time period. Login to {AppConstants.FrontendUrl}/dashboard to manage this host and enable if necessary.</p>");
                }
            }
            catch (Exception ex)
            {
                errorFlag = true;
                _logger.LogError($"Error generating report for host {monitorIP.Address}: {ex.Message}");
                reportBuilder.AppendLine($"<p style=\"color: #eb5160;\">Oops! We ran into an issue while generating your report: {ex.Message}</p>");
            }
            if (errorFlag || !llmResult.Success)
            {
                return reportBuilder.ToString();
            }
            else
            {
                return llmResult.Message;
            }
        }
        private string GenerateResponseTimeGraph(List<PingInfoDTO> pingInfos, string fileName)
        {

            try
            {  // Prepare data for graph (response times and dates)
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
            catch (Exception e) {
                _logger.LogError($" Error : could not produce a image graph. Error was : {e.Message}");
                return "";
             }

        }

        private string DetermineUptimeCategory(double uptimePercentage, bool serverDownWholeTime)
        {
            if (serverDownWholeTime) return "ZeroUptime";
            if (uptimePercentage > 98) return "ExcellentUptime";
            if (uptimePercentage > 95) return "GoodUptime";
            if (uptimePercentage > 90) return "FairUptime";
            if (uptimePercentage > 80) return "PoorUptime";
            return "BadUptime";
        }

        public string DetermineResponseTimeCategory(double averageResponseTime, string endpointType, int port = 0)
        {
            // Retrieve thresholds based on endpoint type and port
            var thresholds = EndPointTypeFactory.ResponseTimeThresholds.ContainsKey(endpointType.ToLower())
                ? EndPointTypeFactory.ResponseTimeThresholds[endpointType.ToLower()].GetThresholds(port)
                : new ThresholdValues(500, 1000, 2000); // Default thresholds

            // Determine category based on thresholds
            if (averageResponseTime < thresholds.Excellent) return "ExcellentResponseTime";
            if (averageResponseTime < thresholds.Good) return "GoodResponseTime";
            if (averageResponseTime < thresholds.Fair) return "FairResponseTime";
            return "PoorResponseTime";
        }
        private string DetermineStabilityCategory(int incidentCount)
        {
            if (incidentCount == 0) return "ExcellentStability";
            if (incidentCount <= 2) return "GoodStability";
            return "PoorStability";
        }

        private string DeterminePerformanceCategory(bool serverDownWholeTime, double uptimePercentage, double averageResponseTime, int incidentCount, string endpointType, int port)
        {
            if (serverDownWholeTime) return "DownWholeTime";
            // Get individual category results
            string uptimeCategory = DetermineUptimeCategory(uptimePercentage, serverDownWholeTime);
            string responseTimeCategory = DetermineResponseTimeCategory(averageResponseTime, endpointType, port);
            string stabilityCategory = DetermineStabilityCategory(incidentCount);

            int score = 0;

            // Assign scores based on uptime category
            score += uptimeCategory == "ExcellentUptime" ? 3 :
                     uptimeCategory == "GoodUptime" ? 2 :
                     uptimeCategory == "FairUptime" ? 1 :
                     uptimeCategory == "PoorUptime" ? 0 : -1;

            // Assign scores based on response time category
            score += responseTimeCategory == "ExcellentResponseTime" ? 2 :
                     responseTimeCategory == "GoodResponseTime" ? 1 :
                     responseTimeCategory == "FairResponseTime" ? 0 : -1;

            // Assign scores based on stability category
            score += stabilityCategory == "ExcellentStability" ? 2 :
                     stabilityCategory == "GoodStability" ? 1 : 0;

            // Determine the overall performance category based on the total score
            if (score >= 7) return "ExcellentPerformance";
            if (score >= 4) return "GoodPerformance";
            if (score >= 2) return "FairPerformance";
            return "PoorPerformance";
        }


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
        private Dictionary<string, List<string>> reportPhrases = new Dictionary<string, List<string>>
{
    // Uptime
    // Excellent Uptime
    {"ExcellentUptime", new List<string>
        {
            "Exceptional uptime! Your network is achieving near-perfect availability.",
            "Outstanding performance! Uptime levels are consistently high, reflecting a reliable network.",
            "Impressive uptime! The system is highly dependable with minimal interruptions.",
            "Remarkable uptime! Your network is operating at peak reliability.",
            "Top-notch reliability! The system remains available almost all the time, indicating excellent network health."
        }
    },

    // Good Uptime
    {"GoodUptime", new List<string>
        {
            "Great uptime! Your network remains consistently available with minor fluctuations.",
            "Strong performance! Uptime is reliable, with only occasional, minor downtimes.",
            "Good uptime achieved! The network operates smoothly with few interruptions.",
            "Solid uptime! The system is performing well with high availability.",
            "Reliable uptime! Minimal downtime observed, maintaining a high level of availability."
        }
    },

    // Fair Uptime
    {"FairUptime", new List<string>
        {
            "Decent uptime, though improvement is possible for higher consistency.",
            "Acceptable uptime, but consider optimizing to reduce interruptions.",
            "Average performance. Uptime is fair, though more consistency would enhance reliability.",
            "Uptime is adequate, but a focus on reducing downtime would be beneficial.",
            "Fair uptime noted. The network is stable but could achieve higher reliability with adjustments."
        }
    },

    // Poor Uptime
    {"PoorUptime", new List<string>
        {
            "Uptime is below expectations, indicating potential reliability issues.",
            "Moderate uptime observed; improvements are needed to enhance consistency.",
            "Noticeable downtimes impacting network availability. Review for possible optimizations.",
            "Uptime is lacking consistency, suggesting issues that may need attention.",
            "Fluctuating availability detected. Consider steps to increase network stability."
        }
    },

    // Bad Uptime
    {"BadUptime", new List<string>
        {
            "Significant downtime detected, severely affecting network reliability.",
            "Frequent downtimes observed, suggesting serious system issues.",
            "Low uptime! Persistent interruptions point to potential network health problems.",
            "Reliability is compromised due to regular and prolonged downtime.",
            "Poor network availability. Major improvements are necessary to restore stability."
        }
    },

    // Zero Uptime
    {"ZeroUptime", new List<string>
        {
            "No uptime recorded throughout the period. Immediate investigation is crucial.",
            "The system was down for the entire monitored period; urgent attention is needed.",
            "Complete downtime observed, indicating a critical network failure.",
            "Continuous downtime detected. System health needs immediate action.",
            "Zero uptime recorded; the network has been non-functional and requires urgent resolution."
        }
    },


     // Excellent Response Time
    {"ExcellentResponseTime", new List<string>
        {
            "Outstanding response times! Your server is operating at peak responsiveness.",
            "Exceptional performance! Response times are lightning-fast, providing an optimal experience.",
            "Near-instant responses! Users experience minimal delay, contributing to smooth interactions.",
            "Impressive speed! Response times are consistently low, ensuring a high-quality user experience.",
            "Ultra-fast response times, indicating a well-optimized and efficient server setup."
        }
    },

    // Good Response Time
    {"GoodResponseTime", new List<string>
        {
            "Good response times! The server is responsive, with only occasional delays.",
            "Strong performance! Response times are fast, maintaining a positive user experience.",
            "Quick and reliable response times, supporting smooth server operations.",
            "The server is responsive with minor delays, generally providing a solid experience.",
            "Consistently good response times, with only minimal impact on user interactions."
        }
    },

    // Fair Response Time
    {"FairResponseTime", new List<string>
        {
            "Moderate response times, though there is room for improvement.",
            "Response times are acceptable but could be optimized for a smoother experience.",
            "Average performance; response times could be quicker to enhance user experience.",
            "Decent response times, but reducing latency would improve overall performance.",
            "Fair response times observed, though improvements could contribute to a better experience."
        }
    },

    // Poor Response Time
    {"PoorResponseTime", new List<string>
        {
            "High response times detected, potentially impacting user experience negatively.",
            "Slow response times observed. Optimization is recommended for better performance.",
            "Significant delays in response times, affecting overall server efficiency.",
            "High latency detected; consider adjustments to improve server responsiveness.",
            "Response times are notably slow, suggesting the need for load or resource optimization."
        }
    },
    
       // Excellent Stability
    {"ExcellentStability", new List<string>
        {
            "Your network demonstrated flawless stability throughout the week.",
            "Outstanding stability! The network remained consistently operational without issues.",
            "Exceptional stability observed, ensuring uninterrupted network performance.",
            "Rock-solid stability! Your system ran smoothly with no notable fluctuations.",
            "Impeccable stability, providing a seamless and reliable network experience."
        }
    },

    // Good Stability
    {"GoodStability", new List<string>
        {
            "Solid stability with only minor, infrequent disruptions.",
            "The network showed reliable stability, with minimal interruptions.",
            "Good stability achieved! The network is mostly consistent and reliable.",
            "Network stability is strong, though occasional minor adjustments could enhance it.",
            "Stable network performance observed, maintaining dependable uptime with minor fluctuations."
        }
    },

    // Poor Stability
    {"PoorStability", new List<string>
        {
            "Inconsistent stability detected, impacting network reliability.",
            "Network stability was frequently compromised, warranting further investigation.",
            "Noticeable instability observed; consider reviewing system health.",
            "Periodic interruptions have affected stability, indicating potential issues.",
            "Frequent stability issues detected, suggesting the need for corrective measures."
        }
    },
    // Excellent Performance
    {"ExcellentPerformance", new List<string>
        {
            "Peak performance achieved, with every aspect of the network functioning flawlessly.",
            "Optimal efficiency! The network operates at maximum capacity, ensuring a seamless user experience.",
            "Outstanding reliability and speed, with no performance issues detected.",
            "Exceptional performance! The network is setting new standards in operational capacity.",
            "Unmatched efficiency and stability, with all metrics exceeding expectations.",
            "Flawless operation! The network is consistently reliable and exceptionally fast.",
            "Unrivaled network performance, demonstrating the highest levels of reliability and responsiveness."
        }
    },

    // Good Performance
    {"GoodPerformance", new List<string>
        {
            "Solid performance with efficient and reliable operation.",
            "Network is performing well, with only minor areas for potential improvement.",
            "Reliable and stable performance, contributing to an overall positive user experience.",
            "The network is operating efficiently, with minor room for optimization.",
            "Good overall performance, showing dependable uptime and responsiveness.",
            "Commendable performance! The network maintains stability and reliability effectively.",
            "Generally strong performance, balancing efficiency with robust uptime."
        }
    },

    // Fair Performance
    {"FairPerformance", new List<string>
        {
            "Network is functional, but efficiency and consistency need attention.",
            "Performance is acceptable, yet there are clear opportunities for improvement.",
            "Basic functionality is met, but enhanced reliability and speed are needed.",
            "Network operates at a basic level, with stability that could benefit from optimization.",
            "Meets minimum standards, though performance enhancements would add value.",
            "Satisfactory operation, though advanced optimizations are recommended.",
            "Network is generally functional, but gaps in reliability and responsiveness are noticeable."
        }
    },

    // Poor Performance
    {"PoorPerformance", new List<string>
        {
            "Critical performance issues detected; immediate resolution required.",
            "Severe degradation in network functionality, impacting overall usability.",
            "Significant challenges in operation; the network struggles to meet basic needs.",
            "Unreliable performance, with major stability and responsiveness concerns.",
            "Network functionality is compromised, with essential services affected.",
            "Persistent operational failures indicate the need for urgent intervention.",
            "Performance falls short of basic requirements, affecting all aspects of usability."
        }
    }

};

    }
}
