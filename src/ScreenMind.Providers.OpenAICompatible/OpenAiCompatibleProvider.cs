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
using Microsoft.Extensions.Logging;
using ScreenMind.AI;
using ScreenMind.Core.Ai;
using ScreenMind.Core.Imaging;
using ScreenMind.Core.Privacy;
using ScreenMind.Providers.OpenAICompatible.Qwen;

namespace ScreenMind.Providers.OpenAICompatible;

public sealed partial class OpenAiCompatibleProvider : IAiProvider
{
    private static readonly Uri DefaultBaseUri = new("http://localhost:8080/");
    private static readonly char[] WhitespaceChars = [' ', '\r', '\n', '\t'];
    private const string DefaultSecretName = "openai-compatible-api-key";

    private readonly HttpClient httpClient;
    private readonly ProviderConfigurationResolver configurationResolver;
    private readonly ISecretStore secretStore;
    private readonly Qwen.IQwenProxyClient qwenProxyClient;
    private readonly ILogger<OpenAiCompatibleProvider>? logger;

    public OpenAiCompatibleProvider(
        HttpClient httpClient,
        ProviderConfigurationResolver configurationResolver,
        ISecretStore secretStore,
        Qwen.IQwenProxyClient? qwenProxyClient = null,
        ILogger<OpenAiCompatibleProvider>? logger = null)
    {
        this.httpClient = httpClient;
        this.configurationResolver = configurationResolver;
        this.secretStore = secretStore;
        this.qwenProxyClient = qwenProxyClient ?? new Qwen.QwenProxyClient(httpClient);
        this.logger = logger;
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

        bool isManagedQwen = IsManagedQwenProfile(request.Profile, configuration);
        string? qwenCookie = await secretStore.GetAsync("qwen-cookie", cancellationToken).ConfigureAwait(false);
        bool isQwenProxy = isManagedQwen;
        string effectiveModelId = configuration.ModelId;
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(configuration.BaseUri, "v1/chat/completions"));
        AddBearer(httpRequest, configuration.ApiKey);

        if (isQwenProxy && !string.IsNullOrWhiteSpace(qwenCookie))
        {
            httpRequest.Headers.TryAddWithoutValidation("Cookie", qwenCookie);
        }

        QwenChatAttachment? uploadedFile = null;
        Exception? uploadException = null;
        if (isQwenProxy && HasRealImage(request) && request.Image is not null)
        {
            try
            {
                QwenUploadedFile upload = await qwenProxyClient.UploadImageAsync(configuration.BaseUri, request.Image, configuration.ApiKey, qwenCookie, cancellationToken).ConfigureAwait(false);
                uploadedFile = QwenChatAttachment.FromUpload(upload);
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

        if (logger is not null)
        {
            LogModelSelection(
                logger,
                configuration.ModelId,
                effectiveModelId,
                uploadedFile is not null ? "Qwen uploaded file" : "Inline image or text");
        }

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
                    UpdateConversationState(request.Conversation, root, response);
                    if (root.TryGetProperty("choices", out JsonElement choices)
                        && choices.ValueKind == JsonValueKind.Array
                        && choices.GetArrayLength() > 0
                        && choices[0].TryGetProperty("delta", out JsonElement delta))
                    {
                        foreach (string propertyName in new[] { "reasoning_content", "reasoning", "thinking" })
                        {
                            if (delta.TryGetProperty(propertyName, out JsonElement reasoning)
                                && reasoning.ValueKind == JsonValueKind.String)
                            {
                                string? reasoningText = reasoning.GetString();
                                if (!string.IsNullOrEmpty(reasoningText))
                                {
                                    yield return new AiStreamEvent.ReasoningDelta(reasoningText, DateTimeOffset.UtcNow);
                                }
                            }
                        }

                        if (delta.TryGetProperty("content", out JsonElement content)
                            && content.ValueKind == JsonValueKind.String)
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
    }

    public async Task<IReadOnlyList<AiModel>> GetModelsAsync(CancellationToken cancellationToken)
    {
        ProviderRuntimeConfiguration configuration = await ResolveDefaultConfigurationAsync(cancellationToken).ConfigureAwait(false);
        string? qwenCookie = await secretStore.GetAsync("qwen-cookie", cancellationToken).ConfigureAwait(false);
        bool isQwenProxy = IsManagedQwenProfile(
            new AiProfile("qwen-check", "Qwen", Id, configuration.ModelId, string.Empty),
            configuration);
        if (isQwenProxy)
        {
            QwenProxyCapabilities capabilities = await qwenProxyClient.GetCapabilitiesAsync(
                configuration.BaseUri,
                configuration.ApiKey,
                qwenCookie,
                cancellationToken).ConfigureAwait(false);
            if (!capabilities.IsReady || !string.Equals(capabilities.Service, "FreeQwenApi", StringComparison.OrdinalIgnoreCase))
            {
                throw new AiProviderException(new AiError(AiErrorKind.ServiceUnavailable, "FreeQwenApi health check did not report a ready Qwen proxy."));
            }

            List<string> modelIds = await qwenProxyClient.GetModelsAsync(
                configuration.BaseUri,
                configuration.ApiKey,
                qwenCookie,
                cancellationToken).ConfigureAwait(false);
            return modelIds.Select(model => new AiModel(model, model, model.Contains("vl", StringComparison.OrdinalIgnoreCase), true)).ToArray();
        }

        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(configuration.BaseUri, "v1/models"));
        AddBearer(request, configuration.ApiKey);
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
        string? qwenCookie = await secretStore.GetAsync("qwen-cookie", cancellationToken).ConfigureAwait(false);
        bool isQwenProxy = IsManagedQwenProfile(
            new AiProfile("qwen-check", "Qwen", Id, configuration.ModelId, string.Empty),
            configuration);
        if (isQwenProxy)
        {
            QwenProxyCapabilities capabilities = await qwenProxyClient.GetCapabilitiesAsync(
                configuration.BaseUri,
                configuration.ApiKey,
                qwenCookie,
                cancellationToken).ConfigureAwait(false);
            if (!capabilities.IsReady || !string.Equals(capabilities.Service, "FreeQwenApi", StringComparison.OrdinalIgnoreCase))
            {
                throw new AiProviderException(new AiError(AiErrorKind.ServiceUnavailable, "FreeQwenApi health check did not report a ready Qwen proxy."));
            }
            return;
        }

        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(configuration.BaseUri, "v1/models"));
        AddBearer(request, configuration.ApiKey);
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

    private static object[] BuildMessages(AiRequest request, QwenChatAttachment? uploadedFile, bool isQwenProxy)
    {
        List<object> messagesList = new();

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
            foreach (AiMessage msg in request.SessionMessages)
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
        QwenChatAttachment? uploadedFile,
        bool isQwenProxy)
    {
        object[] messages = BuildMessages(request, uploadedFile, isQwenProxy);

        Dictionary<string, object> bodyObj = new()
        {
            { "model", modelId },
            { "stream", true },
            { "messages", messages }
        };

        if (isQwenProxy)
        {
            if (request.Conversation is not null)
            {
                bodyObj["conversation_id"] = request.Conversation.ClientConversationId;
                if (!string.IsNullOrWhiteSpace(request.Conversation.CurrentUpstreamChatId))
                {
                    bodyObj["chatId"] = request.Conversation.CurrentUpstreamChatId;
                    bodyObj["chat_id"] = request.Conversation.CurrentUpstreamChatId;
                }
                if (!string.IsNullOrWhiteSpace(request.Conversation.CurrentParentId))
                {
                    bodyObj["parentId"] = request.Conversation.CurrentParentId;
                    bodyObj["parent_id"] = request.Conversation.CurrentParentId;
                }
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

    private static void UpdateConversationState(
        ProviderConversationState? conversation,
        JsonElement root,
        HttpResponseMessage response)
    {
        if (conversation is null)
        {
            return;
        }

        conversation.CurrentUpstreamChatId = FirstString(root, "chatId", "chat_id", "x_qwen_chat_id")
            ?? HeaderValue(response, "x-qwen-chat-id")
            ?? conversation.CurrentUpstreamChatId;
        conversation.CurrentParentId = FirstString(root, "parentId", "parent_id", "x_qwen_parent_id", "response_id")
            ?? HeaderValue(response, "x-qwen-parent-id")
            ?? conversation.CurrentParentId;
    }

    private static string? FirstString(JsonElement root, params string[] names)
    {
        foreach (string name in names)
        {
            if (root.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string? HeaderValue(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out IEnumerable<string>? values)
            ? values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            : null;

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Selected model: {SelectedModel}; Effective model: {EffectiveModel}; Image transport: {ImageTransport}")]
    private static partial void LogModelSelection(
        ILogger logger,
        string selectedModel,
        string effectiveModel,
        string imageTransport);

    private static void AddBearer(HttpRequestMessage request, string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    private static bool HasRealImage(AiRequest request)
        => request.Image is not null && (request.Image.Width != 1 || request.Image.Height != 1);

    private static bool IsDeepseekModel(string modelId)
        => modelId.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase);

    private static bool IsManagedQwenProfile(AiProfile profile, ProviderRuntimeConfiguration configuration)
        => profile.ProviderId.Equals("openai-compatible", StringComparison.OrdinalIgnoreCase)
            && (profile.Id.StartsWith("qwen", StringComparison.OrdinalIgnoreCase)
                || configuration.ModelId.StartsWith("qwen", StringComparison.OrdinalIgnoreCase))
            && configuration.BaseUri.IsLoopback
            && configuration.BaseUri.AbsolutePath.Trim('/').StartsWith("api", StringComparison.OrdinalIgnoreCase);

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
