using ScreenMind.Core.Imaging;

namespace ScreenMind.Core.Capture;

public interface IScreenCaptureService
{
    Task<ScreenImage> CaptureAsync(
        CaptureTarget target,
        CancellationToken cancellationToken);
}

