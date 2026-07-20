using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ScreenMind.Core.Imaging;

namespace ScreenMind.Providers.OpenAICompatible.Qwen;

public interface IQwenProxyClient
{
    Task<QwenProxyCapabilities> GetCapabilitiesAsync(Uri baseUri, string? apiKey, string? cookie, CancellationToken cancellationToken);
    Task<QwenUploadedFile> UploadImageAsync(Uri baseUri, ScreenImage image, string? apiKey, string? cookie, CancellationToken cancellationToken);
    Task<List<string>> GetModelsAsync(Uri baseUri, string? apiKey, string? cookie, CancellationToken cancellationToken);
}
