namespace ScreenMind.Core.Tray;

public sealed record TrayCommand(
    string Id,
    string Header,
    bool IsEnabled,
    Func<CancellationToken, Task> ExecuteAsync);

