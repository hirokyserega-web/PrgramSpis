using FluentAssertions;
using ScreenMind.Core;

namespace ScreenMind.Core.Tests;

public sealed class CoreArchitectureTests
{
    [Fact]
    public void CoreAssemblyShouldNotReferencePlatformUiHttpOrProviderProjects()
    {
        string[] references = CoreAssembly.Instance
            .GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name ?? string.Empty)
            .ToArray();

        references.Should().NotContain(reference => reference.StartsWith("ScreenMind.", StringComparison.Ordinal));
        references.Should().NotContain("Avalonia");
        references.Should().NotContain("Microsoft.Extensions.Http");
    }
}
