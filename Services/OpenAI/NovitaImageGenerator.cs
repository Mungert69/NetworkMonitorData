using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NetworkMonitor.Service.Services.OpenAI;
using NetworkMonitor.Utils;
using NetworkMonitor.Objects;
using System.Collections.Generic;

public class NovitaImageGenerator : IImageGenerator
{
    private readonly string _url;
    private readonly string _apiKey;
    private readonly string _modelName;
    private readonly int _width;
    private readonly int _height;
    private readonly string _samplerName;
    private readonly double _guidanceScale;
    private readonly int _steps;
    private readonly int _imageNum;
    private readonly int _clipSkip;
    private readonly int _seed;
    private readonly HttpClient _client;

    public NovitaImageGenerator(IConfiguration config, HttpClient client)
    {
        _apiKey = config["Novita:ApiKey"] ?? "Missing";
        _url = config["Novita:Url"] ?? "https://api.novita.ai/v3/async/txt2img";
        _modelName = config["Novita:ModelName"] ?? "sciFiDiffusionV10_v10_4985.ckpt";
        _width = int.TryParse(config["Novita:Width"], out var w) ? w : 512;
        _height = int.TryParse(config["Novita:Height"], out var h) ? h : 512;
        _samplerName = config["Novita:SamplerName"] ?? "DPM++ 2S a Karras";
        _guidanceScale = double.TryParse(config["Novita:GuidanceScale"], out var gs) ? gs : 7.5;
        _steps = int.TryParse(config["Novita:Steps"], out var s) ? s : 20;
        _imageNum = int.TryParse(config["Novita:ImageNum"], out var n) ? n : 1;
        _clipSkip = int.TryParse(config["Novita:ClipSkip"], out var cs) ? cs : 1;
        _seed = int.TryParse(config["Novita:Seed"], out var seed) ? seed : -1;
        _client = client;
    }

    /// <summary>
    /// Returns true if the Novita result indicates success and contains images.
    /// </summary>
    private static bool IsNovitaSuccess(NovitaFullResponse? novitaResult)
    {
        if (novitaResult == null || novitaResult.images == null || novitaResult.images.Count == 0)
            return false;

        var status = novitaResult.task?.status?.ToLowerInvariant() ?? "";
        return status == "succeeded"
            || status == "task_status_succeed"
            || status == "task_status_succeeded";
    }

    public async Task<TResultObj<ImageResponse>> GenerateImage(string prompt)
    {
        var result = new TResultObj<ImageResponse> { Message = "SERVICE: GenerateImageUsingNovita:" };
        string responseBody = "";
        string url = _url;
        string jsonPayload = "";
        try
        {
            var requestPayload = new NovitaImageRequestRoot
            {
                request = new NovitaImageRequest
                {
                    model_name = _modelName,
                    prompt = prompt,
                    negative_prompt = "",
                    width = _width,
                    height = _height,
                    sampler_name = _samplerName,
                    guidance_scale = _guidanceScale,
                    steps = _steps,
                    image_num = _imageNum,
                    clip_skip = _clipSkip,
                    seed = _seed
                }
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
            var novitaTask = JsonUtils.GetJsonObjectFromString<NovitaTaskResponse>(responseBody);
            if (novitaTask == null || string.IsNullOrEmpty(novitaTask.task_id))
            {
                result.Success = false;
                result.Message += " Error: No task_id received from Novita.";
                return result;
            }

            // Poll for result
            string resultUrl = $"https://api.novita.ai/v3/async/task-result?task_id={novitaTask.task_id}";
            NovitaFullResponse? novitaResult = null;
            int maxTries = 20;
            int delayMs = 3000;
            for (int i = 0; i < maxTries; i++)
            {
                await Task.Delay(delayMs);
                using var resultRequest = new HttpRequestMessage(HttpMethod.Get, resultUrl);
                resultRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                var resultResponse = await _client.SendAsync(resultRequest);
                var resultBody = await resultResponse.Content.ReadAsStringAsync();
                novitaResult = JsonUtils.GetJsonObjectFromString<NovitaFullResponse>(resultBody);

                if (IsNovitaSuccess(novitaResult))
                {
                    break;
                }
            }

            if (!IsNovitaSuccess(novitaResult))
            {
                result.Success = false;
                result.Message += " Error: Novita image generation did not succeed or returned no images.";
                return result;
            }

            var convertedData = new ImageResponse();
            foreach (var img in novitaResult.images)
            {
                var imageData = new ImageData() { url = img.image_url };
                convertedData.data.Add(imageData);
            }
            result.Data = convertedData;
            result.Success = true;
        }
        catch (System.Exception ex)
        {
            result.Success = false;
            result.Message += $" Error generating Novita image at {url} with payload {jsonPayload} got response {responseBody}. Error was: {ex.Message}";
        }

        return result;
    }
}
