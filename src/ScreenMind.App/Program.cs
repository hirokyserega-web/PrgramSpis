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
using ScreenMind.Core.Ai;
using ScreenMind.Core.Imaging;
using ScreenMind.Core.Settings;
using System.Linq;
using System.Collections.Generic;

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

// Check if console should be shown
bool showConsole = Array.IndexOf(args, "--console") >= 0 || Array.IndexOf(args, "-c") >= 0;
if (!showConsole)
{
    try
    {
        var settingsStore = host.Services.GetService<ScreenMind.Core.Settings.ISettingsStore>();
        if (settingsStore is not null)
        {
            var settings = settingsStore.LoadAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            if (settings?.Ui?.ShowConsole == true)
            {
                showConsole = true;
            }
        }
    }
    catch
    {
        // Ignore settings loading issues during early startup
    }
}

if (showConsole)
{
    ConsoleHelper.EnableConsole();
}

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

internal static class ConsoleHelper
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static extern bool AllocConsole();

    public static void EnableConsole()
    {
        AllocConsole();
        var writer = new System.IO.StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        Console.SetOut(writer);
        Console.SetError(writer);
    }
}
