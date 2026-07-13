using FluentAssertions;
using ScreenMind.Core.Capture;
using ScreenMind.UI.Selection;

namespace ScreenMind.IntegrationTests;

public sealed class RegionSelectionViewModelTests
{
    [Fact]
    public void DragShouldCreatePositiveBoundsAndConfirm()
    {
        RegionSelectionViewModel viewModel = new();

        viewModel.BeginDrag(new ScreenPoint(50, 40));
        viewModel.Move(new ScreenPoint(10, 12));
        viewModel.Move(new ScreenPoint(80, 90));
        RegionSelectionResult result = viewModel.Confirm();

        result.IsConfirmed.Should().BeTrue();
        result.Bounds.Should().Be(new ScreenRectangle(50, 40, 30, 50));
        viewModel.IsSelectionActive.Should().BeFalse();
        viewModel.IsConfirmed.Should().BeTrue();
    }

    [Fact]
    public void ResizeShouldRespectMinimumSize()
    {
        RegionSelectionViewModel viewModel = new();
        viewModel.BeginDrag(new ScreenPoint(10, 10));
        viewModel.Move(new ScreenPoint(50, 50));

        viewModel.BeginResize(SelectionHandle.Left | SelectionHandle.Top, new ScreenPoint(10, 10));
        viewModel.Move(new ScreenPoint(49, 49));

        viewModel.SelectedBounds.Width.Should().Be(4);
        viewModel.SelectedBounds.Height.Should().Be(4);
    }

    [Fact]
    public void CancelShouldReturnCancelledResultWithoutSnapshot()
    {
        RegionSelectionViewModel viewModel = new();
        viewModel.BeginDrag(new ScreenPoint(10, 10));

        RegionSelectionResult result = viewModel.Cancel();

        result.IsConfirmed.Should().BeFalse();
        viewModel.IsCancelled.Should().BeTrue();
        viewModel.IsSelectionActive.Should().BeFalse();
    }
}
