namespace ScreenMind.Core.Ai;

public sealed record AiProfile(
    string Id,
    string DisplayName,
    string ProviderId,
    string ModelId,
    string SystemPrompt,
    double Temperature = 0.2d,
    TimeSpan? Timeout = null)
{
    public IReadOnlyList<string> FallbackProviderIds { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string> ProviderModelIds { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string GetModelForProvider(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        return ProviderModelIds.TryGetValue(providerId, out string? modelId) && !string.IsNullOrWhiteSpace(modelId)
            ? modelId
            : ModelId;
    }
}

