using System;
using System.Collections.Generic;
using ScreenMind.Core.Imaging;

namespace ScreenMind.Core.Ai;

public sealed class ChatSession : IDisposable
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public AiProfile Profile { get; set; }
    public ScreenImage? Image { get; set; }
    public List<AiMessage> Messages { get; } = [];
    public ProviderConversationState? ConversationState { get; set; }

    public ChatSession(AiProfile profile, ScreenImage? image = null)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Image = image;
    }

    public void Dispose()
    {
        Image?.Dispose();
        Image = null;
        foreach (var msg in Messages)
        {
            msg.Image?.Dispose();
        }
        Messages.Clear();
    }
}

