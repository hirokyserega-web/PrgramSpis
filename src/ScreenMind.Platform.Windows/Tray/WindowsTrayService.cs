using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using ScreenMind.Core.Tray;

namespace ScreenMind.Platform.Windows.Tray;

public sealed partial class WindowsTrayService : ITrayService
{
    private readonly object gate = new();
    private readonly ILogger<WindowsTrayService> logger;
    private readonly NotifyIcon notifyIcon;
    private ContextMenuStrip? currentMenu;
    private bool isDisposed;

    public WindowsTrayService(ILogger<WindowsTrayService> logger)
    {
        this.logger = logger;
        notifyIcon = new NotifyIcon
        {
            Text = "ScreenMind",
            Icon = SystemIcons.Application,
            Visible = true,
        };
    }

    public Task SetCommandsAsync(
        IReadOnlyList<TrayCommand> commands,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commands);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);

            ContextMenuStrip menu = new();
            foreach (TrayCommand command in commands)
            {
                ToolStripMenuItem item = new(command.Header)
                {
                    Enabled = command.IsEnabled,
                    Tag = command,
                };
                item.Click += OnCommandClick;
                menu.Items.Add(item);
            }

            ContextMenuStrip? oldMenu = currentMenu;
            currentMenu = menu;
            notifyIcon.ContextMenuStrip = currentMenu;
            oldMenu?.Dispose();
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (isDisposed)
            {
                return ValueTask.CompletedTask;
            }

            isDisposed = true;
            notifyIcon.Visible = false;
            currentMenu?.Dispose();
            notifyIcon.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private async void OnCommandClick(object? sender, EventArgs eventArgs)
    {
        if (sender is ToolStripMenuItem { Tag: TrayCommand command })
        {
            try
            {
                await command.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TrayCommandCancelled(logger);
            }
            catch (Exception exception)
            {
                TrayCommandFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information, Message = "Tray command was cancelled.")]
    private static partial void TrayCommandCancelled(ILogger logger);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Error, Message = "Tray command failed.")]
    private static partial void TrayCommandFailed(ILogger logger, Exception exception);
}
