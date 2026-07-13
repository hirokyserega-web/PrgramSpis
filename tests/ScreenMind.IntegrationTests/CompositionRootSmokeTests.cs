using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ScreenMind.AI.DependencyInjection;
using ScreenMind.Core.Capture;
using ScreenMind.Infrastructure.DependencyInjection;
using ScreenMind.Platform.Windows.DependencyInjection;
using ScreenMind.Providers.Anthropic.DependencyInjection;
using ScreenMind.Providers.Gemini.DependencyInjection;
using ScreenMind.Providers.Ollama.DependencyInjection;
using ScreenMind.Providers.OpenAI.DependencyInjection;
using ScreenMind.Providers.OpenAICompatible.DependencyInjection;
using ScreenMind.UI.DependencyInjection;

namespace ScreenMind.IntegrationTests;

public sealed class CompositionRootSmokeTests
{
    [Fact]
    public void ServiceCollectionModulesShouldBuildProviderWithHttpClientFactory()
    {
        ServiceCollection services = new();
        IConfiguration configuration = Substitute.For<IConfiguration>();

        services
            .AddScreenMindAi()
            .AddScreenMindInfrastructure(configuration)
            .AddScreenMindWindowsPlatform()
            .AddScreenMindUi()
            .AddScreenMindOpenAiProvider()
            .AddScreenMindOpenAiCompatibleProvider()
            .AddScreenMindAnthropicProvider()
            .AddScreenMindGeminiProvider()
            .AddScreenMindOllamaProvider();

        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

        IHttpClientFactory httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
        IRegionSelectionService regionSelectionService = provider.GetRequiredService<IRegionSelectionService>();

        httpClientFactory.Should().NotBeNull();
        regionSelectionService.Should().NotBeNull();
    }
}
