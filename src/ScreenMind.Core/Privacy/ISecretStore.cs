namespace ScreenMind.Core.Privacy;

public interface ISecretStore
{
    Task SaveAsync(string name, string secret, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string name, CancellationToken cancellationToken);

    Task<string?> GetAsync(string name, CancellationToken cancellationToken);

    Task DeleteAsync(string name, CancellationToken cancellationToken);
}

