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

if (System.Array.IndexOf(args, "--test-history") >= 0)
{
    ConsoleHelper.EnableConsole();
    System.Console.WriteLine("=== STARTING HISTORY TEST ===");
    await host.StartAsync().ConfigureAwait(false);

    try
    {
        var proxyManager = host.Services.GetRequiredService<ScreenMind.Core.Ai.IExternalProxyManager>();
        var settingsStore = host.Services.GetRequiredService<ScreenMind.Core.Settings.ISettingsStore>();
        var settings = await settingsStore.LoadAsync(default);
        
        settings.ManagedProxies.Qwen.Enabled = true;
        await settingsStore.SaveAsync(settings, default);

        var secrets = host.Services.GetRequiredService<ScreenMind.Core.Privacy.ISecretStore>();
        string? qwenCookie = await secrets.GetAsync("qwen-cookie", default);
        System.Console.WriteLine($"Starting Qwen proxy on port {settings.ManagedProxies.Qwen.Port}...");
        await proxyManager.StartAsync("FreeQwenApi", settings.ManagedProxies.Qwen.Port, qwenCookie ?? string.Empty, default);

        var providers = host.Services.GetRequiredService<System.Collections.Generic.IEnumerable<ScreenMind.Core.Ai.IAiProvider>>();
        var provider = providers.First(p => p.Id == "openai-compatible");

        string sessionId = "test-session-" + System.Guid.NewGuid().ToString("N");
        System.Console.WriteLine($"Session ID: {sessionId}");

        // Turn 1: Red Dot image (1x1 red PNG)
        byte[] redPngBytes = System.Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var redImage = new ScreenImage(redPngBytes, "image/png", ScreenImageFormat.Png, 1, 1, System.DateTimeOffset.UtcNow);
        
        var profile = settings.Profiles.Items.FirstOrDefault(p => p.ProviderId == "openai-compatible") 
            ?? new AiProfile("test-qwen", "Test Qwen", "openai-compatible", "qwen3.7-plus", "You are a helpful assistant.");

        var request1 = new AiRequest(
            profile,
            redImage,
            "This image has a red dot. What color is the dot?",
            System.Array.Empty<AiMessage>(),
            sessionId
        );

        System.Console.WriteLine("Sending Turn 1 (with Red Dot image)...");
        var sb1 = new System.Text.StringBuilder();
        await foreach (var ev in provider.StreamAsync(request1, default))
        {
            if (ev is AiStreamEvent.TextDelta d)
            {
                System.Console.Write(d.Text);
                sb1.Append(d.Text);
            }
            else if (ev is AiStreamEvent.Failed f)
            {
                System.Console.WriteLine($"\n[ERROR Turn 1]: {f.Error.Message}");
            }
        }
        System.Console.WriteLine("\nTurn 1 completed.");

        string reply1 = sb1.ToString();

        // Turn 2: Blue Dot image (with history)
        byte[] bluePngBytes = System.Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPj/HwAEhQGAjK3m2wAAAABJRU5ErkJggg==");
        var blueImage = new ScreenImage(bluePngBytes, "image/png", ScreenImageFormat.Png, 1, 1, System.DateTimeOffset.UtcNow);

        var history = new System.Collections.Generic.List<AiMessage>
        {
            new AiMessage(AiMessageRole.User, "This image has a red dot. What color is the dot?", System.DateTimeOffset.UtcNow, redImage),
            new AiMessage(AiMessageRole.Assistant, reply1, System.DateTimeOffset.UtcNow)
        };

        var request2 = new AiRequest(
            profile,
            blueImage,
            "Now look at this new image. It has a blue dot. What was the color of the dot on the previous screenshot?",
            history,
            sessionId
        );

        System.Console.WriteLine("Sending Turn 2 (with Blue Dot image & history)...");
        await foreach (var ev in provider.StreamAsync(request2, default))
        {
            if (ev is AiStreamEvent.TextDelta d)
            {
                System.Console.Write(d.Text);
            }
            else if (ev is AiStreamEvent.Failed f)
            {
                System.Console.WriteLine($"\n[ERROR Turn 2]: {f.Error.Message}");
            }
        }
        System.Console.WriteLine("\nTurn 2 completed.");
    }
    catch (System.Exception ex)
    {
        System.Console.WriteLine($"Test failed: {ex}");
    }
    finally
    {
        await host.StopAsync().ConfigureAwait(false);
    }
    return;
}

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
