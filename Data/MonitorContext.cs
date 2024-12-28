
using Microsoft.EntityFrameworkCore;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Entity;
using System.Collections.Generic;

namespace NetworkMonitor.Data
{
    public class MonitorContext : DbContext
    {

        public MonitorContext(DbContextOptions<MonitorContext> options) : base(options)
        {
            Database.EnsureCreated();
        }


        public DbSet<MonitorPingInfo> MonitorPingInfos { get; set; }
        public DbSet<PingInfo> PingInfos { get; set; }
        public DbSet<MonitorIP> MonitorIPs { get; set; }

        public DbSet<UserInfo> UserInfos { get; set; }
        public DbSet<EmailInfo> EmailInfos { get; set; }
        public DbSet<LoadServer> LoadServers { get; set; }

        public DbSet<StatusItem> StatusList { get; set; }
        public DbSet<Blog> Blogs { get; set; }

        public DbSet<BlogCategory> BlogCategories { get; set; }
        public DbSet<BlogPicture> BlogPictures { get; set; }
        public DbSet<LogChatGPTObj> ChatGPTLogs { get; set; }
        public DbSet<UserAuthInfo> UserAuthInfos { get; set; }
        public DbSet<ProcessorObj> ProcessorObjs { get; set; }
        public DbSet<TestUser> TestUsers { get; set; }
        public DbSet<PredictStatus> PredictStatuses { get; set; }
        public DbSet<LLMSessionLog> LLMSessionLogs { get; set; }
        public DbSet<LLMSessionOutput> LLMSessionOutputs { get; set; }

public DbSet<ExceptionLog> ExceptionLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MonitorPingInfo>().ToTable("MonitorPingInfos");
            modelBuilder.Entity<MonitorIP>().ToTable("MonitorIPs");
            modelBuilder.Entity<UserInfo>().ToTable("UserInfos");
            modelBuilder.Entity<LoadServer>().ToTable("LoadServers");
            modelBuilder.Entity<StatusItem>().ToTable("StatusList");
            modelBuilder.Entity<Blog>().ToTable("Blogs");
            modelBuilder.Entity<BlogPicture>().ToTable("BlogPictures");
            modelBuilder.Entity<BlogCategory>().ToTable("BlogCategories");
            modelBuilder.Entity<PingInfo>().ToTable("PingInfos");
            modelBuilder.Entity<EmailInfo>().ToTable("EmailInfos");
            modelBuilder.Entity<StatusObj>().ToTable("StatusObjs");
            modelBuilder.Entity<LogChatGPTObj>().ToTable("ChatGPTLogs");
            modelBuilder.Entity<UserAuthInfo>().ToTable("UserAuthInfos");
            modelBuilder.Entity<ProcessorObj>().ToTable("ProcessorObjs");
            modelBuilder.Entity<TestUser>().ToTable("TestUsers");
            modelBuilder.Entity<PredictStatus>().ToTable("PredictStatuses");
            modelBuilder.Entity<PredictStatus>().OwnsOne(p => p.ChangeDetectionResult).WithOwner();
            modelBuilder.Entity<PredictStatus>().OwnsOne(p => p.SpikeDetectionResult).WithOwner();
            modelBuilder.Entity<LLMSessionLog>().ToTable("LLMSessionLogs");
            modelBuilder.Entity<LLMSessionOutput>().ToTable("LLMSessionOutputs");
            modelBuilder.Entity<ExceptionLog>().ToTable("ExceptionLogs");

        }
    }

}
