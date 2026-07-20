using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ScreenMind.Core.Ai;

namespace ScreenMind.Infrastructure.Ai;

public sealed class ExternalProxyManager : IExternalProxyManager, IDisposable
{
    private const string PackageJsonFileName = "package.json";

    private readonly ConcurrentDictionary<string, Process> activeProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> proxyLogs = new(StringComparer.OrdinalIgnoreCase);

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

        // Ensure FreeQwenApi maps qwen3.8 as a real model, not an alias of 3.7-max.
        if (proxyName.Equals("FreeQwenApi", StringComparison.OrdinalIgnoreCase))
        {
            await FixQwenProxyModelsAsync(cancellationToken).ConfigureAwait(false);
        }

        // Check if port is already open
        if (await IsPortOpenAsync(port, cancellationToken).ConfigureAwait(false))
        {
            bool isOurProxy = await RunProxyHealthCheckAsync(proxyName, port, cancellationToken).ConfigureAwait(false);
            if (isOurProxy)
            {
                // It is a healthy instance of our proxy. Let's kill it so we can re-apply settings
                KillProcessOnPort(port);
            }
            else
            {
                // Occupied by some other unknown application
                throw new InvalidOperationException($"The configured port is occupied by another process.");
            }
        }

        // Stop if already running via our dictionary
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

        // Prepare Env Settings
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
        else if (proxyName.Equals("FreeQwenApi", StringComparison.OrdinalIgnoreCase))
        {
            psi.EnvironmentVariables["DEFAULT_MODEL"] = "qwen3.8-max-preview";
        }

        // Clear diagnostic log for this proxy
        var logs = proxyLogs.GetOrAdd(proxyName, _ => new ConcurrentQueue<string>());
        while (logs.TryDequeue(out _)) { }

        Process? process = new() { StartInfo = psi, EnableRaisingEvents = true };
        process.Exited += (_, _) => activeProcesses.TryRemove(proxyName, out _);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                AddDiagnosticLine(proxyName, e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                AddDiagnosticLine(proxyName, e.Data);
            }
        };

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
            throw new InvalidOperationException($"{proxyName} exited immediately.");
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

            throw new InvalidOperationException($"{proxyName} did not start listening on the configured port.");
        }

        // Execute health check validation
        bool healthy = await RunProxyHealthCheckAsync(proxyName, port, cancellationToken).ConfigureAwait(false);
        if (!healthy)
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

            // Determine if it was due to authentication or general failure
            bool needsAuth = CheckLogsForAuthFailure(proxyName);
            if (needsAuth)
            {
                throw new InvalidOperationException($"{proxyName} requires authentication.");
            }
            throw new InvalidOperationException($"{proxyName} health check failed.");
        }
    }

    private void AddDiagnosticLine(string proxyName, string line)
    {
        string sanitized = SanitizeDiagnosticLine(line);
        var logs = proxyLogs.GetOrAdd(proxyName, _ => new ConcurrentQueue<string>());
        logs.Enqueue(sanitized);
        while (logs.Count > 100)
        {
            logs.TryDequeue(out _);
        }
    }

    private static string SanitizeDiagnosticLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return line;

        string sanitized = line;

        // Redact typical sensitive values
        sanitized = Regex.Replace(sanitized, @"(?i)(cookie|token|auth|key|secret|password|session_token|session)\s*[:=]\s*[^\s;]+", "$1=REDACTED");
        
        // Redact base64 patterns (e.g. long alphanumeric chains with ending == or /+)
        sanitized = Regex.Replace(sanitized, @"(?:[A-Za-z0-9+/]{40,})={0,2}", "[BASE64_REDACTED]");
        
        // Redact file paths to session files
        sanitized = Regex.Replace(sanitized, @"[a-zA-Z]:\\[^\s]*session[^\s]*", "[PATH_REDACTED]");
        sanitized = Regex.Replace(sanitized, @"/[^\s]*/session/[^\s]*", "[PATH_REDACTED]");

        return sanitized;
    }

    private bool CheckLogsForAuthFailure(string proxyName)
    {
        if (proxyLogs.TryGetValue(proxyName, out var logs))
        {
            foreach (var line in logs)
            {
                if (line.Contains("Не удалось получить токен авторизации", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("authentication is required", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Браузер не инициализирован", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static async Task<bool> RunProxyHealthCheckAsync(string proxyName, int port, CancellationToken cancellationToken)
    {
        using HttpClient client = new();
        client.Timeout = TimeSpan.FromSeconds(5);
        try
        {
            string url = proxyName.Equals("FreeQwenApi", StringComparison.OrdinalIgnoreCase)
                ? $"http://localhost:{port}/api/health"
                : $"http://localhost:{port}/api/health";

            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                url = $"http://localhost:{port}/health";
                response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(content);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("service", out JsonElement serviceProp))
                {
                    string? serviceName = serviceProp.GetString();
                    if (!string.Equals(serviceName, proxyName, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                if (root.TryGetProperty("ok", out JsonElement okProp) && okProp.GetBoolean())
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }
        return false;
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

    public async Task FixQwenProxyModelsAsync(CancellationToken cancellationToken)
    {
        const string modelId = "qwen3.8-max-preview";
        string[] modelAliases =
        [
            "Qwen3.8-Max-Preview",
            "qwen-3.8-max-preview",
            "qwen38-max-preview",
            "qwen38-max",
            "qwen3.8-max",
            "qwen3.8",
        ];

        string dir = GetProxyDirectory("FreeQwenApi");
        string modelsFile = Path.Combine(dir, "src", "AvailableModels.txt");
        string mappingFile = Path.Combine(dir, "src", "api", "modelMapping.js");
        string chatFile = Path.Combine(dir, "src", "api", "chat.js");
        string routesFile = Path.Combine(dir, "src", "api", "routes.js");

        if (!IsRecognizedQwenProxy(dir, modelsFile, mappingFile, chatFile, routesFile))
        {
            return;
        }

        if (File.Exists(modelsFile))
        {
            string modelsContent = await File.ReadAllTextAsync(modelsFile, cancellationToken).ConfigureAwait(false);
            if (!ContainsWholeLine(modelsContent, modelId))
            {
                string lineEnding = modelsContent.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                string updatedModels = modelsContent.TrimEnd() + lineEnding + modelId + lineEnding;
                await File.WriteAllTextAsync(modelsFile, updatedModels, cancellationToken).ConfigureAwait(false);
            }
        }

        string content = await File.ReadAllTextAsync(mappingFile, cancellationToken).ConfigureAwait(false);
        string original = content;
        string nl = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string escapedModelId = Regex.Escape(modelId);

        bool isCanonical = Regex.IsMatch(
            content,
            @"CANONICAL_MODELS\s*=\s*Object\.freeze\(\[[\s\S]*?""" + escapedModelId + @"""",
            RegexOptions.IgnoreCase);
        bool isAliasOfOtherModel = Regex.IsMatch(
            content,
            @"""qwen3\.7-max""\s*:\s*\[[\s\S]*?""" + escapedModelId + @"""",
            RegexOptions.IgnoreCase);
        bool hasOwnAliasGroup = Regex.IsMatch(
            content,
            @"ALIAS_GROUPS\s*=\s*Object\.freeze\(\{[\s\S]*?""" + escapedModelId + @"""\s*:",
            RegexOptions.IgnoreCase);

        if (isAliasOfOtherModel)
        {
            content = Regex.Replace(
                content,
                @"(^[ \t]*""qwen3\.7-max""\s*:\s*\[[^\]]*)""(?:qwen3\.8-max-preview|Qwen3\.8-Max-Preview|qwen-3\.8-max-preview|qwen38-max-preview|qwen38-max|qwen3\.8-max|qwen3\.8)"",?",
                "$1",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            isCanonical = false;
            hasOwnAliasGroup = false;
        }

        if (!isCanonical)
        {
            const string canonicalStart = "const CANONICAL_MODELS = Object.freeze([";
            int canonicalIndex = content.IndexOf(canonicalStart, StringComparison.OrdinalIgnoreCase);
            if (canonicalIndex < 0)
            {
                return;
            }

            int insertPos = canonicalIndex + canonicalStart.Length;
            content = content.Insert(insertPos, $"{nl}    \"{modelId}\",");
        }

        if (!hasOwnAliasGroup)
        {
            const string aliasStart = "const ALIAS_GROUPS = Object.freeze({";
            int aliasIndex = content.IndexOf(aliasStart, StringComparison.OrdinalIgnoreCase);
            if (aliasIndex < 0)
            {
                return;
            }

            int insertPos = aliasIndex + aliasStart.Length;
            string aliasLines = string.Join($",{nl}", modelAliases.Select(alias => $"        \"{alias}\""));
            string insertText = $"{nl}    \"{modelId}\": [{nl}{aliasLines}{nl}    ],";
            content = content.Insert(insertPos, insertText);
        }

        if (!string.Equals(content, original, StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(mappingFile, content, cancellationToken).ConfigureAwait(false);
        }

        await FixQwen38MaxPreviewPayloadAsync(chatFile, cancellationToken).ConfigureAwait(false);
        await FixQwen38MaxPreviewResponseAsync(chatFile, routesFile, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsRecognizedQwenProxy(string dir, string modelsFile, string mappingFile, string chatFile, string routesFile)
    {
        if (!File.Exists(Path.Combine(dir, PackageJsonFileName))
            || !File.Exists(modelsFile)
            || !File.Exists(mappingFile)
            || !File.Exists(chatFile)
            || !File.Exists(routesFile))
        {
            return false;
        }

        string mapping = File.ReadAllText(mappingFile);
        string chat = File.ReadAllText(chatFile);
        return mapping.Contains("const CANONICAL_MODELS = Object.freeze([", StringComparison.Ordinal)
            && mapping.Contains("const ALIAS_GROUPS = Object.freeze({", StringComparison.Ordinal)
            && chat.Contains("function buildPayloadV2(", StringComparison.Ordinal)
            && chat.Contains("feature_config", StringComparison.Ordinal);
    }

    private static bool ContainsWholeLine(string content, string value)
        => Regex.IsMatch(content, $"^\\s*{Regex.Escape(value)}\\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private static async Task FixQwen38MaxPreviewPayloadAsync(string chatFile, CancellationToken cancellationToken)
    {
        if (!File.Exists(chatFile))
        {
            return;
        }

        string content = await File.ReadAllTextAsync(chatFile, cancellationToken).ConfigureAwait(false);
        string original = content;
        string nl = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        if (!content.Contains("function isQwen38MaxPreviewModel", StringComparison.Ordinal))
        {
            const string payloadFunction = "function buildPayloadV2(messageContent, model, chatId, parentId, files, systemMessage, tools, toolChoice, chatType = 't2t', size = null) {";
            int payloadIndex = content.IndexOf(payloadFunction, StringComparison.Ordinal);
            if (payloadIndex < 0)
            {
                return;
            }

            string helper =
                "function isQwen38MaxPreviewModel(model) {" + nl +
                "    return typeof model === 'string' && model.toLowerCase() === 'qwen3.8-max-preview';" + nl +
                "}" + nl + nl;
            content = content.Insert(payloadIndex, helper);
        }

        if (!content.Contains("const qwen38MaxPreview = isQwen38MaxPreviewModel(model);", StringComparison.Ordinal))
        {
            string oldConfig =
                "    const featureConfig = {" + nl +
                "        thinking_enabled: isVideo," + nl +
                "        output_schema: 'phase'" + nl +
                "    };";
            string newConfig =
                "    const qwen38MaxPreview = isQwen38MaxPreviewModel(model);" + nl +
                "    const featureConfig = {" + nl +
                "        thinking_enabled: isVideo || qwen38MaxPreview," + nl +
                "        output_schema: 'phase'" + nl +
                "    };";
            content = content.Replace(oldConfig, newConfig, StringComparison.Ordinal);
        }

        string oldVideoConfig =
            "    if (isVideo) {" + nl +
            "        featureConfig.research_mode = 'normal';" + nl +
            "        featureConfig.auto_thinking = true;" + nl +
            "        featureConfig.thinking_format = 'summary';" + nl +
            "        featureConfig.auto_search = true;" + nl +
            "    }";
        string newVideoConfig =
            "    if (isVideo || qwen38MaxPreview) {" + nl +
            "        featureConfig.research_mode = 'normal';" + nl +
            "        featureConfig.auto_thinking = true;" + nl +
            "        featureConfig.thinking_format = 'summary';" + nl +
            "    }" + nl +
            "    if (isVideo) {" + nl +
            "        featureConfig.auto_search = true;" + nl +
            "    }";
        content = content.Replace(oldVideoConfig, newVideoConfig, StringComparison.Ordinal);

        if (!string.Equals(content, original, StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(chatFile, content, cancellationToken).ConfigureAwait(false);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861", Justification = "The replacement arrays depend on the runtime line ending and are used only while applying the patch.")]
    private static async Task FixQwen38MaxPreviewResponseAsync(string chatFile, string routesFile, CancellationToken cancellationToken)
    {
        if (!File.Exists(chatFile) || !File.Exists(routesFile))
        {
            return;
        }

        string chat = await File.ReadAllTextAsync(chatFile, cancellationToken).ConfigureAwait(false);
        string routes = await File.ReadAllTextAsync(routesFile, cancellationToken).ConfigureAwait(false);
        if (chat.Contains("function emitQwenDelta", StringComparison.Ordinal)
            && routes.Contains("function writeQwenDeltaSse", StringComparison.Ordinal))
        {
            return;
        }

        string nl = chat.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string originalChat = chat;
        const string nodeParser = "async function executeApiRequestWithNodeStreaming(apiUrl, payload, token, onChunk) {";
        int parserIndex = chat.IndexOf(nodeParser, StringComparison.Ordinal);
        if (parserIndex < 0)
        {
            return;
        }

        string helper = string.Join(nl, new[]
        {
            "function emitQwenDelta(delta, onChunk) {",
            "    if (typeof onChunk !== 'function' || !delta) return;",
            "    const phase = typeof delta.phase === 'string' ? delta.phase.toLowerCase() : '';",
            "    const reasoning = delta.reasoning_content ?? delta.reasoning ?? delta.thinking;",
            "    if (typeof reasoning === 'string' && reasoning.length > 0) onChunk(reasoning, 'reasoning');",
            "    if (typeof delta.content === 'string' && delta.content.length > 0) {",
            "        onChunk(delta.content, phase === 'think' || phase === 'thinking' || phase === 'reasoning' ? 'reasoning' : 'content');",
            "    }",
            "}",
            "",
            ""
        });
        chat = chat.Insert(parserIndex, helper);

        string oldNode = string.Join(nl, new[]
        {
            "                        if (delta && delta.content) {",
            "                            fullContent += delta.content;",
            "                            if (typeof onChunk === 'function') {",
            "                                onChunk(delta.content);",
            "                                hasStreamedChunks = true;",
            "                            }",
            "                        }",
            ""
        });
        string newNode = string.Join(nl, new[]
        {
            "                        if (delta) {",
            "                            if (typeof delta.content === 'string' && delta.content.length > 0) {",
            "                                const phase = typeof delta.phase === 'string' ? delta.phase.toLowerCase() : '';",
            "                                if (phase === 'think' || phase === 'thinking' || phase === 'reasoning') {",
            "                                    fullReasoning += delta.content;",
            "                                } else {",
            "                                    fullContent += delta.content;",
            "                                }",
            "                            }",
            "                            if (typeof delta.reasoning_content === 'string') fullReasoning += delta.reasoning_content;",
            "                            if (typeof delta.reasoning === 'string') fullReasoning += delta.reasoning;",
            "                            if (typeof delta.thinking === 'string') fullReasoning += delta.thinking;",
            "                            const emitted = typeof onChunk === 'function'",
            "                                && ((typeof delta.content === 'string' && delta.content.length > 0)",
            "                                    || (typeof delta.reasoning_content === 'string' && delta.reasoning_content.length > 0)",
            "                                    || (typeof delta.reasoning === 'string' && delta.reasoning.length > 0)",
            "                                    || (typeof delta.thinking === 'string' && delta.thinking.length > 0));",
            "                            emitQwenDelta(delta, onChunk);",
            "                            if (emitted) hasStreamedChunks = true;",
            "                        }",
            ""
        });
        if (!chat.Contains(oldNode, StringComparison.Ordinal))
        {
            return;
        }
        chat = chat.Replace("        let fullContent = '';" + nl, "        let fullContent = '';" + nl + "        let fullReasoning = '';" + nl, StringComparison.Ordinal);
        chat = chat.Replace(oldNode, newNode, StringComparison.Ordinal);
        chat = chat.Replace("message: { role: 'assistant', content: fullContent }," + nl, "message: { role: 'assistant', content: fullContent, ...(fullReasoning ? { reasoning_content: fullReasoning } : {}) }," + nl, StringComparison.Ordinal);

        string routesHelper = string.Join(nl, new[]
        {
            "function writeQwenDeltaSse(res, mappedModel, chunk, kind) {",
            "    const delta = kind === 'reasoning' ? { reasoning_content: chunk } : { content: chunk };",
            "    res.write('data: ' + JSON.stringify({ id: 'chatcmpl-stream', object: 'chat.completion.chunk', created: Math.floor(Date.now() / 1000), model: mappedModel || DEFAULT_MODEL, choices: [{ index: 0, delta, finish_reason: null }] }) + '\\n\\n');",
            "}",
            "",
            ""
        });
        const string routeMarker = "function writeOpenAIUsageSse(res, base, usage = null) {";
        int routeIndex = routes.IndexOf(routeMarker, StringComparison.Ordinal);
        if (routeIndex < 0)
        {
            return;
        }
        routes = routes.Insert(routeIndex, routesHelper);
        string oldCallback = string.Join(nl, new[]
        {
            "streamingCallback = (chunk) => {",
            "                        hasStreamedChunks = true;",
            "                        writeSse({",
            ""
        });
        string newCallback = string.Join(nl, new[]
        {
            "streamingCallback = (chunk, kind = 'content') => {",
            "                        hasStreamedChunks = true;",
            "                        if (kind === 'reasoning') {",
            "                            writeQwenDeltaSse(res, mappedModel, chunk, kind);",
            "                            return;",
            "                        }",
            "                        writeSse({",
            ""
        });
        routes = routes.Replace(oldCallback, newCallback, StringComparison.Ordinal);
        string oldCallback2 = string.Join(nl, new[]
        {
            "streamingCallback = (chunk) => {",
            "                        hasStreamedChunks = true;",
            "                        // OpenWebUI не нуждается в role в чанках - только контент",
            ""
        });
        string newCallback2 = string.Join(nl, new[]
        {
            "streamingCallback = (chunk, kind = 'content') => {",
            "                        hasStreamedChunks = true;",
            "                        if (kind === 'reasoning') {",
            "                            writeQwenDeltaSse(res, mappedModel, chunk, kind);",
            "                            return;",
            "                        }",
            "                        // OpenWebUI не нуждается в role в чанках - только контент",
            ""
        });
        routes = routes.Replace(oldCallback2, newCallback2, StringComparison.Ordinal);
        if (string.Equals(chat, originalChat, StringComparison.Ordinal))
        {
            return;
        }
        await File.WriteAllTextAsync(chatFile, chat, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(routesFile, routes, cancellationToken).ConfigureAwait(false);
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
            // Fall through
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
