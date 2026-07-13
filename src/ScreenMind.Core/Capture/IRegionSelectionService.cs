namespace ScreenMind.Core.Capture;

public interface IRegionSelectionService
{
    Task<RegionSelectionResult> SelectAsync(CancellationToken cancellationToken);
}

