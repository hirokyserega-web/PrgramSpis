using Avalonia;
using Avalonia.Threading;
using ScreenMind.Core.Capture;

namespace ScreenMind.UI.Selection;

public sealed class AvaloniaRegionSelectionService : IRegionSelectionService
{
    private readonly IWindowCaptureExclusionService exclusionService;

    public AvaloniaRegionSelectionService(IWindowCaptureExclusionService exclusionService)
    {
        this.exclusionService = exclusionService ?? throw new ArgumentNullException(nameof(exclusionService));
    }

    public Task<RegionSelectionResult> SelectAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(RegionSelectionResult.Cancelled);
        }

        if (Application.Current is null)
        {
            return Task.FromException<RegionSelectionResult>(
                new InvalidOperationException("Avalonia application is not initialized."));
        }

        TaskCompletionSource<RegionSelectionResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SelectionOverlayWindow? window = null;

        CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                window?.CancelAndClose();
                completion.TrySetResult(RegionSelectionResult.Cancelled);
            });
        });

        _ = completion.Task.ContinueWith(
            _ => registration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        Dispatcher.UIThread.Post(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetResult(RegionSelectionResult.Cancelled);
                return;
            }

            window = new SelectionOverlayWindow();
            window.Completed += (_, result) => completion.TrySetResult(result);
            window.Opened += (_, _) => ApplyCaptureExclusion(window);
            window.Show();
            ApplyCaptureExclusion(window);
            window.Activate();
        });

        return completion.Task;
    }

    private void ApplyCaptureExclusion(SelectionOverlayWindow window)
    {
        IntPtr hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
        {
            _ = exclusionService.ApplyAsync(hwnd, CancellationToken.None);
        }
    }
}
