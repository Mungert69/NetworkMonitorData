using System.Collections.Generic;
using System.IO;
using NetworkMonitor.Objects;
using NetworkMonitor.Utils;
using NetworkMonitor.Service.Services.OpenAI;

namespace NetworkMonitor.Data.Services
{
    public interface IBlogFileRepo
    {
        List<BlogList> ReadBlogList(string blogFile);
        void WriteBlogList(string blogFile, List<BlogList> blogList);
    }

    public class BlogFileRepo : IBlogFileRepo
    {
        public List<BlogList> ReadBlogList(string blogFile)
        {
            if (!File.Exists(blogFile)) return new List<BlogList>();

            var jsonStr = File.ReadAllText(blogFile);
            var list = JsonUtils.GetJsonObjectFromString<List<BlogList>>(jsonStr);
            return list ?? new List<BlogList>();
        }

        public void WriteBlogList(string blogFile, List<BlogList> blogList)
        {
            JsonUtils.WriteObjectToFile(blogFile, blogList);
        }
    }
}
