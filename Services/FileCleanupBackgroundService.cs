using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Service.Services;
namespace NetworkMonitor.Data.Services
{
    public class FileCleanupBackgroundService : BackgroundService
    {
        private readonly IDataFileService _dataFileService;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1); // Adjust as needed

        public FileCleanupBackgroundService(IDataFileService dataFileService)
        {
            _dataFileService = dataFileService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _dataFileService.DeleteOldFiles(TimeSpan.FromHours(1)); // Adjust the timespan as needed
                await Task.Delay(_cleanupInterval, stoppingToken);
            }
        }
    }
}