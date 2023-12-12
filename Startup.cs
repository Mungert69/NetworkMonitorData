using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetworkMonitor.Data.Services;
using NetworkMonitor.Data;
using NetworkMonitor.Objects;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using NetworkMonitor.Utils;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Objects.Repository;
using HostInitActions;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Utils.Helpers;
namespace NetworkMonitor.Data
{
    public class Startup
    {
        private readonly CancellationTokenSource _cancellationTokenSource;
        public Startup(IConfiguration configuration)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            Configuration = configuration;
        }
        public IConfiguration Configuration { get; }
        private IServiceCollection _services;
        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            _services = services;
           services.AddLogging(builder =>
               {
                   builder.AddSimpleConsole(options =>
                        {
                            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                            options.IncludeScopes = true;
                        });
               });

            string connectionString = Configuration.GetConnectionString("DefaultConnection");
            services.AddDbContext<MonitorContext>(options =>
                options.UseMySql(connectionString,
                ServerVersion.AutoDetect(connectionString),
                mySqlOptions =>
                     {
                         mySqlOptions.EnableRetryOnFailure(
                         maxRetryCount: 5,
                         maxRetryDelay: TimeSpan.FromSeconds(10),
                         errorNumbersToAdd: null);
                         mySqlOptions.CommandTimeout(1200);  // Set to 20m
                     }
            ));

            services.AddSingleton<IMonitorData, MonitorData>();
            services.AddSingleton<IDatabaseQueueService, DatabaseQueueService>();
            services.AddSingleton<INetLoggerFactory, NetLoggerFactory>();
            services.AddSingleton<IRabbitListener, RabbitListener>();
            services.AddSingleton<IRabbitRepo, RabbitRepo>();
            services.AddSingleton<IFileRepo, FileRepo>();
            services.AddSingleton<IPingInfoService, PingInfoService>();
            services.AddSingleton<IMonitorIPService, MonitorIPService>();
            services.AddSingleton<IProcessorState, ProcessorState>();
            services.AddSingleton<IProcessorBrokerService, ProcessorBrokerService>();
            services.AddSingleton<IReportService, ReportService>();
            services.AddSingleton<ISystemParamsHelper, SystemParamsHelper>();
            services.AddSingleton(_cancellationTokenSource);
            services.Configure<HostOptions>(s => s.ShutdownTimeout = TimeSpan.FromMinutes(5));
            services.AddAsyncServiceInitialization()
            .AddInitAction<IProcessorBrokerService>(async (processorBrokerService) =>
                    {
                        await processorBrokerService.Init();
                    })
                .AddInitAction<IMonitorData>(async (monitorData) =>
                    {
                        await monitorData.Init();
                    })
                 .AddInitAction<IRabbitListener>((rabbitListener) =>
                    {
                        return Task.CompletedTask;
                    });

        }
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime appLifetime)
        {

            appLifetime.ApplicationStopping.Register(() =>
            {
                _cancellationTokenSource.Cancel();
            });

        }
    }
}
