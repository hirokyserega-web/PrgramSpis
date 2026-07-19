using ScreenMind.Core.Imaging;
using System.Collections.Generic;

namespace ScreenMind.Core.Ai;

public sealed record AiRequest(
    AiProfile Profile,
    ScreenImage? Image,
    string Question,
    IReadOnlyList<AiMessage> SessionMessages,
    ProviderConversationState? Conversation = null);


