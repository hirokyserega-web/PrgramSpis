namespace ScreenMind.Core.Diagnostics;

public interface IDiagnosticsService
{
    Task<string> GetDiagnosticReportAsync(CancellationToken cancellationToken);
}
