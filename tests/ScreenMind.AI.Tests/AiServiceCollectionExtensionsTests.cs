using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ScreenMind.AI.DependencyInjection;

namespace ScreenMind.AI.Tests;

public sealed class AiServiceCollectionExtensionsTests
{
    [Fact]
    public void AddScreenMindAiShouldReturnSameServiceCollection()
    {
        ServiceCollection services = new();

        IServiceCollection result = services.AddScreenMindAi();

        result.Should().BeSameAs(services);
    }
}
