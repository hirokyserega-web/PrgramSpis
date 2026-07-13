using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenMind.AI.DependencyInjection;
using ScreenMind.App;
using ScreenMind.Infrastructure.DependencyInjection;
using ScreenMind.Platform.Windows.DependencyInjection;
using ScreenMind.Providers.Anthropic.DependencyInjection;
using ScreenMind.Providers.Gemini.DependencyInjection;
using ScreenMind.Providers.Ollama.DependencyInjection;
using ScreenMind.Providers.OpenAI.DependencyInjection;
using ScreenMind.Providers.OpenAICompatible.DependencyInjection;
using ScreenMind.UI.DependencyInjection;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services
    .AddScreenMindAi()
    .AddScreenMindInfrastructure(builder.Configuration)
    .AddScreenMindWindowsPlatform()
    .AddScreenMindUi()
    .AddScreenMindOpenAiProvider()
    .AddScreenMindOpenAiCompatibleProvider()
    .AddScreenMindAnthropicProvider()
    .AddScreenMindGeminiProvider()
    .AddScreenMindOllamaProvider();

using IHost host = builder.Build();

// Start host background services
await host.StartAsync().ConfigureAwait(false);

try
{
    // Start Avalonia application loop programmatically
    AppBuilder.Configure(() => new AvaloniaApp(host.Services))
        .UsePlatformDetect()
        .LogToTrace()
        .StartWithClassicDesktopLifetime(args);
}
finally
{
    // Gracefully stop the host on exit
    await host.StopAsync().ConfigureAwait(false);
}
