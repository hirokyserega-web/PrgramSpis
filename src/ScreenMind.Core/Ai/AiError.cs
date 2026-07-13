namespace ScreenMind.Core.Ai;

public sealed record AiError(
    AiErrorKind Kind,
    string Message,
    string? ProviderCode = null,
    bool IsRetryable = false);

