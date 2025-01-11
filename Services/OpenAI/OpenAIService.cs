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
        Task<TResultObj<ImageResponse>> GenerateImage(string answer);

        /// <summary>
        /// Attempts to produce a specialized "host report" using an external LLM service or sub-service.
        /// </summary>
        Task<ResultObj> GetLLMReportForHost(string title, string focus);
    }
    public class OpenAIService : IOpenAIService
    {
        private readonly HttpClient _client;
        private readonly string _apiKey;
        private readonly string _endpointUrlBase;
        private readonly string _model;
        private readonly string _picModel;
        private readonly string _llmRunnerType;
        private readonly ILogger<OpenAIService> _logger;
        private readonly IDataLLMService _dataLLMService;

        public OpenAIService(
            IConfiguration config,
            ILogger<OpenAIService> logger,
            IDataLLMService dataLLMService)
        {
            _logger = logger;
            _dataLLMService = dataLLMService;

            _endpointUrlBase = config["OpenAI:EndpointUrlBase"] ?? "https://api.openai.com";
            _model = config["OpenAI:Model"] ?? "gpt-3.5-turbo";
            _picModel = config["OpenAI:PicModel"] ?? "dall-e-3";
            _apiKey = config["OpenAI:ApiKey"] ?? "Missing";
            _llmRunnerType = config["LlmRunnerType"] ?? "TurboLLM";

            _client = new HttpClient()
            {
                Timeout = TimeSpan.FromMinutes(10),
                BaseAddress = new Uri(_endpointUrlBase),
            };
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        /// Asks a question (chat completion) using the configured model.
        /// </summary>
        public async Task<TResultObj<string?>> AskQuestion(string question, string systemPrompt)
        {
            var result = new TResultObj<string?>();
            result.Message = " SERVICE : AskQuestion :";

            var messages = new List<Message>
            {
                new() { role = "system", content = systemPrompt },
                new() { role = "user", content = question }
            };

            var contentObject = new ContentObject()
            {
                model = _model,
                messages = messages
            };

            var jsonPayload = JsonUtils.WriteJsonObjectToString(contentObject);
            _logger.LogDebug(" SERVICE : AskQuestion Payload: " + jsonPayload);

            var stringContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            string endpointUrl = "/v1/chat/completions";

            try
            {
                var response = await _client.PostAsync(endpointUrl, stringContent);
                _logger.LogDebug("Response status code: " + response.StatusCode);
                response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();
                var chatCompletion = JsonUtils.GetJsonObjectFromString<ChatCompletion>(responseBody);

                if (chatCompletion == null || chatCompletion.Choices == null)
                {
                    result.Success = false;
                    result.Message += " Error : No Chat Result received.";
                    return result;
                }

                chatCompletion.Choices.Sort((x, y) => x.Index.CompareTo(y.Index));
                var firstChoice = chatCompletion.Choices.FirstOrDefault()?.Message?.Content;

                if (!string.IsNullOrEmpty(firstChoice))
                {
                    result.Success = true;
                    result.Data = firstChoice;
                    result.Message += " Success : Got chat completion.";
                }
                else
                {
                    result.Success = false;
                    result.Message += " Error : No content in first choice.";
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

        /// <summary>
        /// Generates an image using the pic model (e.g., DALL-E).
        /// </summary>
        public async Task<TResultObj<ImageResponse>> GenerateImage(string answer)
        {
            var result = new TResultObj<ImageResponse>()
            {
                Message = " SERVICE : GenerateImage :"
            };

            // We first ask GPT to produce a prompt for DALL-E
            var systemPrompt = "You are an assistant specialized in generating minimalistic, professional image prompts...";
            var question = $"Generate a prompt for an image creation model ({_picModel}) that best represents this blog post: \"{answer}\". " +
                           "Only respond with the image generation prompt.";

            var chatResult = await AskQuestion(question, systemPrompt);
            result.Message += chatResult.Message;

            if (!chatResult.Success || string.IsNullOrEmpty(chatResult.Data))
            {
                result.Success = false;
                result.Message += " Cannot produce an image prompt for the blog.";
                return result;
            }

            // Now request the actual image
            var imageRequest = new ImageRequest()
            {
                model = _picModel,
                prompt = chatResult.Data!,
                n = 1,
                size = "1024x1024",
                quality = "standard"
            };

            var endpointUrl = "/v1/images/generations";
            var requestPayload = JsonUtils.WriteJsonObjectToString(imageRequest);
            var stringContent = new StringContent(requestPayload, Encoding.UTF8, "application/json");

            try
            {
                var response = await _client.PostAsync(endpointUrl, stringContent);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var imageResponse = JsonUtils.GetJsonObjectFromString<ImageResponse>(responseBody);

                result.Success = true;
                result.Data = imageResponse;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message += " Error generating image: " + ex.Message;
                _logger.LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// Retrieves a specialized LLM-based blog report (title + focus).
        /// </summary>
        public async Task<ResultObj> GetLLMReportForHost(string title, string focus)
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
                serviceObj.UserInput =
                    $"Produce a blog post guiding the user on how to use the Free Network Monitor Assistant: \"{title}\". " +
                    $"Focus: \"{focus}\". ONLY REPLY WITH THE Blog. DO NOT include the title.";
                
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
