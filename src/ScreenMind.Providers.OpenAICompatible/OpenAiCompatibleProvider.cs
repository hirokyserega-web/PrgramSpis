using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ScreenMind.AI;
using ScreenMind.Core.Ai;
using ScreenMind.Core.Imaging;
using ScreenMind.Core.Privacy;
using ScreenMind.Providers.OpenAICompatible.Qwen;

namespace ScreenMind.Providers.OpenAICompatible;

public sealed class OpenAiCompatibleProvider : IAiProvider
{
    private static readonly Uri DefaultBaseUri = new("http://localhost:8080/");
    private static readonly char[] WhitespaceChars = [' ', '\r', '\n', '\t'];
    private const string DefaultSecretName = "openai-compatible-api-key";

    private readonly HttpClient httpClient;
    private readonly ProviderConfigurationResolver configurationResolver;
    private readonly ISecretStore secretStore;
    private readonly Qwen.IQwenProxyClient qwenProxyClient;

    public OpenAiCompatibleProvider(
        HttpClient httpClient,
        ProviderConfigurationResolver configurationResolver,
        ISecretStore secretStore,
        Qwen.IQwenProxyClient? qwenProxyClient = null)
    {
        this.httpClient = httpClient;
        this.configurationResolver = configurationResolver;
        this.secretStore = secretStore;
        this.qwenProxyClient = qwenProxyClient ?? new Qwen.QwenProxyClient(httpClient);
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

        bool isQwenProxy = await IsQwenProxyAsync(configuration.BaseUri, cancellationToken).ConfigureAwait(false);

        string effectiveModelId = configuration.ModelId;
        Exception? resolveException = null;
        if (isQwenProxy && HasRealImage(request))
        {
            try
            {
                effectiveModelId = await ResolveQwenModelIdAsync(configuration.BaseUri, configuration.ModelId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                resolveException = exception;
            }
        }

        if (resolveException is not null)
        {
            yield return new AiStreamEvent.Failed(
                new AiError(AiErrorKind.UnsupportedModel, resolveException.Message),
                DateTimeOffset.UtcNow);
            yield break;
        }

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(configuration.BaseUri, "v1/chat/completions"));
        if (!string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);
        }

        string? qwenCookie = await secretStore.GetAsync("qwen-cookie", cancellationToken).ConfigureAwait(false);
        if (isQwenProxy && !string.IsNullOrWhiteSpace(qwenCookie))
        {
            httpRequest.Headers.TryAddWithoutValidation("Cookie", qwenCookie);
        }

        object? uploadedFile = null;
        Exception? uploadException = null;
        if (isQwenProxy && HasRealImage(request) && request.Image is not null)
        {
            try
            {
                uploadedFile = await qwenProxyClient.UploadImageAsync(configuration.BaseUri, request.Image, qwenCookie, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                uploadException = exception;
            }
        }

        if (uploadException is not null)
        {
            yield return new AiStreamEvent.Failed(
                new AiError(AiErrorKind.Unknown, uploadException.Message),
                DateTimeOffset.UtcNow);
            yield break;
        }

        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(BuildBody(request, effectiveModelId, uploadedFile, isQwenProxy)),
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
        bool isQwenProxy = await IsQwenProxyAsync(configuration.BaseUri, cancellationToken).ConfigureAwait(false);
        if (isQwenProxy && !string.IsNullOrWhiteSpace(qwenCookie))
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
        bool isQwenProxy = await IsQwenProxyAsync(configuration.BaseUri, cancellationToken).ConfigureAwait(false);
        if (isQwenProxy && !string.IsNullOrWhiteSpace(qwenCookie))
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

    private static object[] BuildMessages(AiRequest request, object? uploadedFile, bool isQwenProxy)
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
                    if (isQwenProxy)
                    {
                        messagesList.Add(new
                        {
                            role = "user",
                            content = msg.Content
                        });
                    }
                    else
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

    private static Dictionary<string, object> BuildBody(
        AiRequest request,
        string modelId,
        object? uploadedFile,
        bool isQwenProxy)
    {
        string effectiveModelId = GetEffectiveModelId(request, modelId);
        object[] messages = BuildMessages(request, uploadedFile, isQwenProxy);

        var bodyObj = new Dictionary<string, object>
        {
            { "model", effectiveModelId },
            { "stream", true },
            { "messages", messages }
        };

        if (isQwenProxy)
        {
            if (request.Conversation is not null)
            {
                bodyObj["conversation_id"] = request.Conversation.ClientConversationId;
                if (!string.IsNullOrWhiteSpace(request.Conversation.UpstreamChatId))
                {
                    bodyObj["chatId"] = request.Conversation.UpstreamChatId;
                    bodyObj["chat_id"] = request.Conversation.UpstreamChatId;
                }
                if (!string.IsNullOrWhiteSpace(request.Conversation.ParentId))
                {
                    bodyObj["parentId"] = request.Conversation.ParentId;
                    bodyObj["parent_id"] = request.Conversation.ParentId;
                }
            }

            if (uploadedFile is not null)
            {
                bodyObj["files"] = new object[] { uploadedFile };
            }
        }
        else
        {
            if (request.Conversation is not null)
            {
                bodyObj["chatId"] = request.Conversation.ClientConversationId;
                bodyObj["chat_id"] = request.Conversation.ClientConversationId;
                bodyObj["conversation_id"] = request.Conversation.ClientConversationId;
            }

            if (uploadedFile is not null)
            {
                bodyObj["files"] = new object[] { uploadedFile };
            }
        }

        return bodyObj;
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
        => request.Image is not null && (request.Image.Width != 1 || request.Image.Height != 1);

    private static bool IsQwenTextModel(string modelId)
    {
        return modelId.StartsWith("qwen", StringComparison.OrdinalIgnoreCase)
            && !modelId.Contains("-vl", StringComparison.OrdinalIgnoreCase)
            && !modelId.Contains("vision", StringComparison.OrdinalIgnoreCase)
            && !modelId.Contains("omni", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(modelId, "qwen3.7-plus", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDeepseekModel(string modelId)
        => modelId.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> IsQwenProxyAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        if (!baseUri.IsLoopback || !baseUri.AbsolutePath.Trim('/').StartsWith("api", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var capabilities = await qwenProxyClient.GetCapabilitiesAsync(baseUri, cancellationToken).ConfigureAwait(false);
            return capabilities.IsReady && string.Equals(capabilities.Service, "FreeQwenApi", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> ResolveQwenModelIdAsync(Uri baseUri, string selectedModelId, CancellationToken cancellationToken)
    {
        // Models that natively support vision/image input — do NOT redirect.
        if (selectedModelId.Contains("vl", StringComparison.OrdinalIgnoreCase) || 
            selectedModelId.Contains("vision", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(selectedModelId, "qwen3.7-plus", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(selectedModelId, "qwen3.8-max-preview", StringComparison.OrdinalIgnoreCase))
        {
            return selectedModelId;
        }

        var models = await qwenProxyClient.GetModelsAsync(baseUri, cancellationToken).ConfigureAwait(false);
        if (models.Count == 0)
        {
            return "qwen3-vl-plus";
        }

        string? visionModel = models.FirstOrDefault(m => m.Contains("vl", StringComparison.OrdinalIgnoreCase) || m.Contains("vision", StringComparison.OrdinalIgnoreCase));
        if (visionModel is not null)
        {
            return visionModel;
        }

        throw new QwenProxyException("No vision-capable model is available on the Qwen proxy.");
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
                    return nestedMessage.GetString();
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
}
