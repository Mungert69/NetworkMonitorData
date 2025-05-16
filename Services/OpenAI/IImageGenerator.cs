using System.Threading.Tasks;
using NetworkMonitor.Service.Services.OpenAI;
using NetworkMonitor.Objects;

public interface IImageGenerator
{
    Task<TResultObj<ImageResponse>> GenerateImage(string prompt);
}

