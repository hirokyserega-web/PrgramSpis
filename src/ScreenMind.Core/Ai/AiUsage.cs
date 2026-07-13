namespace ScreenMind.Core.Ai;

public sealed record AiUsage(
    int? InputTokens = null,
    int? OutputTokens = null,
    int? TotalTokens = null);

