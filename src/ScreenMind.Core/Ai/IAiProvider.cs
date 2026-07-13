namespace ScreenMind.Core.Ai;

public interface IAiProvider
{
    string Id { get; }

    IAsyncEnumerable<AiStreamEvent> StreamAsync(
        AiRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AiModel>> GetModelsAsync(CancellationToken cancellationToken);

    Task TestConnectionAsync(CancellationToken cancellationToken);
}

