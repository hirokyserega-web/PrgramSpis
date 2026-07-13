namespace ScreenMind.Core.Imaging;

public sealed class ScreenImage : IDisposable
{
    private byte[]? bytes;

    public ScreenImage(
        byte[] bytes,
        string mediaType,
        ScreenImageFormat format,
        int width,
        int height,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length == 0)
        {
            throw new ArgumentException("Image payload cannot be empty.", nameof(bytes));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        }

        this.bytes = bytes;
        MediaType = string.IsNullOrWhiteSpace(mediaType)
            ? throw new ArgumentException("Media type is required.", nameof(mediaType))
            : mediaType;
        Format = format;
        Width = width;
        Height = height;
        CapturedAt = capturedAt;
    }

    public string MediaType { get; }

    public ScreenImageFormat Format { get; }

    public int Width { get; }

    public int Height { get; }

    public DateTimeOffset CapturedAt { get; }

    public int Length => bytes?.Length ?? 0;

    public ReadOnlyMemory<byte> Bytes
        => bytes is null
            ? throw new ObjectDisposedException(nameof(ScreenImage))
            : bytes;

    public void Dispose()
    {
        byte[]? buffer = Interlocked.Exchange(ref bytes, null);
        if (buffer is not null)
        {
            Array.Clear(buffer);
        }
    }
}

