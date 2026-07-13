namespace ScreenMind.Core.Capture;

public interface ICompactOverlayService
{
    Task ShowAsync(CaptureTarget target, CancellationToken cancellationToken);
}
