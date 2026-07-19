using ScreenMind.Core.Ai;

namespace ScreenMind.AI;

public sealed class AiOrchestrator : IAiOrchestrator, IDisposable
{
    private readonly AiProviderRegistry registry;
    private readonly SemaphoreSlim mainRequestGate = new(1, 1);

    public AiOrchestrator(AiProviderRegistry registry)
    {
        this.registry = registry;
    }

    public async IAsyncEnumerable<AiStreamEvent> AnalyzeAsync(
        AiRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await mainRequestGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            yield return new AiStreamEvent.Failed(
                new AiError(AiErrorKind.Configuration, "Only one main image analysis can run at a time."),
                DateTimeOffset.UtcNow);
            yield break;
        }

        try
        {
            string[] providerIds = BuildProviderRoute(request.Profile).ToArray();
            for (int providerIndex = 0; providerIndex < providerIds.Length; providerIndex++)
            {
                string providerId = providerIds[providerIndex];
                bool allowRetry = true;

                while (true)
                {
                    AiError? failure = null;
                    bool emittedText = false;

                    AiRequest routedRequest = RouteRequest(request, providerId);
                    IAiProvider provider = registry.GetRequired(providerId);

                    IAsyncEnumerable<AiStreamEvent> stream;
                    try
                    {
                        stream = provider.StreamAsync(routedRequest, cancellationToken);
                    }
                    catch (AiProviderException exception)
                    {
                        failure = exception.Error;
                        stream = EmptyStream();
                    }

                    await foreach (AiStreamEvent streamEvent in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
                    {
                        if (streamEvent is AiStreamEvent.Failed failed)
                        {
                            failure = failed.Error;
                            break;
                        }

                        if (streamEvent is AiStreamEvent.TextDelta)
                        {
                            emittedText = true;
                        }

                        yield return streamEvent;
                    }

                    if (failure is null)
                    {
                        yield break;
                    }

                    if (emittedText || !CanRetryOrFallback(failure))
                    {
                        yield return new AiStreamEvent.Failed(failure, DateTimeOffset.UtcNow);
                        yield break;
                    }

                    if (allowRetry)
                    {
                        allowRetry = false;
                        continue;
                    }

                    bool hasFallback = providerIndex + 1 < providerIds.Length;
                    if (!hasFallback)
                    {
                        yield return new AiStreamEvent.Failed(failure, DateTimeOffset.UtcNow);
                        yield break;
                    }

                    break;
                }
            }
        }
        finally
        {
            mainRequestGate.Release();
            request.Image?.Dispose();
        }
    }

    private static IEnumerable<string> BuildProviderRoute(AiProfile profile)
    {
        yield return profile.ProviderId;

        foreach (string providerId in profile.FallbackProviderIds)
        {
            if (!string.Equals(providerId, profile.ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                yield return providerId;
            }
        }
    }

    private static AiRequest RouteRequest(AiRequest request, string providerId)
    {
        AiProfile routedProfile = request.Profile with
        {
            ProviderId = providerId,
            ModelId = request.Profile.GetModelForProvider(providerId),
        };

        return request with { Profile = routedProfile };
    }

    private static bool CanRetryOrFallback(AiError error)
    {
        return error.IsRetryable
            && (error.Kind is AiErrorKind.Network
            or AiErrorKind.Timeout
            or AiErrorKind.RateLimit
            or AiErrorKind.ServiceUnavailable);
    }

    private static async IAsyncEnumerable<AiStreamEvent> EmptyStream()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    public void Dispose()
    {
        mainRequestGate.Dispose();
    }
}
