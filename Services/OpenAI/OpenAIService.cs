using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Service.Services.OpenAI;
using NetworkMonitor.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using NetworkMonitor.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;

namespace NetworkMonitor.Data.Services
{


    public interface IOpenAIService
    {
        /// <summary>
        /// Basic question (chat) call to OpenAI.
        /// </summary>
        Task<TResultObj<string?>> AskQuestion(string question, string systemPrompt);

        /// <summary>
        /// Given a block of text, generate an image representing that text.
        /// </summary>
        Task<TResultObj<ImageResponse>> GenerateImageFromAnswer(string answer);

        /// <summary>
        /// Attempts to produce a specialized "host report" using an external LLM service or sub-service.
        /// </summary>
        Task<ResultObj> GetSystemLLMResponse(string title, string focus);

        Task ProcessorImage(ImageResponse? imageResponse, string imageFilePath);
    }
    public class OpenAIService : IOpenAIService
    {
        private readonly HttpClient _client;
        private readonly string _openAiApiKey;
        private readonly string _openAiEndpointUrlBase;
        private readonly string _huggingFaceApiKey;
        private readonly string _huggingFaceApiUrl;
        private readonly string  _huggingFacePicModel;
        private readonly string    _huggingFaceModel;
        private readonly string _openAiModel;
        private readonly string _openAiPicModel;
        private readonly string _llmRunnerType;
        private string _questionModel = "OpenAI";
        private string _imageModel = "HuggingFace";
        private readonly bool _createImages = true;
        private readonly ILogger<OpenAIService> _logger;
        private readonly IDataLLMService _dataLLMService;

        public OpenAIService(
            IConfiguration config,
            ILogger<OpenAIService> logger,
            IDataLLMService dataLLMService)
        {
            _logger = logger;
            _dataLLMService = dataLLMService;

            // OpenAI configurations
            _openAiApiKey = config["OpenAI:ApiKey"] ?? "Missing";
            _openAiEndpointUrlBase = config["OpenAI:EndpointUrlBase"] ?? "https://api.openai.com";
            _openAiPicModel = config["OpenAI:PicModel"] ?? "dall-e-3";
            _openAiModel = config["OpenAI:Model"] ?? "gpt-4.1-mini";

            // Hugging Face configurations
            _huggingFaceApiKey = config["HuggingFace:ApiKey"] ?? "Missing";
            _huggingFaceApiUrl = config["HuggingFace:ApiUrl"] ?? "Mod";
            _huggingFacePicModel = config["HuggingFace:PicModel"] ?? "cyberrealistic_v32_81390.safetensors";
            _huggingFaceModel = config["HuggingFace:Model"] ?? "qwen/qwen3-4b-fp8";




            _llmRunnerType = config["LlmRunnerType"] ?? "TurboLLM";
            // Image generation toggle
            _createImages = bool.TryParse(config["CreateImages"], out bool createImages) ? createImages : true;

            // HttpClient setup
            _client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        }

        public async Task<TResultObj<string?>> AskQuestion(string question, string systemPrompt)
        {
            if (_questionModel.Equals("HuggingFace", StringComparison.OrdinalIgnoreCase))
            {
                return await AskQuestionUsingHuggingFace(question);
            }
            else
            {
                return await AskQuestionUsingOpenAI(question, systemPrompt);
            }
        }

        private async Task<TResultObj<string?>> AskQuestionUsingOpenAI(string question, string systemPrompt)
        {
            var result = new TResultObj<string?>
            {
                Message = "SERVICE: AskQuestionUsingOpenAI:"
            };

            var messages = new List<Message>
    {
        new() { role = "system", content = systemPrompt },
        new() { role = "user", content = question }
    };

            var contentObject = new ContentObject
            {
                model = _openAiModel,
                messages = messages
            };
            var content = new StringContent(JsonUtils.WriteJsonObjectToString(contentObject), Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_openAiEndpointUrlBase}/v1/chat/completions")
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _openAiApiKey);


            _logger.LogDebug("SERVICE: AskQuestion Payload: " + content);

            try
            {
                var response = await _client.SendAsync(request);
                response.EnsureSuccessStatusCode();



                string responseBody = await response.Content.ReadAsStringAsync();
                var chatCompletion = JsonUtils.GetJsonObjectFromString<ChatCompletion>(responseBody);

                if (chatCompletion?.Choices == null || !chatCompletion.Choices.Any())
                {
                    result.Success = false;
                    result.Message += " Error: No Chat Result received.";
                    return result;
                }

                var firstChoice = chatCompletion.Choices.OrderBy(x => x.Index).FirstOrDefault()?.Message?.Content;

                if (!string.IsNullOrEmpty(firstChoice))
                {
                    result.Success = true;
                    result.Data = firstChoice;
                    result.Message += " Success: Got chat completion.";
                }
                else
                {
                    result.Success = false;
                    result.Message += " Error: No content in first choice.";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message += " Exception while requesting chat completion: " + ex.Message;
                _logger.LogError(result.Message);
            }

            return result;
        }

        private async Task<TResultObj<string?>> AskQuestionUsingHuggingFace(string question)
        {
            var result = new TResultObj<string?>
            {
                Message = "SERVICE: AskQuestionUsingHuggingFace:"
            };

            try
            {
                var payload = new { inputs = question };
                var stringContent = new StringContent(JsonUtils.WriteJsonObjectToString(payload), Encoding.UTF8, "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, _huggingFaceApiUrl)
                {
                    Content = stringContent
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _huggingFaceApiKey);

                var response = await _client.SendAsync(request);
                response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();

                // Assuming Hugging Face returns text content directly
                if (!string.IsNullOrEmpty(responseBody))
                {
                    result.Success = true;
                    result.Data = responseBody;
                    result.Message += " Success: Got response from Hugging Face.";
                }
                else
                {
                    result.Success = false;
                    result.Message += " Error: Empty response from Hugging Face.";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message += " Exception while requesting response from Hugging Face: " + ex.Message;
                _logger.LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// Generates an image using the specified model type.
        /// </summary>
        public async Task<TResultObj<ImageResponse>> GenerateImage(string prompt)
        {
            if (!_createImages)
            {
                return new TResultObj<ImageResponse>
                {
                    Success = false,
                    Message = "Image generation is disabled in the configuration."
                };
            }
            _logger.LogInformation($" Generating an image with prompt {prompt}");
            return _imageModel.Equals("HuggingFace", StringComparison.OrdinalIgnoreCase)
                ? await GenerateImageUsingHuggingFace(prompt)
                : await GenerateImageUsingOpenAI(prompt);
        }

        /// <summary>
        /// Generates an image using OpenAI's DALL-E model.
        /// </summary>
        private async Task<TResultObj<ImageResponse>> GenerateImageUsingOpenAI(string prompt)
        {
            var result = new TResultObj<ImageResponse> { Message = "SERVICE: GenerateImageUsingOpenAI:" };
            string responseBody = "";
            string url = "";
            string jsonPayload = "";
            try
            {
                var requestPayload = new OpenAIImageGenerationRequest
                {
                    model = _openAiPicModel,
                    prompt = prompt
                };

                url = $"{_openAiEndpointUrlBase}/v1/images/generations";
                jsonPayload = JsonUtils.WriteJsonObjectToString(requestPayload);
                StringContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = content
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _openAiApiKey);

                var response = await _client.SendAsync(request);
                response.EnsureSuccessStatusCode();

                responseBody = await response.Content.ReadAsStringAsync();
                result.Data = JsonUtils.GetJsonObjectFromString<ImageResponse>(responseBody);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message += $" Error generating OpenAI image at {url} with payload {jsonPayload} got reponse {responseBody}. Error was: {ex.Message}";
                _logger.LogError(result.Message);
                _logger.LogDebug($"Request payload: {responseBody}");
            }

            return result;
        }

        /// <summary>
        /// Generates an image using OpenAI's DALL-E model.
        /// </summary>
        private async Task<TResultObj<ImageResponse>> GenerateImageUsingHuggingFace(string prompt)
        {
            var result = new TResultObj<ImageResponse> { Message = "SERVICE: GenerateImageUsingHugginFace:" };
            string responseBody = "";
            string url = "";
            string jsonPayload = "";
            try
            {
                var requestPayload = new ImageRequest
                {
                    model_name = _huggingFacePicModel,
                    prompt = prompt
                };

                url = _huggingFaceApiUrl+"/v3/async/txt2img";
                jsonPayload = JsonUtils.WriteJsonObjectToString(requestPayload);
                StringContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = content
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _huggingFaceApiKey);

                var response = await _client.SendAsync(request);
                response.EnsureSuccessStatusCode();

                responseBody = await response.Content.ReadAsStringAsync();
                var hugResult = JsonUtils.GetJsonObjectFromString<HuggingFaceImageResponse>(responseBody);
                var convertedData = new ImageResponse();
                foreach (string str in hugResult.images_encoded)
                {
                    var imageData = new ImageData() { b64_json = str };
                    convertedData.data.Add(imageData);
                }
                result.Data = convertedData;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message += $" Error generating Huggingface image at {url} with payload {jsonPayload} got reponse {responseBody}. Error was: {ex.Message}";
                _logger.LogError(result.Message);
                _logger.LogDebug($"Request payload: {responseBody}");
            }

            return result;
        }

        /* /// <summary>
         /// Generates an image using Hugging Face's model.
         /// </summary>
         private async Task<TResultObj<ImageResponse>> GenerateImageUsingHuggingFace(string prompt)
         {
             var result = new TResultObj<ImageResponse> { Message = "SERVICE: GenerateImageUsingHuggingFace:" };
             const int maxRetries = 10; // Number of retry attempts
             const int delayMilliseconds = 30000; // Delay between retries in milliseconds
             const int requestTimeoutMilliseconds = 1200000; // Timeout per request (2 minutes)

             for (int attempt = 1; attempt <= maxRetries; attempt++)
             {
                 try
                 {
                     _logger.LogInformation($"Attempt {attempt} to call Hugging Face API.");

                     // Manually construct the JSON payload
                     var payloadJson = $"{{\"inputs\":\"{prompt.Replace("\"", "\\\"")}\"}}";
                     var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                     // Create a custom HttpClientHandler with timeout
                     using var client = new HttpClient
                     {
                         Timeout = TimeSpan.FromMilliseconds(requestTimeoutMilliseconds) // Set timeout for this request
                     };

                     // Configure the request
                     using var request = new HttpRequestMessage(HttpMethod.Post, _huggingFaceApiUrl)
                     {
                         Content = content
                     };
                     request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _huggingFaceApiKey);

                     // Send the request
                     var response = await client.SendAsync(request);
                     response.EnsureSuccessStatusCode();

                     // Process the response
                     var imageBytes = await response.Content.ReadAsByteArrayAsync();
                     result.Data = new ImageResponse
                     {
                         data = new List<ImageData> { new ImageData { b64_json = Convert.ToBase64String(imageBytes) } }
                     };
                     result.Success = true;

                     _logger.LogInformation($"Hugging Face API call succeeded on attempt {attempt}.");
                     return result; // Exit the loop on success
                 }
                 catch (HttpRequestException ex) when (attempt < maxRetries)
                 {
                     _logger.LogWarning($"Hugging Face API call failed on attempt {attempt}. Retrying... Error: {ex.Message}");
                     await Task.Delay(delayMilliseconds); // Wait before retrying
                 }
                 catch (Exception ex)
                 {
                     result.Success = false;
                     result.Message += $" Error generating image with Hugging Face: {ex.Message}";
                     _logger.LogError(result.Message);

                     // If it's the last attempt or an unexpected error, break the loop
                     if (attempt == maxRetries)
                         break;
                 }
             }

             // Return the result after all attempts
             result.Success = false;
             result.Message += " All retry attempts failed.";
             return result;
         }*/


        /// <summary>
        /// Generates an image prompt and then creates the image using the configured model type.
        /// </summary>
        public async Task<TResultObj<ImageResponse>> GenerateImageFromAnswer(string answer)
        {
            var result = new TResultObj<ImageResponse> { Message = "SERVICE: GenerateImageFromAnswer:" };

            if (!_createImages)
            {
                result.Success = false;
                result.Message += " Image generation is disabled in the configuration.";
                return result;
            }

            try
            {
                // We first ask GPT to produce a prompt for DALL-E
                var systemPrompt = "You are an assistant specialized in generating image prompts for text-to-image models. You will receive blog post text and respond only with a prompt designed to create an image that best represents the given content. The image should be realistic and engaging. Avoid abstract icons or overly stylized visuals. Don't be to specific on details and be creative with the visual aspects of the image.";
                var question = $"Generate a prompt for an image creation model that best represents this blog post: \"{answer}\". " +
                                "Only respond with the image generation prompt.";

                var chatResult = await AskQuestion(question, systemPrompt);
                if (!chatResult.Success || string.IsNullOrEmpty(chatResult.Data))
                {
                    result.Success = false;
                    result.Message += " Failed to generate an image prompt.";
                    return result;
                }

                var imageResult = await GenerateImage(chatResult.Data);
                result.Success = imageResult.Success;
                result.Data = imageResult.Data;
                result.Message += imageResult.Message;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message += $" Error generating image from answer: {ex.Message}";
                _logger.LogError(result.Message);
            }

            return result;
        }


        /// <summary>
        /// Saves the image (from <see cref="ImageResponse"/>) to disk, handling either base64 or direct URL downloads.
        /// This matches the logic you placed in the TODO comment.
        /// </summary>
        public async Task ProcessorImage(ImageResponse? imageResponse, string imageFilePath)
        {
            if (imageResponse?.data == null || imageResponse.data.Count == 0)
            {
                _logger.LogWarning("No image data found to process.");
                return;
            }

            var imageData = imageResponse.data[0];
            // If you already have an ImageProcessor wrapper class, use it here. 
            var imageProcessor = new ImageProcessor(_client);
            var origImageFilePath = Path.ChangeExtension(imageFilePath, null)
                                    + "-orig"
                                    + Path.GetExtension(imageFilePath);

            try
            {
                // If the image data is in base64, decode and save
                if (!string.IsNullOrEmpty(imageData.b64_json))
                {
                    // Decode and save the base64 image 
                    await imageProcessor.DecodeBase64ImageAsync(imageData.b64_json, imageFilePath);

                    // Optionally save the "original" image without compression or re-encoding
                    await imageProcessor.DecodeBase64OrigImageAsync(imageData.b64_json, origImageFilePath);
                }
                // If the image data is a URL, download directly
                else if (!string.IsNullOrEmpty(imageData.url))
                {
                    // Download and save
                    await imageProcessor.DownloadImageAsync(imageData.url, imageFilePath);

                    // Download an uncompressed/original version if desired
                    await imageProcessor.DownloadOrigImageAsync(imageData.url, origImageFilePath);
                }
                else
                {
                    _logger.LogWarning("Image data is missing b64_json and url fields.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error while processing or saving the image: " + ex.Message);
            }
        }


        /// <summary>
        /// Retrieves a specialized LLM-based blog report (title + focus).
        /// </summary>
        public async Task<ResultObj> GetSystemLLMResponse(string title, string focus)
        {
            var result = new ResultObj();
            var user = new UserInfo() { UserID = "default" };

            var serviceObj = new LLMServiceObj
            {
                RequestSessionId = Guid.NewGuid().ToString(),
                MessageID = StringUtils.GetNanoid(),
                UserInfo = user,
                SourceLlm = "blogmonitor",
                DestinationLlm = "blogmonitor",
                IsSystemLlm = true,
                LLMRunnerType = _llmRunnerType
            };

            // 1. Start
            TResultObj<LLMServiceObj> resultStart;
            try
            {
                resultStart = await _dataLLMService.SystemLlmStart(serviceObj);
                if (!resultStart.Success || resultStart.Data == null)
                {
                    result.Message = resultStart.Message;
                    _logger.LogError(resultStart.Message);
                    return result;
                }
                serviceObj = resultStart.Data;
                _logger.LogInformation(resultStart.Message);
            }
            catch (Exception e)
            {
                result.Message = $" Error : could not start llm. Error was : {e.Message}";
                _logger.LogError(result.Message);
                return result;
            }

            // 2. Generate content
            try
            {

                serviceObj.UserInput = $"Produce a blog post guiding the user on how to use the Free Network Monitor Assistant to achieve this: \"{title}\". Use the focus: \"{focus}\" to ensure the blog is specific and aligned with the topic. ONLY REPLY WITH THE Blog. DO NOT include the title in the blog post.";
                result = await _dataLLMService.LLMInput(serviceObj);
            }
            catch (Exception e)
            {
                var err = $"Error: Could not produce blog from LLM output. {e.Message}";
                _logger.LogError(err);
                result.Message = err;
            }

            // 3. Stop
            try
            {
                var resultStop = await _dataLLMService.SystemLlmStop(serviceObj);
                if (!resultStop.Success)
                {
                    _logger.LogError(resultStop.Message);
                }
                else
                {
                    _logger.LogInformation(resultStop.Message);
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"Error: Could not stop LLM. {e.Message}");
            }

            return result;
        }
    }
}
