namespace ScreenMind.Core.Ai;

public abstract record AiStreamEvent(DateTimeOffset CreatedAt)
{
    public sealed record TextDelta(string Text, DateTimeOffset Created)
        : AiStreamEvent(Created);

    public sealed record ReasoningDelta(string Text, DateTimeOffset Created)
        : AiStreamEvent(Created);

    public sealed record Completed(AiUsage Usage, DateTimeOffset Created)
        : AiStreamEvent(Created);

    public sealed record Failed(AiError Error, DateTimeOffset Created)
        : AiStreamEvent(Created);
}

