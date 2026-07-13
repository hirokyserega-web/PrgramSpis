using ScreenMind.Core.Ai;

namespace ScreenMind.AI;

public sealed class AiProviderRegistry
{
    private readonly Dictionary<string, IAiProvider> providers;

    public AiProviderRegistry(IEnumerable<IAiProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        this.providers = providers.ToDictionary(
            provider => provider.Id,
            StringComparer.OrdinalIgnoreCase);
    }

    public IAiProvider GetRequired(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        return providers.TryGetValue(providerId, out IAiProvider? provider)
            ? provider
            : throw new AiProviderException(new AiError(
                AiErrorKind.Configuration,
                $"AI provider '{providerId}' is not registered."));
    }

    public IReadOnlyList<IAiProvider> All => providers.Values.ToArray();
}
