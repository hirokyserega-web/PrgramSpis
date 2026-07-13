using System.Text;
using System.Text.Json;
using ScreenMind.AI;
using ScreenMind.Core.Ai;

namespace ScreenMind.Providers.Gemini;

public sealed class GeminiProvider : IAiProvider
{
    private static readonly Uri DefaultBaseUri = new("https://generativelanguage.googleapis.com/");
    private const string DefaultSecretName = "gemini-api-key";

    private readonly HttpClient httpClient;
    private readonly ProviderConfigurationResolver configurationResolver;

    public GeminiProvider(HttpClient httpClient, ProviderConfigurationResolver configurationResolver)
    {
        this.httpClient = httpClient;
        this.configurationResolver = configurationResolver;
    }

    public string Id => "gemini";

    public async IAsyncEnumerable<AiStreamEvent> StreamAsync(
        AiRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ProviderRuntimeConfiguration configuration = await configurationResolver
            .ResolveAsync(request.Profile, Id, DefaultBaseUri, DefaultSecretName, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            yield return new AiStreamEvent.Failed(
                new AiError(AiErrorKind.Auth, "Gemini API key is not configured."),
                DateTimeOffset.UtcNow);
            yield break;
        }

        Uri uri = new(
            configuration.BaseUri,
            $"v1beta/models/{Uri.EscapeDataString(configuration.ModelId)}:streamGenerateContent?alt=sse&key={Uri.EscapeDataString(configuration.ApiKey)}");

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, uri);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(BuildBody(request)),
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
                new AiError(AiErrorKind.Unknown, "Gemini request did not return a response."),
                DateTimeOffset.UtcNow);
            yield break;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                yield return new AiStreamEvent.Failed(
                    ProviderErrorMapper.FromHttpStatus(response.StatusCode, Id),
                    DateTimeOffset.UtcNow);
                yield break;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await foreach (string data in SseStreamReader.ReadDataAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                if (!TryParseJson(data, out JsonDocument? document))
                {
                    yield return new AiStreamEvent.Failed(
                        new AiError(AiErrorKind.Unknown, "Gemini stream returned malformed data."),
                        DateTimeOffset.UtcNow);
                    yield break;
                }

                using (document)
                {
                    JsonElement root = document!.RootElement;
                    if (TryGetText(root, out string? text) && !string.IsNullOrEmpty(text))
                    {
                        yield return new AiStreamEvent.TextDelta(text, DateTimeOffset.UtcNow);
                    }

                    if (IsSafetyBlocked(root))
                    {
                        yield return new AiStreamEvent.Failed(
                            new AiError(AiErrorKind.SafetyBlocked, "Gemini blocked the response for safety reasons."),
                            DateTimeOffset.UtcNow);
                        yield break;
                    }
                }
            }
        }

        yield return new AiStreamEvent.Completed(new AiUsage(), DateTimeOffset.UtcNow);
    }

    public Task<IReadOnlyList<AiModel>> GetModelsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<AiModel>>(Array.Empty<AiModel>());

    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        ProviderRuntimeConfiguration configuration = await configurationResolver
            .ResolveAsync(
                new AiProfile("test", "Test", Id, "gemini-2.5-flash", string.Empty),
                Id,
                DefaultBaseUri,
                DefaultSecretName,
                cancellationToken)
            .ConfigureAwait(false);

        Uri uri = new(configuration.BaseUri, $"v1beta/models?key={Uri.EscapeDataString(configuration.ApiKey ?? string.Empty)}");
        using HttpResponseMessage response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AiProviderException(ProviderErrorMapper.FromHttpStatus(response.StatusCode, Id));
        }
    }

    private static object BuildBody(AiRequest request)
    {
        bool isPlaceholder = request.Image.Width == 1 && request.Image.Height == 1;
        object userParts;

        if (isPlaceholder)
        {
            userParts = new object[]
            {
                new { text = request.Question },
            };
        }
        else
        {
            string imageData = Convert.ToBase64String(request.Image.Bytes.Span);
            userParts = new object[]
            {
                new { text = request.Question },
                new
                {
                    inlineData = new
                    {
                        mimeType = request.Image.MediaType,
                        data = imageData,
                    },
                },
            };
        }

        return new
        {
            systemInstruction = new
            {
                parts = new object[]
                {
                    new { text = request.Profile.SystemPrompt },
                },
            },
            contents = new object[]
            {
                new
                {
                    role = "user",
                    parts = userParts,
                },
            },
        };
    }

    private static bool TryGetText(JsonElement root, out string? text)
    {
        text = null;
        if (!root.TryGetProperty("candidates", out JsonElement candidates)
            || candidates.GetArrayLength() == 0
            || !candidates[0].TryGetProperty("content", out JsonElement content)
            || !content.TryGetProperty("parts", out JsonElement parts)
            || parts.GetArrayLength() == 0
            || !parts[0].TryGetProperty("text", out JsonElement textElement))
        {
            return false;
        }

        text = textElement.GetString();
        return true;
    }

    private static bool IsSafetyBlocked(JsonElement root)
    {
        return root.TryGetProperty("promptFeedback", out JsonElement feedback)
            && feedback.TryGetProperty("blockReason", out JsonElement blockReason)
            && !string.IsNullOrWhiteSpace(blockReason.GetString());
    }

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
