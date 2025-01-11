using System;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace NetworkMonitor.Data.Services
{
    public static class TitleFocusExtractor
    {
        public static (string title, string focus) ExtractTitleAndFocus(string input, ILogger logger)
        {
            string title = string.Empty;
            string focus = string.Empty;

            try
            {
                var titleMatch = Regex.Match(input, @"Title:\s*(.+?)(?=\s*Focus:|$)");
                var focusMatch = Regex.Match(input, @"Focus:\s*(.+)");

                if (titleMatch.Success)  title = titleMatch.Groups[1].Value.Trim();
                if (focusMatch.Success)  focus = focusMatch.Groups[1].Value.Trim();

                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(focus))
                {
                    throw new ArgumentException("Could not extract Title or Focus.");
                }
            }
            catch (Exception e)
            {
                logger.LogError($"Error extracting Title/Focus: {e.Message}");
                throw;
            }

            return (title, focus);
        }

        public static string GenerateTitle(string rawTitle, ILogger logger)
        {
            // Some logic to “clean up” the string
            var titleCase = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(rawTitle);
            var cleanedTitle = Regex.Replace(titleCase, @"[^a-zA-Z0-9\s\-_\.]+", "");
            return cleanedTitle;
        }

        public static string GenerateHash(string title)
        {
            // For uniqueness, remove all spaces/punctuation
            return Regex.Replace(title, "[^a-zA-Z0-9]+", "");
        }

        public static string GenerateRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}
