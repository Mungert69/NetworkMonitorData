using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetworkMonitor.Objects;
using NetworkMonitor.Service.Services.OpenAI;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace NetworkMonitor.Data.Services
{
    public interface IBlogDatabaseRepo
    {
        Task CheckInitData();
        Task<bool> BlogHashExistsAsync(string hash);
        Task SaveNewBlogAsync(Blog blog, BlogPicture? picture);
        // Possibly add more as needed
    }

    public class BlogDatabaseRepo : IBlogDatabaseRepo
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BlogDatabaseRepo> _logger;

        public BlogDatabaseRepo(IServiceScopeFactory scopeFactory, ILogger<BlogDatabaseRepo> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task CheckInitData()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<MonitorContext>();

                var blogs = await context.Blogs.ToListAsync();
                if (blogs.Count == 0)
                {
                    var data = FirstData.getData();
                    foreach (var item in data)
                    {
                        var blog = new Blog
                        {
                            Hash = item.Hash,
                            Markdown = item.Markdown,
                            IsFeatured = item.IsFeatured,
                            IsMainFeatured = item.IsMainFeatured,
                            IsPublished = item.IsPublished,
                            IsVideo = item.IsVideo,
                            VideoTitle = item.VideoTitle,
                            VideoUrl = item.VideoUrl,
                            IsImage = item.IsImage,
                            ImageTitle = item.ImageTitle,
                            ImageUrl = item.ImageUrl,
                            Title = item.Title,
                            IsOnBlogSite = item.IsOnBlogSite
                        };
                        context.Blogs.Add(blog);
                    }
                    await context.SaveChangesAsync();
                    _logger.LogInformation("Success: Added default Blogs to database");
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"Error: could not add default Blogs to database. Error was: {e.Message}");
            }
        }

        public async Task<bool> BlogHashExistsAsync(string hash)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MonitorContext>();
            return await context.Blogs.AnyAsync(b => b.Hash == hash);
        }

        public async Task SaveNewBlogAsync(Blog blog, BlogPicture? picture)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MonitorContext>();

            context.Blogs.Add(blog);
            await context.SaveChangesAsync();

            // If there's a picture, link it
            if (picture != null)
            {
                picture.BlogId = blog.Id;
                context.BlogPictures.Add(picture);
                await context.SaveChangesAsync();
            }
        }
    }
}
