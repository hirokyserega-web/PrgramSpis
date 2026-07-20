using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ScreenMind.Core.Imaging;

namespace ScreenMind.Providers.OpenAICompatible.Qwen;

public sealed class QwenProxyClient : IQwenProxyClient
{
    private const int MaximumUploadBytes = 10 * 1024 * 1024;
    private readonly HttpClient httpClient;

    public QwenProxyClient(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<QwenProxyCapabilities> GetCapabilitiesAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(baseUri, "health"));
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response.StatusCode, body, "Qwen proxy health check failed.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            string service = GetString(root, "service") ?? string.Empty;
            bool isReady = GetBoolean(root, "ok");
            int models = GetInt32(root, "models");
            int totalAccounts = 0;
            int availableAccounts = 0;
            if (root.TryGetProperty("accounts", out JsonElement accounts) && accounts.ValueKind == JsonValueKind.Object)
            {
                totalAccounts = GetInt32(accounts, "total");
                availableAccounts = GetInt32(accounts, "available");
            }

            return new QwenProxyCapabilities(isReady, service, models, totalAccounts, availableAccounts);
        }
        catch (JsonException exception)
        {
            throw new QwenProxyException("Qwen proxy health check returned invalid JSON.", exception);
        }
    }

    public async Task<List<string>> GetModelsAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(baseUri, "v1/models"));
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response.StatusCode, body, "Qwen proxy model list request failed.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out JsonElement data)
                || data.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return data.EnumerateArray()
                .Select(item => GetString(item, "id"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToList();
        }
        catch (JsonException exception)
        {
            throw new QwenProxyException("Qwen proxy model list returned invalid JSON.", exception);
        }
    }

    public async Task<QwenUploadedFile> UploadImageAsync(
        Uri baseUri,
        ScreenImage image,
        string? cookie,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        (string extension, string mediaType) = GetUploadFormat(image.Format);
        if (!string.Equals(image.MediaType, mediaType, StringComparison.OrdinalIgnoreCase))
        {
            throw new QwenProxyException($"Qwen image upload rejected: format {image.Format} does not match MIME type {image.MediaType}.");
        }

        byte[] bytes = image.Bytes.ToArray();
        if (bytes.Length == 0)
        {
            throw new QwenProxyException("Qwen image upload failed because the image is empty.");
        }
        if (bytes.Length > MaximumUploadBytes)
        {
            throw new QwenProxyException("Qwen image upload failed because the image is too large.", 413);
        }

        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(baseUri, "files/upload"));
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        using MultipartFormDataContent form = new();
        using ByteArrayContent fileContent = new(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(fileContent, "file", $"screenshot_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{extension}");
        request.Content = form;

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response.StatusCode, body, "Qwen image upload failed.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (!GetBoolean(root, "success") || !root.TryGetProperty("file", out JsonElement file))
            {
                throw new QwenProxyException("Qwen image upload failed: the proxy returned no file object.");
            }

            return new QwenUploadedFile(
                GetString(file, "id") ?? string.Empty,
                GetString(file, "fileId") ?? string.Empty,
                GetString(file, "file_path") ?? GetString(file, "filePath") ?? string.Empty,
                GetString(file, "name") ?? string.Empty,
                GetString(file, "url") ?? string.Empty,
                GetInt64(file, "size"),
                GetString(file, "type") ?? string.Empty);
        }
        catch (JsonException exception)
        {
            throw new QwenProxyException("Qwen image upload returned invalid JSON.", exception);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new QwenProxyException("Qwen proxy is unavailable.", exception);
        }
    }

    private static void EnsureSuccess(HttpStatusCode statusCode, string body, string fallback)
    {
        if ((int)statusCode is >= 200 and <= 299)
        {
            return;
        }

        string message = (int)statusCode switch
        {
            401 => "Qwen proxy rejected authentication.",
            403 => "Qwen proxy denied the request.",
            413 => "Qwen image upload failed because the image is too large.",
            _ when ContainsAntiBot(body) => "Qwen authentication requires attention in the proxy browser session.",
            _ => fallback,
        };
        throw new QwenProxyException(message, (int)statusCode);
    }

    private static bool ContainsAntiBot(string body)
        => body.Contains("captcha", StringComparison.OrdinalIgnoreCase)
            || body.Contains("verification", StringComparison.OrdinalIgnoreCase)
            || body.Contains("rgv587", StringComparison.OrdinalIgnoreCase)
            || body.Contains("purecaptcha", StringComparison.OrdinalIgnoreCase)
            || body.Contains("fail_sys_user_validate", StringComparison.OrdinalIgnoreCase);

    private static (string Extension, string MediaType) GetUploadFormat(ScreenImageFormat format)
        => format switch
        {
            ScreenImageFormat.Png => (".png", "image/png"),
            ScreenImageFormat.Jpeg => (".jpg", "image/jpeg"),
            ScreenImageFormat.WebP => (".webp", "image/webp"),
            _ => throw new QwenProxyException("Qwen image upload rejected: unsupported image format."),
        };

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBoolean(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.True;

    private static int GetInt32(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : 0;

    private static long GetInt64(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement value) && value.TryGetInt64(out long result)
            ? result
            : 0;
}
