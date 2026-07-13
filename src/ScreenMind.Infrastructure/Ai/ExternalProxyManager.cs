using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ScreenMind.Core.Ai;

namespace ScreenMind.Infrastructure.Ai;

public sealed class ExternalProxyManager : IExternalProxyManager, IDisposable
{
    private const string PackageJsonFileName = "package.json";

    private readonly ConcurrentDictionary<string, Process> activeProcesses = new(StringComparer.OrdinalIgnoreCase);

    public ExternalProxyManager()
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    private static string GetProxyDirectory(string proxyName)
    {
        string? firstExisting = null;
        foreach (string candidate in GetCandidateProxyDirectories(proxyName))
        {
            if (IsInstalledDirectory(candidate))
            {
                return candidate;
            }

            if (firstExisting is null && Directory.Exists(candidate))
            {
                firstExisting = candidate;
            }
        }

        return firstExisting ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "proxies", proxyName);
    }

    private static IEnumerable<string> GetCandidateProxyDirectories(string proxyName)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string root in GetSearchRoots())
        {
            string direct = Path.Combine(root, "proxies", proxyName);
            if (seen.Add(direct))
            {
                yield return direct;
            }

            string dist = Path.Combine(root, "dist", "proxies", proxyName);
            if (seen.Add(dist))
            {
                yield return dist;
            }
        }
    }

    private static IEnumerable<string> GetSearchRoots()
    {
        foreach (string root in EnumerateAncestors(AppDomain.CurrentDomain.BaseDirectory))
        {
            yield return root;
        }

        foreach (string root in EnumerateAncestors(Directory.GetCurrentDirectory()))
        {
            yield return root;
        }
    }

    private static IEnumerable<string> EnumerateAncestors(string start)
    {
        DirectoryInfo? directory = new(start);
        while (directory is not null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }

    public Task<bool> IsInstalledAsync(string proxyName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string dir = GetProxyDirectory(proxyName);
        return Task.FromResult(IsInstalledDirectory(dir));
    }

    public async Task InstallAsync(string proxyName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string dir = GetProxyDirectory(proxyName);
        string parentDir = Path.GetDirectoryName(dir)
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "proxies");
        Directory.CreateDirectory(parentDir);

        if (!Directory.Exists(dir))
        {
            // Clone the repository
            ProcessStartInfo gitPsi = new()
            {
                FileName = "git",
                Arguments = $"clone https://github.com/ForgetMeAI/{proxyName}.git",
                WorkingDirectory = parentDir,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using Process? gitProcess = Process.Start(gitPsi);
            if (gitProcess is not null)
            {
                await gitProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                if (gitProcess.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Failed to clone proxy repository {proxyName} via git. Exit code: {gitProcess.ExitCode}");
                }
            }
            else
            {
                throw new InvalidOperationException("Failed to start git process. Ensure git is installed.");
            }
        }

        // Run npm install
        ProcessStartInfo npmPsi = new()
        {
            FileName = "cmd.exe",
            Arguments = $"/c npm install",
            WorkingDirectory = dir,
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using Process? npmProcess = Process.Start(npmPsi);
        if (npmProcess is not null)
        {
            await npmProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (npmProcess.ExitCode != 0)
            {
                throw new InvalidOperationException($"npm install failed for {proxyName}. Exit code: {npmProcess.ExitCode}");
            }
        }
        else
        {
            throw new InvalidOperationException("Failed to start npm process. Ensure Node.js and npm are installed.");
        }

        if (!IsInstalledDirectory(dir))
        {
            throw new InvalidOperationException($"Proxy {proxyName} was installed, but required files were not found.");
        }
    }

    public async Task AuthenticateAsync(string proxyName, CancellationToken cancellationToken)
    {
        string dir = GetProxyDirectory(proxyName);
        if (!Directory.Exists(dir))
        {
            throw new InvalidOperationException($"Proxy {proxyName} is not installed yet. Install it first.");
        }

        // Run auth in a separate visible window so the user can interact with the terminal and browser
        ProcessStartInfo authPsi = new()
        {
            FileName = "cmd.exe",
            Arguments = $"/c title ScreenMind Auth: {proxyName} & npm run auth & pause",
            WorkingDirectory = dir,
            CreateNoWindow = false,
            UseShellExecute = true
        };

        using Process? process = Process.Start(authPsi);
        if (process is not null)
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> IsRunningAsync(string proxyName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (activeProcesses.TryGetValue(proxyName, out Process? process))
        {
            try
            {
                if (!process.HasExited)
                {
                    return true;
                }

                activeProcesses.TryRemove(proxyName, out _);
            }
            catch
            {
                activeProcesses.TryRemove(proxyName, out _);
            }
        }

        int? defaultPort = GetDefaultProxyPort(proxyName);
        return defaultPort is int port
            && await IsPortOpenAsync(port, cancellationToken).ConfigureAwait(false);
    }

    private static void KillProcessOnPort(int port)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "cmd.exe",
                Arguments = $"/c for /f \"tokens=5\" %a in ('netstat -aon ^| findstr :{port} ^| findstr LISTENING') do taskkill /F /PID %a",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using Process? proc = Process.Start(psi);
            proc?.WaitForExit(2000);
        }
        catch
        {
            // Ignore errors
        }
    }

    public async Task StartAsync(string proxyName, int port, string cookie, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (port <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Proxy port must be positive.");
        }

        string dir = GetProxyDirectory(proxyName);
        if (!IsInstalledDirectory(dir))
        {
            throw new InvalidOperationException($"Proxy {proxyName} is not installed.");
        }

        // Kill any process using this port to prevent EADDRINUSE
        KillProcessOnPort(port);

        // Stop if already running
        if (activeProcesses.TryGetValue(proxyName, out Process? existing) && !existing.HasExited)
        {
            try
            {
                existing.Kill(true);
            }
            catch
            {
                // Ignore
            }
        }

        try
        {
            string envPath = Path.Combine(dir, ".env");
            List<string> envLines = [$"PORT={port}"];
            if (!string.IsNullOrWhiteSpace(cookie))
            {
                envLines.Add($"COOKIE={cookie}");
                envLines.Add($"SESSION_TOKEN={cookie}");
            }

            File.WriteAllLines(envPath, envLines);
        }
        catch
        {
            // Ignore write failures
        }

        string nodeExecutable = ResolveNodeExecutable();
        string entryPoint = GetNodeEntryPoint(dir);
        ProcessStartInfo psi = new()
        {
            FileName = nodeExecutable,
            Arguments = $"\"{entryPoint}\"",
            WorkingDirectory = dir,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        psi.EnvironmentVariables["PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        psi.EnvironmentVariables["NON_INTERACTIVE"] = "1";
        psi.EnvironmentVariables["SKIP_ACCOUNT_MENU"] = "1";

        if (proxyName.Equals("FreeDeepseekAPI", StringComparison.OrdinalIgnoreCase))
        {
            psi.EnvironmentVariables["DEEPSEEK_AUTH_PATH"] = Path.Combine(dir, "deepseek-auth.json");
        }
        else if (proxyName.Equals("FreeGLMKimiAPI", StringComparison.OrdinalIgnoreCase))
        {
            psi.EnvironmentVariables["AUTH_PATH"] = Path.Combine(dir, "auth.json");
        }

        Process? process = new() { StartInfo = psi, EnableRaisingEvents = true };
        process.Exited += (_, _) => activeProcesses.TryRemove(proxyName, out _);
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };

        if (!process.Start())
        {
            process.Dispose();
            process = null;
        }

        if (process is null)
        {
            throw new InvalidOperationException($"Failed to start proxy process for {proxyName}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        activeProcesses[proxyName] = process;
        await Task.Delay(750, cancellationToken).ConfigureAwait(false);
        if (process.HasExited)
        {
            activeProcesses.TryRemove(proxyName, out _);
            throw new InvalidOperationException($"Proxy {proxyName} exited immediately with code {process.ExitCode}.");
        }

        bool listening = await WaitForPortAsync(port, TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        if (!listening)
        {
            activeProcesses.TryRemove(proxyName, out _);
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // Ignore
            }

            throw new InvalidOperationException($"Proxy {proxyName} did not start listening on port {port}.");
        }
    }

    public Task StopAsync(string proxyName, CancellationToken cancellationToken)
    {
        if (activeProcesses.TryRemove(proxyName, out Process? process))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // Ignore
            }
        }
        return Task.CompletedTask;
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        Dispose();
    }

    private static bool IsInstalledDirectory(string dir)
    {
        string packagePath = Path.Combine(dir, PackageJsonFileName);
        if (!File.Exists(packagePath))
        {
            return false;
        }

        return !PackageHasDependencies(packagePath)
            || Directory.Exists(Path.Combine(dir, "node_modules"));
    }

    private static bool PackageHasDependencies(string packagePath)
    {
        try
        {
            using FileStream stream = File.OpenRead(packagePath);
            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            return HasNonEmptyObject(root, "dependencies")
                || HasNonEmptyObject(root, "devDependencies")
                || HasNonEmptyObject(root, "optionalDependencies");
        }
        catch
        {
            return true;
        }
    }

    private static string ResolveNodeExecutable()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (string entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string candidate = Path.Combine(entry, "node.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        string[] commonPaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe"),
        ];

        foreach (string candidate in commonPaths)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "node";
    }

    private static string GetNodeEntryPoint(string dir)
    {
        string packagePath = Path.Combine(dir, PackageJsonFileName);
        try
        {
            using FileStream stream = File.OpenRead(packagePath);
            using JsonDocument document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("main", out JsonElement mainElement)
                && mainElement.ValueKind == JsonValueKind.String)
            {
                string? main = mainElement.GetString();
                if (!string.IsNullOrWhiteSpace(main))
                {
                    string candidate = Path.Combine(dir, main.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }
        catch
        {
            // Fall through to common proxy entry point names.
        }

        string[] candidates =
        [
            Path.Combine(dir, "index.js"),
            Path.Combine(dir, "server.js"),
            Path.Combine(dir, "src", "server.js"),
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"Proxy in {dir} does not expose a Node entry point.");
    }

    private static int? GetDefaultProxyPort(string proxyName)
    {
        if (proxyName.Equals("FreeQwenApi", StringComparison.OrdinalIgnoreCase))
        {
            return 3264;
        }

        if (proxyName.Equals("FreeDeepseekAPI", StringComparison.OrdinalIgnoreCase))
        {
            return 9655;
        }

        if (proxyName.Equals("FreeGLMKimiAPI", StringComparison.OrdinalIgnoreCase))
        {
            return 9766;
        }

        return null;
    }

    private static bool HasNonEmptyObject(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement element)
            && element.ValueKind == JsonValueKind.Object
            && element.EnumerateObject().Any();
    }

    private static async Task<bool> WaitForPortAsync(int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsPortOpenAsync(port, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static async Task<bool> IsPortOpenAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using TcpClient client = new();
            await client.ConnectAsync("127.0.0.1", port, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SocketException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (KeyValuePair<string, Process> kvp in activeProcesses)
        {
            try
            {
                if (!kvp.Value.HasExited)
                {
                    kvp.Value.Kill(true);
                }
            }
            catch
            {
                // Ignore
            }
        }
        activeProcesses.Clear();
    }
}
