using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ScreenMind.Core.Capture;
using ScreenMind.Core.Settings;

namespace ScreenMind.UI.Overlay;

public sealed class CompactOverlayWindow : Window
{
    private readonly CompactOverlayViewModel viewModel;
    private readonly TextBlock statusLabel;
    private readonly TextBox responseTextBox;
    private readonly TextBlock errorLabel;
    private readonly Button cancelBtn;
    private readonly Button retryBtn;
    private readonly Button proceedBtn;
    private readonly Button abortBtn;
    private readonly Button copyBtn;
    private readonly Button expandBtn;
    private readonly Button closeBtn;
    private readonly Border mainBorder;
    private readonly LayoutTransformControl mainTransform;
    private static bool isPinned = true;

    // Same deep-space visual language as main workspace.
    private static readonly ISolidColorBrush BgBrush = new SolidColorBrush(Color.Parse("#F5080B14"));
    private static readonly ISolidColorBrush BorderBrushColor = new SolidColorBrush(Color.Parse("#27304A"));
    private static readonly ISolidColorBrush HeaderBgBrush = new SolidColorBrush(Color.Parse("#D20D111E"));
    private static readonly ISolidColorBrush TextMutedBrush = new SolidColorBrush(Color.Parse("#9AA4BC"));
    private static readonly ISolidColorBrush ErrorBgBrush = new SolidColorBrush(Color.FromArgb(50, 239, 68, 68));
    private static readonly ISolidColorBrush ErrorTextBrush = new SolidColorBrush(Color.FromArgb(255, 248, 113, 113));

    private static readonly ISolidColorBrush BtnBgNormal = new SolidColorBrush(Color.Parse("#1A2134"));
    private static readonly ISolidColorBrush BtnBgHover = new SolidColorBrush(Color.Parse("#252E47"));
    private static readonly ISolidColorBrush BtnBgActive = new SolidColorBrush(Color.Parse("#303A58"));
    private static readonly ISolidColorBrush AccentBgNormal = new SolidColorBrush(Color.Parse("#7168F6"));
    private static readonly ISolidColorBrush AccentBgHover = new SolidColorBrush(Color.Parse("#887FFF"));

    public CompactOverlayWindow(CompactOverlayViewModel viewModel)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;

        // Window properties
        Title = "ScreenMind Overlay";
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = true;
        Topmost = isPinned;
        ShowInTaskbar = false;
        Width = 560;
        Height = 460;
        MinWidth = 380;
        MinHeight = 280;
        Opacity = viewModel.OverlayOpacity;
        Background = Brushes.Transparent;
        TransparencyLevelHint =
        [
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Transparent,
            WindowTransparencyLevel.None,
        ];

        // Main layout container (with border and rounded corners)
        mainBorder = new Border
        {
            Background = BgBrush,
            BorderBrush = BorderBrushColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            ClipToBounds = true
        };
        mainTransform = new LayoutTransformControl
        {
            Child = mainBorder
        };
        ApplyUiScale();

        Grid mainGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("54,*,Auto")
        };
        mainBorder.Child = mainGrid;
        Content = mainTransform;

        // --- ROW 0: Header ---
        Grid headerGrid = new Grid
        {
            Background = HeaderBgBrush,
            ColumnDefinitions = new ColumnDefinitions("*,42,38")
        };
        headerGrid.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };

        statusLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 0, 0),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            Text = viewModel.StatusText
        };
        headerGrid.Children.Add(statusLabel);
        Grid.SetColumn(statusLabel, 0);

        Button topmostBtn = CreateButton("Pin", BtnBgNormal, Brushes.White, isHeaderClose: true, isRedHover: false);
        topmostBtn.FontSize = 10;
        topmostBtn.Opacity = Topmost ? 1 : 0.55;
        topmostBtn.Click += (_, _) =>
        {
            Topmost = !Topmost;
            isPinned = Topmost;
            topmostBtn.Opacity = Topmost ? 1 : 0.55;
        };
        headerGrid.Children.Add(topmostBtn);
        Grid.SetColumn(topmostBtn, 1);

        Button headerCloseBtn = CreateButton("✕", BtnBgNormal, Brushes.White, isHeaderClose: true, isRedHover: true);
        headerCloseBtn.Click += (s, e) => viewModel.Close();
        headerGrid.Children.Add(headerCloseBtn);
        Grid.SetColumn(headerCloseBtn, 2);

        mainGrid.Children.Add(headerGrid);
        Grid.SetRow(headerGrid, 0);

        // --- ROW 1: Content Area ---
        Grid contentGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(20)
        };

        // Scrollable AI Response Textbox
        responseTextBox = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            FontSize = 13,
            LineHeight = 19,
            AcceptsReturn = true,
            Padding = new Thickness(0),
            Text = viewModel.ResponseText
        };

        ScrollViewer scrollViewer = new ScrollViewer
        {
            Content = responseTextBox,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        contentGrid.Children.Add(scrollViewer);
        Grid.SetRow(scrollViewer, 0);

        // Error message label panel
        errorLabel = new TextBlock
        {
            Foreground = ErrorTextBrush,
            Background = ErrorBgBrush,
            Padding = new Thickness(8),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            IsVisible = !string.IsNullOrEmpty(viewModel.ErrorMessage),
            Text = viewModel.ErrorMessage
        };
        contentGrid.Children.Add(errorLabel);
        Grid.SetRow(errorLabel, 1);

        mainGrid.Children.Add(contentGrid);
        Grid.SetRow(contentGrid, 1);

        // --- ROW 2: Footer / Actions ---
        Border footerBorder = new Border
        {
            BorderBrush = BorderBrushColor,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(18, 14)
        };

        Grid footerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };
        footerBorder.Child = footerGrid;

        // Left Actions: Cancel / Retry
        StackPanel leftStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        cancelBtn = CreateButton("Cancel", BtnBgNormal, Brushes.White);
        cancelBtn.Click += (s, e) => viewModel.Cancel();
        leftStack.Children.Add(cancelBtn);

        retryBtn = CreateButton("Retry", AccentBgNormal, Brushes.White);
        retryBtn.Click += async (s, e) => await viewModel.RetryAsync();
        leftStack.Children.Add(retryBtn);

        proceedBtn = CreateButton("Proceed", AccentBgNormal, Brushes.White);
        proceedBtn.Click += (s, e) => viewModel.ApprovePrivacyWarning();
        leftStack.Children.Add(proceedBtn);

        abortBtn = CreateButton("Abort", BtnBgNormal, Brushes.White);
        abortBtn.Click += (s, e) => viewModel.RejectPrivacyWarning();
        leftStack.Children.Add(abortBtn);

        footerGrid.Children.Add(leftStack);
        Grid.SetColumn(leftStack, 0);

        // Right Actions: Copy, Expand, Close
        StackPanel rightStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        copyBtn = CreateButton("Copy", BtnBgNormal, Brushes.White);
        copyBtn.Click += async (s, e) => await viewModel.CopyToClipboardAsync();
        rightStack.Children.Add(copyBtn);

        expandBtn = CreateButton("Expand", BtnBgNormal, Brushes.White);
        expandBtn.Click += (s, e) => viewModel.Expand();
        rightStack.Children.Add(expandBtn);

        closeBtn = CreateButton("Close", BtnBgNormal, Brushes.White);
        closeBtn.Click += (s, e) => viewModel.Close();
        rightStack.Children.Add(closeBtn);

        footerGrid.Children.Add(rightStack);
        Grid.SetColumn(rightStack, 2);

        mainGrid.Children.Add(footerBorder);
        Grid.SetRow(footerBorder, 2);

        // Events & Property bindings
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.CloseRequested += OnCloseRequested;

        UpdateControlsState();
    }

    private static Button CreateButton(string text, ISolidColorBrush background, ISolidColorBrush foreground, bool isHeaderClose = false, bool isRedHover = false)
    {
        Button btn = new Button
        {
            Content = text,
            Foreground = foreground,
            Background = background,
            BorderThickness = new Thickness(0),
            FontSize = isHeaderClose ? 14 : 12,
            FontWeight = FontWeight.Medium,
            Padding = isHeaderClose ? new Thickness(0) : new Thickness(12, 6),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = isHeaderClose ? new CornerRadius(0) : new CornerRadius(9)
        };

        if (isHeaderClose)
        {
            btn.Width = 38;
            btn.Height = 38;
        }

        // Elegant hover animations in C# code
        btn.PointerEntered += (s, e) =>
        {
            if (isRedHover)
            {
                btn.Background = new SolidColorBrush(Color.FromArgb(255, 220, 38, 38));
            }
            else
            {
                btn.Background = ReferenceEquals(background, AccentBgNormal) ? AccentBgHover : BtnBgHover;
            }
        };

        btn.PointerExited += (s, e) =>
        {
            btn.Background = background;
        };

        btn.PointerPressed += (s, e) =>
        {
            if (!isHeaderClose && !ReferenceEquals(background, AccentBgNormal))
            {
                btn.Background = BtnBgActive;
            }
        };

        return btn;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Must run on UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (e.PropertyName == nameof(CompactOverlayViewModel.StatusText))
            {
                statusLabel.Text = viewModel.StatusText;
            }
            else if (e.PropertyName == nameof(CompactOverlayViewModel.ResponseText))
            {
                responseTextBox.Text = viewModel.ResponseText;
            }
            else if (e.PropertyName == nameof(CompactOverlayViewModel.ErrorMessage))
            {
                errorLabel.Text = viewModel.ErrorMessage;
                errorLabel.IsVisible = !string.IsNullOrEmpty(viewModel.ErrorMessage);
            }
            else if (e.PropertyName == nameof(CompactOverlayViewModel.OverlayOpacity))
            {
                Opacity = viewModel.OverlayOpacity;
            }
            else if (e.PropertyName == nameof(CompactOverlayViewModel.UiScale))
            {
                ApplyUiScale();
            }

            UpdateControlsState();
        });
    }

    private void ApplyUiScale()
    {
        double scale = double.IsFinite(viewModel.UiScale)
            ? Math.Clamp(viewModel.UiScale, UiSettings.MinUiScale, UiSettings.MaxUiScale)
            : 1d;

        mainTransform.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private void UpdateControlsState()
    {
        bool warningActive = viewModel.IsPrivacyWarningActive;
        proceedBtn.IsVisible = warningActive;
        abortBtn.IsVisible = warningActive;

        cancelBtn.IsVisible = !warningActive && viewModel.CanCancel;
        retryBtn.IsVisible = !warningActive && viewModel.CanRetry;
        copyBtn.IsEnabled = !warningActive && viewModel.CanCopy;
        expandBtn.IsEnabled = !warningActive;
        closeBtn.IsEnabled = !warningActive;
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.CloseRequested -= OnCloseRequested;
        Close();
    }
}
