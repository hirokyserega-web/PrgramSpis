using System.Linq;
using ScreenMind.Core.Ai;
using ScreenMind.Core.Privacy;
using ScreenMind.Core.Settings;

namespace ScreenMind.AI;

public sealed class ProviderConfigurationResolver
{
    private const int DefaultQwenProxyPort = 3264;
    private const int DefaultDeepseekProxyPort = 9655;
    private const int LegacyGlmKimiProxyPort = 3265;
    private const int DefaultGlmKimiProxyPort = 9766;

    private readonly ISettingsStore settingsStore;
    private readonly ISecretStore? secretStore;

    public ProviderConfigurationResolver(
        ISettingsStore settingsStore,
        ISecretStore? secretStore = null)
    {
        this.settingsStore = settingsStore;
        this.secretStore = secretStore;
    }

    public async Task<ProviderRuntimeConfiguration> ResolveAsync(
        AiProfile profile,
        string providerId,
        Uri defaultBaseUri,
        string? defaultSecretName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(defaultBaseUri);

        ScreenMindSettings settings = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        settings.Providers.Providers.TryGetValue(providerId, out ProviderEndpointSettings? endpoint);

        Uri baseUri = !string.IsNullOrWhiteSpace(endpoint?.BaseUrl)
            ? new Uri(endpoint.BaseUrl, UriKind.Absolute)
            : defaultBaseUri;
        baseUri = EnsureDirectoryUri(baseUri);

        string profileModelId = profile.GetModelForProvider(providerId);
        string modelId = !string.IsNullOrWhiteSpace(profileModelId)
            ? profileModelId
            : endpoint?.ModelId ?? string.Empty;

        if (providerId == "openai-compatible")
        {
            ManagedProxiesSettings managedProxies = settings.ManagedProxies ?? new ManagedProxiesSettings();
            managedProxies.Qwen ??= new ManagedProxyItem { Port = DefaultQwenProxyPort };
            managedProxies.Deepseek ??= new ManagedProxyItem { Port = DefaultDeepseekProxyPort };
            managedProxies.GlmKimi ??= new ManagedProxyItem { Port = LegacyGlmKimiProxyPort };
            NormalizeManagedProxyPorts(managedProxies);
            bool isManagedProxyUri = IsKnownManagedProxyUri(baseUri, managedProxies);
            bool isUnconfiguredCompatibleUri = UriEquals(baseUri, EnsureDirectoryUri(defaultBaseUri));
            bool shouldUseSelectedLocalProxy = isManagedProxyUri || isUnconfiguredCompatibleUri;

            if (IsQwen(profile, modelId) && (managedProxies.Qwen.Enabled || shouldUseSelectedLocalProxy))
            {
                baseUri = BuildQwenProxyUri(managedProxies.Qwen.Port);
            }
            else if (IsQwen(profile, modelId))
            {
                baseUri = NormalizeQwenProxyUri(baseUri, managedProxies.Qwen.Port);
            }
            else if (IsDeepseek(profile, modelId) && (managedProxies.Deepseek.Enabled || shouldUseSelectedLocalProxy))
            {
                baseUri = BuildRootProxyUri(managedProxies.Deepseek.Port);
            }
            else if (IsGlmKimi(profile, modelId) && (managedProxies.GlmKimi.Enabled || shouldUseSelectedLocalProxy))
            {
                baseUri = BuildRootProxyUri(managedProxies.GlmKimi.Port);
            }
        }

        string? secretName = !string.IsNullOrWhiteSpace(endpoint?.SecretName)
            ? endpoint.SecretName
            : defaultSecretName;

        string? apiKey = null;
        if (!string.IsNullOrWhiteSpace(secretName) && secretStore is not null)
        {
            apiKey = await secretStore.GetAsync(secretName, cancellationToken).ConfigureAwait(false);
        }

        return new ProviderRuntimeConfiguration(providerId, baseUri, modelId, apiKey);
    }

    private static Uri NormalizeQwenProxyUri(Uri baseUri, int configuredPort)
    {
        if (!IsLocalQwenProxyUri(baseUri, configuredPort))
        {
            return baseUri;
        }

        return BuildQwenProxyUri(baseUri.Port);
    }

    private static bool IsLocalQwenProxyUri(Uri uri, int configuredPort)
    {
        if (!uri.IsLoopback)
        {
            return false;
        }

        return uri.Port == DefaultQwenProxyPort || uri.Port == configuredPort;
    }

    private static bool IsKnownManagedProxyUri(Uri uri, ManagedProxiesSettings managedProxies)
    {
        if (!uri.IsLoopback)
        {
            return false;
        }

        int[] knownPorts =
        [
            DefaultQwenProxyPort,
            DefaultDeepseekProxyPort,
            LegacyGlmKimiProxyPort,
            DefaultGlmKimiProxyPort,
            managedProxies.Qwen.Port,
            managedProxies.Deepseek.Port,
            managedProxies.GlmKimi.Port,
        ];

        return knownPorts.Contains(uri.Port);
    }

    private static void NormalizeManagedProxyPorts(ManagedProxiesSettings managedProxies)
    {
        if (managedProxies.Qwen.Port <= 0)
        {
            managedProxies.Qwen.Port = DefaultQwenProxyPort;
        }

        if (managedProxies.Deepseek.Port <= 0)
        {
            managedProxies.Deepseek.Port = DefaultDeepseekProxyPort;
        }

        if (managedProxies.GlmKimi.Port <= 0)
        {
            managedProxies.GlmKimi.Port = LegacyGlmKimiProxyPort;
        }
    }

    private static bool IsQwen(AiProfile profile, string modelId)
    {
        return profile.Id.StartsWith("qwen", StringComparison.OrdinalIgnoreCase)
            || modelId.StartsWith("qwen", StringComparison.OrdinalIgnoreCase)
            || modelId.StartsWith("qwq", StringComparison.OrdinalIgnoreCase)
            || modelId.StartsWith("qvq", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDeepseek(AiProfile profile, string modelId)
    {
        return profile.Id.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase)
            || modelId.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGlmKimi(AiProfile profile, string modelId)
    {
        return profile.Id.StartsWith("kimi", StringComparison.OrdinalIgnoreCase)
            || profile.Id.StartsWith("glm", StringComparison.OrdinalIgnoreCase)
            || modelId.StartsWith("kimi", StringComparison.OrdinalIgnoreCase)
            || modelId.StartsWith("glm", StringComparison.OrdinalIgnoreCase);
    }

    private static Uri BuildQwenProxyUri(int port)
    {
        return new Uri($"http://localhost:{port}/api/", UriKind.Absolute);
    }

    private static Uri BuildRootProxyUri(int port)
    {
        return new Uri($"http://localhost:{port}/", UriKind.Absolute);
    }

    private static Uri EnsureDirectoryUri(Uri uri)
    {
        string text = uri.ToString();
        return text.EndsWith('/')
            ? uri
            : new Uri(text + "/", UriKind.Absolute);
    }

    private static bool UriEquals(Uri left, Uri right)
    {
        return Uri.Compare(
            left,
            right,
            UriComponents.SchemeAndServer | UriComponents.Path,
            UriFormat.Unescaped,
            StringComparison.OrdinalIgnoreCase) == 0;
    }
}

