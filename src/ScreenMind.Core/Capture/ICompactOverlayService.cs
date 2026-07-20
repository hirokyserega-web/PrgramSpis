namespace ScreenMind.Core.Capture;

public interface ICompactOverlayService
{
    Task ShowAsync(CaptureTarget target, CancellationToken cancellationToken);
    Task ShowAsync(CaptureTarget target, string? promptOverride, CancellationToken cancellationToken);
}
