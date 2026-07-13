namespace ScreenMind.Core.Ai;

public sealed record AiMessage(
    AiMessageRole Role,
    string Content,
    DateTimeOffset CreatedAt);

