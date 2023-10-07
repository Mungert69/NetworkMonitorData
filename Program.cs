using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MetroLog;
using NetworkMonitor.Data;
using NetworkMonitor.Objects.Factory;
using System;
using System.Net;
namespace NetworkMonitor.Data
{
    public class Program
    {
        //private bool _isDevelopmentMode;
        public static void Main(string[] args)
        {
            bool isDevelopmentMode = false;
            string appFile = "appsettings.json";
          
            IConfigurationRoot config = new ConfigurationBuilder()
                .AddJsonFile(appFile, optional: false)
                .Build();
            IWebHost host = CreateWebHostBuilder(isDevelopmentMode).Build();
            using (IServiceScope scope = host.Services.CreateScope())
            {
                IServiceProvider services = scope.ServiceProvider;
                try
                {
                    MonitorContext context = services.GetRequiredService<MonitorContext>();
                    DbInitializer.Initialize(context);
                }
                catch (Exception ex)
                {
                    INetLoggerFactory loggerFactory = services.GetRequiredService<INetLoggerFactory>();
                    ILogger logger = loggerFactory.GetLogger("MonitorData");
                    logger.Error("An error occurred while seeding the database. Error was : " + ex.ToString());
                }
            }
            host.Run();
        }
        public static IWebHostBuilder CreateWebHostBuilder(bool isDevelopmentMode) =>
            WebHost.CreateDefaultBuilder().UseKestrel(options =>
                {
                   
                }).UseStartup<Startup>();
    }
}
