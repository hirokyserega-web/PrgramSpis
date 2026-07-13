namespace ScreenMind.Core.State;

public interface IClock
{
    DateTimeOffset GetUtcNow();
}

