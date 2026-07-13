namespace ScreenMind.Core.Ai;

public sealed record AiModel(
    string Id,
    string DisplayName,
    bool SupportsVision,
    bool SupportsStreaming,
    int? MaxInputTokens = null,
    long? MaxImageBytes = null);

