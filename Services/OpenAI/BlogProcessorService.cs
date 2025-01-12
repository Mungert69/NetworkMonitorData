using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Utils;
using NetworkMonitor.Service.Services.OpenAI;

namespace NetworkMonitor.Data.Services
{
    public interface IBlogProcessorService
    {
        Task<ResultObj> ProcessBlogList();
        Task<ResultObj> ProcessBlogListAll();
    }

    public class BlogProcessorService : IBlogProcessorService
    {
        private readonly IBlogFileRepo _fileService;
        private readonly IOpenAIService _openAIService;
        private readonly IBlogDatabaseRepo _databaseService;
        private readonly ILogger<BlogProcessorService> _logger;

        private readonly string _blogFile;
        private readonly string _blogFileGuides;
        private bool _isUsingGuidesFile = false;

        public BlogProcessorService(
            IBlogFileRepo fileService,
            IOpenAIService openAIService,
            IBlogDatabaseRepo databaseService,
            ILogger<BlogProcessorService> logger)
        {
            _fileService = fileService;
            _openAIService = openAIService;
            _databaseService = databaseService;
            _logger = logger;

            // Hard-coded or from config
            _blogFile = "BlogList.json";
            _blogFileGuides = "BlogListGuides.json";

            // Make sure DB is initialized
            _databaseService.CheckInitData();
        }

        public async Task<ResultObj> ProcessBlogList()
        {
            string fileToProcess = _isUsingGuidesFile ? _blogFileGuides : _blogFile;

            var result = await ProcessBlogListInternal(fileToProcess, processAll: false);

            // Toggle for next time
            _isUsingGuidesFile = !_isUsingGuidesFile;
            return result;
        }

        public async Task<ResultObj> ProcessBlogListAll()
        {
            string fileToProcess = _isUsingGuidesFile ? _blogFileGuides : _blogFile;

            var result = await ProcessBlogListInternal(fileToProcess, processAll: true);

            // Toggle for next time
            _isUsingGuidesFile = !_isUsingGuidesFile;
            return result;
        }

        private async Task<ResultObj> ProcessBlogListInternal(string blogFile, bool processAll)
        {
            var result = new ResultObj() { Message = "Processing blog list." };
            var blogList = _fileService.ReadBlogList(blogFile);

            if (blogList.Count == 0)
            {
                result.Success = false;
                result.Message += " Error: BlogList is empty.";
                _logger.LogError(result.Message);
                return result;
            }

            if (processAll)
            {
                var results = new List<ResultObj>();
                var removeIndexes = new List<int>();

                for (int i = 0; i < blogList.Count; i++)
                {
                    var blogItem = blogList[i];
                    var currentResult = await ProcessSingleBlogItem(blogItem, blogFile);
                    results.Add(currentResult);

                    if (currentResult.Success)
                    {
                        removeIndexes.Add(i);
                    }
                    else
                    {
                        // If you want to stop on first failure, break here
                        // or keep going to process the rest
                        _logger.LogError("Failed to process item: " + currentResult.Message);
                    }

                    // Pause between calls if needed
                    await Task.Delay(TimeSpan.FromSeconds(3));
                }

                // Remove processed items in descending order
                foreach (int idx in removeIndexes.OrderByDescending(i => i))
                {
                    blogList.RemoveAt(idx);
                }

                // Write the updated file
                _fileService.WriteBlogList(blogFile, blogList);

                result.Success = true;
                result.Message += " Processed all blog items.";
                result.Data = results;
            }
            else
            {
                // Process just one
                var firstItem = blogList[0];
                var singleResult = await ProcessSingleBlogItem(firstItem, blogFile);
                if (singleResult.Success)
                {
                    blogList.RemoveAt(0);
                    _fileService.WriteBlogList(blogFile, blogList);

                    result.Success = true;
                    result.Message += " Successfully processed a single blog item.";
                }
                else
                {
                    result.Success = false;
                    result.Message += " Failed to process a single blog item: " + singleResult.Message;
                }
            }

            return result;
        }

        private async Task<ResultObj> ProcessSingleBlogItem(BlogList item, string blogFile)
        {
            var result = new ResultObj();

            try
            {
                if (string.IsNullOrWhiteSpace(item.Content))
                {
                    result.Success = false;
                    result.Message = "Blog item Content is empty.";
                    return result;
                }

                // If the file is "BlogListGuides.json", we treat it differently
                bool useDataLLMService = blogFile == "BlogListGuides.json";
                var answerResult = await PutAnswerIntoBlogs(item, useDataLLMService);

                result.Success = answerResult.Success;
                result.Message = answerResult.Message;
                result.Data = answerResult.Data;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Exception: {ex.Message}";
                _logger.LogError(result.Message);
            }

            return result;
        }

        private async Task<ResultObj> PutAnswerIntoBlogs(BlogList blogList, bool useDataLLMService)
        {
            var result = new ResultObj() { Message = "PutAnswerIntoBlogs: " };
            try
            {
                // 1. Check the date
                DateTime date = blogList.Date ?? DateTime.Now;
                if (date > DateTime.Now)
                {
                    result.Success = false;
                    result.Message += "Warning: Blog date is in the future, skipping.";
                    return result;
                }

                // 2. Generate or retrieve content
                string question = blogList.Content ?? "";
                var chatResult = new TResultObj<string?>();

                if (useDataLLMService)
                {
                    // Extract Title and Focus
                    var (title, focus) = TitleFocusExtractor.ExtractTitleAndFocus(question, _logger);

                    // specialized LLM call
                    var resultLlm = await _openAIService.GetSystemLLMResponse(title, focus);
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
                    var systemPrompt = "You are a writing assistant specialized in generating human-like blog posts. The user will provide a title for the blog post, and you will create the content based on that title. Your response should be written in Markdown format, but DO NOT include the title in the response. Ensure the content is detailed, informative, and provides thorough explanations of the topics discussed. Respond strictly with the blog content, omitting the title and any other instructions.";
                    chatResult = await _openAIService.AskQuestion(question, systemPrompt);
                }


                if (!chatResult.Success || string.IsNullOrEmpty(chatResult.Data))
                {
                    result.Success = false;
                    result.Message += "No blog content returned from LLM.";
                    return result;
                }
                string answer = chatResult.Data;

                // 3. Build the final blog object
                // In a real scenario, you might want more robust “title” generation
                var cleanedTitle = TitleFocusExtractor.GenerateTitle(question, _logger);
                var hash = TitleFocusExtractor.GenerateHash(cleanedTitle);

                // 4. Possibly generate an image
                var imageResult = await _openAIService.GenerateImage(answer);
                bool isImage = imageResult.Success && imageResult.Data?.data?.Any() == true;

                // For simplicity, skip the detailed logic of saving the image
                // Create a BlogPicture object if we want
                BlogPicture? picture = null;
                if (isImage)
                {
                    var randomString = TitleFocusExtractor.GenerateRandomString(10);
                    var picName = $"{hash}-{randomString}.jpg";
                    var imageFilePath = System.IO.Path.Combine("apipicgen", picName);
                    var blogCat = blogList.Categories?.FirstOrDefault() ?? "";
                    picture = new BlogPicture
                    {
                        Name = picName,
                        Path = imageFilePath,
                        IsUsed = true,
                        Category = blogCat,
                        DateCreated = date
                    };
                    // Process and save the image to file.
                   await _openAIService.ProcessorImage(imageResult.Data,imageFilePath);
                }

                // 5. Build categories
                var blogCategories = blogList.Categories?.Select(cat => new BlogCategory { Category = cat }).ToList()
                                     ?? new List<BlogCategory>();

                // 6. Save the blog to the database
                var blog = new Blog
                {
                    Title = cleanedTitle,
                    Header = $"# {cleanedTitle}\n\n{DateTime.Now:dd MMMM yyyy}\n\nby [Mahadeva](https://...)",
                    Hash = hash,
                    Markdown = answer,
                    DateCreated = date,
                    IsImage = isImage,
                    ImageUrl = isImage ? "/blogpics/" + (picture?.Path ?? "") : "",
                    BlogCategories = blogCategories,
                    IsFeatured = false,
                    IsMainFeatured = false,
                    IsPublished = true,
                    IsOnBlogSite = true
                };

                await _databaseService.SaveNewBlogAsync(blog, picture);

                // 7. Done
                result.Success = true;
                result.Data = answer;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message += $"Exception: {ex.Message}";
                _logger.LogError(result.Message);
            }

            return result;
        }
    }
}
