using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ScreenMind.AI;
using ScreenMind.Core.Ai;
using ScreenMind.Core.Imaging;
using ScreenMind.Core.Privacy;

namespace ScreenMind.Providers.OpenAICompatible;

public sealed class OpenAiCompatibleProvider : IAiProvider
{
    private static readonly Uri DefaultBaseUri = new("http://localhost:8080/");
    private static readonly char[] WhitespaceChars = [' ', '\r', '\n', '\t'];
    private const string DefaultSecretName = "openai-compatible-api-key";

    private readonly HttpClient httpClient;
    private readonly ProviderConfigurationResolver configurationResolver;
    private readonly ISecretStore secretStore;

    public OpenAiCompatibleProvider(
        HttpClient httpClient,
        ProviderConfigurationResolver configurationResolver,
        ISecretStore secretStore)
    {
        this.httpClient = httpClient;
        this.configurationResolver = configurationResolver;
        this.secretStore = secretStore;
    }

    public string Id => "openai-compatible";

    public async IAsyncEnumerable<AiStreamEvent> StreamAsync(
        AiRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ProviderRuntimeConfiguration configuration = await configurationResolver
            .ResolveAsync(request.Profile, Id, DefaultBaseUri, DefaultSecretName, cancellationToken)
            .ConfigureAwait(false);

        if (HasRealImage(request) && IsDeepseekModel(configuration.ModelId))
        {
            yield return new AiStreamEvent.Failed(
                new AiError(
                    AiErrorKind.UnsupportedModel,
                    "DeepSeek local proxy does not currently support screenshot/image input. Use Qwen VL or another vision-capable model for screen analysis."),
                DateTimeOffset.UtcNow);
            yield break;
        }

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(configuration.BaseUri, "v1/chat/completions"));
        if (!string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);
        }

        string? qwenCookie = await secretStore.GetAsync("qwen-cookie", cancellationToken).ConfigureAwait(false);
        if (IsQwenProxyUri(configuration.BaseUri) && !string.IsNullOrWhiteSpace(qwenCookie))
        {
            httpRequest.Headers.TryAddWithoutValidation("Cookie", qwenCookie);
        }

        object? uploadedFile = null;
        if (IsQwenProxyUri(configuration.BaseUri) && HasRealImage(request))
        {
            uploadedFile = await UploadImageToProxyAsync(configuration.BaseUri, request.Image, qwenCookie, cancellationToken).ConfigureAwait(false);
        }

        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(BuildBody(request, configuration.ModelId, uploadedFile)),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage? response = null;
        AiError? startupFailure = null;
        try
        {
            response = await httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            startupFailure = ProviderErrorMapper.FromException(exception, Id);
        }

        if (startupFailure is not null)
        {
            yield return new AiStreamEvent.Failed(startupFailure, DateTimeOffset.UtcNow);
            yield break;
        }

        if (response is null)
        {
            yield return new AiStreamEvent.Failed(
                new AiError(AiErrorKind.Unknown, "OpenAI-compatible request did not return a response."),
                DateTimeOffset.UtcNow);
            yield break;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                AiError error = await CreateHttpErrorAsync(response, Id, cancellationToken).ConfigureAwait(false);
                yield return new AiStreamEvent.Failed(
                    error,
                    DateTimeOffset.UtcNow);
                yield break;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await foreach (string data in SseStreamReader.ReadDataAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                if (data == "[DONE]")
                {
                    yield return new AiStreamEvent.Completed(new AiUsage(), DateTimeOffset.UtcNow);
                    yield break;
                }

                if (!TryParseJson(data, out JsonDocument? document))
                {
                    yield return new AiStreamEvent.Failed(
                        new AiError(AiErrorKind.Unknown, "OpenAI-compatible stream returned malformed data."),
                        DateTimeOffset.UtcNow);
                    yield break;
                }

                using (document)
                {
                    JsonElement root = document!.RootElement;
                    if (root.TryGetProperty("choices", out JsonElement choices)
                        && choices.GetArrayLength() > 0
                        && choices[0].TryGetProperty("delta", out JsonElement delta)
                        && delta.TryGetProperty("content", out JsonElement content))
                    {
                        string? text = content.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            yield return new AiStreamEvent.TextDelta(text, DateTimeOffset.UtcNow);
                        }
                    }
                }
            }
        }
    }

    public async Task<IReadOnlyList<AiModel>> GetModelsAsync(CancellationToken cancellationToken)
    {
        ProviderRuntimeConfiguration configuration = await ResolveDefaultConfigurationAsync(cancellationToken).ConfigureAwait(false);
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(configuration.BaseUri, "v1/models"));
        if (!string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);
        }

        string? qwenCookie = await secretStore.GetAsync("qwen-cookie", cancellationToken).ConfigureAwait(false);
        if (IsQwenProxyUri(configuration.BaseUri) && !string.IsNullOrWhiteSpace(qwenCookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", qwenCookie);
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AiProviderException(ProviderErrorMapper.FromHttpStatus(response.StatusCode, Id));
        }

        return Array.Empty<AiModel>();
    }

    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        ProviderRuntimeConfiguration configuration = await ResolveDefaultConfigurationAsync(cancellationToken).ConfigureAwait(false);
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(configuration.BaseUri, "v1/models"));
        if (!string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);
        }

        string? qwenCookie = await secretStore.GetAsync("qwen-cookie", cancellationToken).ConfigureAwait(false);
        if (IsQwenProxyUri(configuration.BaseUri) && !string.IsNullOrWhiteSpace(qwenCookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", qwenCookie);
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AiProviderException(ProviderErrorMapper.FromHttpStatus(response.StatusCode, Id));
        }
    }

    private Task<ProviderRuntimeConfiguration> ResolveDefaultConfigurationAsync(CancellationToken cancellationToken)
    {
        return configurationResolver.ResolveAsync(
            new AiProfile("test", "Test", Id, "gpt-4o-mini", string.Empty),
            Id,
            DefaultBaseUri,
            DefaultSecretName,
            cancellationToken);
    }

    private static object[] BuildMessages(AiRequest request, object? uploadedFile = null)
    {
        var messagesList = new List<object>();

        // System prompt
        if (!string.IsNullOrWhiteSpace(request.Profile.SystemPrompt))
        {
            messagesList.Add(new
            {
                role = "system",
                content = request.Profile.SystemPrompt
            });
        }

        // Historical messages
        if (request.SessionMessages is not null)
        {
            foreach (var msg in request.SessionMessages)
            {
                if (msg.Role == AiMessageRole.User)
                {
                    if (msg.Image is not null && (msg.Image.Width != 1 || msg.Image.Height != 1))
                    {
                        string imageData = Convert.ToBase64String(msg.Image.Bytes.Span);
                        messagesList.Add(new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = msg.Content },
                                new { type = "image_url", image_url = new { url = $"data:{msg.Image.MediaType};base64,{imageData}" } }
                            }
                        });
                    }
                    else
                    {
                        messagesList.Add(new
                        {
                            role = "user",
                            content = msg.Content
                        });
                    }
                }
                else if (msg.Role == AiMessageRole.Assistant)
                {
                    messagesList.Add(new
                    {
                        role = "assistant",
                        content = msg.Content
                    });
                }
            }
        }

        // Current message
        if (uploadedFile is not null)
        {
            messagesList.Add(new
            {
                role = "user",
                content = request.Question,
                files = new object[] { uploadedFile }
            });
        }
        else
        {
            bool currentHasImage = request.Image is not null && (request.Image.Width != 1 || request.Image.Height != 1);
            if (currentHasImage && request.Image is not null)
            {
                string imageData = Convert.ToBase64String(request.Image.Bytes.Span);
                messagesList.Add(new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = request.Question },
                        new { type = "image_url", image_url = new { url = $"data:{request.Image.MediaType};base64,{imageData}" } }
                    }
                });
            }
            else
            {
                messagesList.Add(new
                {
                    role = "user",
                    content = request.Question
                });
            }
        }

        return messagesList.ToArray();
    }

    private static Dictionary<string, object> BuildBody(AiRequest request, string modelId, object? uploadedFile = null)
    {
        string effectiveModelId = GetEffectiveModelId(request, modelId);
        object[] messages = BuildMessages(request, uploadedFile);

        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var bodyObj = new Dictionary<string, object>
            {
                { "model", effectiveModelId },
                { "stream", true },
                { "chatId", request.SessionId },
                { "chat_id", request.SessionId },
                { "conversation_id", request.SessionId },
                { "messages", messages }
            };

            if (uploadedFile is not null)
            {
                bodyObj["files"] = new object[] { uploadedFile };
            }

            return bodyObj;
        }

        var defaultBodyObj = new Dictionary<string, object>
        {
            { "model", effectiveModelId },
            { "stream", true },
            { "messages", messages }
        };

        if (uploadedFile is not null)
        {
            defaultBodyObj["files"] = new object[] { uploadedFile };
        }

        return defaultBodyObj;
    }

    private static string GetEffectiveModelId(AiRequest request, string modelId)
    {
        if (HasRealImage(request) && IsQwenTextModel(modelId))
        {
            return "qwen3-vl-plus";
        }

        return modelId;
    }

    private static bool HasRealImage(AiRequest request)
        => request.Image.Width != 1 || request.Image.Height != 1;

    private static bool IsQwenTextModel(string modelId)
    {
        return modelId.StartsWith("qwen", StringComparison.OrdinalIgnoreCase)
            && !modelId.Contains("-vl", StringComparison.OrdinalIgnoreCase)
            && !modelId.Contains("vision", StringComparison.OrdinalIgnoreCase)
            && !modelId.Contains("omni", StringComparison.OrdinalIgnoreCase)
            && !modelId.Contains("3.7", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDeepseekModel(string modelId)
        => modelId.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase);

    private static bool IsQwenProxyUri(Uri baseUri)
    {
        return baseUri.IsLoopback
            && baseUri.AbsolutePath.Trim('/').StartsWith("api", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<AiError> CreateHttpErrorAsync(
        HttpResponseMessage response,
        string providerId,
        CancellationToken cancellationToken)
    {
        AiError mapped = ProviderErrorMapper.FromHttpStatus(response.StatusCode, providerId);
        string? detail = await TryReadErrorDetailAsync(response, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(detail)
            ? mapped
            : mapped with { Message = $"{mapped.Message} {detail}" };
    }

    private static async Task<string?> TryReadErrorDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        string? parsed = TryExtractErrorMessage(body);
        return Truncate(Compact(parsed ?? body), 600);
    }

    private static string? TryExtractErrorMessage(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString();
                }

                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out JsonElement nestedMessage)
                    && nestedMessage.ValueKind == JsonValueKind.String)
                {
                    return nestedMessage.GetString();
                }
            }

            if (root.TryGetProperty("message", out JsonElement message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string Compact(string value)
        => string.Join(
            " ",
            value.Split(
                WhitespaceChars,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";

    private static bool TryParseJson(string data, out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(data);
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }

    private async Task<object?> UploadImageToProxyAsync(
        Uri baseUri,
        ScreenImage image,
        string? qwenCookie,
        CancellationToken cancellationToken)
    {
        try
        {
            Uri uploadUri = baseUri.ToString().EndsWith('/')
                ? new Uri(baseUri, "files/upload")
                : new Uri(new Uri(baseUri.ToString() + "/"), "files/upload");

            using HttpRequestMessage httpRequest = new(HttpMethod.Post, uploadUri);
            if (!string.IsNullOrWhiteSpace(qwenCookie))
            {
                httpRequest.Headers.TryAddWithoutValidation("Cookie", qwenCookie);
            }

            using MultipartFormDataContent form = new();
            using ByteArrayContent fileContent = new(image.Bytes.ToArray());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(image.MediaType);
            
            string extension = image.Format == ScreenImageFormat.Png ? "png" : "jpg";
            string filename = $"screenshot_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.{extension}";
            
            form.Add(fileContent, "file", filename);
            httpRequest.Content = form;

            using HttpResponseMessage response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                string jsonString = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(jsonString);
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("success", out JsonElement successProp) && successProp.GetBoolean()
                    && root.TryGetProperty("file", out JsonElement fileProp))
                {
                    return new
                    {
                        id = fileProp.GetProperty("id").GetString(),
                        fileId = fileProp.GetProperty("fileId").GetString(),
                        file_path = fileProp.GetProperty("file_path").GetString(),
                        name = fileProp.GetProperty("name").GetString(),
                        url = fileProp.GetProperty("url").GetString(),
                        size = fileProp.GetProperty("size").GetInt64(),
                        type = fileProp.GetProperty("type").GetString()
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OpenAiCompatibleProvider] OSS upload failed: {ex}");
        }
        return null;
    }
}
