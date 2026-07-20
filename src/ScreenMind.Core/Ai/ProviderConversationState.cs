namespace ScreenMind.Core.Ai;

public sealed class ProviderConversationState
{
    public ProviderConversationState(
        string providerId,
        string clientConversationId,
        string? upstreamChatId = null,
        string? parentId = null)
    {
        ProviderId = providerId;
        ClientConversationId = clientConversationId;
        CurrentUpstreamChatId = upstreamChatId;
        CurrentParentId = parentId;
    }

    public string ProviderId { get; }

    public string ClientConversationId { get; }

    public string? CurrentUpstreamChatId { get; set; }

    public string? CurrentParentId { get; set; }
}
