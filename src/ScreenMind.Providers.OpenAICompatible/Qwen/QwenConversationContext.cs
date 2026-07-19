namespace ScreenMind.Providers.OpenAICompatible.Qwen;

public sealed record QwenConversationContext(
    string ClientConversationId,
    string? UpstreamChatId = null,
    string? ParentId = null);
