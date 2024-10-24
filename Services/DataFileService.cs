using System;
using System.Collections.Generic;
using System.IO;
using NetworkMonitor.Objects;
using System.Linq;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using NetworkMonitor.Utils;
using NetworkMonitor.Utils.Helpers;
using System.Threading.Tasks;

namespace NetworkMonitor.Data.Services
{
    public interface IDataFileService
    {
        string SaveDataToFile<T>(T data, int id) where T : class;
        void DeleteOldFiles(TimeSpan maxAge);
        Task<ResultObj> CreateAndTarPingInfoFilesForUser(string userId);

    }

    public class DataFileService : IDataFileService
    {
        private readonly string _baseFilePath;

        private readonly IServiceScopeFactory _scopeFactory;

        private IFileRepo _fileRepo;
        private ILogger _logger;
        private string _serverBaseUrl;

        public DataFileService(ILogger<DataFileService> logger, IServiceScopeFactory scopeFactory, IFileRepo fileRepo, ISystemParamsHelper systemParamsHelper)
        {
            _baseFilePath = "wwwroot/files";
            _fileRepo = fileRepo;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _serverBaseUrl = systemParamsHelper.GetSystemParams().ThisSystemUrl.ExternalUrl;
        }

        public string SaveDataToFile<T>(T data, int id) where T : class
        {

            // Generate a unique filename
            string dateTimeString = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{typeof(T).Name}_{dateTimeString}_{id}.json";
            string fullPath = Path.Combine(_baseFilePath, fileName);

            // Save the file
            JsonUtils.WriteObjectToFile<T>(fullPath, data);

            // Return the URL where the file can be accessed
            string fileUrl = $"{_serverBaseUrl}/files/{fileName}";
            return fileUrl;
        }

        public void DeleteOldFiles(TimeSpan maxAge)
        {
            // Recursively delete files and directories that exceed the maxAge
            void DeleteOldFilesAndDirectories(string path)
            {
                foreach (var directory in Directory.GetDirectories(path))
                {
                    DeleteOldFilesAndDirectories(directory); // Recursive call to handle subdirectories
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        // Check if the directory is old enough to be deleted
                        var dirInfo = new DirectoryInfo(directory);
                        if (DateTime.UtcNow - dirInfo.CreationTimeUtc > maxAge)
                        {
                            Directory.Delete(directory, recursive: true);
                            _logger.LogInformation($"Deleted old directory: {directory}");
                        }
                    }
                }

                foreach (var file in Directory.GetFiles(path))
                {
                    var fileInfo = new FileInfo(file);
                    if (DateTime.UtcNow - fileInfo.CreationTimeUtc > maxAge)
                    {
                        File.Delete(file);
                        _logger.LogInformation($"Deleted old file: {file}");
                    }
                }
            }

            // Initial call to clean up files and directories within the base file path
            DeleteOldFilesAndDirectories(_baseFilePath);
        }


        public async Task<ResultObj> CreateAndTarPingInfoFilesForUser(string userId)
        {
            ResultObj result = new ResultObj();
            result.Message = "SERVICE : CreateAndTarPingInfoFilesForUser : ";
            result.Success = false;

            // Define the base directory for user files
            string baseDirectory = _baseFilePath + "/" + userId + "/";

            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();

                    // Retrieve MonitorPingInfos for the user with their PingInfos
                    var monitorPingInfos = await monitorContext.MonitorPingInfos
                        .Include(m => m.PingInfos)
                        .Where(m => m.UserID == userId)
                        .ToListAsync();

                    if (!monitorPingInfos.Any())
                    {
                        result.Message += "Info : No MonitorPingInfos found for user.";
                        return result;
                    }

                    // Create the base directory if it doesn't exist
                    if (!Directory.Exists(baseDirectory))
                    {
                        Directory.CreateDirectory(baseDirectory);
                    }

                    // Create a temporary directory under base directory
                    var tempDirectory = Path.Combine(baseDirectory, Guid.NewGuid().ToString());
                    Directory.CreateDirectory(tempDirectory);

                    // Generate .br files for each MonitorPingInfo
                    foreach (var monitorPingInfo in monitorPingInfos)
                    {
                        string dateTimeFormatted = monitorPingInfo.DateStarted.ToString("yyyyMMdd_HHmmss");
                        var filePath = Path.Combine(tempDirectory, $"{monitorPingInfo.ID}_{dateTimeFormatted}");

                        // Assuming SaveStateJsonZAsync method creates a .br file with PingInfos
                        await _fileRepo.SaveStateJsonAsync(filePath, monitorPingInfo);
                    }

                    // Tar the folder
                    string tarPath = $"{tempDirectory}.tar.gz";
                    TarFolder(tempDirectory, tarPath);

                    // Cleanup: Delete the temporary directory
                    Directory.Delete(tempDirectory, recursive: true);

                    result.Message += $"Info : Created and tarred MonitorPingInfo files for user {userId}. Tar file located at: {tarPath}";
                    result.Success = true;

                    string fileUrl = $"{_serverBaseUrl}/files/{userId}/{Path.GetFileName(tarPath)}";
                    result.Data =_serverBaseUrl+"/files/"+ CreateHtmlFile(fileUrl, _baseFilePath, userId);
                }
            }
            catch (Exception e)
            {
                result.Message += "Error : Exception occurred while processing MonitorPingInfos. Error was : " + e.Message;
                result.Success = false;
                _logger.LogError("Error : Exception occurred while processing MonitorPingInfos. Error was : " + e.Message + " Inner Exception :" + e.InnerException?.Message);
            }


            return result;
        }
       private string CreateHtmlFile(string fileUrl, string directoryPath, string userId)
{
    string htmlFileName = $"download_page_{userId}.html";
    string htmlFilePath = Path.Combine(directoryPath, htmlFileName);
    string htmlContent = $@"
<html>
<head>
    <title>Download Your Data</title>
    <style>
        body {{
            font-family: Arial, sans-serif;
            margin: 40px;
            color: #333;
            background-color: #f4f4f9;
        }}
        h1, h2 {{
            color: #607466;
        }}
        a {{
            color: #6239AB;
            text-decoration: none;
        }}
        .content {{
            margin-top: 20px;
            background-color: #fff;
            padding: 20px;
            border-radius: 8px;
            box-shadow: 0 0 10px rgba(0,0,0,0.1);
        }}
        .logo {{
            width: 150px;
        }}
    </style>
</head>
<body>
    <img src='https://freenetworkmonitor.click/img/logo.jpg' alt='Logo' class='logo'/>
    <div class='content'>
        <h1>Data File Ready for Download</h1>
        <p>Your requested data file has been generated and is ready for download:</p>
        <a href='{fileUrl}' download='UserData.tar.gz'>Download Data File</a>
        <p>This file contains all the ping info data collected for your account. Click the link above to download it to your device.</p>
        <h2>How to Extract the Data</h2>
        <p><strong>Windows:</strong> Use 7-Zip or similar software to extract the file. Right-click the downloaded file and select 'Extract here'.</p>
        <p><strong>macOS:</strong> Double-click the .tar.gz file to extract it automatically.</p>
        <p><strong>Linux:</strong> Use the terminal command <code>tar -xzvf filename.tar.gz</code> in the directory where the file is downloaded.</p>
        <p>For detailed instructions, refer to the documentation provided or contact support if you face any issues during the extraction process.</p>
    </div>
</body>
</html>";

    File.WriteAllText(htmlFilePath, htmlContent);
    return htmlFileName;
}

        private void TarFolder(string sourceDirectory, string destinationTarFile)
        {
            // Use System.Diagnostics to run the 'tar' command
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "tar",
                    Arguments = $"-czf {destinationTarFile} -C {sourceDirectory} .",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit();
        }

    }
}
