using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ScreenMind.AI;
using ScreenMind.Core.Ai;

namespace ScreenMind.Providers.OpenAI;

public sealed class OpenAiProvider : IAiProvider
{
    private static readonly Uri DefaultBaseUri = new("https://api.openai.com/");
    private const string DefaultSecretName = "openai-api-key";

    private readonly HttpClient httpClient;
    private readonly ProviderConfigurationResolver configurationResolver;

    public OpenAiProvider(HttpClient httpClient, ProviderConfigurationResolver configurationResolver)
    {
        this.httpClient = httpClient;
        this.configurationResolver = configurationResolver;
    }

    public string Id => "openai";

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
                new AiError(AiErrorKind.Auth, "OpenAI API key is not configured."),
                DateTimeOffset.UtcNow);
            yield break;
        }

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(configuration.BaseUri, "v1/responses"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
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
                new AiError(AiErrorKind.Unknown, "OpenAI request did not return a response."),
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
                if (data == "[DONE]")
                {
                    yield break;
                }

                if (!TryParseJson(data, out JsonDocument? document))
                {
                    yield return new AiStreamEvent.Failed(
                        new AiError(AiErrorKind.Unknown, "OpenAI returned malformed stream data."),
                        DateTimeOffset.UtcNow);
                    yield break;
                }

                using (document)
                {
                    JsonElement root = document!.RootElement;
                    string? type = root.TryGetProperty("type", out JsonElement typeElement)
                        ? typeElement.GetString()
                        : null;

                    if (type == "response.output_text.delta"
                        && root.TryGetProperty("delta", out JsonElement deltaElement))
                    {
                        string? delta = deltaElement.GetString();
                        if (!string.IsNullOrEmpty(delta))
                        {
                            yield return new AiStreamEvent.TextDelta(delta, DateTimeOffset.UtcNow);
                        }
                    }
                    else if (type == "response.completed")
                    {
                        yield return new AiStreamEvent.Completed(new AiUsage(), DateTimeOffset.UtcNow);
                    }
                    else if (type == "error")
                    {
                        yield return new AiStreamEvent.Failed(
                            new AiError(AiErrorKind.Unknown, "OpenAI stream returned an error."),
                            DateTimeOffset.UtcNow);
                        yield break;
                    }
                }
            }
        }
    }

    public async Task<IReadOnlyList<AiModel>> GetModelsAsync(CancellationToken cancellationToken)
    {
        ProviderRuntimeConfiguration configuration = await configurationResolver
            .ResolveAsync(
                new AiProfile("test", "Test", Id, "gpt-4o-mini", string.Empty),
                Id,
                DefaultBaseUri,
                DefaultSecretName,
                cancellationToken)
            .ConfigureAwait(false);

        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(configuration.BaseUri, "v1/models"));
        if (!string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);
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
        ProviderRuntimeConfiguration configuration = await configurationResolver
            .ResolveAsync(
                new AiProfile("test", "Test", Id, "gpt-4o-mini", string.Empty),
                Id,
                DefaultBaseUri,
                DefaultSecretName,
                cancellationToken)
            .ConfigureAwait(false);

        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(configuration.BaseUri, "v1/models"));
        if (!string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);
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
                    type = "input_text",
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
                    type = "input_text",
                    text = request.Question,
                },
                new
                {
                    type = "input_image",
                    image_url = $"data:{request.Image.MediaType};base64,{imageData}",
                },
            };
        }

        return new
        {
            model = modelId,
            instructions = request.Profile.SystemPrompt,
            stream = true,
            input = new object[]
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
