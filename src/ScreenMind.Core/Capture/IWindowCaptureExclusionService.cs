namespace ScreenMind.Core.Capture;

public interface IWindowCaptureExclusionService
{
    Task<WindowCaptureExclusionStatus> ApplyAsync(
        IntPtr windowHandle,
        CancellationToken cancellationToken);

    Task<WindowCaptureExclusionStatus> GetStatusAsync(
        IntPtr windowHandle,
        CancellationToken cancellationToken);
}

