using ScreenMind.Core.Imaging;

namespace ScreenMind.Core.Ai;

public sealed record AiMessage(
    AiMessageRole Role,
    string Content,
    DateTimeOffset CreatedAt,
    ScreenImage? Image = null);

