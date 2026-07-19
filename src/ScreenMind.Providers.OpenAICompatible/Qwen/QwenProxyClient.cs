using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ScreenMind.Core.Imaging;

namespace ScreenMind.Providers.OpenAICompatible.Qwen;

public sealed class QwenProxyClient : IQwenProxyClient
{
    private readonly HttpClient httpClient;

    public QwenProxyClient(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<QwenProxyCapabilities> GetCapabilitiesAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        Uri healthUri = new Uri(baseUri, "health");
        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(healthUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new QwenProxyException($"Health check returned status code {(int)response.StatusCode}.");
            }

            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(content);
            JsonElement root = doc.RootElement;

            string service = root.TryGetProperty("service", out var s) ? s.GetString() ?? "" : "";
            bool ok = root.TryGetProperty("ok", out var o) && o.GetBoolean();
            int models = root.TryGetProperty("models", out var m) ? m.GetInt32() : 0;
            int total = 0;
            int available = 0;

            if (root.TryGetProperty("accounts", out var acc))
            {
                total = acc.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
                available = acc.TryGetProperty("available", out var a) ? a.GetInt32() : 0;
            }

            return new QwenProxyCapabilities(ok, service, models, total, available);
        }
        catch (Exception ex) when (ex is not QwenProxyException)
        {
            throw new QwenProxyException("Qwen proxy is unavailable.", ex);
        }
    }

    public async Task<QwenUploadedFile> UploadImageAsync(Uri baseUri, ScreenImage image, string? cookie, CancellationToken cancellationToken)
    {
        Uri uploadUri = new Uri(baseUri, "files/upload");
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, uploadUri);
            if (!string.IsNullOrWhiteSpace(cookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }

            using MultipartFormDataContent form = new();
            byte[] bytes = image.Bytes.ToArray();
            
            if (bytes.Length == 0)
            {
                throw new QwenProxyException("Qwen image upload failed: payload is empty.");
            }
            if (bytes.Length > 20 * 1024 * 1024)
            {
                throw new QwenProxyException("Qwen image upload failed: payload is too large.");
            }

            using ByteArrayContent fileContent = new(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(image.MediaType);

            string extension = image.Format == ScreenImageFormat.Png ? "png" : "jpg";
            string filename = $"screenshot_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.{extension}";

            form.Add(fileContent, "file", filename);
            request.Content = form;

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new QwenProxyException("Qwen image upload failed: authentication is required.");
                }
                if (response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge)
                {
                    throw new QwenProxyException("Qwen image upload failed: payload is too large.");
                }
                throw new QwenProxyException($"Qwen image upload failed: the proxy rejected the file. Status: {(int)response.StatusCode}");
            }

            string jsonString = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(jsonString);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("success", out var successProp) && successProp.GetBoolean()
                && root.TryGetProperty("file", out var fileProp))
            {
                return new QwenUploadedFile(
                    fileProp.GetProperty("id").GetString() ?? "",
                    fileProp.GetProperty("fileId").GetString() ?? "",
                    fileProp.GetProperty("file_path").GetString() ?? "",
                    fileProp.GetProperty("name").GetString() ?? "",
                    fileProp.GetProperty("url").GetString() ?? "",
                    fileProp.GetProperty("size").GetInt64(),
                    fileProp.GetProperty("type").GetString() ?? ""
                );
            }
            else
            {
                throw new QwenProxyException("Qwen image upload failed: unsupported response format.");
            }
        }
        catch (Exception ex) when (ex is not QwenProxyException)
        {
            throw new QwenProxyException("Qwen proxy is unavailable.", ex);
        }
    }

    public async Task<List<string>> GetModelsAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        Uri modelsUri = new Uri(baseUri, "v1/models");
        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(modelsUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new List<string>();
            }

            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(content);
            JsonElement root = doc.RootElement;
            List<string> models = new();
            if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in dataProp.EnumerateArray())
                {
                    if (el.TryGetProperty("id", out var idProp))
                    {
                        string? modelId = idProp.GetString();
                        if (!string.IsNullOrWhiteSpace(modelId))
                        {
                            models.Add(modelId);
                        }
                    }
                }
            }
            return models;
        }
        catch
        {
            return new List<string>();
        }
    }
}
