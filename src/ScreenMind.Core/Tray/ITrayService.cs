namespace ScreenMind.Core.Tray;

public interface ITrayService : IAsyncDisposable
{
    Task SetCommandsAsync(
        IReadOnlyList<TrayCommand> commands,
        CancellationToken cancellationToken);
}

