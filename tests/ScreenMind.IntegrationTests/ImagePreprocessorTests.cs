using FluentAssertions;
using ScreenMind.Core.Capture;
using ScreenMind.Core.Imaging;
using ScreenMind.Infrastructure.Imaging;
using SkiaSharp;

namespace ScreenMind.IntegrationTests;

public sealed class ImagePreprocessorTests
{
    [Fact]
    public async Task ProcessAsyncShouldResizeAndRespectPayloadLimit()
    {
        ScreenImage source = CreateImage(120, 80);
        SkiaSharpImagePreprocessor preprocessor = new();

        ScreenImage processed = await preprocessor.ProcessAsync(
            source,
            new ImagePreprocessingOptions(
                ScreenImageFormat.Jpeg,
                MaxPayloadBytes: 32 * 1024,
                MaxWidth: 40,
                MaxHeight: 40),
            CancellationToken.None);

        processed.Format.Should().Be(ScreenImageFormat.Jpeg);
        processed.MediaType.Should().Be("image/jpeg");
        processed.Width.Should().BeLessThanOrEqualTo(40);
        processed.Height.Should().BeLessThanOrEqualTo(40);
        processed.Length.Should().BeLessThanOrEqualTo(32 * 1024);
    }

    [Fact]
    public async Task ProcessAsyncShouldApplyFillMask()
    {
        ScreenImage source = CreateImage(16, 16);
        SkiaSharpImagePreprocessor preprocessor = new();

        ScreenImage processed = await preprocessor.ProcessAsync(
            source,
            new ImagePreprocessingOptions(
                ScreenImageFormat.Png,
                MaxPayloadBytes: 64 * 1024,
                Masks:
                [
                    new ImageMask(new ScreenRectangle(0, 0, 8, 8), ImageMaskMode.Fill, "#ff0000"),
                ]),
            CancellationToken.None);

        using SKBitmap? bitmap = SKBitmap.Decode(processed.Bytes.Span.ToArray());
        bitmap.Should().NotBeNull();
        bitmap!.GetPixel(2, 2).Red.Should().BeGreaterThan(200);
        bitmap.GetPixel(2, 2).Green.Should().BeLessThan(40);
    }

    private static ScreenImage CreateImage(int width, int height)
    {
        using SKBitmap bitmap = new(width, height);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new ScreenImage(
            data.ToArray(),
            "image/png",
            ScreenImageFormat.Png,
            width,
            height,
            DateTimeOffset.UtcNow);
    }
}
