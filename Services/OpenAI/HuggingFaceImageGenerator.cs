using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NetworkMonitor.Service.Services.OpenAI;
using NetworkMonitor.Utils;
using NetworkMonitor.Objects;using NetworkMonitor.Objects;
using NetworkMonitor.Service.Services.OpenAI;

public class HuggingFaceImageGenerator : IImageGenerator
{
    private readonly string _apiKey;
    private readonly string _apiUrl;
    private readonly HttpClient _client;

    public HuggingFaceImageGenerator(IConfiguration config, HttpClient client)
    {
        _apiKey = config["HuggingFace:ApiKey"] ?? "Missing";
        _apiUrl = config["HuggingFace:ApiUrl"] ?? "Mod";
        _client = client;
    }

    public async Task<TResultObj<ImageResponse>> GenerateImage(string prompt)
    {
        var result = new TResultObj<ImageResponse> { Message = "SERVICE: GenerateImageUsingHuggingFace:" };
        string responseBody = "";
        string url = _apiUrl + "/v3beta/flux-1-schnell";
        string jsonPayload = "";
        try
        {
            var requestPayload = new HuggingFaceImageRequest
            {
                seed = SaltGeneration.GetRandomUInt(),
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
            var hugResult = JsonUtils.GetJsonObjectFromString<HuggingFaceImageResponse>(responseBody);
            var convertedData = new ImageResponse();
            foreach (HuggingFaceImageData hfImageData in hugResult.images)
            {
                var imageData = new ImageData() { url = hfImageData.image_url };
                convertedData.data.Add(imageData);
            }
            result.Data = convertedData;
            result.Success = true;
        }
        catch (System.Exception ex)
        {
            result.Success = false;
            result.Message += $" Error generating Huggingface image at {url} with payload {jsonPayload} got reponse {responseBody}. Error was: {ex.Message}";
        }

        return result;
    }
}
