using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ScreenMind.AI;
using ScreenMind.Core.Ai;
using ScreenMind.Core.Imaging;
using ScreenMind.Core.Privacy;
using ScreenMind.Core.Settings;
using ScreenMind.Providers.Anthropic;
using ScreenMind.Providers.Gemini;
using ScreenMind.Providers.Ollama;
using ScreenMind.Providers.OpenAI;
using ScreenMind.Providers.OpenAICompatible;
using ScreenMind.Providers.OpenAICompatible.Qwen;

namespace ScreenMind.AI.Tests;

public sealed class ProviderContractTests
{
    [Fact]
    public async Task OpenAiShouldStreamResponsesApiDeltas()
    {
        QueueHttpMessageHandler handler = new(SseResponse(
            """
            data: {"type":"response.output_text.delta","delta":"hello"}

            data: {"type":"response.completed"}

            data: [DONE]

            """));
        OpenAiProvider provider = new(CreateClient(handler), CreateResolver("openai", "https://unit.test/", "openai-api-key", "secret"));

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(provider.StreamAsync(CreateImageRequest("openai"), CancellationToken.None));

        handler.Requests.Single().Uri.Should().Be("https://unit.test/v1/responses");
        handler.Requests.Single().Authorization.Should().Be("Bearer secret");
        handler.Requests.Single().Body.Should().Contain("input_image");
        events.OfType<AiStreamEvent.TextDelta>().Single().Text.Should().Be("hello");
        events.OfType<AiStreamEvent.Completed>().Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AiErrorKind.Auth)]
    [InlineData(HttpStatusCode.TooManyRequests, AiErrorKind.RateLimit)]
    [InlineData(HttpStatusCode.InternalServerError, AiErrorKind.ServiceUnavailable)]
    public async Task OpenAiShouldMapHttpErrors(HttpStatusCode statusCode, AiErrorKind expected)
    {
        QueueHttpMessageHandler handler = new(new HttpResponseMessage(statusCode));
        OpenAiProvider provider = new(CreateClient(handler), CreateResolver("openai", "https://unit.test/", "openai-api-key", "secret"));

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(provider.StreamAsync(CreateRequest("openai"), CancellationToken.None));

        events.OfType<AiStreamEvent.Failed>().Single().Error.Kind.Should().Be(expected);
    }

    [Fact]
    public async Task OpenAiShouldMapTimeout()
    {
        QueueHttpMessageHandler handler = new(new TaskCanceledException("timeout"));
        OpenAiProvider provider = new(CreateClient(handler), CreateResolver("openai", "https://unit.test/", "openai-api-key", "secret"));

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(provider.StreamAsync(CreateRequest("openai"), CancellationToken.None));

        events.OfType<AiStreamEvent.Failed>().Single().Error.Kind.Should().Be(AiErrorKind.Timeout);
    }

    [Fact]
    public async Task OpenAiShouldMapMalformedStream()
    {
        QueueHttpMessageHandler handler = new(SseResponse("data: {broken}\n\n"));
        OpenAiProvider provider = new(CreateClient(handler), CreateResolver("openai", "https://unit.test/", "openai-api-key", "secret"));

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(provider.StreamAsync(CreateRequest("openai"), CancellationToken.None));

        events.OfType<AiStreamEvent.Failed>().Single().Error.Kind.Should().Be(AiErrorKind.Unknown);
    }

    [Fact]
    public async Task OpenAiCompatibleShouldEmitReasoningFieldsSeparately()
    {
        QueueHttpMessageHandler handler = new(SseResponse(
            "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"internal\",\"content\":\"answer\"}}]}\n\ndata: [DONE]\n\n"));
        OpenAiCompatibleProvider provider = new(
            CreateClient(handler),
            CreateResolver("openai-compatible", "https://compatible.test/", "compatible-key", "secret"),
            new FakeSecretStore(null, null));

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(provider.StreamAsync(CreateRequest("openai-compatible"), CancellationToken.None));

        events.OfType<AiStreamEvent.ReasoningDelta>().Single().Text.Should().Be("internal");
        events.OfType<AiStreamEvent.TextDelta>().Single().Text.Should().Be("answer");
    }

    [Fact]
    public async Task OpenAiCompatibleShouldStreamChatCompletionsDeltas()
    {
        QueueHttpMessageHandler handler = new(SseResponse(
            """
            data: {"choices":[{"delta":{"content":"hi"}}]}

            data: [DONE]

            """));
        OpenAiCompatibleProvider provider = new(
            CreateClient(handler),
            CreateResolver("openai-compatible", "https://compatible.test/", "compatible-key", "secret"),
            new FakeSecretStore(null, null));

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(provider.StreamAsync(CreateRequest("openai-compatible"), CancellationToken.None));

        handler.Requests.Single().Uri.Should().Be("https://compatible.test/v1/chat/completions");
        handler.Requests.Single().Authorization.Should().Be("Bearer secret");
        events.OfType<AiStreamEvent.TextDelta>().Single().Text.Should().Be("hi");
        events.OfType<AiStreamEvent.Completed>().Should().ContainSingle();
    }

    [Fact]
    public async Task Qwen38MaxPreviewImageRequestMustNotSwitchModel()
    {
        QueueHttpMessageHandler handler = new(SseResponse("data: {\"chatId\":\"upstream-after\",\"parentId\":\"parent-after\",\"choices\":[]}\n\ndata: [DONE]\n\n"));
        OpenAiCompatibleProvider provider = new(
            CreateClient(handler),
            CreateResolver("openai-compatible", "http://localhost:3264/api", "compatible-key", "secret", "qwen3.8-max-preview"),
            new FakeSecretStore("qwen-cookie", "cookie"),
            new FakeQwenProxyClient { IsQwen = true, Models = new List<string> { "qwen3.8-max-preview" } });

        AiRequest request = CreateImageRequest("openai-compatible", "qwen3.8-max-preview");
        await CollectAsync(provider.StreamAsync(request, CancellationToken.None));

        RecordedRequest sentRequest = handler.Requests.Single();
        using JsonDocument body = JsonDocument.Parse(sentRequest.Body!);
        JsonElement root = body.RootElement;
        root.GetProperty("model").GetString().Should().Be("qwen3.8-max-preview");
        root.GetProperty("messages").EnumerateArray().Last().GetProperty("files").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Qwen38MaxPreviewTextRequestMustNotSwitchModel()
    {
        QueueHttpMessageHandler handler = new(SseResponse("data: [DONE]\n\n"));
        OpenAiCompatibleProvider provider = new(
            CreateClient(handler),
            CreateResolver("openai-compatible", "http://localhost:3264/api", "compatible-key", "secret", "qwen3.8-max-preview"),
            new FakeSecretStore("qwen-cookie", "cookie"),
            new FakeQwenProxyClient { IsQwen = true });

        AiRequest request = CreateRequest("openai-compatible", "qwen3.8-max-preview", image: null);
        await CollectAsync(provider.StreamAsync(request, CancellationToken.None));

        using JsonDocument body = JsonDocument.Parse(handler.Requests.Single().Body!);
        body.RootElement.GetProperty("model").GetString().Should().Be("qwen3.8-max-preview");
    }

    [Fact]
    public async Task OpenAiCompatibleShouldUploadImageAndSendConversationStateOnQwenProxy()
    {
        QueueHttpMessageHandler handler = new(SseResponse("data: {\"chatId\":\"upstream-after\",\"parentId\":\"parent-after\",\"choices\":[]}\n\ndata: [DONE]\n\n"));
        FakeQwenProxyClient fakeQwenClient = new FakeQwenProxyClient { IsQwen = true, Models = new List<string> { "qwen3.8-max-preview" } };
        OpenAiCompatibleProvider provider = new(
            CreateClient(handler),
            CreateResolver("openai-compatible", "http://localhost:3264/api", "compatible-key", "secret", "qwen3.7-max"),
            new FakeSecretStore(null, null),
            fakeQwenClient);

        ProviderConversationState conversation = new("openai-compatible", "client-conv-id", "upstream-chat-id", "parent-id");
        AiRequest request = new AiRequest(
            new AiProfile("profile", "Profile", "openai-compatible", "qwen3.7-max", "system"),
            new ScreenImage([1, 2, 3], "image/png", ScreenImageFormat.Png, 800, 600, DateTimeOffset.UtcNow),
            "question",
            [],
            conversation);

        await CollectAsync(provider.StreamAsync(request, CancellationToken.None));

        handler.Requests.Single().Uri.Should().Be("http://localhost:3264/api/v1/chat/completions");
        handler.Requests.Single().Body.Should().Contain("\"conversation_id\":\"client-conv-id\"");
        handler.Requests.Single().Body.Should().Contain("\"chatId\":\"upstream-chat-id\"");
        handler.Requests.Single().Body.Should().Contain("\"parentId\":\"parent-id\"");
        handler.Requests.Single().Body.Should().Contain("\"files\"");
        conversation.CurrentUpstreamChatId.Should().Be("upstream-after");
        conversation.CurrentParentId.Should().Be("parent-after");
    }


    [Theory]
    [InlineData(ScreenImageFormat.Png, "image/png", ".png")]
    [InlineData(ScreenImageFormat.Jpeg, "image/jpeg", ".jpg")]
    [InlineData(ScreenImageFormat.WebP, "image/webp", ".webp")]
    public async Task QwenProxyClientUploadUsesBearerFileFieldMimeAndFormat(ScreenImageFormat format, string mediaType, string extension)
    {
        QueueHttpMessageHandler handler = new(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"success\":true,\"file\":{\"id\":\"file-1\",\"fileId\":\"file-1\",\"file_path\":\"/tmp/file\",\"name\":\"remote\",\"url\":\"http://file\",\"size\":3,\"type\":\"" + mediaType + "\"}}", Encoding.UTF8, "application/json")
        });
        QwenProxyClient client = new(CreateClient(handler));
        byte[] bytes = format switch
        {
            ScreenImageFormat.Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
            ScreenImageFormat.Jpeg => [0xFF, 0xD8, 0xFF, 0xD9],
            ScreenImageFormat.WebP => [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50],
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
        ScreenImage image = new(bytes, mediaType, format, 800, 600, DateTimeOffset.UtcNow);

        QwenUploadedFile uploaded = await client.UploadImageAsync(new Uri("http://localhost:3264/api/"), image, "qwen-api-key", "qwen-cookie", CancellationToken.None);

        RecordedRequest request = handler.Requests.Single();
        request.Authorization.Should().Be("Bearer qwen-api-key");
        request.Headers.Should().Contain("Cookie: qwen-cookie");
        request.Body.Should().Contain("name=file");
        request.Body.Should().Contain("Content-Type: " + mediaType);
        request.Body.Should().Contain(extension);
        uploaded.Type.Should().Be(mediaType);
    }

    [Fact]
    public async Task QwenProxyClientHealthAndModelsUseBearerKey()
    {
        QueueHttpMessageHandler handler = new(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"service\":\"FreeQwenApi\",\"models\":1,\"accounts\":{\"total\":1,\"available\":1}}", Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[{\"id\":\"qwen3.7\"}]}", Encoding.UTF8, "application/json")
            });
        QwenProxyClient client = new(CreateClient(handler));

        await client.GetCapabilitiesAsync(new Uri("http://localhost:3264/api/"), "qwen-api-key", null, CancellationToken.None);
        await client.GetModelsAsync(new Uri("http://localhost:3264/api/"), "qwen-api-key", null, CancellationToken.None);

        handler.Requests.Should().AllSatisfy(request => request.Authorization.Should().Be("Bearer qwen-api-key"));
        handler.Requests.Should().AllSatisfy(request => request.Headers.Should().NotContain(header => header.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task OrdinaryQwenMessageDoesNotRequireHealthPreflight()
    {
        QueueHttpMessageHandler handler = new(SseResponse("data: {\"choices\":[{\"delta\":{\"content\":\"Да\"}}]}\n\ndata: [DONE]\n\n"));
        FakeQwenProxyClient qwen = new() { HealthException = new QwenProxyException("health unavailable") };
        OpenAiCompatibleProvider provider = new(CreateClient(handler), CreateResolver("openai-compatible", "http://localhost:3264/api", "compatible-key", "secret", "qwen3.8-max-preview"), new FakeSecretStore(null, null), qwen);
        IReadOnlyList<AiStreamEvent> events = await CollectAsync(provider.StreamAsync(CreateTextRequest("openai-compatible", "qwen3.8-max-preview"), CancellationToken.None));
        events.OfType<AiStreamEvent.TextDelta>().Single().Text.Should().Be("Да");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task InvalidImageSignatureIsRejectedBeforeUpload()
    {
        QueueHttpMessageHandler handler = new();
        QwenProxyClient client = new(CreateClient(handler));
        Func<Task> action = () => client.UploadImageAsync(new Uri("http://localhost:3264/api/"), new ScreenImage([1, 2, 3], "image/png", ScreenImageFormat.Png, 320, 200, DateTimeOffset.UtcNow), "key", null, CancellationToken.None);
        await action.Should().ThrowAsync<QwenProxyException>().WithMessage("*bytes do not match*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task QwenSecondRequestUsesChatAndParentFromFirstSse()
    {
        QueueHttpMessageHandler handler = new(
            SseResponse("data: {\"chatId\":\"chat-after\",\"parentId\":\"parent-after\",\"choices\":[]}\n\ndata: [DONE]\n\n"),
            SseResponse("data: {\"choices\":[]}\n\ndata: [DONE]\n\n"));
        FakeQwenProxyClient qwen = new() { IsQwen = true };
        OpenAiCompatibleProvider provider = new(
            CreateClient(handler),
            CreateResolver("openai-compatible", "http://localhost:3264/api", "compatible-key", "secret", "qwen3.7"),
            new FakeSecretStore(null, null),
            qwen);
        ProviderConversationState conversation = new("openai-compatible", "client-conv-id");

        await CollectAsync(provider.StreamAsync(new AiRequest(new AiProfile("qwen", "Qwen", "openai-compatible", "qwen3.7", "exact-system"), null, "one", [], conversation), CancellationToken.None));
        await CollectAsync(provider.StreamAsync(new AiRequest(new AiProfile("qwen", "Qwen", "openai-compatible", "qwen3.7", "exact-system"), null, "two", [], conversation), CancellationToken.None));

        using JsonDocument second = JsonDocument.Parse(handler.Requests[1].Body!);
        second.RootElement.GetProperty("chatId").GetString().Should().Be("chat-after");
        second.RootElement.GetProperty("parentId").GetString().Should().Be("parent-after");
        second.RootElement.GetProperty("messages")[0].GetProperty("content").GetString().Should().Be("exact-system");
    }

    [Fact]
    public async Task ResolverShouldNormalizeLocalQwenProxyBaseUrl()
    {
        ScreenMindSettings settings = ScreenMindSettings.CreateDefault();
        settings.Providers.Providers["openai-compatible"].BaseUrl = "http://localhost:3264";
        settings.ManagedProxies.Qwen.Enabled = false;
        ProviderConfigurationResolver resolver = new(
            new FakeSettingsStore(settings),
            new FakeSecretStore(null, null));

        ProviderRuntimeConfiguration configuration = await resolver.ResolveAsync(
            new AiProfile("qwen-free", "Qwen", "openai-compatible", "qwen3.7-max", "system"),
            "openai-compatible",
            new Uri("http://localhost:8080/"),
            null,
            CancellationToken.None);

        configuration.BaseUri.ToString().Should().Be("http://localhost:3264/api/");
    }

    [Fact]
    public async Task ResolverShouldRouteDeepseekModelToDeepseekLocalProxy()
    {
        ScreenMindSettings settings = ScreenMindSettings.CreateDefault();
        settings.Providers.Providers["openai-compatible"].BaseUrl = "http://localhost:3264/";
        settings.ManagedProxies.Deepseek.Enabled = false;
        ProviderConfigurationResolver resolver = new(
            new FakeSettingsStore(settings),
            new FakeSecretStore(null, null));

        ProviderRuntimeConfiguration configuration = await resolver.ResolveAsync(
            new AiProfile("deepseek-free", "DeepSeek", "openai-compatible", "deepseek-chat", "system"),
            "openai-compatible",
            new Uri("http://localhost:8080/"),
            null,
            CancellationToken.None);

        configuration.BaseUri.ToString().Should().Be("http://localhost:9655/");
    }

    [Fact]
    public async Task ResolverShouldPreferProfileModelOverEndpointModelForManagedProxyRouting()
    {
        ScreenMindSettings settings = ScreenMindSettings.CreateDefault();
        settings.Providers.Providers["openai-compatible"].BaseUrl = "http://localhost:3264/";
        settings.Providers.Providers["openai-compatible"].ModelId = "deepseek-chat";
        ProviderConfigurationResolver resolver = new(
            new FakeSettingsStore(settings),
            new FakeSecretStore(null, null));

        ProviderRuntimeConfiguration configuration = await resolver.ResolveAsync(
            new AiProfile("universal", "Universal", "openai-compatible", "qwen3.7-max", "system"),
            "openai-compatible",
            new Uri("http://localhost:8080/"),
            null,
            CancellationToken.None);

        configuration.ModelId.Should().Be("qwen3.7-max");
        configuration.BaseUri.ToString().Should().Be("http://localhost:3264/api/");
    }

    [Fact]
    public async Task OpenAiCompatibleShouldRejectDeepseekImageRequestsBeforeHttp()
    {
        QueueHttpMessageHandler handler = new();
        OpenAiCompatibleProvider provider = new(
            CreateClient(handler),
            CreateResolver("openai-compatible", "http://localhost:9655/", "compatible-key", "secret", "deepseek-chat"),
            new FakeSecretStore(null, null));

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(provider.StreamAsync(CreateImageRequest("openai-compatible", "deepseek-chat"), CancellationToken.None));

        events.OfType<AiStreamEvent.Failed>().Single().Error.Kind.Should().Be(AiErrorKind.UnsupportedModel);
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AiErrorKind.Auth)]
    [InlineData(HttpStatusCode.TooManyRequests, AiErrorKind.RateLimit)]
    [InlineData(HttpStatusCode.BadGateway, AiErrorKind.ServiceUnavailable)]
    public async Task OpenAiCompatibleShouldMapHttpErrors(HttpStatusCode statusCode, AiErrorKind expected)
    {
        QueueHttpMessageHandler handler = new(new HttpResponseMessage(statusCode));
        OpenAiCompatibleProvider provider = new(
            CreateClient(handler),
            CreateResolver("openai-compatible", "https://compatible.test/", "compatible-key", "secret"),
            new FakeSecretStore(null, null));

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(provider.StreamAsync(CreateRequest("openai-compatible"), CancellationToken.None));

        events.OfType<AiStreamEvent.Failed>().Single().Error.Kind.Should().Be(expected);
    }

    [Fact]
    public async Task OpenAiCompatibleShouldMapMalformedStream()
    {
        QueueHttpMessageHandler handler = new(SseResponse("data: nope\n\n"));
        OpenAiCompatibleProvider provider = new(
            CreateClient(handler),
            CreateResolver("openai-compatible", "https://compatible.test/", "compatible-key", "secret"),
            new FakeSecretStore(null, null));

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(provider.StreamAsync(CreateRequest("openai-compatible"), CancellationToken.None));

        events.OfType<AiStreamEvent.Failed>().Single().Error.Kind.Should().Be(AiErrorKind.Unknown);
    }

    [Fact]
    public async Task AnthropicShouldStreamMessageEvents()
    {
        QueueHttpMessageHandler handler = new(SseResponse(
            """
            data: {"type":"content_block_delta","delta":{"text":"claude"}}

            data: {"type":"message_stop"}

            """));
        AnthropicProvider provider = new(CreateClient(handler), CreateResolver("anthropic", "https://anthropic.test/", "anthropic-key", "secret"));

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(provider.StreamAsync(CreateRequest("anthropic"), CancellationToken.None));

        handler.Requests.Single().Uri.Should().Be("https://anthropic.test/v1/messages");
        handler.Requests.Single().Headers.Should().Contain("x-api-key: secret");
        events.OfType<AiStreamEvent.TextDelta>().Single().Text.Should().Be("claude");
        events.OfType<AiStreamEvent.Completed>().Should().ContainSingle();
    }

    [Fact]
    public async Task GeminiShouldMapSafetyBlock()
    {
        QueueHttpMessageHandler handler = new(SseResponse(
            """
            data: {"promptFeedback":{"blockReason":"SAFETY"}}

            """));
        GeminiProvider provider = new(CreateClient(handler), CreateResolver("gemini", "https://gemini.test/", "gemini-key", "secret"));

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(provider.StreamAsync(CreateRequest("gemini"), CancellationToken.None));

        handler.Requests.Single().Uri.Should().StartWith("https://gemini.test/v1beta/models/model:streamGenerateContent");
        events.OfType<AiStreamEvent.Failed>().Single().Error.Kind.Should().Be(AiErrorKind.SafetyBlocked);
    }

    [Fact]
    public async Task OllamaShouldStreamGenerateResponses()
    {
        QueueHttpMessageHandler handler = new(JsonLinesResponse(
            """
            {"response":"local","done":false}
            {"done":true}
            """));
        OllamaProvider provider = new(CreateClient(handler), CreateResolver("ollama", "http://localhost:11434/", null, null));

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(provider.StreamAsync(CreateRequest("ollama"), CancellationToken.None));

        handler.Requests.Single().Uri.Should().Be("http://localhost:11434/api/generate");
        events.OfType<AiStreamEvent.TextDelta>().Single().Text.Should().Be("local");
        events.OfType<AiStreamEvent.Completed>().Should().ContainSingle();
    }

    [Fact]
    public async Task OllamaShouldMapServiceUnavailable()
    {
        QueueHttpMessageHandler handler = new(new HttpRequestException("connection refused"));
        OllamaProvider provider = new(CreateClient(handler), CreateResolver("ollama", "http://localhost:11434/", null, null));

        IReadOnlyList<AiStreamEvent> events = await CollectAsync(provider.StreamAsync(CreateRequest("ollama"), CancellationToken.None));

        events.OfType<AiStreamEvent.Failed>().Single().Error.Kind.Should().Be(AiErrorKind.Network);
    }

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler);
    }

    private static ProviderConfigurationResolver CreateResolver(
        string providerId,
        string baseUrl,
        string? secretName,
        string? secret,
        string modelId = "model")
    {
        ScreenMindSettings settings = ScreenMindSettings.CreateDefault();
        settings.Providers.Providers[providerId] = new ProviderEndpointSettings
        {
            BaseUrl = baseUrl,
            ModelId = modelId,
            SecretName = secretName,
        };

        return new ProviderConfigurationResolver(
            new FakeSettingsStore(settings),
            new FakeSecretStore(secretName, secret));
    }

    private static AiRequest CreateRequest(string providerId, string modelId = "model", ScreenImage? image = null)
    {
        return new AiRequest(
            new AiProfile("profile", "Profile", providerId, modelId, "system"),
            image,
            "question",
            []);
    }

    private static AiRequest CreateImageRequest(string providerId, string modelId = "model")
    {
        return CreateRequest(
            providerId,
            modelId,
            new ScreenImage([1, 2, 3], "image/png", ScreenImageFormat.Png, 800, 600, DateTimeOffset.UtcNow));
    }

    private static AiRequest CreateTextRequest(string providerId, string modelId = "model")
        => CreateRequest(providerId, modelId);

    private static HttpResponseMessage SseResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
        };
    }

    private static HttpResponseMessage JsonLinesResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-ndjson"),
        };
    }

    private static async Task<IReadOnlyList<AiStreamEvent>> CollectAsync(IAsyncEnumerable<AiStreamEvent> stream)
    {
        List<AiStreamEvent> events = [];
        await foreach (AiStreamEvent streamEvent in stream.ConfigureAwait(false))
        {
            events.Add(streamEvent);
        }

        return events;
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<object> outcomes;

        public QueueHttpMessageHandler(params object[] outcomes)
        {
            this.outcomes = new Queue<object>(outcomes);
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add(new RecordedRequest(
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                request.Headers.Select(header => $"{header.Key}: {string.Join(",", header.Value)}").ToArray(),
                body));

            object outcome = outcomes.Dequeue();
            if (outcome is Exception exception)
            {
                throw exception;
            }

            return (HttpResponseMessage)outcome;
        }
    }

    private sealed record RecordedRequest(
        string Uri,
        string? Authorization,
        IReadOnlyList<string> Headers,
        string? Body);

    private sealed class FakeSettingsStore : ISettingsStore
    {
        private readonly ScreenMindSettings settings;

        public FakeSettingsStore(ScreenMindSettings settings)
        {
            this.settings = settings;
        }

        public Task<ScreenMindSettings> LoadAsync(CancellationToken cancellationToken)
            => Task.FromResult(settings);

        public Task SaveAsync(ScreenMindSettings settings, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        private readonly string? secretName;
        private readonly string? secret;

        public FakeSecretStore(string? secretName, string? secret)
        {
            this.secretName = secretName;
            this.secret = secret;
        }

        public Task SaveAsync(string name, string secret, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<bool> ExistsAsync(string name, CancellationToken cancellationToken)
            => Task.FromResult(string.Equals(name, secretName, StringComparison.OrdinalIgnoreCase) && secret is not null);

        public Task<string?> GetAsync(string name, CancellationToken cancellationToken)
            => Task.FromResult(string.Equals(name, secretName, StringComparison.OrdinalIgnoreCase) ? secret : null);

        public Task DeleteAsync(string name, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeQwenProxyClient : ScreenMind.Providers.OpenAICompatible.Qwen.IQwenProxyClient
    {
        public bool IsQwen { get; set; }
        public Exception? HealthException { get; set; }
        public int UploadCalls { get; private set; }
        public List<string> Models { get; set; } = [];

        public Task<ScreenMind.Providers.OpenAICompatible.Qwen.QwenProxyCapabilities> GetCapabilitiesAsync(Uri baseUri, string? apiKey, string? cookie, CancellationToken cancellationToken)
        {
            if (HealthException is not null)
            {
                return Task.FromException<ScreenMind.Providers.OpenAICompatible.Qwen.QwenProxyCapabilities>(HealthException);
            }
            if (IsQwen)
            {
                return Task.FromResult(new ScreenMind.Providers.OpenAICompatible.Qwen.QwenProxyCapabilities(true, "FreeQwenApi", Models.Count, 1, 1));
            }
            return Task.FromResult(new ScreenMind.Providers.OpenAICompatible.Qwen.QwenProxyCapabilities(false, "Unknown", 0, 0, 0));
        }

        public Task<ScreenMind.Providers.OpenAICompatible.Qwen.QwenUploadedFile> UploadImageAsync(Uri baseUri, ScreenImage image, string? apiKey, string? cookie, CancellationToken cancellationToken)
        {
            UploadCalls++;
            return Task.FromResult(new ScreenMind.Providers.OpenAICompatible.Qwen.QwenUploadedFile("file-123", "file-123", "path/to/file", "name", "http://url", 100, "image/png"));
        }

        public Task<List<string>> GetModelsAsync(Uri baseUri, string? apiKey, string? cookie, CancellationToken cancellationToken)
        {
            return Task.FromResult(Models);
        }
    }
}
