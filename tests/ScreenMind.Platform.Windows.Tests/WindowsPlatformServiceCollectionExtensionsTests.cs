using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ScreenMind.Platform.Windows.DependencyInjection;

namespace ScreenMind.Platform.Windows.Tests;

public sealed class WindowsPlatformServiceCollectionExtensionsTests
{
    [Fact]
    public void AddScreenMindWindowsPlatformShouldReturnSameServiceCollection()
    {
        ServiceCollection services = new();

        IServiceCollection result = services.AddScreenMindWindowsPlatform();

        result.Should().BeSameAs(services);
    }
}
