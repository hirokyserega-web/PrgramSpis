using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using ScreenMind.Core.Capture;

namespace ScreenMind.UI.Selection;

public sealed class SelectionOverlayWindow : Window
{
    private const double HandleHitSize = 10d;

    private readonly RegionSelectionViewModel viewModel = new();
    private readonly Canvas root = new();
    private readonly Rectangle shade = new();
    private readonly Rectangle selection = new();
    private readonly TextBlock sizeText = new();
    private readonly Border instructions = new();

    private bool completionRaised;
    private IPointer? capturedPointer;

    public SelectionOverlayWindow()
    {
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        WindowStartupLocation = WindowStartupLocation.Manual;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint =
        [
            WindowTransparencyLevel.Transparent,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.None,
        ];
        Content = root;
        DataContext = viewModel;
        Cursor = new Cursor(StandardCursorType.Cross);

        ConfigureVisuals();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        AddHandler(KeyDownEvent, OnKeyDown, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnPointerPressed, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMoved, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, handledEventsToo: true);
    }

    public event EventHandler<RegionSelectionResult>? Completed;

    public void CancelAndClose()
    {
        Complete(RegionSelectionResult.Cancelled);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        CoverAllScreens();
        Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (!completionRaised)
        {
            Completed?.Invoke(this, RegionSelectionResult.Cancelled);
        }

        base.OnClosed(e);
    }

    private void ConfigureVisuals()
    {
        root.Background = Brushes.Transparent;

        shade.Fill = new SolidColorBrush(Color.FromArgb(132, 0, 0, 0));
        root.Children.Add(shade);

        selection.Stroke = new SolidColorBrush(Color.Parse("#887FFF"));
        selection.StrokeThickness = 2d;
        selection.Fill = new SolidColorBrush(Color.FromArgb(32, 113, 104, 246));
        selection.IsVisible = false;
        root.Children.Add(selection);

        sizeText.Foreground = Brushes.White;
        sizeText.Background = new SolidColorBrush(Color.Parse("#F2111729"));
        sizeText.Padding = new Thickness(8, 4);
        sizeText.FontSize = 12;
        sizeText.IsVisible = false;
        root.Children.Add(sizeText);

        instructions.Background = new SolidColorBrush(Color.Parse("#F2111729"));
        instructions.BorderBrush = new SolidColorBrush(Color.Parse("#35415F"));
        instructions.BorderThickness = new Thickness(1);
        instructions.CornerRadius = new CornerRadius(10);
        instructions.Padding = new Thickness(16, 11);
        instructions.Child = new TextBlock
        {
            Text = "Drag to capture an area   ·   Esc to cancel",
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeight.Medium,
        };
        root.Children.Add(instructions);
    }

    private void CoverAllScreens()
    {
        PixelRect bounds = GetVirtualScreenBounds();
        Position = bounds.Position;
        double scale = RenderScaling <= 0 ? 1d : RenderScaling;
        Width = bounds.Width / scale;
        Height = bounds.Height / scale;
        shade.Width = Width;
        shade.Height = Height;
        instructions.Measure(Size.Infinity);
        Canvas.SetLeft(instructions, Math.Max(16, (Width - instructions.DesiredSize.Width) / 2));
        Canvas.SetTop(instructions, 18);
    }

    private PixelRect GetVirtualScreenBounds()
    {
        IReadOnlyList<Avalonia.Platform.Screen> screens = Screens.All;
        if (screens.Count == 0)
        {
            return new PixelRect(Position, new PixelSize(1200, 800));
        }

        int left = screens.Min(screen => screen.Bounds.X);
        int top = screens.Min(screen => screen.Bounds.Y);
        int right = screens.Max(screen => screen.Bounds.X + screen.Bounds.Width);
        int bottom = screens.Max(screen => screen.Bounds.Y + screen.Bounds.Height);
        return new PixelRect(new PixelPoint(left, top), new PixelSize(right - left, bottom - top));
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        capturedPointer = e.Pointer;
        capturedPointer.Capture(this);

        ScreenPoint screenPoint = ToScreenPoint(point.Position);
        SelectionHandle handle = GetHandle(point.Position);
        if (handle == SelectionHandle.None)
        {
            viewModel.BeginDrag(screenPoint);
        }
        else
        {
            viewModel.BeginResize(handle, screenPoint);
        }

        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (capturedPointer is null)
        {
            return;
        }

        viewModel.Move(ToScreenPoint(e.GetPosition(this)));
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        capturedPointer?.Capture(null);
        capturedPointer = null;
        e.Handled = true;

        if (viewModel.IsSelectionActive && viewModel.SelectedBounds.Width >= 5 && viewModel.SelectedBounds.Height >= 5)
        {
            Complete(viewModel.Confirm());
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Complete(viewModel.Cancel());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            Complete(viewModel.Confirm());
            e.Handled = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RegionSelectionViewModel.SelectedBounds)
            or nameof(RegionSelectionViewModel.IsSelectionActive))
        {
            UpdateSelectionVisual();
        }
    }

    private void UpdateSelectionVisual()
    {
        ScreenRectangle bounds = viewModel.SelectedBounds;
        if (!viewModel.IsSelectionActive || bounds.IsEmpty)
        {
            selection.IsVisible = false;
            sizeText.IsVisible = false;
            return;
        }

        double scale = RenderScaling <= 0 ? 1d : RenderScaling;
        double left = (bounds.X - Position.X) / scale;
        double top = (bounds.Y - Position.Y) / scale;
        double width = bounds.Width / scale;
        double height = bounds.Height / scale;

        Canvas.SetLeft(selection, left);
        Canvas.SetTop(selection, top);
        selection.Width = width;
        selection.Height = height;
        selection.IsVisible = true;

        sizeText.Text = $"{bounds.Width} x {bounds.Height}";
        Canvas.SetLeft(sizeText, left);
        Canvas.SetTop(sizeText, top + height + 8 <= Height - 30 ? top + height + 8 : Math.Max(0, top - 30));
        sizeText.IsVisible = true;
    }

    private SelectionHandle GetHandle(Point point)
    {
        ScreenRectangle bounds = viewModel.SelectedBounds;
        if (bounds.IsEmpty)
        {
            return SelectionHandle.None;
        }

        double scale = RenderScaling <= 0 ? 1d : RenderScaling;
        double left = (bounds.X - Position.X) / scale;
        double top = (bounds.Y - Position.Y) / scale;
        double right = left + bounds.Width / scale;
        double bottom = top + bounds.Height / scale;

        bool nearLeft = Math.Abs(point.X - left) <= HandleHitSize;
        bool nearRight = Math.Abs(point.X - right) <= HandleHitSize;
        bool nearTop = Math.Abs(point.Y - top) <= HandleHitSize;
        bool nearBottom = Math.Abs(point.Y - bottom) <= HandleHitSize;

        SelectionHandle handle = SelectionHandle.None;
        if (nearLeft)
        {
            handle |= SelectionHandle.Left;
        }

        if (nearRight)
        {
            handle |= SelectionHandle.Right;
        }

        if (nearTop)
        {
            handle |= SelectionHandle.Top;
        }

        if (nearBottom)
        {
            handle |= SelectionHandle.Bottom;
        }

        return handle;
    }

    private ScreenPoint ToScreenPoint(Point point)
    {
        double scale = RenderScaling <= 0 ? 1d : RenderScaling;
        return new ScreenPoint(
            (int)Math.Round(Position.X + point.X * scale),
            (int)Math.Round(Position.Y + point.Y * scale));
    }

    private void Complete(RegionSelectionResult result)
    {
        if (completionRaised)
        {
            return;
        }

        completionRaised = true;
        Hide();
        Completed?.Invoke(this, result);
        Close();
    }
}
