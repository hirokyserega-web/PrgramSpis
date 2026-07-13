namespace ScreenMind.Core.Ai;

public enum AiErrorKind
{
    Auth,
    RateLimit,
    Network,
    Timeout,
    ServiceUnavailable,
    UnsupportedModel,
    PayloadTooLarge,
    Cancelled,
    SafetyBlocked,
    Configuration,
    Unknown,
}

