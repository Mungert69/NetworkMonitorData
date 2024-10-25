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
        string SaveImageFile(byte[] imageData, string imageName);
        Task<ResultObj> SaveFileAsync(DataFileObj message);
         string SaveHtmlFile(string htmlContent, string htmlName); 
   
        void DeleteOldFiles(TimeSpan maxAge);
        Task<ResultObj> CreateAndTarPingInfoFilesForUser(string userId);

    }

    public class DataFileService : IDataFileService
    {
        private readonly string _baseFilePath;

        private readonly IServiceScopeFactory _scopeFactory;

        private IFileRepo _fileRepo;
        private IRabbitRepo _rabbitRepo;
        private ILogger _logger;
        private string _serverBaseUrl;
        private readonly string _imageFilePath;
        private readonly string _htmlDirectory;
        private bool _useAlternateBehavior = false;

        public DataFileService(ILogger<DataFileService> logger, IServiceScopeFactory scopeFactory, IFileRepo fileRepo, ISystemParamsHelper systemParamsHelper, IRabbitRepo rabbitRepo, bool useAlternateBehavior)
        {
            _baseFilePath = "wwwroot/files";
            _imageFilePath = Path.Combine(_baseFilePath, "images");
            _htmlDirectory = Path.Combine(_baseFilePath, "html");
            _fileRepo = fileRepo;
            _rabbitRepo = rabbitRepo;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _useAlternateBehavior = useAlternateBehavior;
            _serverBaseUrl = systemParamsHelper.GetSystemParams().ThisSystemUrl.ExternalUrl;
            if (!Directory.Exists(_imageFilePath))
            {
                Directory.CreateDirectory(_imageFilePath);
            }
            if (!Directory.Exists(_baseFilePath))
            {
                Directory.CreateDirectory(_baseFilePath);
            }
              if (!Directory.Exists(_htmlDirectory))
            {
                Directory.CreateDirectory(_htmlDirectory);
            }
        }

 public string SaveHtmlFile(string htmlContent, string htmlName)
    {
        try
        {
            string fileName = $"html_{htmlName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.html";
            string fullPath = Path.Combine(_htmlDirectory, fileName); // Save under 'html' directory
            string fileUrl = $"{_serverBaseUrl}{fullPath}";

            if (_useAlternateBehavior)
                {
                    var dataFileObj = new DataFileObj()
                    {
                        FilePath = fullPath,
                        Url = fileUrl,
                        Html=htmlContent
                    };
                    _rabbitRepo.PublishAsync<DataFileObj>("saveDataToFile", dataFileObj);
                }
                else
                {
                               File.WriteAllText(fullPath, htmlContent);
                }

            _logger.LogInformation($"HTML file saved successfully: {fileUrl}");
            return fileUrl;
        }
        catch (Exception e)
        {
            _logger.LogError($"Error saving HTML file: {e.Message}");
            throw;
        }
    }
        public string SaveImageFile(byte[] imageData, string imageName)
        {
            try
            {


                string fileName = $"image_{imageName}.png";
                string fullPath = Path.Combine(_imageFilePath, fileName);
                string fileUrl = $"{_serverBaseUrl}/files/images/{fileName}";
                if (_useAlternateBehavior)
                {
                    var dataFileObj = new DataFileObj()
                    {
                        FilePath = fullPath,
                        Url = fileUrl,
                        Data = imageData
                    };
                    _rabbitRepo.PublishAsync<DataFileObj>("saveDataToFile", dataFileObj);
                }
                else
                {
                    File.WriteAllBytes(fullPath, imageData); // Save image data as a PNG file
                }


                _logger.LogInformation($"Image saved successfully: {fileUrl}");
                return fileUrl;
            }
            catch (Exception e)
            {
                _logger.LogError($"Error saving image: {e.Message}");
                throw;
            }
        }
        public string SaveDataToFile<T>(T data, int id) where T : class
        {

            // Generate a unique filename
            string dateTimeString = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{typeof(T).Name}_{dateTimeString}_{id}.json";
            string fullPath = Path.Combine(_baseFilePath, fileName);
            string fileUrl = $"{_serverBaseUrl}/files/{fileName}";

            // Save the file
            if (_useAlternateBehavior)
            {
                var dataFileObj = new DataFileObj()
                {
                    FilePath = fullPath,
                    Url = fileUrl,
                    Json = JsonUtils.WriteJsonObjectToString<T>(data)
                };
                _rabbitRepo.PublishAsync<DataFileObj>("saveDataFile", dataFileObj);
            }
            else { 
                JsonUtils.WriteObjectToFile<T>(fullPath, data);
             }


            // Return the URL where the file can be accessed

            return fileUrl;
        }

        public async Task<ResultObj> SaveFileAsync(DataFileObj message)
        {
            var result=new ResultObj();
            try

            {
                
                if (message.Json != null)
                {
                    // Handle saving JSON data
                    if (string.IsNullOrEmpty(message.FilePath))
                    {
                        result.Message="Invalid file path for JSON data.";
                        result.Success=false;
                        return result;
                    }

                    // Ensure the directory exists before saving
                    var directory = Path.GetDirectoryName(message.FilePath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Write JSON data to file
                    await File.WriteAllTextAsync(message.FilePath, message.Json);
                      result.Message=$"JSON file saved successfully at {message.FilePath}";
                        result.Success=true;
                }
                else if (message.Data != null)
                {
                    // Handle saving image data
                    if (string.IsNullOrEmpty(message.FilePath))
                    {
                        result.Message="Invalid file path for image data.";
                        result.Success=false;
                        return result;
                    }

                    // Ensure the directory exists before saving
                    var directory = Path.GetDirectoryName(message.FilePath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Save the image file
                    await File.WriteAllBytesAsync(message.FilePath, message.Data);
                    result.Message=$"Image file saved successfully at {message.FilePath}";
                    result.Success=true;
                }
                else  if (message.Html != null)
                {
                    // Handle saving JSON data
                    if (string.IsNullOrEmpty(message.FilePath))
                    {
                        result.Message="Invalid file path for Html data.";
                        result.Success=false;
                        return result;
                    }

                    // Ensure the directory exists before saving
                    var directory = Path.GetDirectoryName(message.FilePath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Write JSON data to file
                    await File.WriteAllTextAsync(message.FilePath, message.Html);
                      result.Message=$"Html file saved successfully at {message.FilePath}";
                        result.Success=true;
                }
                else
                {
                    result.Message="Message contains neither Json, html or data";
                    result.Success=false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving file: {ex.Message}");
                throw; // Let the exception bubble up
            }
            return result;
        }


        public void DeleteOldFiles(TimeSpan maxAge)
        {
            // Recursively delete files and directories that exceed the maxAge
            void DeleteOldFilesAndDirectories(string path)
            {
                foreach (var directory in Directory.GetDirectories(path))
                {
                    // Skip directories that are used for reports (e.g., "/reports" folder)
                    if (directory.Contains("/images"))
                    {
                        _logger.LogInformation($"Skipping report directory: {directory}");
                        continue;
                    }

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

                    // Skip report files based on naming convention or other criteria
                    if (fileInfo.FullName.Contains("/images"))
                    {
                        _logger.LogInformation($"Skipping report file: {file}");
                        continue;
                    }

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
                    result.Data = _serverBaseUrl + "/files/" + CreateHtmlFile(fileUrl, _baseFilePath, userId);
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
