namespace ScreenMind.Core.State;

public sealed class SystemClock : IClock
{
    public DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
}

