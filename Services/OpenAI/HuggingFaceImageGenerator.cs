using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using NetworkMonitor.Service.Services.OpenAI;
using NetworkMonitor.Utils;
using NetworkMonitor.Objects;
using NetworkMonitor.Service.Services.OpenAI;

public class HuggingFaceImageGenerator : IImageGenerator
{
    private readonly string _apiKey;
    private readonly string _apiUrl;
    private readonly string _picModel;
    private readonly int _width;
    private readonly int _height;
    private readonly int _steps;
    private readonly int _imageNum;
    private readonly string _responseImageType;
    private readonly HttpClient _client;

    public HuggingFaceImageGenerator(IConfiguration config, HttpClient client)
    {
        _apiKey = config["HuggingFace:ApiKey"] ?? "Missing";
        _apiUrl = config["HuggingFace:ApiUrl"] ?? "https://api.novita.ai";
        _width = int.TryParse(config["HuggingFace:Width"], out var w) ? w : 512;
        _height = int.TryParse(config["HuggingFace:Height"], out var h) ? h : 512;
        _steps = int.TryParse(config["HuggingFace:Steps"], out var s) ? s : 4;
        _imageNum = int.TryParse(config["HuggingFace:ImageNum"], out var n) ? n : 1;
        _responseImageType = config["HuggingFace:ResponseImageType"] ?? "png";
        _client = client;
    }

    /// <summary>
    /// Returns true if the HuggingFace result indicates success and contains images.
    /// </summary>
    private static bool IsHuggingFaceSuccess(HuggingFaceAsyncResult? hfResult)
    {
        if (hfResult == null || hfResult.images == null || hfResult.images.Count == 0)
            return false;

        var status = hfResult.status?.ToLowerInvariant() ?? "";
        return status == "succeeded"
            || status == "task_status_succeed"
            || status == "task_status_succeeded";
    }

    public async Task<TResultObj<ImageResponse>> GenerateImage(string prompt)
    {
        var result = new TResultObj<ImageResponse> { Message = "SERVICE: GenerateImageUsingHuggingFace:" };
        string responseBody = "";
        string url = $"{_apiUrl}/v3beta/flux-1-schnell";
        string jsonPayload = "";
        try
        {
            var requestPayload = new HuggingFaceImageRequest
            {
                response_image_type = _responseImageType,
                prompt = prompt,
                seed = SaltGeneration.GetRandomUInt(),
                steps = _steps,
                width = _width,
                height = _height,
                image_num = _imageNum
            };

            jsonPayload = JsonUtils.WriteJsonObjectToString(requestPayload);
            StringContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            responseBody = await response.Content.ReadAsStringAsync();
            var taskResult = JsonUtils.GetJsonObjectFromString<HuggingFaceAsyncTaskResponse>(responseBody);
            string taskId = null;
            if (taskResult != null && taskResult.task != null && !string.IsNullOrEmpty(taskResult.task.task_id))
            {
                taskId = taskResult.task.task_id;
            }
            else if (taskResult != null && taskResult.images != null && taskResult.images.Count > 0)
            {
                // Sometimes the image is returned immediately
                var convertedData = new ImageResponse();
                foreach (var img in taskResult.images)
                {
                    var imageData = new ImageData() { url = img.image_url };
                    convertedData.data.Add(imageData);
                }
                result.Data = convertedData;
                result.Success = true;
                return result;
            }
            else
            {
                result.Success = false;
                result.Message += " Error: No task_id or images received from HuggingFace.";
                return result;
            }

            // Poll for result
            string resultUrl = $"{_apiUrl}/v3beta/async/task-result?task_id={taskId}";
            HuggingFaceAsyncResult? hfResult = null;
            int maxTries = 20;
            int delayMs = 3000;
            for (int i = 0; i < maxTries; i++)
            {
                await Task.Delay(delayMs);
                using var resultRequest = new HttpRequestMessage(HttpMethod.Get, resultUrl);
                resultRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                var resultResponse = await _client.SendAsync(resultRequest);
                var resultBody = await resultResponse.Content.ReadAsStringAsync();
                hfResult = JsonUtils.GetJsonObjectFromString<HuggingFaceAsyncResult>(resultBody);

                if (hfResult != null && hfResult.images != null && hfResult.images.Count > 0)
                {
                    break;
                }
            }

            if (hfResult == null || hfResult.images == null || hfResult.images.Count == 0)
            {
                result.Success = false;
                result.Message += " Error: HuggingFace image generation did not succeed or returned no images.";
                return result;
            }

            var convertedData2 = new ImageResponse();
            foreach (var img in hfResult.images)
            {
                var imageData = new ImageData() { url = img.image_url };
                convertedData2.data.Add(imageData);
            }
            result.Data = convertedData2;
            result.Success = true;
        }
        catch (System.Exception ex)
        {
            result.Success = false;
            result.Message += $" Error generating Huggingface image at {url} with payload {jsonPayload} got response {responseBody}. Error was: {ex.Message}";
        }

        return result;
    }
}
