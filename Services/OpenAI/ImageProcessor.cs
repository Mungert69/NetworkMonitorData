using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace NetworkMonitor.Data.Services
{
    public class ImageProcessor
    {
        private readonly HttpClient _client;

        public ImageProcessor(HttpClient client)
        {
            _client = client;
        }

        private async Task<string> CompressAndSaveImageAsync(byte[] imageBytes, string savePath, int quality = 75)
        {
            using (var image = Image.Load(imageBytes))
            {
                // Ensure the directory exists
                var path = Path.GetDirectoryName(savePath);
                if (path != null) Directory.CreateDirectory(path);

                // Always save as JPEG for better compression
                string jpegPath = Path.ChangeExtension(savePath, ".jpg");
                await image.SaveAsJpegAsync(jpegPath, new JpegEncoder { Quality = quality });

                return jpegPath;
            }
        }

        public async Task<string> DownloadImageAsync(string imageUrl, string savePath)
        {
            using (var response = await _client.GetAsync(imageUrl))
            {
                response.EnsureSuccessStatusCode();
                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                return await CompressAndSaveImageAsync(imageBytes, savePath);
            }
        }

        public async Task<string> DecodeBase64ImageAsync(string base64Image, string savePath)
        {
            byte[] imageBytes = Convert.FromBase64String(base64Image);
            return await CompressAndSaveImageAsync(imageBytes, savePath);
        }

        public async Task<string> DownloadOrigImageAsync(string imageUrl, string savePath)
        {
            using (var response = await _client.GetAsync(imageUrl))
            {
                response.EnsureSuccessStatusCode();
                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                
                // Ensure the directory exists
                var path = Path.GetDirectoryName(savePath);
                if (path != null) Directory.CreateDirectory(path!);
                
                // Write the image to a file
                await File.WriteAllBytesAsync(savePath, imageBytes);
            }
            return savePath; // Return the path of the saved image
        }

        public async Task<string> DecodeBase64OrigImageAsync(string base64Image, string savePath)
        {
            byte[] imageBytes = Convert.FromBase64String(base64Image);
            
            // Ensure the directory exists
            var path = Path.GetDirectoryName(savePath);
            if (path != null) Directory.CreateDirectory(path);
            
            // Write the image to a file
            await File.WriteAllBytesAsync(savePath, imageBytes);
            return savePath; // Return the path of the saved image
        }
    }
}