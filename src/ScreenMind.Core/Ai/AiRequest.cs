using ScreenMind.Core.Imaging;

namespace ScreenMind.Core.Ai;

public sealed record AiRequest(
    AiProfile Profile,
    ScreenImage Image,
    string Question,
    IReadOnlyList<AiMessage> SessionMessages,
    string? SessionId = null);

