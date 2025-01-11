using System;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Connection;
using NetworkMonitor.Service.Services.OpenAI;
using NetworkMonitor.Data;
using System.Web;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects.Factory;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Globalization;
using System.IO;
using NetworkMonitor.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
namespace NetworkMonitor.Data.Services
{
    public interface IOpenAIService_Old
    {

        Task<TResultObj<string?>> AskQuestion(string question, string systemPrompt);
        void OnStopping();
        Task<ResultObj> PutAnswerIntoBlogs(BlogList blogList, string systemPrompt, bool useDataLLMService);
        Task<ResultObj> ProcessBlogList(string blogFile);
        Task<ResultObj> ProcessBlogListAll(string blogFile);

        Task<ResultObj> ProcessBlogList();
        Task<ResultObj> ProcessBlogListAll();
    }
    public class OpenAIService_Old : IOpenAIService_Old
    {
        private readonly HttpClient _client;
        private readonly string _apiKey;
        private readonly IConfiguration _config;
        private readonly ILogger _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly CancellationToken _token;
        private List<BlogList> _blogList = new List<BlogList>();
        private string _endpointUrlBase = "https://api.openai.com";
        private string _frontEndUrl = "https://freenetworkmonior.click";
        private string _model = "gpt-4o-mini";
        private string _picModel = "dall-e-3";
        private string _llmRunnerType = "TurboLLM";
        private string _systemPrompt = "You are a writing assistant specialized in generating human-like blog posts. The user will provide a title for the blog post, and you will create the content based on that title. Your response should be written in Markdown format, but DO NOT include the title in the response. Ensure the content is detailed, informative, and provides thorough explanations of the topics discussed. Respond strictly with the blog content, omitting the title and any other instructions.";

        private string _blogFile = "BlogList.json";
        private string _blogFileGuides = "BlogListGuides.json";
        private bool _isUsingGuidesFile = false; // Initially start with _blogFile
        private IDataLLMService _dataLLMService;
        private readonly Random _random = new Random();
        public OpenAIService_Old(IConfiguration config, ILogger<OpenAIService> logger, IServiceScopeFactory scopeFactory, CancellationTokenSource cancellationTokenSource, IDataLLMService dataLLMService)
        {
            _config = config;
            _frontEndUrl = _config["FrontEndUrl"] ?? _frontEndUrl;
            _endpointUrlBase = _config["OpenAI:EndpointUrlBase"] ?? _endpointUrlBase;
            _model = _config["OpenAI:Model"] ?? _model;
            _picModel = _config["OpenAI:PicModel"] ?? _picModel;
            _apiKey = _config["OpenAI:ApiKey"] ?? "Missing";
            _llmRunnerType = _config["LlmRunnerType"] ?? _llmRunnerType;
            _token = cancellationTokenSource.Token;
            _token.Register(() => OnStopping());
            _scopeFactory = scopeFactory;
            _logger = logger;
            _dataLLMService = dataLLMService;
            _client = new HttpClient()
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            _client.BaseAddress = new Uri(_endpointUrlBase);

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            CheckInitData();
        }

        private void CheckInitData()
        {
            try {
                 using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                var blogs = context.Blogs.ToList();
                if (blogs.Count == 0)
                {
                    var data = FirstData.getData();
                    foreach (var item in data)
                    {
                        var blog = new Blog();
                        blog.Hash = item.Hash;
                        blog.Markdown = item.Markdown;
                        blog.IsFeatured = item.IsFeatured;
                        blog.IsMainFeatured = item.IsMainFeatured;
                        blog.IsPublished = item.IsPublished;
                        blog.IsVideo = item.IsVideo;
                        blog.VideoTitle = item.VideoTitle;
                        blog.VideoUrl = item.VideoUrl;
                        blog.IsImage = item.IsImage;
                        blog.ImageTitle = item.ImageTitle;
                        blog.ImageUrl = item.ImageUrl;
                        blog.Title = item.Title;
                        blog.IsOnBlogSite = item.IsOnBlogSite;
                        context.Blogs.Add(blog);
                    }
                    context.SaveChanges();
                    _logger.LogInformation(" Success : Added default Blogs to database");
                }
            }
            }
            catch (Exception e){
                 _logger.LogError($" Error : could not add default Blogs to database. Error was :{e.Message}");
            }
           
        }

        private async Task<ResultObj> GetLLMReportForHost(string title, string focus)
        {
            var result = new ResultObj();
            result.Success = false;
            var user = new UserInfo()
            {
                UserID = "default"
            };

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

            var resultStart = new TResultObj<LLMServiceObj>();
            try
            {
                resultStart = await _dataLLMService.SystemLlmStart(serviceObj);
                if (resultStart != null && resultStart.Success && resultStart.Data != null)
                {
                    serviceObj = resultStart.Data;
                    _logger.LogInformation(resultStart.Message);
                }
                else
                {
                    result.Message = resultStart!.Message;
                    _logger.LogError(resultStart.Message);
                    return result;
                }
            }
            catch (Exception e)
            {
                result.Message = $" Error : could not start llm. Error was : {e.Message}";
                _logger.LogError(result.Message);
                return result;
            }



            try
            {
                // Construct the user input with the extracted title and focus
                serviceObj.UserInput = $"Produce a blog post guiding the user on how to use the Free Network Monitor Assistant to achieve this: \"{title}\". Use the focus: \"{focus}\" to ensure the blog is specific and aligned with the topic. ONLY REPLY WITH THE Blog. DO NOT include the title in the blog post.";

                result = await _dataLLMService.LLMInput(serviceObj);
            }
            catch (Exception e)
            {
                _logger.LogError($"Error: Could not produce report from LLM output. Error was: {e.Message}");
            }

            try
            {
                var resultStop = await _dataLLMService.SystemLlmStop(serviceObj);
                if (resultStop.Success)
                {
                    _logger.LogInformation(result.Message);
                }
                else
                {
                    _logger.LogError(result.Message);
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"Error: Could not stop LLM. Error was: {e.Message}");
            }

            return result;
        }

        private (string title, string focus) ExtractTitleAndFocus(string input)
        {
            string title = string.Empty;
            string focus = string.Empty;

            try
            {
                // Use regex to extract the title and focus
                var titleMatch = System.Text.RegularExpressions.Regex.Match(input, @"Title:\s*(.+?)(?=\s*Focus:|$)");
                var focusMatch = System.Text.RegularExpressions.Regex.Match(input, @"Focus:\s*(.+)");

                if (titleMatch.Success)
                {
                    title = titleMatch.Groups[1].Value.Trim();
                }

                if (focusMatch.Success)
                {
                    focus = focusMatch.Groups[1].Value.Trim();
                }

                // Validate extracted values
                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(focus))
                {
                    throw new ArgumentException("Title or Focus could not be extracted from the input.");
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"Error: Failed to parse Title and Focus from input. Error was: {e.Message}");
                throw; // Re-throw the exception to handle it in the caller
            }

            return (title, focus);
        }


        public async Task<ResultObj> ProcessBlogList()
        {
            string blogFile = _isUsingGuidesFile ? _blogFileGuides : _blogFile;

            // Call the existing method with the selected file
            var result = await ProcessBlogList(blogFile);

            // Toggle the flag for the next call
            _isUsingGuidesFile = !_isUsingGuidesFile;

            return result;
        }

        public async Task<ResultObj> ProcessBlogListAll()
        {
            string blogFile = _isUsingGuidesFile ? _blogFileGuides : _blogFile;

            // Call the existing method with the selected file
            var result = await ProcessBlogListAll(blogFile);

            // Toggle the flag for the next call
            _isUsingGuidesFile = !_isUsingGuidesFile;

            return result;
        }

        // A method to Process all The Blogs in the BlogList
        public async Task<ResultObj> ProcessBlogListAll(string blogFile)
        {
            var result = new ResultObj();
            var results = new List<ResultObj>();
            var removeBlogIndexes = new List<int>();
            result.Message = " SERVICE : ProcessBlogListAll :";
            try
            {
                _blogList = ReadBlogListJson(blogFile);
                if (_blogList.Count == 0)
                {
                    result.Success = false;
                    result.Message = " Error : BlogList is empty. ";
                    _logger.LogError(result.Message);
                    return result;
                }
                string systemPrompt = _systemPrompt;
                bool useDataLLMService = false;
                if (blogFile == "BlogListGuides.json") useDataLLMService = true;
                // Loop through the list and process each item
                foreach (var item in _blogList)
                {
                    var currentResult = await PutAnswerIntoBlogs(item, systemPrompt, useDataLLMService);
                    // pause for 30 seconds
                    await Task.Delay(1000);
                    results.Add(currentResult);
                    var itemIndex = _blogList.IndexOf(item);
                    if (currentResult.Success == false)
                    {
                        result.Message = " SERVICE : ProcessBlogListAll : Error : " + result.Message;
                        _logger.LogError(result.Message);
                        return result;
                    }
                    else
                    {
                        removeBlogIndexes.Add(itemIndex);
                    }

                    _logger.LogInformation(" SERVICE : ProcessBlogListAll item index " + itemIndex + " : Success : ");
                }
                foreach (int index in removeBlogIndexes.OrderByDescending(i => i))
                {
                    _blogList.RemoveAt(index);
                }

                WriteBlogListJson(blogFile);
                result.Success = true;
                result.Message += " SERVICE : ProcessBlogListAll : Success";
                result.Data = results;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message += " SERVICE : ProcessBlogListAll : Error : " + ex.Message;
                _logger.LogError(result.Message);
                return result;
            }
        }
        // A method to read the a question from the first item in _blogList. 
        public async Task<ResultObj> ProcessBlogList(string blogFile)
        {
            var result = new ResultObj();
            result.Message = " SERVICE : ProcessBlogList :";
            try
            {
                _blogList = ReadBlogListJson(blogFile);
                if (_blogList.Count == 0)
                {
                    result.Success = false;
                    result.Message += " Error : BlogList is empty. ";
                    _logger.LogError(result.Message);
                    return result;
                }
                var blogList = _blogList[0];
                //blogList.Date = null;
                var question = blogList.Content;
                // question is empty return
                if (question == "")
                {
                    result.Success = false;
                    result.Message += " Error : BlogList question is empty. ";
                    _logger.LogError(result.Message);
                    return result;
                }
                string systemPrompt = _systemPrompt;
                bool useDataLLMService = false;
                if (blogFile == "BlogListGuides.json") useDataLLMService = true;
                //var blogQuestion = "Write a blog post, that sound like a human wrote it, using this title for content : '" + question + "'. Style this article using Markdown. Put the blog post content inside a json object for example {\"postContent\" : \"\"}.";
                result = await PutAnswerIntoBlogs(blogList, systemPrompt, useDataLLMService);
                if (result.Success == false)
                {
                    result.Message += " SERVICE : ProcessBlogListAll : Error : " + result.Message;
                    _logger.LogError(result.Message);
                    return result;
                }
                else
                {
                    _blogList.RemoveAt(0);
                    // write the list to the file
                    WriteBlogListJson(blogFile);
                    result.Success = true;
                    result.Message += " Success : BlogList processing completed. ";
                }

            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message += " Error : BlogList processing failed. Error was : " + ex.Message;
                _logger.LogError(result.Message);
            }
            return result;
        }
        // Convert the above method ReadBlogListFile to a method that reads the BlogList.json file and serialises it to return a List<BlogList>.
        public List<BlogList> ReadBlogListJson(string blogFile)
        {
            var jsonStr = System.IO.File.ReadAllText(blogFile);
            var list = JsonUtils.GetJsonObjectFromString<List<BlogList>>(jsonStr);
            if (list != null) return list;
            return new List<BlogList>();
        }
        // Convert the above method WriteBlogListFile to a method that serialises the List<BlogList> to a json string and writes it to the file BlogList.json.
        public void WriteBlogListJson(string blogFile)
        {
            JsonUtils.WriteObjectToFile<List<BlogList>>(blogFile, _blogList);
        }
        public void OnStopping()
        {
            _logger.LogInformation("OnStopping has been called.");
        }
        public async Task<string> QueryModels()
        {
            var result = await _client.GetAsync("/v1/models");
            return await result.Content.ReadAsStringAsync();
        }
        public async Task<TResultObj<string?>> AskQuestion(string question, string systemPrompt)
        {
            var result = new TResultObj<string?>();
            result.Message = " SERVICE : AskQuestion :";
            var messageSystem = new Message() { role = "system", content = systemPrompt };
            var messages = new List<Message>();
            var message = new Message() { role = "user", content = question };
            messages.Add(messageSystem);
            messages.Add(message);
            var contentObject = new ContentObject() { model = _model, messages = messages };
            var stringStr = JsonUtils.WriteJsonObjectToString<ContentObject>(contentObject);
            _logger.LogDebug(" SERVICE : AskQuestion : stringStr : " + stringStr);
            var stringContent = new StringContent(JsonUtils.WriteJsonObjectToString<ContentObject>(contentObject), Encoding.UTF8, "application/json");
            string endpointUrl = "/v1/chat/completions";
            try
            {
                _logger.LogDebug("Endpoint URL: " + _endpointUrlBase + endpointUrl + " using model : " + _model);
                HttpResponseMessage response = await _client.PostAsync(endpointUrl, stringContent);
                _logger.LogDebug("Response status code: " + response.StatusCode);
                response.EnsureSuccessStatusCode();
                // Read the response body as a string
                string responseBody = await response.Content.ReadAsStringAsync();
                var chatCompletion = JsonUtils.GetJsonObjectFromString<ChatCompletion>(responseBody);
                result.Success = true;
                if (chatCompletion == null || chatCompletion.Choices == null)
                {
                    result.Success = false;
                    result.Message += " Error : No Chat Result received . ";
                    return result;
                }
                result.Message += " Success : Got chat completion ";

                chatCompletion.Choices.Sort((x, y) => x.Index.CompareTo(y.Index));

                List<ChatCompletionChoice> choices = chatCompletion.Choices;
                ChatCompletionChoice? firstChoice = choices.FirstOrDefault();
                if (firstChoice != null && firstChoice!.Message != null)
                {
                    result.Data = firstChoice.Message.Content;
                    result.Success = true;
                }
                else
                {
                    result.Data = null;
                    result.Success = false;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Data = null;
                result.Message += " Error : Could getting Chat Completion . Error was " + ex.Message;
                _logger.LogError(result.Message);
            }
            return result;
        }
        public async Task<ResultObj> PutAnswerIntoBlogs(BlogList blogList, string systemPrompt, bool useDataLLMService)
        {
            var result = new ResultObj();
            result.Message = " API : PutAnswerIntoBlogs :";
            try
            {

                DateTime date;
                if (blogList.Date == null)
                {
                    date = DateTime.Now;
                }
                else
                {
                    date = (DateTime)blogList.Date;
                    if (date > DateTime.Now)
                    {
                        result.Success = false;
                        result.Message += $"Warning : Date of Blog post {date.ToShortDateString()} is in the future not processing.";
                        return result;
                    }

                }
                var question = blogList.Content;
                if (question == null)
                {
                    result.Success = false;
                    result.Message += " Error : No question found in BlogList";
                    return result;
                }
                var chatResult = new TResultObj<string?>();
                if (useDataLLMService)
                {
                    string title;
                    string focus;

                    try
                    {
                        // Call the new method to extract title and focus
                        (title, focus) = ExtractTitleAndFocus(question);
                    }
                    catch (Exception e)
                    {
                        result.Message = $"Error: {e.Message}";
                        return result;
                    }
                    var resultLlm = await GetLLMReportForHost(title, focus);
                    if (resultLlm.Success)
                    {
                        chatResult.Data = (string?)resultLlm.Message;
                        question = title;
                    }
                    chatResult.Message = resultLlm.Message;
                    chatResult.Success = resultLlm.Success;
                }
                else
                {
                    chatResult = await AskQuestion(question, systemPrompt);
                }

                result.Message += chatResult.Message;
                if (chatResult.Success == false)
                {
                    return new ResultObj() { Message = chatResult.Message, Success = false };
                }

                if (chatResult.Success && chatResult.Data != null)
                {
                    string answer = chatResult.Data;
                    int index;
                    var firstSentance = answer.Substring(0, answer.IndexOf("."));
                    // remove the first sentance from the answer. This is the question.
                    if (firstSentance.Contains("AI language model") || firstSentance.Contains("language model AI"))
                    {
                        index = answer.IndexOf(".");
                        answer = answer.Substring(index + 1);
                    }
                    // Genrate a new string from answer. This string will be short but will be used as the title of the blog. It will have no spaces.
                    var title = question;
                    // remove any special characters
                    //title = Regex.Replace(title, "[^a-zA-Z0-9_.]+", "", RegexOptions.Compiled);
                    // check if the title already exists in the database. If it does, generate a new title.
                    var titleExists = true;
                    using var scope = _scopeFactory.CreateScope();
                    var _monitorContext = scope.ServiceProvider.GetRequiredService<MonitorContext>();
                    while (titleExists)
                    {
                        if (!await _monitorContext.Blogs.AnyAsync(b => b.Hash == title))
                        {
                            titleExists = false;
                        }
                        else
                        {
                            title = title + "-1";
                        }
                    }
                    // Capitalise every first Letter of every word in title
                    var titleCase = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(title);
                    // a Reg expression to remove any special characters from the titleCase string.
                    title = Regex.Replace(titleCase, @"[^a-zA-Z0-9_.'""-]+", " ", RegexOptions.Compiled);
                    var hash = Regex.Replace(titleCase, "[^a-zA-Z0-9_.]+", "", RegexOptions.Compiled);
                    // Remove the first sentence from the answer if it contains the words AI language model .
                    var header = "# " + question + " \n\n" + DateTime.Now.ToString("dd MMMM yyyy") + " \n\n by [Mahadeva](https://www.mahadeva.co.uk) \n\n";
                    // convert blogList.Categories to blog.BlogCategories
                    var blogCategories = new List<BlogCategory>();
                    if (blogList.Categories == null)
                    {
                        blogList.Categories = new List<string>();
                        _logger.LogWarning(" Warning : creating new blog list Catefories . ");
                    }
                    foreach (var cat in blogList.Categories)
                    {
                        var blogCategory = new BlogCategory
                        {
                            Category = cat
                        };
                        blogCategories.Add(blogCategory);
                    }
                    // Choose a picture from BlogPictures. Where the picture is not already in use. And its Category is in blogList.Categories.
                    // Get the picture from the database.
                    //var picture = await _monitorContext.BlogPictures.FirstOrDefaultAsync(p => p.IsUsed == false && blogList.Categories.Contains(p.Category));

                    // After generating the content, now generate the image
                    bool isImage = false;

                    string picHashExt = GenerateRandomString(10);
                    string picName = hash + "-" + picHashExt + ".png";
                    string picDir = "apipicgen";
                    string imageFilePath = Path.Combine(picDir, picName);
                    var picture = new BlogPicture()
                    {
                        Name = picName,
                        Path = imageFilePath,
                        IsUsed = true,
                        Category = blogCategories[0].Category,
                        DateCreated = DateTime.UtcNow
                    };
                    //var imagePrompt = $"Generate an image for the blog titled: {question}";
                    var imageResult = await GenerateImage(answer);

                    if (imageResult.Success)
                    {
                        var imageResponse = imageResult.Data; // Adjust according to your response model
                        if (imageResponse != null && imageResponse.data != null && imageResponse.data.Any())
                        {
                            var imageData = imageResponse.data[0];
                            var imageProcessor = new ImageProcessor(_client);
                            var origImageFilePath = Path.ChangeExtension(imageFilePath, null) + "-orig" + Path.GetExtension(imageFilePath);

                            if (!string.IsNullOrEmpty(imageData.b64_json))
                            {
                                // Decode and save the base64 image as JPEG
                                imageFilePath = await imageProcessor.DecodeBase64ImageAsync(imageData.b64_json, imageFilePath);
                                isImage = true;

                                // Save the original image without compression
                                await imageProcessor.DecodeBase64OrigImageAsync(imageData.b64_json, origImageFilePath);
                            }
                            else if (!string.IsNullOrEmpty(imageData.url))
                            {
                                // Download and save the image as JPEG
                                imageFilePath = await imageProcessor.DownloadImageAsync(imageData.url, imageFilePath);
                                isImage = true;

                                // Download and save the original image without compression
                                await imageProcessor.DownloadOrigImageAsync(imageData.url, origImageFilePath);
                            }

                        }

                    }
                    else
                    {
                        _logger.LogWarning("Image generation failed: " + imageResult.Message);
                        picture = null;
                        imageFilePath = "";
                    }
                    var blog = new Blog
                    {
                        Title = title,
                        Header = header,
                        Hash = hash,
                        Markdown = answer,
                        IsFeatured = false,
                        IsPublished = true,
                        IsMainFeatured = false,
                        DateCreated = date,
                        ImageUrl = "/" + Path.Combine("blogpics", imageFilePath),
                        IsImage = isImage,
                        BlogCategories = blogCategories,
                        IsOnBlogSite = true
                    };
                    // get monitorContext from _scopeFactory
                    _monitorContext.Blogs.Add(blog);
                    await _monitorContext.SaveChangesAsync();
                    // Update used BlogPicture with BlogId and save changes.
                    if (picture != null)
                    {
                        picture.BlogId = blog.Id;
                        _monitorContext.BlogPictures.Add(picture);
                        await _monitorContext.SaveChangesAsync();
                    }
                    result.Success = true;
                    result.Data = answer;
                }
                else
                {
                    result.Success = false;
                    result.Message += " Error : No choices returned from OpenAI";
                    // handle the case where the list is empty
                }
            }
            catch (Exception ex)
            {
                result.Message += " Error : Failed to retrieve blogs. Error was : " + ex.Message;
                result.Success = false;
                _logger.LogError(result.Message);
                return result;
            }
            return result;
        }


        public async Task<TResultObj<ImageResponse>> GenerateImage(string answer)
        {
            var result = new TResultObj<ImageResponse>();
            result.Message = " SERVICE : GenerateImage :";
            string endpointUrl = "/v1/images/generations"; // Adjust the endpoint based on the actual API
            string systemPrompt = "You are an assistant specialized in generating image prompts for text-to-image models. You will receive blog post text and respond only with a prompt designed to create an image that best represents the given content. The image should be clean, minimalistic, and professional, avoiding excessive detail, small icons, or clutter. It should be simple but visually appealing, suitable for a network monitoring service website. Respond exclusively with the image generation prompt.";
            string question = $"Generate a prompt for an image creation model (dall-e-3) that best represents this blog post: \"{answer}\". Only respond with the image generation prompt.";
            var chatResult = await AskQuestion(question, systemPrompt);
            result.Message += chatResult.Message;
            if (chatResult.Success == true && chatResult.Data != null)
            {
                var imageRequest = new ImageRequest()
                {
                    model = "dall-e-3",
                    prompt = chatResult.Data,
                    n = 1, // number of images to generate
                    size = "1024x1024",
                    quality = "standard"
                };

                var stringContent = new StringContent(JsonUtils.WriteJsonObjectToString(imageRequest), Encoding.UTF8, "application/json");

                try
                {
                    HttpResponseMessage response = await _client.PostAsync(endpointUrl, stringContent);
                    response.EnsureSuccessStatusCode();

                    // Read the response body as a string
                    string responseBody = await response.Content.ReadAsStringAsync();
                    var imageResponse = JsonUtils.GetJsonObjectFromString<ImageResponse>(responseBody); // Adjust according to your response model

                    result.Success = true;
                    result.Data = imageResponse; // Return the image response object
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message += " Error : " + ex.Message;
                    _logger.LogError(result.Message);
                }
            }
            else
            {
                result.Success = false;
                result.Message += "Can not produce an image for the blog.";
            }

            return result;
        }


        private string GenerateRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[_random.Next(s.Length)]).ToArray());
        }
    }
}
