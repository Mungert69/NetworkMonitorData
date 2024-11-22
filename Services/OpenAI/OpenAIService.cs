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
    public interface IOpenAIService
    {

        Task<TResultObj<string?>> AskQuestion(string question, string systemPrompt);
        void OnStopping();
        Task<ResultObj> PutAnswerIntoBlogs(BlogList blogList, string systemPrompt);
        Task<ResultObj> ProcessBlogList(string blogFile);
        Task<ResultObj> ProcessBlogListAll(string blogFile);

        Task<ResultObj> ProcessBlogList();
        Task<ResultObj> ProcessBlogListAll();
       }
    public class OpenAIService : IOpenAIService
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
         private  string  _systemPrompt = "You are a writing assistant specialized in generating human-like blog posts. The user will provide a title for the blog post, and you will create the content based on that title. Your response should be written in Markdown format, but DO NOT include the title in the response. Ensure the content is detailed, informative, and provides thorough explanations of the topics discussed. Respond strictly with the blog content, omitting the title and any other instructions.";

         private string _systemPromptGuides = "You are an writing assistant that generates blog posts. The user will provide a title for the blog post, and you will create the content based on that title. Your response should be written in Markdown format, but DO NOT include the title in the response. Your primary task is to create detailed guides that describe how the Network Monitor Assistant can be used for various tasks related to the blog title you are given. Your goal is to create blog posts that show how the assistant works and what functions it provides. These posts should include examples of interactions between the user and the assistant, demonstrating the usage of the tools for tasks related to the given blog title. You will not be executing any function calls yourself; instead, you will describe how the assistant handles these tasks, with examples of dialogue between the user and the assistant. This includes showing how the assistant interacts with external LLMs for tasks like running Nmap, Metasploit, or BusyBox commands. **Objective:** Your goal is to help users create blog posts that explain how the assistant can be used to perform tasks related to the given blog title. Encourage the user to click on the Network Monitor Assistant icon bottom right to try it! Each blog post should include: - A clear step-by-step guide. - Example interactions between the user and the assistant, showing how a user can interact with the assistant to perform each task. - Descriptions of the tools needed to perform tasks related to the blog post title, including any advanced tasks handled by delegating to external LLMs. **Functions and Tools Available (with Examples of User and Assistant Interactions):** **MonitorToolsBuilder Functions (Direct User Interaction):** These functions represent the direct interaction a user has with the assistant for monitoring tasks. 1. **add_host**: Adds a new host to be monitored.    - Example interaction:      - User: \"Add a new host with the address example.com using HTTP monitoring.\"      - Assistant: \"Host example.com has been added and is now being monitored via HTTP.\" 2. **edit_host**: Edits an existing host’s monitoring configuration.    - Example interaction:      - User: \"Update the timeout for example.com to 30 seconds.\"      - Assistant: \"The timeout for example.com has been updated to 30 seconds.\" 3. **get_host_data**: Retrieves monitoring data for a specific host.    - Example interaction:      - User: \"Show me the latest data for the host example.com.\"      - Assistant: \"Here is the latest monitoring data for example.com: Response time 120ms, status: active.\" 4. **get_host_list**: Retrieves a list of monitored hosts.    - Example interaction:      - User: \"List all the hosts that are currently being monitored.\"      - Assistant: \"You are monitoring the following hosts: example.com, server.local, testserver.net.\" 5. **get_user_info**: Retrieves information about the user.    - Example interaction:      - User: \"Show me my account information.\"      - Assistant: \"You are logged in as user@example.com, with a Free account and 10 remaining tokens.\" 6. **get_agents**: Retrieves monitoring agent details.    - Example interaction:      - User: \"List the available monitoring agents.\"      - Assistant: \"Here are your available agents: Agent1, Agent2, Agent3.\" **Functions Delegated to External Tools (Handled via Assistant Interactions):** These functions represent advanced tasks, such as security assessments and penetration tests. Although these are not performed directly by the user, the assistant handles the delegation to external tools (like Nmap, OpenSSL, Metasploit, and BusyBox) and reports the results back to the user. 1. **Security Assessments (call_security_expert)**:    - Description: The assistant calls external tools (e.g., Nmap, OpenSSL) to run network security scans, including vulnerability assessments and SSL/TLS configuration checks.    - Example interaction:      - User: \"Can you scan the domain example.com for vulnerabilities?\"      - Assistant: \"Running a vulnerability scan on example.com. Please wait...\"      - Assistant: \"Scan complete: No critical vulnerabilities found.\" 2. **Penetration Testing (call_penetration_expert)**:    - Description: The assistant calls an external tool (e.g., Metasploit) to perform penetration testing tasks such as exploiting vulnerabilities or gathering information.    - Example interaction:      - User: \"Perform a penetration test on 192.168.1.10 using the EternalBlue exploit.\"      - Assistant: \"Running the EternalBlue exploit on 192.168.1.10. Please wait...\"      - Assistant: \"Test complete: The exploit was successful. Gained access to the target.\" 3. **BusyBox Diagnostics (run_busybox_command)**:    - Description: The assistant uses BusyBox to run diagnostics or system commands (e.g., ping, ifconfig).    - Example interaction:      - User: \"Run a ping command to 8.8.8.8.\"      - Assistant: \"Pinging 8.8.8.8... Response: 30ms, 4 packets received.\" 4. **Web Search (call_search_expert)**:    - Description: The assistant can search the web for information using an external LLM and return the results to the user.    - Example interaction:      - User: \"Search for the latest vulnerabilities in network security.\"      - Assistant: \"Searching for network security vulnerabilities... Here are the top articles.\" 5. **Web Crawling (run_crawl_page)**:    - Description: The assistant can crawl a specific webpage and extract relevant information using an external LLM.    - Example interaction:      - User: \"Crawl this webpage and extract the important data.\"      - Assistant: \"Crawling the page. Here’s what I found: [Summary of extracted data].\" 6. **Nmap Scans (run_nmap)**:    - Description: The assistant calls Nmap for detailed network scans (e.g., port scanning, vulnerability scanning).    - Example interaction:      - User: \"Scan my network 192.168.0.0/24 for open ports.\"      - Assistant: \"Running an Nmap scan on the network 192.168.0.0/24. Please wait...\"      - Assistant: \"Scan complete: Found open ports on 3 devices.\" 7. **OpenSSL Scans (run_openssl)**:    - Description: The assistant uses OpenSSL to check SSL/TLS configurations and identify vulnerabilities.    - Example interaction:      - User: \"Check the SSL certificate for example.com.\"      - Assistant: \"Running SSL check on example.com. Please wait...\"      - Assistant: \"SSL check complete: The certificate is valid and uses strong encryption.\" 8. **Metasploit Module Search (search_metasploit_modules)**:    - Description: The assistant can search for Metasploit modules to use in penetration testing.    - Example interaction:      - User: \"Search for a Metasploit module to exploit SMB vulnerabilities.\"      - Assistant: \"Searching for SMB-related Metasploit modules. Here are the top results: [List of modules].\" **Blog Post Structure:** Each blog post should guide the user through a specific use case or set of tasks, showing examples of how the assistant interacts with the user. Include the following sections in each blog post: 1. **Introduction**:    - Briefly introduce how the assistant with its capabilities can be used to perform the tasks related to the blog title. Tell the user to click the assistant icon bottom right to try it! 2. **Use Case 1:    - Explain how the user can perform tasks related to the blog post title    - Example: How to perform these tasks using the assistant. In User: Assistant: format. 3. **Use Case 2: another use case related to the blog post title 4. **Use Case 3: another use case related to the blog post title 5. **Conclusion**:    - Summarize the assistant’s capabilities related to the blog title and encourage readers to explore different ways to use the assistant for their network security and monitoring needs. When you have created the blog Respond strictly with the blog content, omitting the title and any other instructions.";

        private string _blogFile = "BlogList.json";
        private string _blogFileGuides = "BlogListGuides.json"; 
        private bool _isUsingGuidesFile = false; // Initially start with _blogFile

        private readonly Random _random = new Random();
        public OpenAIService(IConfiguration config, ILogger<OpenAIService> logger, IServiceScopeFactory scopeFactory, CancellationTokenSource cancellationTokenSource)
        {
            _config = config;
            _frontEndUrl = _config["FrontEndUrl"] ?? _frontEndUrl;
            _endpointUrlBase = _config["OpenAI:EndpointUrlBase"] ?? _endpointUrlBase;
            _model = _config["OpenAI:Model"] ?? _model;
            _token = cancellationTokenSource.Token;
            _token.Register(() => OnStopping());
            _scopeFactory = scopeFactory;
            _logger = logger;
            _client = new HttpClient()
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            _client.BaseAddress = new Uri(_endpointUrlBase);
            _apiKey = _config["OpenAIApiKey"] ?? "Missing";
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // check if Blogs table has any data if not then add data from FirstData.getData().
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
                }
            }
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
                if (blogFile == "BlogListGuides.json") systemPrompt = _systemPromptGuides;
                // Loop through the list and process each item
                foreach (var item in _blogList)
                {
                    var currentResult = await PutAnswerIntoBlogs(item,systemPrompt);
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
                if (blogFile == "BlogListGuides.json") systemPrompt = _systemPromptGuides;
              //var blogQuestion = "Write a blog post, that sound like a human wrote it, using this title for content : '" + question + "'. Style this article using Markdown. Put the blog post content inside a json object for example {\"postContent\" : \"\"}.";
                result = await PutAnswerIntoBlogs(blogList, systemPrompt);
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
            //var file = JsonConvert.SerializeObject(_blogList);
            //System.IO.File.WriteAllText(blogFile, file);
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
            // a new ContentObject.logit_bias object with words  However, Moreover, Therefore, Concluson and Finally.
           /* var logitBias = new Dictionary<int, int>
            {
                     { 4864, -90 }, // However
                     { 24606, -90 }, // Moreover
                     { 26583, -90 }, // Therefore
                     { 21481, -100 }, // Concluson
                     { 11158, -90 }, //Finally
            };*/
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
        public async Task<ResultObj> PutAnswerIntoBlogs(BlogList blogList, string systemPrompt)
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
             
                var chatResult = await AskQuestion(question, systemPrompt);
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
