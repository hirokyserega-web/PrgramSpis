namespace ScreenMind.Providers.OpenAICompatible.Qwen;

public sealed record QwenProxyCapabilities(
    bool IsReady,
    string Service,
    int ModelsCount,
    int TotalAccounts,
    int AvailableAccounts);
