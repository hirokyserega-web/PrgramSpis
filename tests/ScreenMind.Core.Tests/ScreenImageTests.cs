using FluentAssertions;
using ScreenMind.Core.Imaging;

namespace ScreenMind.Core.Tests;

public sealed class ScreenImageTests
{
    [Fact]
    public void DisposeShouldClearAndRejectFurtherAccess()
    {
        ScreenImage image = new(
            [1, 2, 3],
            "image/png",
            ScreenImageFormat.Png,
            1,
            1,
            DateTimeOffset.UtcNow);

        image.Dispose();

        image.Length.Should().Be(0);
        Action action = () => _ = image.Bytes;
        action.Should().Throw<ObjectDisposedException>();
    }
}

