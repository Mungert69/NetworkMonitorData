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
    Task<TResultObj<LLMServiceObj>> LLMOutput(LLMServiceObj serviceObj);
    Task<TResultObj<LLMServiceObj>> LLMStarted(LLMServiceObj serviceObj);
    Task<TResultObj<LLMServiceObj>> LLMStopped(LLMServiceObj serviceObj);

    Task<ResultObj> SystemLlmStop(LLMServiceObj serviceObj);
    Task<TResultObj<LLMServiceObj>> SystemLlmStart(LLMServiceObj serviceObj);
    Task<ResultObj> LLMInput(LLMServiceObj serviceObj);
}

public class DataLLMService : IDataLLMService
{
    private readonly ILogger _logger;
    private readonly IRabbitRepo _rabbitRepo;
    private readonly IUserRepo _userRepo;

    // Dictionary to store tasks that wait for LLMStarted and LLMOutput responses.
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TResultObj<LLMServiceObj>>> _sessionStartTasks = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TResultObj<LLMServiceObj>>> _sessionOutputTasks = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TResultObj<LLMServiceObj>>> _sessionStopTasks = new();

    private static readonly TimeSpan TimeoutDuration = TimeSpan.FromMinutes(10);

    public DataLLMService(IRabbitRepo rabbitRepo, ILogger<DataLLMService> logger, IUserRepo userRepo)
    {
        _rabbitRepo = rabbitRepo;
        _logger = logger;
        _userRepo = userRepo;
    }

    public async Task<TResultObj<LLMServiceObj>> SystemLlmStart(LLMServiceObj serviceObj)
    {
        var result = new TResultObj<LLMServiceObj> { Message = "DataLLMService : SystemLlmStart : " };

        try
        {
            var tcs = new TaskCompletionSource<TResultObj<LLMServiceObj>>();
            _sessionStartTasks[serviceObj.RequestSessionId] = tcs;

            await _rabbitRepo.PublishAsync("systemLlmStart", serviceObj);
            result.Success = true;
            result.Message += " Success : published system LLM start";

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeoutDuration));
            if (completedTask == tcs.Task)
            {
                var taskResult = await tcs.Task;
                if (taskResult.Data != null)
                {
                    result.Message = taskResult.Data.LlmMessage;
                    result.Success = taskResult.Data.ResultSuccess;
                    result.Data = taskResult.Data;
                }
                else
                {
                    result.Success = false;
                    result.Message = "Error : the result returned from SystemLlmStart did not return any data.";
                    _logger.LogError(result.Message);
                }

                return result;
            }
            else
            {
                _sessionStartTasks.TryRemove(serviceObj.RequestSessionId, out _);
                result.Success = false;
                result.Message += "Error: Timeout waiting for LLMStarted response.";
                _logger.LogError(result.Message);
                return result;
            }
        }
        catch (Exception e)
        {
            result.Success = false;
            result.Message += $" Error : Unable to send start message. The error was : {e.Message}";
            _logger.LogError(result.Message);
            return result;
        }
    }

    public async Task<ResultObj> SystemLlmStop(LLMServiceObj serviceObj)
    {
        var result = new ResultObj { Message = "DataLLMService : SystemLlmStop : " };

        try
        {
            var tcs = new TaskCompletionSource<TResultObj<LLMServiceObj>>();
            _sessionStopTasks[serviceObj.RequestSessionId] = tcs;

            await _rabbitRepo.PublishAsync("systemLlmStop", serviceObj);
            result.Success = true;
            result.Message += " Success : published system LLM stop";

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeoutDuration));
            if (completedTask == tcs.Task)
            {
                var taskResult = await tcs.Task;
                result.Message = taskResult.Data.LlmMessage;
                result.Success = taskResult.Data.ResultSuccess;
                return result;
            }
            else
            {
                _sessionStopTasks.TryRemove(serviceObj.RequestSessionId, out _);
                result.Success = false;
                result.Message += "Error: Timeout waiting for LLMStopped response.";
                _logger.LogError(result.Message);
                return result;
            }
        }
        catch (Exception e)
        {
            result.Success = false;
            result.Message += $" Error : Unable to send stop message. The error was : {e.Message}";
            _logger.LogError(result.Message);
            return result;
        }
    }


    public async Task<ResultObj> LLMInput(LLMServiceObj serviceObj)
    {
        var result = new ResultObj { Message = "DataLLMService : LLMInput : " };

        try
        {
            var tcs = new TaskCompletionSource<TResultObj<LLMServiceObj>>();
            _sessionOutputTasks[serviceObj.RequestSessionId] = tcs;

            await _rabbitRepo.PublishAsync("systemLlmInput", serviceObj);
            result.Success = true;
            result.Message += " Success : published system LLM Input";

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeoutDuration));
            if (completedTask == tcs.Task)
            {
                var taskResult = await tcs.Task;
                result.Message = taskResult.Data.LlmMessage;
                result.Success = taskResult.Data.ResultSuccess;
                return result;
            }
            else
            {
                _sessionOutputTasks.TryRemove(serviceObj.RequestSessionId, out _);
                result.Success = false;
                result.Message += $" Error : Timeout waiting for LLMOutput response. SessionId: {serviceObj.RequestSessionId}";
                _logger.LogError(result.Message);
                return result;
            }
        }
        catch (Exception e)
        {
            result.Success = false;
            result.Message += $" Error : Unable to send Input message for LLMOutput response. SessionId: {serviceObj.RequestSessionId}. The error was : {e.Message}";
            _logger.LogError(result.Message);
            return result;
        }
    }

    public async Task<TResultObj<LLMServiceObj>> LLMOutput(LLMServiceObj serviceObj)
    {
        var result = new TResultObj<LLMServiceObj> { Message = "DataLLMService : LLMOutput " };

        if (_sessionOutputTasks.TryRemove(serviceObj.RequestSessionId, out var tcs))
        {
            result.Success = true;
            result.Data = serviceObj;
            tcs.SetResult(result);
        }
        else
        {
            result.Message = $" Error: No matching session found for LLMOutput with SessionId: {serviceObj.RequestSessionId}";
            _logger.LogWarning(result.Message);
            result.Success = false;
        }

        return result;
    }

    public async Task<TResultObj<LLMServiceObj>> LLMStarted(LLMServiceObj serviceObj)
    {
        var result = new TResultObj<LLMServiceObj> { Message = "DataLLMService : LLMStarted " };

        if (_sessionStartTasks.TryRemove(serviceObj.RequestSessionId, out var tcs))
        {
            result.Success = true;
            result.Data = serviceObj;
            tcs.SetResult(result);
        }
        else
        {
            result.Success = false;
            result.Message += $"No matching session found for LLMStarted with SessionId: {serviceObj.RequestSessionId}";
            _logger.LogError(result.Message);
        }

        return result;
    }
    public async Task<TResultObj<LLMServiceObj>> LLMStopped(LLMServiceObj serviceObj)
    {
        var result = new TResultObj<LLMServiceObj> { Message = "DataLLMService : LLMStopped " };

        if (_sessionStopTasks.TryRemove(serviceObj.RequestSessionId, out var tcs))
        {
            result.Success = true;
            result.Data = serviceObj;
            tcs.SetResult(result);
        }
        else
        {
            result.Success = false;
            result.Message += $"No matching session found for LLMStopped with SessionId: {serviceObj.RequestSessionId}";
            _logger.LogError(result.Message);
        }

        return result;
    }
}
