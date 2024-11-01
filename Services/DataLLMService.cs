
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Entity;
using NetworkMonitor.Data.Repo;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Data;
using System.Threading;
using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using NetworkMonitor.Objects.Repository;
using Microsoft.Extensions.Configuration;
using NetworkMonitor.Utils.Helpers;
namespace NetworkMonitor.Data.Services;

public interface IDataLLMService
{
  
    Task<ResultObj> LLMOutput(LLMServiceObj serviceObj);
        Task<ResultObj> LLMStarted(LLMServiceObj serviceObj);
}


public class DataLLMService : IDataLLMService
{
    private readonly ILogger _logger;
    private readonly IRabbitRepo _rabbitRepo;
    private readonly IUserRepo _userRepo;
    

    public DataLLMService(IRabbitRepo rabbitRepo, ILogger<DataLLMService> logger, IUserRepo userRepo)
    {
        _rabbitRepo = rabbitRepo;
        _logger = logger;
        _userRepo = userRepo;
     
    }

 
    public async Task<ResultObj> LLMOutput(LLMServiceObj serviceObj)
    {
        var result = new ResultObj();
        var resultUpdate = new ResultObj();
        result.Message = " DataLLMService : LLMOutput : ";


        return result;
    }

    public async Task<ResultObj> LLMStarted(LLMServiceObj serviceObj)
    {
        var result = new ResultObj();
        var resultUpdate = new ResultObj();
        result.Message = " DataLLMService : LLMStarted : ";


        return result;
    }
   
}