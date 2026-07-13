namespace ScreenMind.Core.Imaging;

public sealed record ImagePreprocessingOptions(
    ScreenImageFormat OutputFormat,
    int MaxPayloadBytes,
    int? MaxWidth = null,
    int? MaxHeight = null,
    int JpegQuality = 88,
    IReadOnlyList<ImageMask>? Masks = null)
{
    public IReadOnlyList<ImageMask> EffectiveMasks => Masks ?? Array.Empty<ImageMask>();
}

