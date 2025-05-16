using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Service.Services.OpenAI;
using NetworkMonitor.Utils;
using NetworkMonitor.Objects;
using Microsoft.Extensions.Configuration;

public class OpenAIImageGenerator : IImageGenerator
{
    private readonly string _apiKey;
    private readonly string _endpointUrlBase;
    private readonly string _picModel;
    private readonly HttpClient _client;

    public OpenAIImageGenerator(IConfiguration config, HttpClient client)
    {
        _apiKey = config["OpenAI:ApiKey"] ?? "Missing";
        _endpointUrlBase = config["OpenAI:EndpointUrlBase"] ?? "https://api.openai.com";
        _picModel = config["OpenAI:PicModel"] ?? "dall-e-3";
        _client = client;
    }

    public async Task<TResultObj<ImageResponse>> GenerateImage(string prompt)
    {
        var result = new TResultObj<ImageResponse> { Message = "SERVICE: GenerateImageUsingOpenAI:" };
        string responseBody = "";
        string url = $"{_endpointUrlBase}/v1/images/generations";
        string jsonPayload = "";
        try
        {
            var requestPayload = new ImageRequest
            {
                model = _picModel,
                prompt = prompt
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
            result.Data = JsonUtils.GetJsonObjectFromString<ImageResponse>(responseBody);
            result.Success = true;
        }
        catch (System.Exception ex)
        {
            result.Success = false;
            result.Message += $" Error generating OpenAI image at {url} with payload {jsonPayload} got reponse {responseBody}. Error was: {ex.Message}";
        }

        return result;
    }
}
