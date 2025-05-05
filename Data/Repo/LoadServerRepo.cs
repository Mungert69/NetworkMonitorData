
using System;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Utils;
using NetworkMonitor.Objects.Repository;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Utils.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;

namespace NetworkMonitor.Data.Repo;

public interface ILoadServerRepo
{
    Task<LoadServer> GetLoadServerFromUserID(string userId);
    Task<List<LoadServer>> GetAllLoadServersDBNoTracking(); // This can be removed if you don't expose this functionality
    Task<ResultObj> AddLoadServer(LoadServer loadServer);
    Task UpdateCachedLoadServer(LoadServer newLoadServer); // This can be made internal if cache updates are handled internally
}
public class LoadServerRepo : ILoadServerRepo
{

    private readonly IServiceScopeFactory _scopeFactory;
    private List<LoadServer> _cachedLoadServers;
    private ILogger _logger;
    private IRabbitRepo _rabbitRepo;
    private ISystemParamsHelper _systemParamsHelper;



    public async Task RefreshLoadServers()
    {
        _cachedLoadServers = await GetAllLoadServersDBNoTracking();
    }

    public LoadServerRepo(ILogger<UserRepo> logger, IServiceScopeFactory scopeFactory, ISystemParamsHelper systemParamsHelper, IRabbitRepo rabbitRepo)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _rabbitRepo = rabbitRepo;
        _systemParamsHelper = systemParamsHelper;

    }

    public async Task<ResultObj> AddLoadServer(LoadServer loadServer)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();

            try
            {
                // Add to database
                await monitorContext.LoadServers.AddAsync(loadServer);
                await monitorContext.SaveChangesAsync();

                // Update cache (consider using RefreshLoadServers if needed)
                _cachedLoadServers.Add(loadServer);

                return new ResultObj { Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error adding LoadServer: {ex.Message}");
                return new ResultObj { Success = false, Message = "Error adding LoadServer." };
            }
        }
    }
    public async Task<List<LoadServer>> GetCachedLoadServers()
    {

        if (_cachedLoadServers == null || _cachedLoadServers.Count == 0) _cachedLoadServers = await GetAllLoadServersDBNoTracking();
        return _cachedLoadServers;

    }
    public async Task UpdateCachedLoadServer(LoadServer newLoadServer)
    {
        var cachedLoadservers = await GetCachedLoadServers();
        var loadServer = cachedLoadservers.Where(w => w.ID == newLoadServer.ID).FirstOrDefault();
        if (loadServer == null)
        {
            _logger.LogError($" Error ; Unabled to update cachedLoadserver . Could not find loadServer with Id {newLoadServer.ID}");
            return;
        }
        loadServer.SetFields(newLoadServer);

    }
    public async Task<List<LoadServer>> GetAllLoadServersDBNoTracking()
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
            var loadServers = await monitorContext.LoadServers.AsNoTracking().ToListAsync();
            return loadServers;
        }

    }


    public async Task<LoadServer> GetLoadServerFromUserID(string userId)
    {
        var cachedLoadservers = await GetCachedLoadServers();
        if (cachedLoadservers==null || cachedLoadservers.Count==0) return new LoadServer();

        return cachedLoadservers.Where(w => w.UserID == userId).FirstOrDefault();

    }
    public async Task<LoadServer?> GetLoadServerFromUserIDDB(string userId)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            MonitorContext monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
            return await monitorContext.LoadServers.Where(w => w.UserID == userId).FirstOrDefaultAsync();
        }

    }


}