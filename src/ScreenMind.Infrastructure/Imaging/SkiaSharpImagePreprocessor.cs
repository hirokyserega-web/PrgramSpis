using System.Globalization;
using ScreenMind.Core.Capture;
using ScreenMind.Core.Imaging;
using SkiaSharp;

namespace ScreenMind.Infrastructure.Imaging;

public sealed class SkiaSharpImagePreprocessor : IImagePreprocessor
{
    public Task<ScreenImage> ProcessAsync(
        ScreenImage source,
        ImagePreprocessingOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (options.MaxPayloadBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Max payload bytes must be positive.");
        }

        SKBitmap? decoded = SKBitmap.Decode(source.Bytes.Span.ToArray());
        if (decoded is null)
        {
            throw new InvalidOperationException("Image payload could not be decoded.");
        }

        using (decoded)
        {
            SKBitmap bitmap = decoded;
            try
            {
                ApplyResizeLimit(ref bitmap, options);
                ApplyMasks(bitmap, options.EffectiveMasks, options.OutputFormat, cancellationToken);
                byte[] payload = EncodeWithinLimit(ref bitmap, options, cancellationToken);

                return Task.FromResult(new ScreenImage(
                    payload,
                    GetMediaType(options.OutputFormat),
                    options.OutputFormat,
                    bitmap.Width,
                    bitmap.Height,
                    source.CapturedAt));
            }
            finally
            {
                if (!ReferenceEquals(bitmap, decoded))
                {
                    bitmap.Dispose();
                }
            }
        }
    }

    private static void ApplyResizeLimit(ref SKBitmap bitmap, ImagePreprocessingOptions options)
    {
        int maxWidth = options.MaxWidth ?? bitmap.Width;
        int maxHeight = options.MaxHeight ?? bitmap.Height;
        if (maxWidth <= 0 || maxHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Resize bounds must be positive.");
        }

        if (bitmap.Width <= maxWidth && bitmap.Height <= maxHeight)
        {
            return;
        }

        double ratio = Math.Min((double)maxWidth / bitmap.Width, (double)maxHeight / bitmap.Height);
        ReplaceBitmap(ref bitmap, Resize(bitmap, Math.Max(1, (int)Math.Round(bitmap.Width * ratio)), Math.Max(1, (int)Math.Round(bitmap.Height * ratio))));
    }

    private static void ApplyMasks(
        SKBitmap bitmap,
        IReadOnlyList<ImageMask> masks,
        ScreenImageFormat outputFormat,
        CancellationToken cancellationToken)
    {
        foreach (ImageMask mask in masks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SKRectI rectangle = Clamp(mask.Bounds, bitmap.Width, bitmap.Height);
            if (rectangle.Width <= 0 || rectangle.Height <= 0)
            {
                continue;
            }

            switch (mask.Mode)
            {
                case ImageMaskMode.Blur:
                    Pixelate(bitmap, rectangle, cancellationToken);
                    break;
                case ImageMaskMode.Fill:
                    Fill(bitmap, rectangle, ParseColor(mask.FillColor, opaqueFallback: true), cancellationToken);
                    break;
                case ImageMaskMode.Exclude:
                    Fill(
                        bitmap,
                        rectangle,
                        outputFormat == ScreenImageFormat.Jpeg ? SKColors.Black : SKColors.Transparent,
                        cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(masks), "Unsupported image mask mode.");
            }
        }
    }

    private static byte[] EncodeWithinLimit(
        ref SKBitmap bitmap,
        ImagePreprocessingOptions options,
        CancellationToken cancellationToken)
    {
        int quality = Math.Clamp(options.JpegQuality, 35, 100);
        for (int attempt = 0; attempt < 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] payload = Encode(bitmap, options.OutputFormat, quality);
            if (payload.Length <= options.MaxPayloadBytes)
            {
                return payload;
            }

            if ((options.OutputFormat == ScreenImageFormat.Jpeg || options.OutputFormat == ScreenImageFormat.WebP)
                && quality > 45)
            {
                quality = Math.Max(45, quality - 12);
                continue;
            }

            int width = Math.Max(1, (int)Math.Floor(bitmap.Width * 0.85d));
            int height = Math.Max(1, (int)Math.Floor(bitmap.Height * 0.85d));
            if (width == bitmap.Width && height == bitmap.Height)
            {
                break;
            }

            ReplaceBitmap(ref bitmap, Resize(bitmap, width, height));
        }

        throw new InvalidOperationException("Image payload could not be reduced below the configured limit.");
    }

    private static byte[] Encode(SKBitmap bitmap, ScreenImageFormat format, int quality)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(ToEncodedFormat(format), quality);
        return data.ToArray();
    }

    private static SKBitmap Resize(SKBitmap source, int width, int height)
    {
        SKImageInfo info = new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        SKBitmap resized = new(info);
        using SKCanvas canvas = new(resized);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, new SKRect(0, 0, width, height));
        canvas.Flush();
        return resized;
    }

    private static void ReplaceBitmap(ref SKBitmap bitmap, SKBitmap replacement)
    {
        bitmap.Dispose();
        bitmap = replacement;
    }

    private static void Pixelate(SKBitmap bitmap, SKRectI rectangle, CancellationToken cancellationToken)
    {
        const int BlockSize = 10;

        for (int y = rectangle.Top; y < rectangle.Bottom; y += BlockSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = rectangle.Left; x < rectangle.Right; x += BlockSize)
            {
                int width = Math.Min(BlockSize, rectangle.Right - x);
                int height = Math.Min(BlockSize, rectangle.Bottom - y);
                SKColor average = Average(bitmap, x, y, width, height);
                Fill(bitmap, SKRectI.Create(x, y, width, height), average, cancellationToken);
            }
        }
    }

    private static SKColor Average(SKBitmap bitmap, int x, int y, int width, int height)
    {
        long red = 0;
        long green = 0;
        long blue = 0;
        long alpha = 0;
        int count = width * height;

        for (int row = y; row < y + height; row++)
        {
            for (int column = x; column < x + width; column++)
            {
                SKColor pixel = bitmap.GetPixel(column, row);
                red += pixel.Red;
                green += pixel.Green;
                blue += pixel.Blue;
                alpha += pixel.Alpha;
            }
        }

        return new SKColor(
            (byte)(red / count),
            (byte)(green / count),
            (byte)(blue / count),
            (byte)(alpha / count));
    }

    private static void Fill(SKBitmap bitmap, SKRectI rectangle, SKColor color, CancellationToken cancellationToken)
    {
        for (int row = rectangle.Top; row < rectangle.Bottom; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int column = rectangle.Left; column < rectangle.Right; column++)
            {
                bitmap.SetPixel(column, row, color);
            }
        }
    }

    private static SKRectI Clamp(ScreenRectangle rectangle, int width, int height)
    {
        int left = Math.Clamp(rectangle.X, 0, width);
        int top = Math.Clamp(rectangle.Y, 0, height);
        int right = Math.Clamp(rectangle.X + rectangle.Width, 0, width);
        int bottom = Math.Clamp(rectangle.Y + rectangle.Height, 0, height);

        return SKRectI.Create(left, top, right - left, bottom - top);
    }

    private static SKColor ParseColor(string value, bool opaqueFallback)
    {
        string hex = value.Trim().TrimStart('#');
        if (hex.Length == 6
            && byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red)
            && byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green)
            && byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue))
        {
            return new SKColor(red, green, blue, 255);
        }

        if (hex.Length == 8
            && byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red)
            && byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green)
            && byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue)
            && byte.TryParse(hex[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte alpha))
        {
            return new SKColor(red, green, blue, alpha);
        }

        return opaqueFallback ? SKColors.Black : SKColors.Transparent;
    }

    private static SKEncodedImageFormat ToEncodedFormat(ScreenImageFormat format)
    {
        return format switch
        {
            ScreenImageFormat.Png => SKEncodedImageFormat.Png,
            ScreenImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            ScreenImageFormat.WebP => SKEncodedImageFormat.Webp,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported image format."),
        };
    }

    private static string GetMediaType(ScreenImageFormat format)
    {
        return format switch
        {
            ScreenImageFormat.Png => "image/png",
            ScreenImageFormat.Jpeg => "image/jpeg",
            ScreenImageFormat.WebP => "image/webp",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported image format."),
        };
    }
}
