using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MetroLog;
using NetworkMonitor.Data;
using NetworkMonitor.Objects.Factory;
using System;

namespace NetworkMonitor.Data
{
    public class Program
    {
        public static void Main(string[] args)
        {
            string appFile = "appsettings.json";
            IConfigurationRoot config = new ConfigurationBuilder()
                .AddJsonFile(appFile, optional: false)
                .Build();

            IHost host = CreateHostBuilder(config).Build();

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

        public static IHostBuilder CreateHostBuilder(IConfigurationRoot config) =>
            Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(builder =>
                {
                    builder.AddConfiguration(config);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    // Register your Startup class's ConfigureServices method
                    var startup = new Startup(hostContext.Configuration);
                    startup.ConfigureServices(services);
                });
    }
}
