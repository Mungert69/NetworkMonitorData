using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;

public static class ImageGeneratorFactory
{
    public static IImageGenerator Create(string provider, IConfiguration config, HttpClient client)
    {
        switch (provider.ToLower())
        {
            case "novita":
                return new NovitaImageGenerator(config, client);
            case "huggingface":
                return new HuggingFaceImageGenerator(config, client);
            case "openai":
            default:
                return new OpenAIImageGenerator(config, client);
        }
    }
}
