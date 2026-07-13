namespace ScreenMind.Core.Ai;

public sealed record AiResult(
    string Text,
    AiUsage Usage,
    AiError? Error = null);

