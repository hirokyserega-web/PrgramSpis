using System.Text;
using System.Text.Json;
using ScreenMind.AI;
using ScreenMind.Core.Ai;

namespace ScreenMind.Providers.Ollama;

public sealed class OllamaProvider : IAiProvider
{
    private static readonly Uri DefaultBaseUri = new("http://localhost:11434/");

    private readonly HttpClient httpClient;
    private readonly ProviderConfigurationResolver configurationResolver;

    public OllamaProvider(HttpClient httpClient, ProviderConfigurationResolver configurationResolver)
    {
        this.httpClient = httpClient;
        this.configurationResolver = configurationResolver;
    }

    public string Id => "ollama";

    public async IAsyncEnumerable<AiStreamEvent> StreamAsync(
        AiRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ProviderRuntimeConfiguration configuration = await configurationResolver
            .ResolveAsync(request.Profile, Id, DefaultBaseUri, defaultSecretName: null, cancellationToken)
            .ConfigureAwait(false);

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(configuration.BaseUri, "api/generate"));
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
                new AiError(AiErrorKind.Network, "Ollama service is not reachable.", IsRetryable: true),
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
            await foreach (string line in NdjsonStreamReader.ReadLinesAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                if (!TryParseJson(line, out JsonDocument? document))
                {
                    yield return new AiStreamEvent.Failed(
                        new AiError(AiErrorKind.Unknown, "Ollama stream returned malformed data."),
                        DateTimeOffset.UtcNow);
                    yield break;
                }

                using (document)
                {
                    JsonElement root = document!.RootElement;
                    if (root.TryGetProperty("error", out JsonElement errorElement))
                    {
                        yield return new AiStreamEvent.Failed(
                            MapOllamaError(errorElement.GetString()),
                            DateTimeOffset.UtcNow);
                        yield break;
                    }

                    if (root.TryGetProperty("response", out JsonElement responseElement))
                    {
                        string? text = responseElement.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            yield return new AiStreamEvent.TextDelta(text, DateTimeOffset.UtcNow);
                        }
                    }

                    if (root.TryGetProperty("done", out JsonElement doneElement)
                        && doneElement.ValueKind == JsonValueKind.True)
                    {
                        yield return new AiStreamEvent.Completed(new AiUsage(), DateTimeOffset.UtcNow);
                        yield break;
                    }
                }
            }
        }
    }

    public async Task<IReadOnlyList<AiModel>> GetModelsAsync(CancellationToken cancellationToken)
    {
        ProviderRuntimeConfiguration configuration = await ResolveDefaultConfigurationAsync(cancellationToken).ConfigureAwait(false);
        using HttpResponseMessage response = await httpClient.GetAsync(new Uri(configuration.BaseUri, "api/tags"), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AiProviderException(ProviderErrorMapper.FromHttpStatus(response.StatusCode, Id));
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        List<AiModel> models = [];
        if (!document.RootElement.TryGetProperty("models", out JsonElement modelsElement))
        {
            return models;
        }

        foreach (JsonElement model in modelsElement.EnumerateArray())
        {
            string? name = model.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.GetString()
                : null;
            if (!string.IsNullOrWhiteSpace(name))
            {
                models.Add(new AiModel(name, name, SupportsVisionByName(name), SupportsStreaming: true));
            }
        }

        return models;
    }

    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        ProviderRuntimeConfiguration configuration = await ResolveDefaultConfigurationAsync(cancellationToken).ConfigureAwait(false);
        using HttpResponseMessage response = await httpClient.GetAsync(new Uri(configuration.BaseUri, "api/tags"), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AiProviderException(ProviderErrorMapper.FromHttpStatus(response.StatusCode, Id));
        }
    }

    private Task<ProviderRuntimeConfiguration> ResolveDefaultConfigurationAsync(CancellationToken cancellationToken)
    {
        return configurationResolver.ResolveAsync(
            new AiProfile("test", "Test", Id, "llava", string.Empty),
            Id,
            DefaultBaseUri,
            defaultSecretName: null,
            cancellationToken);
    }

    private static object BuildBody(AiRequest request, string modelId)
    {
        var promptBuilder = new StringBuilder();
        var imagesList = new List<string>();

        // System prompt
        if (!string.IsNullOrWhiteSpace(request.Profile.SystemPrompt))
        {
            promptBuilder.Append("System: ").AppendLine(request.Profile.SystemPrompt);
            promptBuilder.AppendLine();
        }

        // History messages
        if (request.SessionMessages is not null)
        {
            int imageIndex = 1;
            foreach (var msg in request.SessionMessages)
            {
                string roleName = msg.Role == AiMessageRole.User ? "User" : "Assistant";
                promptBuilder.Append(roleName).Append(": ").Append(msg.Content);
                
                if (msg.Image is not null && (msg.Image.Width != 1 || msg.Image.Height != 1))
                {
                    promptBuilder.Append(" [Attached Image ").Append(imageIndex).Append(']');
                    imagesList.Add(Convert.ToBase64String(msg.Image.Bytes.Span));
                    imageIndex++;
                }
                
                promptBuilder.AppendLine();
            }
        }

        // Current message
        promptBuilder.Append("User: ").Append(request.Question);
        bool currentHasImage = request.Image is not null && (request.Image.Width != 1 || request.Image.Height != 1);
        if (currentHasImage && request.Image is not null)
        {
            promptBuilder.Append(" [Attached Image]");
            imagesList.Add(Convert.ToBase64String(request.Image.Bytes.Span));
        }
        promptBuilder.AppendLine();
        promptBuilder.Append("Assistant:");

        if (imagesList.Count == 0)
        {
            return new
            {
                model = modelId,
                prompt = promptBuilder.ToString(),
                stream = true,
                options = new
                {
                    temperature = request.Profile.Temperature,
                },
            };
        }

        return new
        {
            model = modelId,
            prompt = promptBuilder.ToString(),
            stream = true,
            images = imagesList.ToArray(),
            options = new
            {
                temperature = request.Profile.Temperature,
            },
        };
    }

    private static AiError MapOllamaError(string? message)
    {
        if (message?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new AiError(AiErrorKind.UnsupportedModel, "Ollama model is not installed.");
        }

        if (message?.Contains("image", StringComparison.OrdinalIgnoreCase) == true
            || message?.Contains("vision", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new AiError(AiErrorKind.UnsupportedModel, "Ollama model does not support vision input.");
        }

        return new AiError(AiErrorKind.Unknown, "Ollama request failed.");
    }

    private static bool SupportsVisionByName(string name)
    {
        return name.Contains("vision", StringComparison.OrdinalIgnoreCase)
            || name.Contains("llava", StringComparison.OrdinalIgnoreCase)
            || name.Contains("bakllava", StringComparison.OrdinalIgnoreCase)
            || name.Contains("moondream", StringComparison.OrdinalIgnoreCase);
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
