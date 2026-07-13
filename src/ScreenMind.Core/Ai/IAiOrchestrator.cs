namespace ScreenMind.Core.Ai;

public interface IAiOrchestrator
{
    IAsyncEnumerable<AiStreamEvent> AnalyzeAsync(
        AiRequest request,
        CancellationToken cancellationToken);
}

