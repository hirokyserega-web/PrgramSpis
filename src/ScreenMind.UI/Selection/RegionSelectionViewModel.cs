using CommunityToolkit.Mvvm.ComponentModel;
using ScreenMind.Core.Capture;

namespace ScreenMind.UI.Selection;

public sealed partial class RegionSelectionViewModel : ObservableObject
{
    private const int MinimumSize = 4;

    [ObservableProperty]
    private ScreenRectangle selectedBounds;

    [ObservableProperty]
    private bool isSelectionActive;

    [ObservableProperty]
    private bool isConfirmed;

    [ObservableProperty]
    private bool isCancelled;

    private ScreenPoint dragStart;
    private SelectionHandle activeHandle = SelectionHandle.None;

    public void BeginDrag(ScreenPoint point)
    {
        if (IsConfirmed || IsCancelled)
        {
            return;
        }

        dragStart = point;
        activeHandle = SelectionHandle.Body;
        SelectedBounds = new ScreenRectangle(point.X, point.Y, 0, 0);
        IsSelectionActive = true;
    }

    public void BeginResize(SelectionHandle handle, ScreenPoint point)
    {
        if (!IsSelectionActive || IsConfirmed || IsCancelled)
        {
            return;
        }

        activeHandle = handle;
        dragStart = point;
    }

    public void Move(ScreenPoint point)
    {
        if (activeHandle == SelectionHandle.None || IsConfirmed || IsCancelled)
        {
            return;
        }

        SelectedBounds = activeHandle == SelectionHandle.Body
            ? CreateRectangle(dragStart, point)
            : ResizeRectangle(SelectedBounds, activeHandle, point);
    }

    public RegionSelectionResult Confirm()
    {
        if (!IsSelectionActive || SelectedBounds.IsEmpty)
        {
            return Cancel();
        }

        IsConfirmed = true;
        IsSelectionActive = false;
        activeHandle = SelectionHandle.None;
        return new RegionSelectionResult(true, SelectedBounds);
    }

    public RegionSelectionResult Cancel()
    {
        IsCancelled = true;
        IsSelectionActive = false;
        activeHandle = SelectionHandle.None;
        return RegionSelectionResult.Cancelled;
    }

    private static ScreenRectangle CreateRectangle(ScreenPoint first, ScreenPoint second)
    {
        int left = Math.Min(first.X, second.X);
        int top = Math.Min(first.Y, second.Y);
        int right = Math.Max(first.X, second.X);
        int bottom = Math.Max(first.Y, second.Y);

        return new ScreenRectangle(left, top, right - left, bottom - top);
    }

    private static ScreenRectangle ResizeRectangle(
        ScreenRectangle rectangle,
        SelectionHandle handle,
        ScreenPoint point)
    {
        int left = rectangle.X;
        int top = rectangle.Y;
        int right = rectangle.X + rectangle.Width;
        int bottom = rectangle.Y + rectangle.Height;

        if (handle.HasFlag(SelectionHandle.Left))
        {
            left = Math.Min(point.X, right - MinimumSize);
        }

        if (handle.HasFlag(SelectionHandle.Right))
        {
            right = Math.Max(point.X, left + MinimumSize);
        }

        if (handle.HasFlag(SelectionHandle.Top))
        {
            top = Math.Min(point.Y, bottom - MinimumSize);
        }

        if (handle.HasFlag(SelectionHandle.Bottom))
        {
            bottom = Math.Max(point.Y, top + MinimumSize);
        }

        return new ScreenRectangle(left, top, right - left, bottom - top);
    }
}
