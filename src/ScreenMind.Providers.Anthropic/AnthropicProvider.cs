using System.Text;
using System.Text.Json;
using ScreenMind.AI;
using ScreenMind.Core.Ai;

namespace ScreenMind.Providers.Anthropic;

public sealed class AnthropicProvider : IAiProvider
{
    private static readonly Uri DefaultBaseUri = new("https://api.anthropic.com/");
    private const string DefaultSecretName = "anthropic-api-key";

    private readonly HttpClient httpClient;
    private readonly ProviderConfigurationResolver configurationResolver;

    public AnthropicProvider(HttpClient httpClient, ProviderConfigurationResolver configurationResolver)
    {
        this.httpClient = httpClient;
        this.configurationResolver = configurationResolver;
    }

    public string Id => "anthropic";

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
                new AiError(AiErrorKind.Auth, "Anthropic API key is not configured."),
                DateTimeOffset.UtcNow);
            yield break;
        }

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(configuration.BaseUri, "v1/messages"));
        httpRequest.Headers.Add("x-api-key", configuration.ApiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Headers.Accept.ParseAdd("text/event-stream");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(BuildBody(request, configuration.ModelId)),
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
                new AiError(AiErrorKind.Unknown, "Anthropic request did not return a response."),
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
                        new AiError(AiErrorKind.Unknown, "Anthropic stream returned malformed data."),
                        DateTimeOffset.UtcNow);
                    yield break;
                }

                using (document)
                {
                    JsonElement root = document!.RootElement;
                    string? type = root.TryGetProperty("type", out JsonElement typeElement)
                        ? typeElement.GetString()
                        : null;

                    if (type == "content_block_delta"
                        && root.TryGetProperty("delta", out JsonElement delta)
                        && delta.TryGetProperty("text", out JsonElement textElement))
                    {
                        string? text = textElement.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            yield return new AiStreamEvent.TextDelta(text, DateTimeOffset.UtcNow);
                        }
                    }
                    else if (type == "message_stop")
                    {
                        yield return new AiStreamEvent.Completed(new AiUsage(), DateTimeOffset.UtcNow);
                        yield break;
                    }
                    else if (type == "error")
                    {
                        yield return new AiStreamEvent.Failed(
                            new AiError(AiErrorKind.Unknown, "Anthropic stream returned an error."),
                            DateTimeOffset.UtcNow);
                        yield break;
                    }
                }
            }
        }
    }

    public Task<IReadOnlyList<AiModel>> GetModelsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<AiModel>>(Array.Empty<AiModel>());

    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        ProviderRuntimeConfiguration configuration = await configurationResolver
            .ResolveAsync(
                new AiProfile("test", "Test", Id, "claude-sonnet-4-5", string.Empty),
                Id,
                DefaultBaseUri,
                DefaultSecretName,
                cancellationToken)
            .ConfigureAwait(false);

        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(configuration.BaseUri, "v1/models"));
        if (!string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            request.Headers.Add("x-api-key", configuration.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AiProviderException(ProviderErrorMapper.FromHttpStatus(response.StatusCode, Id));
        }
    }

    private static object BuildBody(AiRequest request, string modelId)
    {
        bool isPlaceholder = request.Image.Width == 1 && request.Image.Height == 1;
        object userContent;

        if (isPlaceholder)
        {
            userContent = new object[]
            {
                new
                {
                    type = "text",
                    text = request.Question,
                }
            };
        }
        else
        {
            string imageData = Convert.ToBase64String(request.Image.Bytes.Span);
            userContent = new object[]
            {
                new
                {
                    type = "text",
                    text = request.Question,
                },
                new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = request.Image.MediaType,
                        data = imageData,
                    },
                },
            };
        }

        return new
        {
            model = modelId,
            max_tokens = 2048,
            stream = true,
            system = request.Profile.SystemPrompt,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = userContent,
                },
            },
        };
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
