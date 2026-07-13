using FluentAssertions;
using ScreenMind.Platform.Windows.Privacy;

namespace ScreenMind.Platform.Windows.Tests;

public sealed class WindowsCredentialSecretStoreTests
{
    [Fact]
    public async Task SecretStoreShouldSaveExistReadAndDeleteSecret()
    {
        WindowsCredentialSecretStore store = new();
        string name = "test-" + Guid.NewGuid().ToString("N");

        try
        {
            await store.SaveAsync(name, "secret-value", CancellationToken.None);

            bool exists = await store.ExistsAsync(name, CancellationToken.None);
            string? value = await store.GetAsync(name, CancellationToken.None);

            exists.Should().BeTrue();
            value.Should().Be("secret-value");
        }
        finally
        {
            await store.DeleteAsync(name, CancellationToken.None);
        }

        bool existsAfterDelete = await store.ExistsAsync(name, CancellationToken.None);
        existsAfterDelete.Should().BeFalse();
    }
}

