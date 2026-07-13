using System.Threading;
using System.Threading.Tasks;

namespace ScreenMind.Core.Ai;

public interface IExternalProxyManager
{
    Task<bool> IsInstalledAsync(string proxyName, CancellationToken cancellationToken);

    Task InstallAsync(string proxyName, CancellationToken cancellationToken);

    Task AuthenticateAsync(string proxyName, CancellationToken cancellationToken);

    Task<bool> IsRunningAsync(string proxyName, CancellationToken cancellationToken);

    Task StartAsync(string proxyName, int port, string cookie, CancellationToken cancellationToken);

    Task StopAsync(string proxyName, CancellationToken cancellationToken);
}
