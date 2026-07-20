namespace ScreenMind.Core.Ai;

public sealed record ProviderConversationState(
    string ProviderId,
    string ClientConversationId,
    string? UpstreamChatId = null,
    string? ParentId = null)
{
    public string? CurrentUpstreamChatId { get; set; } = UpstreamChatId;
    public string? CurrentParentId { get; set; } = ParentId;
}
