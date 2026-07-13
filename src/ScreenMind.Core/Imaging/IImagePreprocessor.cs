namespace ScreenMind.Core.Imaging;

public interface IImagePreprocessor
{
    Task<ScreenImage> ProcessAsync(
        ScreenImage source,
        ImagePreprocessingOptions options,
        CancellationToken cancellationToken);
}

