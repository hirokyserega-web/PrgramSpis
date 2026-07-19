using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ScreenMind.Core.Ai;
using ScreenMind.Core.Imaging;
using ScreenMind.Core.Settings;
using ScreenMind.Core.Hotkeys;

namespace ScreenMind.UI.Chat;

public sealed class ChatWindow : Window, IDisposable
{
    private readonly ChatViewModel viewModel;
    private readonly ListBox sessionsListBox;
    private readonly StackPanel chatHistoryPanel;
    private readonly ScrollViewer chatScrollViewer;
    private readonly TextBox messageInput;
    private readonly Button sendBtn;
    private readonly Border errorBanner;
    private readonly TextBlock errorText;
    private readonly Border rootBorder;
    private readonly LayoutTransformControl rootTransform;
    private Border? composerToolsPanel;
    private Border? chatComposer;
    private Grid? chatMainGrid;
    private Button? cleanChatToggleBtn;
    private Control? chatTitleBar;
    private Bitmap? activePreview;
    private string? activePreviewSessionId;
    private ScreenImage? activePreviewImage;
    private string? renderedSessionId;
    private int renderedMessageCount;
    private bool isUpdatingSettings;

    // Settings panel controls
    private readonly Grid mainGrid;
    private readonly Grid sidebarGrid;
    private readonly Border settingsOverlay;
    private readonly Border privacyWarningOverlay;
    private readonly ComboBox themeCombo;
    private readonly Slider opacitySlider;
    private readonly Slider uiScaleSlider;
    private readonly CheckBox alwaysOnTopCheck;
    private readonly CheckBox clickThroughCheck;
    private readonly CheckBox showConsoleCheck;
    private readonly CheckBox hideSidebarCheck;
    private readonly CheckBox cleanChatModeCheck;
    private readonly TextBox systemPromptInput;
    private readonly ComboBox formatCombo;
    private readonly TextBox payloadInput;
    private readonly CheckBox cursorCheck;
    private readonly TextBox openAiKeyInput;
    private readonly TextBox anthropicKeyInput;
    private readonly TextBox geminiKeyInput;
    private readonly CheckBox warnCheck;
    private readonly TextBox blockedProcessInput;
    private readonly TextBox blockedTitleInput;
    private readonly TextBlock regionHotkeyDisplay;
    private readonly TextBlock activeWindowHotkeyDisplay;
    private readonly TextBlock monitorHotkeyDisplay;
    private readonly TextBlock cleanChatHotkeyDisplay;
    private readonly TextBlock clickThroughHotkeyDisplay;
    private readonly TextBlock emergencyExitHotkeyDisplay;
    private string? capturingHotkeySlot;
    private Button? activeCaptureButton;
    private readonly Dictionary<string, (TextBlock Display, Func<string> Getter, Action<string> Setter)> hotkeySlots = new();
    private readonly ComboBox profileCombo;
    private readonly ComboBox providerCombo;
    private readonly ComboBox modelCombo;
    private readonly TextBox customModelInput;
    private readonly TextBlock customModelLabel;
    private readonly TextBox qwenBaseUrlInput;
    private readonly TextBox qwenCookieInput;
    private readonly CheckBox silentModeCheck;
    private readonly TextBox defaultPromptInput;
    private readonly CheckBox keepSessionHistoryCheck;
    private readonly TextBox prompt1Input;
    private readonly TextBlock prompt1HotkeyDisplay;
    private readonly TextBox prompt2Input;
    private readonly TextBlock prompt2HotkeyDisplay;
    private readonly TextBox prompt3Input;
    private readonly TextBlock prompt3HotkeyDisplay;

    // Managed background proxies UI
    private readonly CheckBox qwenProxyCheck;
    private readonly TextBox qwenProxyPortInput;
    private readonly TextBox qwenProxyCookieInput;
    private readonly Button qwenInstallBtn;
    private readonly Button qwenAuthBtn;
    private readonly TextBlock qwenStatusText;
    private readonly Button qwenStartBtn;
    private readonly Button qwenStopBtn;

    private readonly CheckBox deepseekProxyCheck;
    private readonly TextBox deepseekProxyPortInput;
    private readonly TextBox deepseekProxyCookieInput;
    private readonly Button deepseekInstallBtn;
    private readonly Button deepseekAuthBtn;
    private readonly TextBlock deepseekStatusText;
    private readonly Button deepseekStartBtn;
    private readonly Button deepseekStopBtn;

    private readonly CheckBox kimiProxyCheck;
    private readonly TextBox kimiProxyPortInput;
    private readonly TextBox kimiProxyCookieInput;
    private readonly Button kimiInstallBtn;
    private readonly Button kimiAuthBtn;
    private readonly TextBlock kimiStatusText;
    private readonly Button kimiStartBtn;
    private readonly Button kimiStopBtn;

    // Mutable surface brushes — alpha follows Overlay Opacity so the whole window is
    // truly see-through (Window.Opacity would also fade text; we keep text at full opacity).
    private readonly SolidColorBrush WindowBg = new(Color.FromArgb(200, 9, 13, 20));
    private readonly SolidColorBrush SidebarBg = new(Color.FromArgb(170, 16, 21, 31));
    private readonly SolidColorBrush PanelBg = new(Color.FromArgb(140, 16, 22, 33));
    private readonly SolidColorBrush CardBg = new(Color.FromArgb(110, 17, 23, 34));
    private readonly SolidColorBrush BorderColor = new(Color.FromArgb(110, 91, 103, 122));
    private readonly SolidColorBrush DividerColor = new(Color.FromArgb(70, 120, 132, 148));
    private readonly SolidColorBrush HeaderBg = new(Color.FromArgb(90, 20, 26, 38));
    private readonly SolidColorBrush AccentColor = new(Color.Parse("#5B72FF"));
    private readonly SolidColorBrush AccentColorHover = new(Color.Parse("#7185FF"));
    private readonly SolidColorBrush UserBubbleBg = new(Color.FromArgb(220, 82, 105, 246));
    private readonly SolidColorBrush AssistantBubbleBg = new(Color.FromArgb(120, 16, 22, 32));
    private readonly SolidColorBrush InputBg = new(Color.FromArgb(150, 18, 24, 34));

    private const byte WindowBgMaxAlpha = 190;
    private const byte SidebarBgMaxAlpha = 160;
    private const byte PanelBgMaxAlpha = 120;
    private const byte CardBgMaxAlpha = 110;
    private const byte HeaderBgMaxAlpha = 90;
    private const byte InputBgMaxAlpha = 140;
    // Bubbles must stay glass-like; high alpha here made the window look solid after send.
    private const byte AssistantBubbleMaxAlpha = 70;
    private const byte UserBubbleMaxAlpha = 110;
    private const byte BorderMaxAlpha = 110;
    private const byte DividerMaxAlpha = 70;

    private Border? conversationSurface;

    public ChatViewModel ViewModel => viewModel;

    public void Dispose()
    {
        activePreview?.Dispose();
        activePreview = null;
        GC.SuppressFinalize(this);
    }

    private static void ShutdownApplication()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        // Finish window teardown, then stop the Avalonia loop (and therefore Program.cs host).
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                desktop.Shutdown();
            }
            catch (InvalidOperationException)
            {
                // Already shutting down.
            }
        });
    }

    public ChatWindow(ChatViewModel viewModel)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;

        // Window config
        Title = "ScreenMind — visual AI workspace";
        SystemDecorations = SystemDecorations.Full;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = true;
        ShowInTaskbar = false;
        Width = 960;
        Height = 650;
        MinWidth = 350;
        MinHeight = 300;
        Topmost = viewModel.AlwaysOnTop;
        // Keep window Opacity at 1 so text stays sharp; surface alpha is applied separately.
        Opacity = 1;
        Background = Brushes.Transparent;
        // Prefer true transparency so desktop shows through (Mica/Acrylic hide that).
        TransparencyLevelHint =
        [
            WindowTransparencyLevel.Transparent,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.None,
        ];

        // Root Border
        rootBorder = new Border
        {
            Background = WindowBg,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true
        };
        ApplySurfaceOpacity(viewModel.OverlayOpacity);
        rootTransform = new LayoutTransformControl
        {
            Child = rootBorder
        };

        mainGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("292,*,*")
        };
        rootBorder.Child = mainGrid;
        Content = rootTransform;

        // Left Panel: Sidebar
        sidebarGrid = new Grid
        {
            Background = SidebarBg,
            RowDefinitions = new RowDefinitions("68,Auto,*,64")
        };

        Border sidebarHeaderBorder = new Border
        {
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(18, 0)
        };
        TextBlock sidebarTitle = new TextBlock
        {
            Text = "✦  ScreenMind",
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            FontSize = 19,
            VerticalAlignment = VerticalAlignment.Center
        };
        sidebarHeaderBorder.Child = sidebarTitle;
        sidebarGrid.Children.Add(sidebarHeaderBorder);
        Grid.SetRow(sidebarHeaderBorder, 0);

        StackPanel captureActions = new()
        {
            Spacing = 8,
            Margin = new Thickness(14, 16, 14, 10),
        };
        Button regionCaptureBtn = CreateButton("＋  Select area", AccentColor, Brushes.White);
        regionCaptureBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        regionCaptureBtn.Click += (_, _) => viewModel.RequestNewCapture();
        captureActions.Children.Add(regionCaptureBtn);

        Button windowCaptureBtn = CreateButton("▣  Active window", BtnBgNormal(), Brushes.White);
        windowCaptureBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        windowCaptureBtn.Click += (_, _) => viewModel.RequestActiveWindowCapture();
        captureActions.Children.Add(windowCaptureBtn);

        Button monitorCaptureBtn = CreateButton("▤  Current monitor", BtnBgNormal(), Brushes.White);
        monitorCaptureBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        monitorCaptureBtn.Click += (_, _) => viewModel.RequestMonitorCapture();
        captureActions.Children.Add(monitorCaptureBtn);

        sidebarGrid.Children.Add(captureActions);
        Grid.SetRow(captureActions, 1);

        sessionsListBox = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(6, 4),
            ItemTemplate = new FuncDataTemplate<ChatSession>((session, _) =>
            {
                Border border = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6),
                    Margin = new Thickness(0, 1),
                    Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255))
                };

                Grid grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
                };

                TextBlock icon = new TextBlock
                {
                    Text = "💬",
                    Foreground = AccentColorHover,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12
                };
                Grid.SetColumn(icon, 0);
                grid.Children.Add(icon);

                TextBlock title = new TextBlock
                {
                    Text = session.Profile.DisplayName,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeight.Medium,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(title, 1);
                grid.Children.Add(title);

                Button deleteBtn = new Button
                {
                    Content = "✕",
                    Foreground = Brushes.Gray,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(4, 2),
                    FontSize = 9,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    VerticalAlignment = VerticalAlignment.Center
                };
                deleteBtn.Click += (s, e) =>
                {
                    e.Handled = true;
                    viewModel.DeleteSession(session);
                };
                Grid.SetColumn(deleteBtn, 2);
                grid.Children.Add(deleteBtn);

                border.Child = grid;
                return border;
            })
        };
        sessionsListBox.SelectionChanged += OnSessionSelectionChanged;
        sidebarGrid.Children.Add(sessionsListBox);
        Grid.SetRow(sessionsListBox, 2);

        Grid sidebarFooterGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Margin = new Thickness(8)
        };
        Button settingsBtn = CreateButton("⚙ Settings", BtnBgNormal(), Brushes.White);
        settingsBtn.Click += async (s, e) => await viewModel.LoadSettingsAsync();
        Grid.SetColumn(settingsBtn, 0);
        sidebarFooterGrid.Children.Add(settingsBtn);

        Button clearHistoryBtn = CreateButton("🗑 Clear all", BtnBgNormal(), Brushes.White);
        clearHistoryBtn.Margin = new Thickness(8, 0, 0, 0);
        clearHistoryBtn.Click += (s, e) => viewModel.ClearAllHistory();
        Grid.SetColumn(clearHistoryBtn, 1);
        sidebarFooterGrid.Children.Add(clearHistoryBtn);

        sidebarGrid.Children.Add(sidebarFooterGrid);
        Grid.SetRow(sidebarFooterGrid, 3);

        RebuildModernSidebar(sidebarGrid);
        mainGrid.Children.Add(sidebarGrid);
        Grid.SetColumn(sidebarGrid, 0);

        // Right Panel: Active Chat
        Grid chatGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("68,Auto,*,Auto")
        };
        ApplyUiScale();
        Grid.SetColumnSpan(chatGrid, 2); // spans center and right initially

        // Chat Header
        Grid headerGrid = new Grid
        {
            Background = HeaderBg,
            ColumnDefinitions = new ColumnDefinitions("Auto,*,45,45")
        };
        headerGrid.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };

        Button toggleSidebarBtn = CreateButton("☰", BtnBgNormal(), Brushes.White);
        toggleSidebarBtn.Width = 36;
        toggleSidebarBtn.Height = 36;
        toggleSidebarBtn.Margin = new Thickness(12, 0, 0, 0);
        toggleSidebarBtn.Click += (s, e) =>
        {
            sidebarGrid.IsVisible = !sidebarGrid.IsVisible;
            mainGrid.ColumnDefinitions[0] = new ColumnDefinition(sidebarGrid.IsVisible ? 292 : 0, GridUnitType.Pixel);
        };
        Grid.SetColumn(toggleSidebarBtn, 0);
        headerGrid.Children.Add(toggleSidebarBtn);

        TextBlock titleText = new TextBlock
        {
            Text = "Screen assistant",
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            FontSize = 16,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        headerGrid.Children.Add(titleText);
        Grid.SetColumn(titleText, 1);

        Button headerSettingsBtn = CreateButton("⚙", BtnBgNormal(), Brushes.White);
        headerSettingsBtn.Width = 36;
        headerSettingsBtn.Height = 36;
        headerSettingsBtn.Click += async (s, e) => await viewModel.LoadSettingsAsync();
        headerGrid.Children.Add(headerSettingsBtn);
        Grid.SetColumn(headerSettingsBtn, 2);

        Button windowCloseBtn = CreateButton("✕", BtnBgNormal(), Brushes.White, isClose: true);
        windowCloseBtn.Click += (s, e) => viewModel.CloseChat();
        headerGrid.Children.Add(windowCloseBtn);
        Grid.SetColumn(windowCloseBtn, 3);

        chatGrid.Children.Add(headerGrid);
        Grid.SetRow(headerGrid, 0);

        errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#FCA5A5")),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        errorBanner = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(48, 239, 68, 68)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(96, 248, 113, 113)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(22, 14, 22, 0),
            Padding = new Thickness(12, 9),
            IsVisible = false,
            Child = errorText
        };
        chatGrid.Children.Add(errorBanner);
        Grid.SetRow(errorBanner, 1);

        // Chat History Scroll
        chatHistoryPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12,
            Margin = new Thickness(28, 24)
        };
        chatScrollViewer = new ScrollViewer
        {
            Content = chatHistoryPanel,
            Background = Brushes.Transparent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        chatHistoryPanel.Background = Brushes.Transparent;
        chatGrid.Children.Add(chatScrollViewer);
        Grid.SetRow(chatScrollViewer, 2);

        // Chat Input Area
        Border inputAreaBorder = new Border
        {
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(22, 18)
        };
        Grid inputGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto")
        };
        messageInput = new TextBox
        {
            Watermark = "Ask something about the screen...",
            Background = InputBg,
            BorderBrush = BorderColor,
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(12),
            AcceptsReturn = true,
            MinHeight = 42,
            MaxHeight = 130,
            Padding = new Thickness(15, 11)
        };
        messageInput.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                viewModel.InputText = messageInput.Text ?? string.Empty;
                if (viewModel.CanSend)
                {
                    _ = viewModel.SendMessageAsync();
                }
            }
        };
        messageInput.TextChanged += (s, e) =>
        {
            viewModel.InputText = messageInput.Text ?? string.Empty;
        };
        inputGrid.Children.Add(messageInput);
        Grid.SetColumn(messageInput, 0);

        Button attachBtn = CreateButton("📎", BtnBgNormal(), Brushes.White);
        attachBtn.Margin = new Thickness(8, 0, 0, 0);
        attachBtn.Click += async (s, e) =>
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;

            IReadOnlyList<Avalonia.Platform.Storage.IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select Image to Analyze",
                AllowMultiple = false,
                FileTypeFilter = new[] { Avalonia.Platform.Storage.FilePickerFileTypes.ImageAll }
            });
            if (files.Count > 0)
            {
                using Stream stream = await files[0].OpenReadAsync();
                using MemoryStream ms = new System.IO.MemoryStream();
                await stream.CopyToAsync(ms);
                byte[] bytes = ms.ToArray();

                (string mediaType, ScreenImageFormat format) = DetectImageFormat(bytes);
                using MemoryStream dimensionsStream = new(bytes, writable: false);
                using Bitmap decoded = new(dimensionsStream);
                ScreenImage screenImage = new(
                    bytes,
                    mediaType,
                    format,
                    decoded.PixelSize.Width,
                    decoded.PixelSize.Height,
                    DateTimeOffset.UtcNow);
                await viewModel.CreateSessionFromImageAsync(screenImage, CancellationToken.None);
            }
        };
        inputGrid.Children.Add(attachBtn);
        Grid.SetColumn(attachBtn, 1);

        sendBtn = CreateButton("Send  ↑", AccentColor, Brushes.White);
        sendBtn.MinWidth = 88;
        sendBtn.Margin = new Thickness(8, 0, 0, 0);
        sendBtn.Click += async (s, e) =>
        {
            viewModel.InputText = messageInput.Text ?? string.Empty;
            await viewModel.SendMessageAsync();
        };
        inputGrid.Children.Add(sendBtn);
        Grid.SetColumn(sendBtn, 2);

        inputAreaBorder.Child = inputGrid;
        chatGrid.Children.Add(inputAreaBorder);
        Grid.SetRow(inputAreaBorder, 3);

        RebuildModernChat(chatGrid);
        mainGrid.Children.Add(chatGrid);
        Grid.SetColumn(chatGrid, 1);

        // Settings Overlay panel
        settingsOverlay = new Border
        {
            Background = WindowBg,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1, 0, 0, 0),
            IsVisible = false,
            Opacity = 0,
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(200),
                    Easing = new Avalonia.Animation.Easings.CubicEaseOut()
                }
            }
        };
        Grid.SetColumn(settingsOverlay, 1);
        Grid.SetColumnSpan(settingsOverlay, 2);

        Grid settingsGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("45,*,55"),
            Margin = new Thickness(16)
        };

        TextBlock settingsTitle = new TextBlock
        {
            Text = "Settings",
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center
        };
        settingsGrid.Children.Add(settingsTitle);
        Grid.SetRow(settingsTitle, 0);

        StackPanel aiPanel = CreateSettingsPanel();
        StackPanel appearancePanel = CreateSettingsPanel();
        StackPanel capturePanel = CreateSettingsPanel();
        StackPanel providersPanel = CreateSettingsPanel();
        StackPanel proxiesPanel = CreateSettingsPanel();
        StackPanel privacyPanel = CreateSettingsPanel();
        StackPanel hotkeysPanel = CreateSettingsPanel();

        TabControl settingsTabs = new TabControl
        {
            ItemsSource = new[]
            {
                CreateSettingsTab("AI", aiPanel),
                CreateSettingsTab("Appearance", appearancePanel),
                CreateSettingsTab("Capture", capturePanel),
                CreateSettingsTab("Providers", providersPanel),
                CreateSettingsTab("Proxies", proxiesPanel),
                CreateSettingsTab("Privacy", privacyPanel),
                CreateSettingsTab("Hotkeys", hotkeysPanel),
            },
        };
        settingsGrid.Children.Add(settingsTabs);
        Grid.SetRow(settingsTabs, 1);

        StackPanel scrollPanel = aiPanel;

        // Model / Profile Selector
        scrollPanel.Children.Add(CreateHeaderLabel("🤖 Chat Focus / Mode"));
        profileCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = Brushes.White,
            Background = InputBg,
        };
        scrollPanel.Children.Add(profileCombo);

        scrollPanel.Children.Add(CreateHeaderLabel("🔌 AI Model Provider"));
        providerCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = Brushes.White,
            Background = InputBg,
        };
        scrollPanel.Children.Add(providerCombo);

        scrollPanel.Children.Add(CreateHeaderLabel("🧠 Selected Model"));
        modelCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = Brushes.White,
            Background = InputBg,
        };
        scrollPanel.Children.Add(modelCombo);

        customModelLabel = CreateHeaderLabel("✍️ Custom Model ID");
        customModelInput = new TextBox
        {
            Watermark = "e.g. gpt-4o-2024-08-06",
            Foreground = Brushes.White,
            Background = InputBg,
        };
        scrollPanel.Children.Add(customModelLabel);
        scrollPanel.Children.Add(customModelInput);

        // Wire up immediate interactive events
        providerCombo.SelectionChanged += (s, e) =>
        {
            if (isUpdatingSettings) return;
            if (providerCombo.SelectedIndex >= 0)
            {
                viewModel.SelectedProviderIndex = providerCombo.SelectedIndex;
                modelCombo.ItemsSource = viewModel.AvailableModels;
                modelCombo.SelectedIndex = viewModel.SelectedModelIndex;
            }
        };

        modelCombo.SelectionChanged += (s, e) =>
        {
            if (isUpdatingSettings) return;
            if (modelCombo.SelectedIndex >= 0)
            {
                viewModel.SelectedModelIndex = modelCombo.SelectedIndex;
                customModelLabel.IsVisible = viewModel.IsCustomModelVisible;
                customModelInput.IsVisible = viewModel.IsCustomModelVisible;
            }
        };

        customModelInput.TextChanged += (s, e) =>
        {
            if (isUpdatingSettings) return;
            viewModel.CustomModelName = customModelInput.Text ?? string.Empty;
        };

        profileCombo.SelectionChanged += (s, e) =>
        {
            if (isUpdatingSettings) return;
            if (profileCombo.SelectedIndex >= 0)
            {
                viewModel.SelectedProfileIndex = profileCombo.SelectedIndex;
                providerCombo.SelectedIndex = viewModel.SelectedProviderIndex;
                modelCombo.ItemsSource = viewModel.AvailableModels;
                modelCombo.SelectedIndex = viewModel.SelectedModelIndex;
                customModelInput.Text = viewModel.CustomModelName;
                customModelLabel.IsVisible = viewModel.IsCustomModelVisible;
                customModelInput.IsVisible = viewModel.IsCustomModelVisible;
            }
        };

        scrollPanel.Children.Add(new Border
        {
            Height = 1,
            Background = BorderColor,
            Margin = new Thickness(0, 8, 0, 4)
        });

        scrollPanel = appearancePanel;

        // Theme combobox
        scrollPanel.Children.Add(CreateHeaderLabel("Theme"));
        themeCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 12) };
        themeCombo.ItemsSource = new[] { "system", "dark", "light" };
        scrollPanel.Children.Add(themeCombo);

        // Opacity — live preview of real window transparency (not just settings panel).
        scrollPanel.Children.Add(CreateHeaderLabel("Window transparency (0 = glass, 1 = solid)"));
        opacitySlider = new Slider
        {
            Minimum = UiSettings.MinOverlayOpacity,
            Maximum = UiSettings.MaxOverlayOpacity,
            TickFrequency = 0.05,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 0, 0, 12)
        };
        opacitySlider.PropertyChanged += (_, e) =>
        {
            if (isUpdatingSettings || e.Property != Slider.ValueProperty)
            {
                return;
            }

            double value = opacitySlider.Value;
            viewModel.OverlayOpacity = value;
            ApplySurfaceOpacity(value);
        };
        scrollPanel.Children.Add(opacitySlider);

        scrollPanel.Children.Add(CreateHeaderLabel("UI Scale"));
        uiScaleSlider = new Slider
        {
            Minimum = UiSettings.MinUiScale,
            Maximum = UiSettings.MaxUiScale,
            TickFrequency = 0.05,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 0, 0, 12)
        };
        scrollPanel.Children.Add(uiScaleSlider);

        alwaysOnTopCheck = new CheckBox { Content = "Keep ScreenMind above other windows", Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 12) };
        alwaysOnTopCheck.IsCheckedChanged += (_, _) => Topmost = alwaysOnTopCheck.IsChecked ?? true;
        scrollPanel.Children.Add(alwaysOnTopCheck);

        clickThroughCheck = new CheckBox { Content = "Click-through mode (ignore mouse interactions)", Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 12) };
        scrollPanel.Children.Add(clickThroughCheck);

        showConsoleCheck = new CheckBox { Content = "Show debug console at startup", Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 12) };
        scrollPanel.Children.Add(showConsoleCheck);

        hideSidebarCheck = new CheckBox { Content = "Hide sidebar (chat list) by default", Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 12) };
        scrollPanel.Children.Add(hideSidebarCheck);

        cleanChatModeCheck = new CheckBox
        {
            Content = "Clean chat: hide input box + send button (only messages & AI reply)",
            Foreground = Brushes.White,
            Margin = new Thickness(0, 4, 0, 12)
        };
        scrollPanel.Children.Add(cleanChatModeCheck);

        scrollPanel = aiPanel;
        scrollPanel.Children.Add(CreateHeaderLabel("System prompt for selected profile"));
        systemPromptInput = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 110,
            MaxHeight = 220,
            Watermark = "Instructions applied to text and screenshot requests",
            Margin = new Thickness(0, 0, 0, 12)
        };
        scrollPanel.Children.Add(systemPromptInput);

        scrollPanel = capturePanel;

        // Capture Format
        scrollPanel.Children.Add(CreateHeaderLabel("Default Image Format"));
        formatCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 12) };
        formatCombo.ItemsSource = Enum.GetValues<ScreenImageFormat>();
        scrollPanel.Children.Add(formatCombo);

        // Max Payload Size
        scrollPanel.Children.Add(CreateHeaderLabel("Max Payload Limit (bytes)"));
        payloadInput = new TextBox { Watermark = "8388608", Margin = new Thickness(0, 0, 0, 12) };
        scrollPanel.Children.Add(payloadInput);

        // Include Cursor Check
        cursorCheck = new CheckBox { Content = "Include mouse cursor in captures", Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 12) };
        scrollPanel.Children.Add(cursorCheck);

        // Silent Capture Mode Check
        silentModeCheck = new CheckBox { Content = "Silent capture mode (send straight to chat via hotkeys without overlay)", Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 12) };
        scrollPanel.Children.Add(silentModeCheck);

        // Default Prompt for Silent Capture
        scrollPanel.Children.Add(CreateHeaderLabel("Default Prompt for Silent Captures (e.g. Что на экране? / Explain this)"));
        defaultPromptInput = new TextBox { Watermark = "What is on my screen?", Margin = new Thickness(0, 0, 0, 12) };
        scrollPanel.Children.Add(defaultPromptInput);

        keepSessionHistoryCheck = new CheckBox { Content = "Remember history of screenshots and queries", Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 12) };
        scrollPanel.Children.Add(keepSessionHistoryCheck);

        scrollPanel = providersPanel;

        // API Keys (Secrets)
        scrollPanel.Children.Add(CreateHeaderLabel("OpenAI API Key"));
        openAiKeyInput = new TextBox { PasswordChar = '*', Watermark = "sk-...", Margin = new Thickness(0, 0, 0, 12) };
        scrollPanel.Children.Add(openAiKeyInput);

        scrollPanel.Children.Add(CreateHeaderLabel("Anthropic API Key"));
        anthropicKeyInput = new TextBox { PasswordChar = '*', Watermark = "sk-ant-...", Margin = new Thickness(0, 0, 0, 12) };
        scrollPanel.Children.Add(anthropicKeyInput);

        scrollPanel.Children.Add(CreateHeaderLabel("Gemini API Key"));
        geminiKeyInput = new TextBox { PasswordChar = '*', Watermark = "AIzaSy...", Margin = new Thickness(0, 0, 0, 12) };
        scrollPanel.Children.Add(geminiKeyInput);

        scrollPanel.Children.Add(new Border
        {
            Height = 1,
            Background = BorderColor,
            Margin = new Thickness(0, 8, 0, 4)
        });

        // FreeQwenApi / OpenAI-Compatible section
        scrollPanel.Children.Add(CreateHeaderLabel("🌐 FreeQwenApi / OpenAI-Compatible Base URL"));
        qwenBaseUrlInput = new TextBox { Watermark = "http://localhost:3264/api", Margin = new Thickness(0, 0, 0, 12) };
        scrollPanel.Children.Add(qwenBaseUrlInput);

        scrollPanel.Children.Add(CreateHeaderLabel("FreeQwenApi Cookie (optional, from browser DevTools)"));
        qwenCookieInput = new TextBox { PasswordChar = '*', Watermark = "cna=...; acw_tc=...", AcceptsReturn = false, Margin = new Thickness(0, 0, 0, 12) };
        scrollPanel.Children.Add(qwenCookieInput);

        scrollPanel.Children.Add(new Border
        {
            Height = 1,
            Background = BorderColor,
            Margin = new Thickness(0, 8, 0, 4)
        });

        scrollPanel = proxiesPanel;

        // Section: Managed Background Proxies
        scrollPanel.Children.Add(CreateHeaderLabel("🤖 MANAGED BACKGROUND PROXIES (Autoinstaller & Autostarter)"));

        TextBlock managedInfo = new TextBlock
        {
            Text = "Enable proxies to use free web accounts directly. First click Install, then Authenticate (log in in the browser window), paste the Cookie if needed, then click Enable and Save.",
            Foreground = TextMutedBrush(),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        scrollPanel.Children.Add(managedInfo);

        // Qwen Proxy
        scrollPanel.Children.Add(new TextBlock { Text = "FreeQwenApi (Qwen Chat)", Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 12 });
        qwenProxyCheck = new CheckBox { Content = "Enable Qwen Background Proxy", Foreground = Brushes.White };
        scrollPanel.Children.Add(qwenProxyCheck);

        Grid qwenGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*,Auto,Auto"), Margin = new Thickness(0, 2) };
        qwenProxyPortInput = new TextBox { Watermark = "Port: 3264", Margin = new Thickness(0, 0, 4, 0) };
        qwenProxyCookieInput = new TextBox { PasswordChar = '*', Watermark = "Cookie (Optional)", Margin = new Thickness(0, 0, 4, 0) };
        qwenInstallBtn = CreateButton("Install", BtnBgNormal(), Brushes.White);
        qwenInstallBtn.Click += async (s, e) => await viewModel.InstallProxyAsync("FreeQwenApi");
        qwenAuthBtn = CreateButton("Authenticate", AccentColor, Brushes.White);
        qwenAuthBtn.Margin = new Thickness(4, 0, 0, 0);
        qwenAuthBtn.Click += async (s, e) => await viewModel.AuthenticateProxyAsync("FreeQwenApi");

        qwenGrid.Children.Add(qwenProxyPortInput); Grid.SetColumn(qwenProxyPortInput, 0);
        qwenGrid.Children.Add(qwenProxyCookieInput); Grid.SetColumn(qwenProxyCookieInput, 1);
        qwenGrid.Children.Add(qwenInstallBtn); Grid.SetColumn(qwenInstallBtn, 2);
        qwenGrid.Children.Add(qwenAuthBtn); Grid.SetColumn(qwenAuthBtn, 3);
        scrollPanel.Children.Add(qwenGrid);

        SolidColorBrush greenBrush = new SolidColorBrush(Color.Parse("#22C55E"));
        SolidColorBrush redBrush = new SolidColorBrush(Color.Parse("#EF4444"));

        Grid qwenStatusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 2, 0, 8) };
        qwenStatusText = new TextBlock { Foreground = TextMutedBrush(), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        qwenStartBtn = CreateButton("Start", greenBrush, Brushes.White);
        qwenStartBtn.Margin = new Thickness(0, 0, 4, 0);
        qwenStartBtn.Click += async (s, e) =>
        {
            ApplySettingsToViewModel();
            await viewModel.StartProxyManualAsync("FreeQwenApi");
        };
        qwenStopBtn = CreateButton("Stop", redBrush, Brushes.White);
        qwenStopBtn.Click += async (s, e) =>
        {
            await viewModel.StopProxyManualAsync("FreeQwenApi");
        };
        qwenStatusGrid.Children.Add(qwenStatusText); Grid.SetColumn(qwenStatusText, 0);
        qwenStatusGrid.Children.Add(qwenStartBtn); Grid.SetColumn(qwenStartBtn, 1);
        qwenStatusGrid.Children.Add(qwenStopBtn); Grid.SetColumn(qwenStopBtn, 2);
        scrollPanel.Children.Add(qwenStatusGrid);

        // Deepseek Proxy
        scrollPanel.Children.Add(new TextBlock { Text = "FreeDeepseekAPI (Deepseek Chat)", Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 12 });
        deepseekProxyCheck = new CheckBox { Content = "Enable Deepseek Background Proxy", Foreground = Brushes.White };
        scrollPanel.Children.Add(deepseekProxyCheck);

        Grid dsGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*,Auto,Auto"), Margin = new Thickness(0, 2) };
        deepseekProxyPortInput = new TextBox { Watermark = "Port: 9655", Margin = new Thickness(0, 0, 4, 0) };
        deepseekProxyCookieInput = new TextBox { PasswordChar = '*', Watermark = "Cookie (Optional)", Margin = new Thickness(0, 0, 4, 0) };
        deepseekInstallBtn = CreateButton("Install", BtnBgNormal(), Brushes.White);
        deepseekInstallBtn.Click += async (s, e) => await viewModel.InstallProxyAsync("FreeDeepseekAPI");
        deepseekAuthBtn = CreateButton("Authenticate", AccentColor, Brushes.White);
        deepseekAuthBtn.Margin = new Thickness(4, 0, 0, 0);
        deepseekAuthBtn.Click += async (s, e) => await viewModel.AuthenticateProxyAsync("FreeDeepseekAPI");

        dsGrid.Children.Add(deepseekProxyPortInput); Grid.SetColumn(deepseekProxyPortInput, 0);
        dsGrid.Children.Add(deepseekProxyCookieInput); Grid.SetColumn(deepseekProxyCookieInput, 1);
        dsGrid.Children.Add(deepseekInstallBtn); Grid.SetColumn(deepseekInstallBtn, 2);
        dsGrid.Children.Add(deepseekAuthBtn); Grid.SetColumn(deepseekAuthBtn, 3);
        scrollPanel.Children.Add(dsGrid);

        Grid dsStatusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 2, 0, 8) };
        deepseekStatusText = new TextBlock { Foreground = TextMutedBrush(), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        deepseekStartBtn = CreateButton("Start", greenBrush, Brushes.White);
        deepseekStartBtn.Margin = new Thickness(0, 0, 4, 0);
        deepseekStartBtn.Click += async (s, e) =>
        {
            ApplySettingsToViewModel();
            await viewModel.StartProxyManualAsync("FreeDeepseekAPI");
        };
        deepseekStopBtn = CreateButton("Stop", redBrush, Brushes.White);
        deepseekStopBtn.Click += async (s, e) =>
        {
            await viewModel.StopProxyManualAsync("FreeDeepseekAPI");
        };
        dsStatusGrid.Children.Add(deepseekStatusText); Grid.SetColumn(deepseekStatusText, 0);
        dsStatusGrid.Children.Add(deepseekStartBtn); Grid.SetColumn(deepseekStartBtn, 1);
        dsStatusGrid.Children.Add(deepseekStopBtn); Grid.SetColumn(deepseekStopBtn, 2);
        scrollPanel.Children.Add(dsStatusGrid);

        // Kimi / GLM Proxy
        scrollPanel.Children.Add(new TextBlock { Text = "FreeGLMKimiAPI (GLM / Kimi)", Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 12 });
        kimiProxyCheck = new CheckBox { Content = "Enable GLM/Kimi Background Proxy", Foreground = Brushes.White };
        scrollPanel.Children.Add(kimiProxyCheck);

        Grid kimiGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*,Auto,Auto"), Margin = new Thickness(0, 2) };
        kimiProxyPortInput = new TextBox { Watermark = "Port: 3265", Margin = new Thickness(0, 0, 4, 0) };
        kimiProxyCookieInput = new TextBox { PasswordChar = '*', Watermark = "Cookie (Optional)", Margin = new Thickness(0, 0, 4, 0) };
        kimiInstallBtn = CreateButton("Install", BtnBgNormal(), Brushes.White);
        kimiInstallBtn.Click += async (s, e) => await viewModel.InstallProxyAsync("FreeGLMKimiAPI");
        kimiAuthBtn = CreateButton("Authenticate", AccentColor, Brushes.White);
        kimiAuthBtn.Margin = new Thickness(4, 0, 0, 0);
        kimiAuthBtn.Click += async (s, e) => await viewModel.AuthenticateProxyAsync("FreeGLMKimiAPI");

        kimiGrid.Children.Add(kimiProxyPortInput); Grid.SetColumn(kimiProxyPortInput, 0);
        kimiGrid.Children.Add(kimiProxyCookieInput); Grid.SetColumn(kimiProxyCookieInput, 1);
        kimiGrid.Children.Add(kimiInstallBtn); Grid.SetColumn(kimiInstallBtn, 2);
        kimiGrid.Children.Add(kimiAuthBtn); Grid.SetColumn(kimiAuthBtn, 3);
        scrollPanel.Children.Add(kimiGrid);

        Grid kimiStatusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 2, 0, 12) };
        kimiStatusText = new TextBlock { Foreground = TextMutedBrush(), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        kimiStartBtn = CreateButton("Start", greenBrush, Brushes.White);
        kimiStartBtn.Margin = new Thickness(0, 0, 4, 0);
        kimiStartBtn.Click += async (s, e) =>
        {
            ApplySettingsToViewModel();
            await viewModel.StartProxyManualAsync("FreeGLMKimiAPI");
        };
        kimiStopBtn = CreateButton("Stop", redBrush, Brushes.White);
        kimiStopBtn.Click += async (s, e) =>
        {
            await viewModel.StopProxyManualAsync("FreeGLMKimiAPI");
        };
        kimiStatusGrid.Children.Add(kimiStatusText); Grid.SetColumn(kimiStatusText, 0);
        kimiStatusGrid.Children.Add(kimiStartBtn); Grid.SetColumn(kimiStartBtn, 1);
        kimiStatusGrid.Children.Add(kimiStopBtn); Grid.SetColumn(kimiStopBtn, 2);
        scrollPanel.Children.Add(kimiStatusGrid);

        scrollPanel.Children.Add(new Border
        {
            Height = 1,
            Background = BorderColor,
            Margin = new Thickness(0, 8, 0, 4)
        });

        scrollPanel = privacyPanel;

        warnCheck = new CheckBox { IsVisible = false };

        // Process exclusions (Phase 14)
        scrollPanel.Children.Add(CreateHeaderLabel("Blocked Process Names (one per line)"));
        blockedProcessInput = new TextBox { AcceptsReturn = true, Height = 60, Watermark = "cmd\npowershell" };
        scrollPanel.Children.Add(blockedProcessInput);

        scrollPanel.Children.Add(CreateHeaderLabel("Blocked Window Titles (one per line)"));
        blockedTitleInput = new TextBox { AcceptsReturn = true, Height = 60, Watermark = "Secret\nPrivate" };
        scrollPanel.Children.Add(blockedTitleInput);

        scrollPanel = hotkeysPanel;

        scrollPanel.Children.Add(new TextBlock
        {
            Text = "Нажми «Задать», затем нужную комбинацию или одну клавишу. Esc — отмена.",
            Foreground = TextMutedBrush(),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        regionHotkeyDisplay = new TextBlock();
        scrollPanel.Children.Add(CreateHotkeyCaptureRow(
            "Захват области",
            "region",
            regionHotkeyDisplay,
            () => viewModel.RegionHotkeyText,
            value => viewModel.RegionHotkeyText = value));

        activeWindowHotkeyDisplay = new TextBlock();
        scrollPanel.Children.Add(CreateHotkeyCaptureRow(
            "Захват активного окна",
            "active_window",
            activeWindowHotkeyDisplay,
            () => viewModel.ActiveWindowHotkeyText,
            value => viewModel.ActiveWindowHotkeyText = value));

        monitorHotkeyDisplay = new TextBlock();
        scrollPanel.Children.Add(CreateHotkeyCaptureRow(
            "Захват монитора",
            "monitor",
            monitorHotkeyDisplay,
            () => viewModel.MonitorHotkeyText,
            value => viewModel.MonitorHotkeyText = value));

        cleanChatHotkeyDisplay = new TextBlock();
        scrollPanel.Children.Add(CreateHotkeyCaptureRow(
            "Clean chat (скрыть поле ввода)",
            "clean_chat",
            cleanChatHotkeyDisplay,
            () => viewModel.CleanChatHotkeyText,
            value => viewModel.CleanChatHotkeyText = value));

        clickThroughHotkeyDisplay = new TextBlock();
        scrollPanel.Children.Add(CreateHotkeyCaptureRow(
            "Режим \"Призрак\" (нажатия сквозь окно)",
            "click_through",
            clickThroughHotkeyDisplay,
            () => viewModel.ClickThroughHotkeyText,
            value => viewModel.ClickThroughHotkeyText = value));

        emergencyExitHotkeyDisplay = new TextBlock();
        scrollPanel.Children.Add(CreateHotkeyCaptureRow(
            "⚠ Аварийный выход (мгновенно закрыть программу)",
            "emergency_exit",
            emergencyExitHotkeyDisplay,
            () => viewModel.EmergencyExitHotkeyText,
            value => viewModel.EmergencyExitHotkeyText = value));

        scrollPanel.Children.Add(CreateHeaderLabel("Prompt 1 Text"));
        prompt1Input = new TextBox { Watermark = "Prompt text for hotkey 1", Margin = new Thickness(0, 0, 0, 6) };
        scrollPanel.Children.Add(prompt1Input);
        prompt1HotkeyDisplay = new TextBlock();
        scrollPanel.Children.Add(CreateHotkeyCaptureRow(
            "Prompt 1 Hotkey",
            "prompt_1",
            prompt1HotkeyDisplay,
            () => viewModel.Prompt1HotkeyText,
            value => viewModel.Prompt1HotkeyText = value));

        scrollPanel.Children.Add(CreateHeaderLabel("Prompt 2 Text"));
        prompt2Input = new TextBox { Watermark = "Prompt text for hotkey 2", Margin = new Thickness(0, 0, 0, 6) };
        scrollPanel.Children.Add(prompt2Input);
        prompt2HotkeyDisplay = new TextBlock();
        scrollPanel.Children.Add(CreateHotkeyCaptureRow(
            "Prompt 2 Hotkey",
            "prompt_2",
            prompt2HotkeyDisplay,
            () => viewModel.Prompt2HotkeyText,
            value => viewModel.Prompt2HotkeyText = value));

        scrollPanel.Children.Add(CreateHeaderLabel("Prompt 3 Text"));
        prompt3Input = new TextBox { Watermark = "Prompt text for hotkey 3", Margin = new Thickness(0, 0, 0, 6) };
        scrollPanel.Children.Add(prompt3Input);
        prompt3HotkeyDisplay = new TextBlock();
        scrollPanel.Children.Add(CreateHotkeyCaptureRow(
            "Prompt 3 Hotkey",
            "prompt_3",
            prompt3HotkeyDisplay,
            () => viewModel.Prompt3HotkeyText,
            value => viewModel.Prompt3HotkeyText = value));

        // Save/Cancel buttons
        Grid settingsActionsGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        Button saveBtn = CreateButton("Save", AccentColor, Brushes.White);
        saveBtn.Click += async (s, e) =>
        {
            ApplySettingsToViewModel();
            await viewModel.SaveSettingsAsync();
        };
        settingsActionsGrid.Children.Add(saveBtn);
        Grid.SetColumn(saveBtn, 1);

        Button cancelSettingsBtn = CreateButton("Cancel", BtnBgNormal(), Brushes.White);
        cancelSettingsBtn.Margin = new Thickness(8, 0, 0, 0);
        cancelSettingsBtn.Click += (s, e) => viewModel.CloseSettings();
        settingsActionsGrid.Children.Add(cancelSettingsBtn);
        Grid.SetColumn(cancelSettingsBtn, 2);

        settingsGrid.Children.Add(settingsActionsGrid);
        Grid.SetRow(settingsActionsGrid, 2);

        settingsOverlay.Child = settingsGrid;
        mainGrid.Children.Add(settingsOverlay);

        // Privacy Warning Overlay panel
        privacyWarningOverlay = new Border
        {
            Background = WindowBg,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1, 0, 0, 0),
            IsVisible = false
        };
        Grid.SetColumn(privacyWarningOverlay, 1);
        Grid.SetColumnSpan(privacyWarningOverlay, 2);

        Grid warningGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,*"),
            Margin = new Thickness(24)
        };

        StackPanel warningContent = new StackPanel
        {
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock warningIcon = new TextBlock
        {
            Text = "⚠️",
            FontSize = 48,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        warningContent.Children.Add(warningIcon);

        TextBlock warningTitle = new TextBlock
        {
            Text = "Privacy Warning",
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        warningContent.Children.Add(warningTitle);

        TextBlock warningDesc = new TextBlock
        {
            Text = "Warning: This action sends your screen contents to a cloud AI provider. Do you want to proceed?",
            Foreground = TextMutedBrush(),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 400
        };
        warningContent.Children.Add(warningDesc);

        warningGrid.Children.Add(warningContent);
        Grid.SetRow(warningContent, 0);
        Grid.SetRowSpan(warningContent, 2);

        StackPanel warningBtns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        Button proceedBtn = CreateButton("Proceed", AccentColor, Brushes.White);
        proceedBtn.Click += (s, e) => viewModel.ApprovePrivacyWarning();
        warningBtns.Children.Add(proceedBtn);

        Button abortBtn = CreateButton("Abort", BtnBgNormal(), Brushes.White);
        abortBtn.Click += (s, e) => viewModel.RejectPrivacyWarning();
        warningBtns.Children.Add(abortBtn);

        warningGrid.Children.Add(warningBtns);
        Grid.SetRow(warningBtns, 2);

        privacyWarningOverlay.Child = warningGrid;
        mainGrid.Children.Add(privacyWarningOverlay);

        // Bindings & properties
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) =>
        {
            Dispose();
            // Previously only the window closed; process stayed in tray
            // (ShutdownMode.OnExplicitShutdown). Exit the whole app on X.
            ShutdownApplication();
        };
        viewModel.CloseRequested += (s, e) => Close();

        UpdateSessionsView();
        UpdateMessagesView();
        UpdateSettingsView();
        UpdateErrorBanner();
        ApplySidebarVisibility();
        ApplyCleanChatMode();
    }

    private static TextBlock CreateHeaderLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = TextMutedBrush(),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 4, 0, 2)
        };
    }

    private void RebuildModernSidebar(Grid sidebarGrid)
    {
        sidebarGrid.Children.Clear();
        sidebarGrid.RowDefinitions = new RowDefinitions("82,88,*,86");
        sidebarGrid.Background = SidebarBg;

        Grid header = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,44"),
            Margin = new Thickness(22, 0, 18, 0)
        };
        Control logo = CreateLogoMark();
        header.Children.Add(logo);
        Grid.SetColumn(logo, 0);
        TextBlock title = new()
        {
            Text = "AI Assistant",
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        header.Children.Add(title);
        Grid.SetColumn(title, 1);
        Button captureShortcut = CreateSquareButton("📷");
        captureShortcut.Click += (_, _) => viewModel.RequestNewCapture();
        header.Children.Add(captureShortcut);
        Grid.SetColumn(captureShortcut, 2);
        sidebarGrid.Children.Add(header);
        Grid.SetRow(header, 0);

        Border newChatSurface = new()
        {
            Margin = new Thickness(18, 12, 18, 10),
            Background = CardBg,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7)
        };
        Button newChat = new()
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(18, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = "+", Foreground = Brushes.White, FontSize = 24, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = "Новый чат", Foreground = Brushes.White, FontSize = 15, VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        newChat.Click += (_, _) => viewModel.StartNewChat();
        newChatSurface.Child = newChat;
        sidebarGrid.Children.Add(newChatSurface);
        Grid.SetRow(newChatSurface, 1);

        StackPanel sessionsPanel = new()
        {
            Spacing = 10,
            Margin = new Thickness(18, 14, 14, 0)
        };
        sessionsPanel.Children.Add(new TextBlock
        {
            Text = "Сегодня",
            Foreground = TextMutedBrush(),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 4)
        });
        sessionsListBox.Background = Brushes.Transparent;
        sessionsListBox.BorderThickness = new Thickness(0);
        sessionsListBox.Padding = new Thickness(0);
        sessionsListBox.Margin = new Thickness(0);
        sessionsListBox.ItemTemplate = new FuncDataTemplate<ChatSession>((session, _) => CreateConversationRow(session));
        sessionsPanel.Children.Add(sessionsListBox);
        sessionsPanel.Children.Add(new Border
        {
            Height = 1,
            Background = DividerColor,
            Margin = new Thickness(0, 12, 0, 6)
        });
        sessionsPanel.Children.Add(new TextBlock
        {
            Text = "Вчера",
            Foreground = TextMutedBrush(),
            FontSize = 13
        });
        ScrollViewer sessionsScroll = new()
        {
            Content = sessionsPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        sidebarGrid.Children.Add(sessionsScroll);
        Grid.SetRow(sessionsScroll, 2);

        Grid profileGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,50"),
            Margin = new Thickness(18, 10, 18, 18)
        };
        Border profileCard = new()
        {
            Background = CardBg,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10)
        };
        Grid profileContent = new()
        {
            ColumnDefinitions = new ColumnDefinitions("38,*,Auto")
        };
        Control avatar = CreateAvatar("U", 34, AccentColor);
        profileContent.Children.Add(avatar);
        Grid.SetColumn(avatar, 0);
        TextBlock profileText = new()
        {
            Text = "Профиль",
            Foreground = Brushes.White,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        profileContent.Children.Add(profileText);
        Grid.SetColumn(profileText, 1);
        TextBlock chevron = new()
        {
            Text = "▼",
            Foreground = TextMutedBrush(),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        profileContent.Children.Add(chevron);
        Grid.SetColumn(chevron, 2);
        profileCard.Child = profileContent;
        profileGrid.Children.Add(profileCard);
        Grid.SetColumn(profileCard, 0);
        Button settings = CreateSquareButton("⚙");
        settings.Click += async (_, _) => await viewModel.LoadSettingsAsync();
        profileGrid.Children.Add(settings);
        Grid.SetColumn(settings, 1);
        sidebarGrid.Children.Add(profileGrid);
        Grid.SetRow(profileGrid, 3);
    }

    private void RebuildModernChat(Grid chatGrid)
    {
        if (messageInput.Parent is Panel messageInputPanel)
        {
            messageInputPanel.Children.Remove(messageInput);
        }
        else if (messageInput.Parent is Decorator messageInputDecorator)
        {
            messageInputDecorator.Child = null;
        }

        if (sendBtn.Parent is Panel sendBtnPanel)
        {
            sendBtnPanel.Children.Remove(sendBtn);
        }
        else if (sendBtn.Parent is Decorator sendBtnDecorator)
        {
            sendBtnDecorator.Child = null;
        }

        chatGrid.Children.Clear();
        chatMainGrid = chatGrid;
        chatGrid.RowDefinitions = new RowDefinitions("64,Auto,*,124");
        chatGrid.Background = Brushes.Transparent;

        Grid titleBar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,44,44,44,44"),
            Background = Brushes.Transparent
        };
        titleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };
        cleanChatToggleBtn = CreateWindowControlButton("◎");
        ToolTip.SetTip(cleanChatToggleBtn, "Clean chat: only messages + AI (Ctrl+Shift+H)");
        cleanChatToggleBtn.Click += async (_, _) => await viewModel.ToggleCleanChatModeAsync();
        titleBar.Children.Add(cleanChatToggleBtn);
        Grid.SetColumn(cleanChatToggleBtn, 1);
        Button minimize = CreateWindowControlButton("🗕");
        minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        titleBar.Children.Add(minimize);
        Grid.SetColumn(minimize, 2);
        Button maximize = CreateWindowControlButton("🗖");
        maximize.Click += (_, _) =>
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        };
        titleBar.Children.Add(maximize);
        Grid.SetColumn(maximize, 3);
        Button close = CreateWindowControlButton("🗙", isClose: true);
        close.Click += (_, _) => viewModel.CloseChat();
        titleBar.Children.Add(close);
        Grid.SetColumn(close, 4);
        chatTitleBar = titleBar;
        chatGrid.Children.Add(titleBar);
        Grid.SetRow(titleBar, 0);

        errorBanner.Margin = new Thickness(16, 0, 16, 8);
        errorBanner.CornerRadius = new CornerRadius(8);
        chatGrid.Children.Add(errorBanner);
        Grid.SetRow(errorBanner, 1);

        conversationSurface = new Border
        {
            Margin = new Thickness(16, 0, 16, 10),
            Background = PanelBg,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true
        };
        chatHistoryPanel.Margin = new Thickness(22, 22);
        chatHistoryPanel.Spacing = 18;
        chatHistoryPanel.Background = Brushes.Transparent;
        chatScrollViewer.Background = Brushes.Transparent;
        chatScrollViewer.Content = chatHistoryPanel;
        chatScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        conversationSurface.Child = chatScrollViewer;
        chatGrid.Children.Add(conversationSurface);
        Grid.SetRow(conversationSurface, 2);

        Border composer = new()
        {
            Margin = new Thickness(16, 0, 16, 18),
            Background = InputBg,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12)
        };
        Grid composerGrid = new()
        {
            RowDefinitions = new RowDefinitions("*,38"),
            ColumnDefinitions = new ColumnDefinitions("*,58")
        };
        messageInput.Watermark = "Напишите сообщение...";
        messageInput.Background = Brushes.Transparent;
        messageInput.BorderThickness = new Thickness(0);
        messageInput.Foreground = Brushes.White;
        messageInput.FontSize = 14;
        messageInput.MinHeight = 44;
        messageInput.MaxHeight = 64;
        messageInput.Padding = new Thickness(0);
        composerGrid.Children.Add(messageInput);
        Grid.SetRow(messageInput, 0);
        Grid.SetColumnSpan(messageInput, 2);

        StackPanel composerTools = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Button capture = CreateComposerIconButton("📷");
        ToolTip.SetTip(capture, "Сделать скриншот области");
        capture.Click += (_, _) => viewModel.RequestNewCapture();
        composerTools.Children.Add(capture);
        Button image = CreateComposerIconButton("📎");
        ToolTip.SetTip(image, "Прикрепить изображение");
        image.Click += async (_, _) => await AttachImageFromPickerAsync();
        composerTools.Children.Add(image);
        composerToolsPanel = new Border { Child = composerTools, Background = Brushes.Transparent };
        composerGrid.Children.Add(composerToolsPanel);
        Grid.SetRow(composerToolsPanel, 1);
        Grid.SetColumn(composerToolsPanel, 0);

        sendBtn.Content = "↑";
        sendBtn.Width = 44;
        sendBtn.Height = 44;
        sendBtn.MinWidth = 44;
        sendBtn.Margin = new Thickness(0);
        sendBtn.Padding = new Thickness(0);
        sendBtn.Background = AccentColor;
        sendBtn.BorderThickness = new Thickness(0);
        sendBtn.CornerRadius = new CornerRadius(5);
        sendBtn.FontSize = 22;
        sendBtn.HorizontalAlignment = HorizontalAlignment.Right;
        sendBtn.VerticalAlignment = VerticalAlignment.Bottom;
        composerGrid.Children.Add(sendBtn);
        Grid.SetRow(sendBtn, 1);
        Grid.SetColumn(sendBtn, 1);
        composer.Child = composerGrid;
        chatComposer = composer;
        chatGrid.Children.Add(composer);
        Grid.SetRow(composer, 3);
        ApplyCleanChatMode();
    }

    private Border CreateConversationRow(ChatSession session)
    {
        bool isActive = viewModel.ActiveSession?.Id == session.Id;
        Border row = new()
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 2),
            Background = isActive 
                ? new SolidColorBrush(Color.FromArgb(32, 91, 114, 255))
                : new SolidColorBrush(Color.FromArgb(12, 255, 255, 255))
        };

        row.PointerEntered += (s, e) =>
        {
            if (viewModel.ActiveSession?.Id != session.Id)
            {
                row.Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));
            }
        };
        row.PointerExited += (s, e) =>
        {
            if (viewModel.ActiveSession?.Id != session.Id)
            {
                row.Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255));
            }
        };

        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("34,*,Auto,Auto")
        };
        Control icon = CreateSmallConversationIcon();
        grid.Children.Add(icon);
        Grid.SetColumn(icon, 0);
        TextBlock title = new()
        {
            Text = GetSessionTitle(session),
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(title);
        Grid.SetColumn(title, 1);
        TextBlock time = new()
        {
            Text = GetSessionTime(session),
            Foreground = TextMutedBrush(),
            FontSize = 11,
            Margin = new Thickness(10, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(time);
        Grid.SetColumn(time, 2);
        Button delete = new()
        {
            Content = "🗑",
            Foreground = TextMutedBrush(),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2),
            FontSize = 10,
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center
        };
        delete.Click += (s, e) =>
        {
            e.Handled = true;
            viewModel.DeleteSession(session);
        };
        grid.Children.Add(delete);
        Grid.SetColumn(delete, 3);
        row.Child = grid;
        return row;
    }

    private Border CreateLogoMark()
    {
        return new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(7),
            Background = AccentColor,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "AI",
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static Border CreateSmallConversationIcon()
    {
        return new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromArgb(72, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "💬",
                Foreground = Brushes.White,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static Border CreateAvatar(string text, double size, IBrush background)
    {
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 4),
            Background = background,
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = Math.Max(10, size * 0.4),
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private Button CreateSquareButton(string text)
    {
        Button button = new()
        {
            Content = text,
            Width = 40,
            Height = 40,
            Background = CardBg,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Foreground = Brushes.White,
            FontSize = 18,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        button.PointerEntered += (_, _) => button.Background = BtnBgHover();
        button.PointerExited += (_, _) => button.Background = CardBg;
        return button;
    }

    private static Button CreateWindowControlButton(string text, bool isClose = false)
    {
        Button button = new()
        {
            Content = text,
            Width = 38,
            Height = 38,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            FontSize = 15,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        button.PointerEntered += (_, _) => button.Background = isClose
            ? new SolidColorBrush(Color.FromArgb(255, 232, 17, 35))
            : new SolidColorBrush(Color.FromArgb(25, 255, 255, 255));
        button.PointerExited += (_, _) => button.Background = Brushes.Transparent;
        return button;
    }

    private static Button CreateComposerIconButton(string text)
    {
        Button button = new()
        {
            Content = text,
            Width = 34,
            Height = 34,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            FontSize = 18,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        button.PointerEntered += (_, _) => button.Background = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255));
        button.PointerExited += (_, _) => button.Background = Brushes.Transparent;
        return button;
    }

    private async Task AttachImageFromPickerAsync()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        IReadOnlyList<Avalonia.Platform.Storage.IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select Image to Analyze",
            AllowMultiple = false,
            FileTypeFilter = new[] { Avalonia.Platform.Storage.FilePickerFileTypes.ImageAll }
        });
        if (files.Count == 0)
        {
            return;
        }

        using Stream stream = await files[0].OpenReadAsync();
        using MemoryStream ms = new();
        await stream.CopyToAsync(ms);
        byte[] bytes = ms.ToArray();

        (string mediaType, ScreenImageFormat format) = DetectImageFormat(bytes);
        using MemoryStream dimensionsStream = new(bytes, writable: false);
        using Bitmap decoded = new(dimensionsStream);
        ScreenImage screenImage = new(
            bytes,
            mediaType,
            format,
            decoded.PixelSize.Width,
            decoded.PixelSize.Height,
            DateTimeOffset.UtcNow);
        await viewModel.CreateSessionFromImageAsync(screenImage, CancellationToken.None);
    }

    private static string GetSessionTitle(ChatSession session)
    {
        string? userText = session.Messages.FirstOrDefault(message => message.Role == AiMessageRole.User)?.Content;
        if (string.IsNullOrWhiteSpace(userText))
        {
            return session.Profile.DisplayName;
        }

        string normalized = userText.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 34 ? normalized : normalized[..34] + "...";
    }

    private static string GetSessionTime(ChatSession session)
    {
        DateTimeOffset timestamp = session.Messages.LastOrDefault()?.CreatedAt ?? session.Image?.CapturedAt ?? DateTimeOffset.UtcNow;
        return timestamp.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void OnSessionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sessionsListBox.SelectedItem is ChatSession session)
        {
            viewModel.SelectSession(session);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (e.PropertyName == nameof(ChatViewModel.ActiveSession))
            {
                UpdateMessagesView();
            }
            else if (e.PropertyName == nameof(ChatViewModel.Sessions))
            {
                UpdateSessionsView();
            }
            else if (e.PropertyName == nameof(ChatViewModel.ActiveMessages))
            {
                UpdateMessagesView();
            }
            else if (e.PropertyName == nameof(ChatViewModel.IsSettingsVisible))
            {
                if (viewModel.IsSettingsVisible)
                {
                    settingsOverlay.Opacity = 0;
                    settingsOverlay.IsVisible = true;
                    UpdateSettingsView();
                    Dispatcher.UIThread.Post(() => settingsOverlay.Opacity = 1);
                }
                else
                {
                    settingsOverlay.Opacity = 0;
                    DispatcherTimer.RunOnce(() =>
                    {
                        if (!viewModel.IsSettingsVisible)
                        {
                            settingsOverlay.IsVisible = false;
                        }
                    }, TimeSpan.FromMilliseconds(200));
                }
            }
            else if (e.PropertyName == nameof(ChatViewModel.IsPrivacyWarningActive))
            {
                privacyWarningOverlay.IsVisible = viewModel.IsPrivacyWarningActive;
            }
            else if (e.PropertyName == nameof(ChatViewModel.InputText))
            {
                messageInput.Text = viewModel.InputText;
            }
            else if (e.PropertyName == nameof(ChatViewModel.ErrorMessage))
            {
                UpdateErrorBanner();
            }
            else if (e.PropertyName == nameof(ChatViewModel.OverlayOpacity))
            {
                ApplySurfaceOpacity(viewModel.OverlayOpacity);
            }
            else if (e.PropertyName == nameof(ChatViewModel.UiScale))
            {
                ApplyUiScale();
            }
            else if (e.PropertyName == nameof(ChatViewModel.AlwaysOnTop))
            {
                Topmost = viewModel.AlwaysOnTop;
            }
            else if (e.PropertyName == nameof(ChatViewModel.ClickThroughMode))
            {
                clickThroughCheck.IsChecked = viewModel.ClickThroughMode;
            }
            else if (e.PropertyName == nameof(ChatViewModel.QwenStatus))
            {
                qwenStatusText.Text = $"Status: {viewModel.QwenStatus}";
            }
            else if (e.PropertyName == nameof(ChatViewModel.DeepseekStatus))
            {
                deepseekStatusText.Text = $"Status: {viewModel.DeepseekStatus}";
            }
            else if (e.PropertyName == nameof(ChatViewModel.KimiStatus))
            {
                kimiStatusText.Text = $"Status: {viewModel.KimiStatus}";
            }
            else if (e.PropertyName == nameof(ChatViewModel.IsQwenInstalled) ||
                     e.PropertyName == nameof(ChatViewModel.IsQwenRunning) ||
                     e.PropertyName == nameof(ChatViewModel.IsQwenInstalling) ||
                     e.PropertyName == nameof(ChatViewModel.IsQwenStarting) ||
                     e.PropertyName == nameof(ChatViewModel.IsDeepseekInstalled) ||
                     e.PropertyName == nameof(ChatViewModel.IsDeepseekRunning) ||
                     e.PropertyName == nameof(ChatViewModel.IsDeepseekInstalling) ||
                     e.PropertyName == nameof(ChatViewModel.IsDeepseekStarting) ||
                     e.PropertyName == nameof(ChatViewModel.IsKimiInstalled) ||
                     e.PropertyName == nameof(ChatViewModel.IsKimiRunning) ||
                     e.PropertyName == nameof(ChatViewModel.IsKimiInstalling) ||
                     e.PropertyName == nameof(ChatViewModel.IsKimiStarting))
            {
                UpdateProxyButtonsState();
            }
            else if (e.PropertyName == nameof(ChatViewModel.AvailableProviders))
            {
                providerCombo.ItemsSource = viewModel.AvailableProviders;
            }
            else if (e.PropertyName == nameof(ChatViewModel.AvailableModels))
            {
                modelCombo.ItemsSource = viewModel.AvailableModels;
            }
            else if (e.PropertyName == nameof(ChatViewModel.SelectedProviderIndex))
            {
                providerCombo.SelectedIndex = viewModel.SelectedProviderIndex;
            }
            else if (e.PropertyName == nameof(ChatViewModel.SelectedModelIndex))
            {
                modelCombo.SelectedIndex = viewModel.SelectedModelIndex;
            }
            else if (e.PropertyName == nameof(ChatViewModel.CustomModelName))
            {
                customModelInput.Text = viewModel.CustomModelName;
            }
            else if (e.PropertyName == nameof(ChatViewModel.IsCustomModelVisible))
            {
                customModelLabel.IsVisible = viewModel.IsCustomModelVisible;
                customModelInput.IsVisible = viewModel.IsCustomModelVisible;
            }

            else if (e.PropertyName == nameof(ChatViewModel.HideSidebar)
                     || e.PropertyName == nameof(ChatViewModel.CleanChatMode))
            {
                ApplySidebarVisibility();
                ApplyCleanChatMode();
                if (e.PropertyName == nameof(ChatViewModel.CleanChatMode))
                {
                    UpdateMessagesView();
                }
            }

            sendBtn.IsEnabled = viewModel.CanSend;
        });
    }

    private void UpdateErrorBanner()
    {
        errorText.Text = viewModel.ErrorMessage;
        errorBanner.IsVisible = !string.IsNullOrWhiteSpace(viewModel.ErrorMessage);
    }

    private void ApplyUiScale()
    {
        double scale = double.IsFinite(viewModel.UiScale)
            ? Math.Clamp(viewModel.UiScale, UiSettings.MinUiScale, UiSettings.MaxUiScale)
            : 1d;

        rootTransform.LayoutTransform = new ScaleTransform(scale, scale);
    }

    /// <summary>
    /// Makes the whole chat window glass-like by scaling surface alphas.
    /// Does not change Window.Opacity, so message text stays fully readable.
    /// </summary>
    private void ApplySurfaceOpacity(double opacity)
    {
        double t = double.IsFinite(opacity) ? Math.Clamp(opacity, 0d, 1d) : 0.96d;
        // Floor so UI never fully disappears at 0 (still barely visible for controls).
        double surface = Math.Clamp(t, 0.08d, 1d);

        static byte A(byte max, double factor) => (byte)Math.Clamp((int)Math.Round(max * factor), 0, 255);

        WindowBg.Color = Color.FromArgb(A(WindowBgMaxAlpha, surface), 9, 13, 20);
        SidebarBg.Color = Color.FromArgb(A(SidebarBgMaxAlpha, surface), 16, 21, 31);
        PanelBg.Color = Color.FromArgb(A(PanelBgMaxAlpha, surface), 16, 22, 33);
        CardBg.Color = Color.FromArgb(A(CardBgMaxAlpha, surface), 17, 23, 34);
        HeaderBg.Color = Color.FromArgb(A(HeaderBgMaxAlpha, surface), 20, 26, 38);
        InputBg.Color = Color.FromArgb(A(InputBgMaxAlpha, surface), 18, 24, 34);
        AssistantBubbleBg.Color = Color.FromArgb(A(AssistantBubbleMaxAlpha, surface), 16, 22, 32);
        // Keep user bubble readable but never fully solid (that kills the glass look).
        UserBubbleBg.Color = Color.FromArgb(A(UserBubbleMaxAlpha, Math.Max(surface, 0.25d)), 82, 105, 246);
        BorderColor.Color = Color.FromArgb(A(BorderMaxAlpha, Math.Max(surface, 0.35d)), 91, 103, 122);
        DividerColor.Color = Color.FromArgb(A(DividerMaxAlpha, Math.Max(surface, 0.35d)), 120, 132, 148);

        // Ensure window itself never multiplies-away child alpha.
        Opacity = 1;
        Background = Brushes.Transparent;
        if (chatScrollViewer is not null)
        {
            chatScrollViewer.Background = Brushes.Transparent;
        }

        if (chatHistoryPanel is not null)
        {
            chatHistoryPanel.Background = Brushes.Transparent;
        }

        if (conversationSurface is not null)
        {
            conversationSurface.Background = PanelBg;
        }
    }

    private void ApplySidebarVisibility()
    {
        bool showSidebar = !viewModel.HideSidebar && !viewModel.CleanChatMode;
        sidebarGrid.IsVisible = showSidebar;
        mainGrid.ColumnDefinitions[0] = new ColumnDefinition(showSidebar ? 292 : 0, GridUnitType.Pixel);
    }

    private void ApplyCleanChatMode()
    {
        bool clean = viewModel.CleanChatMode;

        // Hide the whole compose bar: text field + send button (+ capture tools).
        if (chatComposer is not null)
        {
            chatComposer.IsVisible = !clean;
        }

        if (composerToolsPanel is not null)
        {
            composerToolsPanel.IsVisible = !clean;
        }

        // Collapse the bottom row so only the message list remains.
        if (chatMainGrid is not null)
        {
            chatMainGrid.RowDefinitions = clean
                ? new RowDefinitions("36,Auto,*,0")
                : new RowDefinitions("64,Auto,*,124");
        }

        // Clean mode: drop the solid conversation chrome so only text floats.
        if (conversationSurface is not null)
        {
            conversationSurface.Background = clean ? Brushes.Transparent : PanelBg;
            conversationSurface.BorderThickness = clean ? new Thickness(0) : new Thickness(1);
        }

        if (cleanChatToggleBtn is not null)
        {
            cleanChatToggleBtn.Background = clean
                ? new SolidColorBrush(Color.FromArgb(90, 91, 114, 255))
                : Brushes.Transparent;
            ToolTip.SetTip(
                cleanChatToggleBtn,
                clean
                    ? "Показать поле ввода и полный UI"
                    : "Скрыть поле ввода: только сообщения + ответ ИИ");
        }

        // Keep a thin title bar so the window can still be dragged/closed / toggled.
        if (chatTitleBar is not null)
        {
            chatTitleBar.Opacity = clean ? 0.55 : 1.0;
        }
    }

    private void UpdateSessionsView()
    {
        if (sessionsListBox.ItemsSource != viewModel.Sessions)
        {
            sessionsListBox.ItemsSource = viewModel.Sessions;
        }
        sessionsListBox.SelectedItem = viewModel.ActiveSession;
    }

    private void RenderModernMessages()
    {
        string? sessionId = viewModel.ActiveSession?.Id;
        int animateFrom = string.Equals(renderedSessionId, sessionId, StringComparison.Ordinal)
            ? renderedMessageCount
            : 0;

        chatHistoryPanel.Children.Clear();
        ChatSession? session = viewModel.ActiveSession;
        if (session is null)
        {
            renderedSessionId = null;
            renderedMessageCount = 0;
            activePreviewSessionId = null;
            activePreviewImage = null;
            activePreview?.Dispose();
            activePreview = null;
            chatHistoryPanel.Children.Add(CreateModernEmptyState());
            return;
        }

        bool hasVisibleImage = session.Image is not null && (session.Image.Width > 1 || session.Image.Height > 1);
        bool imageRendered = false;
        IReadOnlyList<AiMessage> messages = viewModel.ActiveMessages;

        // Status placeholder only in full UI — clean mode shows pure messages.
        if (messages.Count == 0 && hasVisibleImage && !viewModel.CleanChatMode && session.Image is not null)
        {
            chatHistoryPanel.Children.Add(CreateModernMessageRow(
                new AiMessage(AiMessageRole.User, "Скриншот готов к анализу.", session.Image.CapturedAt),
                includeActiveImage: true));
            imageRendered = true;
        }

        int messageIndex = 0;
        foreach (AiMessage message in messages)
        {
            bool includeImage = !imageRendered && hasVisibleImage && message.Role == AiMessageRole.User;
            Control row = CreateModernMessageRow(message, includeImage);
            chatHistoryPanel.Children.Add(row);
            if (messageIndex >= animateFrom)
            {
                AnimateMessageIn(row, messageIndex - animateFrom);
            }

            imageRendered |= includeImage;
            messageIndex++;
        }

        renderedSessionId = sessionId;
        renderedMessageCount = messages.Count;
        chatScrollViewer.Offset = new Vector(0, double.MaxValue);
    }

    private Border CreateModernEmptyState()
    {
        if (viewModel.CleanChatMode)
        {
            return new Border
            {
                Background = Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = string.Empty,
                    IsVisible = false
                }
            };
        }

        Border shell = new()
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(28),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        StackPanel content = new()
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(CreateAvatar("AI", 42, AccentColor));
        content.Children.Add(new TextBlock
        {
            Text = "AI Assistant",
            Foreground = Brushes.White,
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = "Спросите что-нибудь или сделайте скриншот области.",
            Foreground = TextMutedBrush(),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        shell.Child = content;
        return shell;
    }

    private Grid CreateModernMessageRow(AiMessage message, bool includeActiveImage)
    {
        bool clean = viewModel.CleanChatMode;
        bool isUser = message.Role == AiMessageRole.User;

        Grid row = new()
        {
            ColumnDefinitions = clean
                ? new ColumnDefinitions("*")
                : new ColumnDefinitions("46,*"),
            Margin = new Thickness(0, 0, 0, clean ? 10 : 8)
        };

        StackPanel content = new()
        {
            Spacing = clean ? 6 : 10,
            MaxWidth = 760,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };

        if (!clean)
        {
            Control avatar = isUser
                ? CreateAvatar("U", 34, AccentColor)
                : CreateAvatar("AI", 34, new SolidColorBrush(Color.FromArgb(88, 60, 84, 150)));
            avatar.VerticalAlignment = VerticalAlignment.Top;
            avatar.Margin = new Thickness(6, 4, 0, 0);
            row.Children.Add(avatar);
            Grid.SetColumn(avatar, 0);

            content.Children.Add(CreateMessageHeader(
                isUser ? "Вы" : "AI Assistant",
                message.CreatedAt,
                showActions: !isUser));
        }

        if (isUser)
        {
            if (!string.IsNullOrWhiteSpace(message.Content))
            {
                // Shared UserBubbleBg tracks OverlayOpacity — never invent opaque per-message brushes.
                Border userCard = new()
                {
                    Background = UserBubbleBg,
                    BorderBrush = clean ? Brushes.Transparent : DividerColor,
                    BorderThickness = new Thickness(clean ? 0 : 1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 10),
                    HorizontalAlignment = clean ? HorizontalAlignment.Right : HorizontalAlignment.Left
                };
                userCard.Child = new TextBlock
                {
                    Text = message.Content,
                    Foreground = Brushes.White,
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap
                };
                content.Children.Add(userCard);
            }

            if (includeActiveImage && !clean)
            {
                Control? preview = CreateActiveImagePreview();
                if (preview is not null)
                {
                    content.Children.Add(preview);
                }
            }
        }
        else
        {
            // Glass assistant bubble (Transparent in clean mode so only text is visible).
            Border assistantCard = new()
            {
                Background = clean ? Brushes.Transparent : AssistantBubbleBg,
                BorderBrush = clean ? Brushes.Transparent : DividerColor,
                BorderThickness = new Thickness(clean ? 0 : 1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            StackPanel assistantBody = new()
            {
                Spacing = 14,
                Margin = new Thickness(0)
            };
            Border textPad = new()
            {
                Padding = new Thickness(clean ? 4 : 18, clean ? 4 : 16, clean ? 4 : 18, clean ? 4 : 18),
                Background = Brushes.Transparent,
                Child = new MarkdownMessageView(message.Content)
            };
            assistantBody.Children.Add(textPad);
            assistantCard.Child = assistantBody;
            content.Children.Add(assistantCard);
        }

        row.Children.Add(content);
        Grid.SetColumn(content, clean ? 0 : 1);
        return row;
    }

    private static Grid CreateMessageHeader(string title, DateTimeOffset timestamp, bool showActions)
    {
        Grid header = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            MinHeight = 26
        };
        header.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(header.Children[^1], 0);
        header.Children.Add(new TextBlock
        {
            Text = timestamp.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            Foreground = TextMutedBrush(),
            FontSize = 12,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(header.Children[^1], 1);

        if (showActions)
        {
            StackPanel actions = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };
            actions.Children.Add(CreateTinyActionButton("📋"));
            actions.Children.Add(CreateTinyActionButton("👍"));
            actions.Children.Add(CreateTinyActionButton("👎"));
            header.Children.Add(actions);
            Grid.SetColumn(actions, 3);
        }

        return header;
    }

    private static Button CreateTinyActionButton(string text)
    {
        return new Button
        {
            Content = text,
            Width = 24,
            Height = 24,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = TextMutedBrush(),
            FontSize = 12,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
    }

    private Border? CreateActiveImagePreview()
    {
        ChatSession? session = viewModel.ActiveSession;
        if (session?.Image is null)
        {
            return null;
        }

        if (!string.Equals(activePreviewSessionId, session.Id, StringComparison.Ordinal) || activePreviewImage != session.Image)
        {
            activePreview?.Dispose();
            using MemoryStream stream = new(session.Image.Bytes.ToArray(), writable: false);
            activePreview = new Bitmap(stream);
            activePreviewSessionId = session.Id;
            activePreviewImage = session.Image;
        }

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0),
            MaxWidth = 430,
            Child = new Image
            {
                Source = activePreview,
                Stretch = Stretch.Uniform,
                MaxWidth = 430,
                MaxHeight = 220
            }
        };
    }

    private void UpdateMessagesView()
    {
#pragma warning disable CS0162
        RenderModernMessages();
        // Stream/send rebuilds the message tree with new brushes — re-apply glass alphas
        // so the window does not "go solid" after the first question.
        ApplySurfaceOpacity(viewModel.OverlayOpacity);
        return;

        string? sessionId = viewModel.ActiveSession?.Id;
        int animateFrom = string.Equals(renderedSessionId, sessionId, StringComparison.Ordinal)
            ? renderedMessageCount
            : 0;
        chatHistoryPanel.Children.Clear();
        if (viewModel.ActiveSession is null)
        {
            renderedSessionId = null;
            renderedMessageCount = 0;
            StackPanel emptyState = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24),
            };
            emptyState.Children.Add(new TextBlock
            {
                Text = "Your screen, understood",
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            emptyState.Children.Add(new TextBlock
            {
                Text = "Capture anything, then ask a question. Markdown is supported.",
                Foreground = TextMutedBrush(),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            chatHistoryPanel.Children.Add(emptyState);
            return;
        }

        // Draw active screenshot thumbnail
        Border imageBorder = new Border
        {
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(12),
            Background = SidebarBg
        };
        TextBlock imageInfo = new TextBlock
        {
            Text = $"Screen context  ·  {viewModel.ActiveSession.Image.Width} x {viewModel.ActiveSession.Image.Height}  ·  {viewModel.ActiveSession.Image.Format}",
            Foreground = TextMutedBrush(),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        bool hasVisibleImage = viewModel.ActiveSession.Image.Width > 1 || viewModel.ActiveSession.Image.Height > 1;
        if (hasVisibleImage)
        {
            if (!string.Equals(activePreviewSessionId, viewModel.ActiveSession.Id, StringComparison.Ordinal) || activePreviewImage != viewModel.ActiveSession.Image)
            {
                activePreview?.Dispose();
                using MemoryStream stream = new(viewModel.ActiveSession.Image.Bytes.ToArray(), writable: false);
                activePreview = new Bitmap(stream);
                activePreviewSessionId = viewModel.ActiveSession.Id;
                activePreviewImage = viewModel.ActiveSession.Image;
            }

            StackPanel contextPanel = new() { Spacing = 8 };
            contextPanel.Children.Add(new Image
            {
                Source = activePreview,
                Stretch = Stretch.Uniform,
                MaxHeight = 210,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            contextPanel.Children.Add(imageInfo);
            imageBorder.Child = contextPanel;
            chatHistoryPanel.Children.Add(imageBorder);
        }

        int messageIndex = 0;
        foreach (AiMessage msg in viewModel.ActiveMessages)
        {
            Grid bubbleContainer = new Grid
            {
                Margin = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = msg.Role == AiMessageRole.User ? HorizontalAlignment.Right : HorizontalAlignment.Left
            };

            Border bubbleBorder = new Border
            {
                Background = msg.Role == AiMessageRole.User ? UserBubbleBg : AssistantBubbleBg,
                CornerRadius = msg.Role == AiMessageRole.User
                    ? new CornerRadius(16, 16, 4, 16)
                    : new CornerRadius(16, 16, 16, 4),
                BorderBrush = msg.Role == AiMessageRole.User ? AccentColorHover : BorderColor,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(15, 11),
                MaxWidth = 720
            };

            // Both sides accept Markdown; useful for pasted snippets and formatted prompts.
            bubbleBorder.Child = new MarkdownMessageView(msg.Content);
            bubbleContainer.Children.Add(bubbleBorder);
            chatHistoryPanel.Children.Add(bubbleContainer);
            if (messageIndex >= animateFrom) AnimateMessageIn(bubbleContainer, messageIndex - animateFrom);
            messageIndex++;
        }

        renderedSessionId = sessionId;
        renderedMessageCount = viewModel.ActiveMessages.Count;

        // Auto-scroll to bottom
        chatScrollViewer.Offset = new Vector(0, double.MaxValue);
    }
#pragma warning restore CS0162

    private static void AnimateMessageIn(Control message, int sequence)
    {
        message.Opacity = 0;
        message.RenderTransform = new TranslateTransform(0, 10);
        message.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(220),
                Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            },
        };

        DispatcherTimer.RunOnce(() =>
        {
            message.Opacity = 1;
            if (message.RenderTransform is TranslateTransform transform)
            {
                transform.Y = 0;
            }
        }, TimeSpan.FromMilliseconds(Math.Min(sequence * 45, 180)));
    }

    private void UpdateSettingsView()
    {
        isUpdatingSettings = true;
        try
        {
            profileCombo.ItemsSource = viewModel.ProfileNames;
            providerCombo.ItemsSource = viewModel.AvailableProviders;
            modelCombo.ItemsSource = viewModel.AvailableModels;

            profileCombo.SelectedIndex = viewModel.SelectedProfileIndex;
            providerCombo.SelectedIndex = viewModel.SelectedProviderIndex;
            modelCombo.SelectedIndex = viewModel.SelectedModelIndex;

            customModelInput.Text = viewModel.CustomModelName;
            customModelLabel.IsVisible = viewModel.IsCustomModelVisible;
            customModelInput.IsVisible = viewModel.IsCustomModelVisible;

            themeCombo.SelectedItem = viewModel.SelectedTheme;
            opacitySlider.Value = viewModel.OverlayOpacity;
            uiScaleSlider.Value = viewModel.UiScale;
            alwaysOnTopCheck.IsChecked = viewModel.AlwaysOnTop;
            clickThroughCheck.IsChecked = viewModel.ClickThroughMode;
            showConsoleCheck.IsChecked = viewModel.ShowConsole;
            hideSidebarCheck.IsChecked = viewModel.HideSidebar;
            cleanChatModeCheck.IsChecked = viewModel.CleanChatMode;
            systemPromptInput.Text = viewModel.SystemPrompt;
            formatCombo.SelectedItem = viewModel.DefaultFormat;
            payloadInput.Text = viewModel.MaxPayloadBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            cursorCheck.IsChecked = viewModel.IncludeCursor;
            silentModeCheck.IsChecked = viewModel.SilentMode;
            defaultPromptInput.Text = viewModel.DefaultPrompt;
            openAiKeyInput.Text = viewModel.OpenAiKey;
            anthropicKeyInput.Text = viewModel.AnthropicKey;
            geminiKeyInput.Text = viewModel.GeminiKey;
            qwenBaseUrlInput.Text = viewModel.QwenBaseUrl;
            qwenCookieInput.Text = viewModel.QwenCookie;
            warnCheck.IsChecked = viewModel.WarnBeforeCloudUpload;
            blockedProcessInput.Text = viewModel.BlockedProcesses;
            blockedTitleInput.Text = viewModel.BlockedTitles;
            regionHotkeyDisplay.Text = viewModel.RegionHotkeyText;
            activeWindowHotkeyDisplay.Text = viewModel.ActiveWindowHotkeyText;
            monitorHotkeyDisplay.Text = viewModel.MonitorHotkeyText;
            cleanChatHotkeyDisplay.Text = viewModel.CleanChatHotkeyText;
            clickThroughHotkeyDisplay.Text = viewModel.ClickThroughHotkeyText;
            emergencyExitHotkeyDisplay.Text = viewModel.EmergencyExitHotkeyText;

            keepSessionHistoryCheck.IsChecked = viewModel.KeepSessionHistory;
            prompt1Input.Text = viewModel.PromptText1;
            prompt1HotkeyDisplay.Text = viewModel.Prompt1HotkeyText;
            prompt2Input.Text = viewModel.PromptText2;
            prompt2HotkeyDisplay.Text = viewModel.Prompt2HotkeyText;
            prompt3Input.Text = viewModel.PromptText3;
            prompt3HotkeyDisplay.Text = viewModel.Prompt3HotkeyText;

            // Managed background proxies
            qwenProxyCheck.IsChecked = viewModel.QwenProxyEnabled;
            qwenProxyPortInput.Text = viewModel.QwenProxyPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
            qwenProxyCookieInput.Text = viewModel.QwenProxyCookie;
            qwenStatusText.Text = $"Status: {viewModel.QwenStatus}";

            deepseekProxyCheck.IsChecked = viewModel.DeepseekProxyEnabled;
            deepseekProxyPortInput.Text = viewModel.DeepseekProxyPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
            deepseekProxyCookieInput.Text = viewModel.DeepseekProxyCookie;
            deepseekStatusText.Text = $"Status: {viewModel.DeepseekStatus}";

            kimiProxyCheck.IsChecked = viewModel.KimiProxyEnabled;
            kimiProxyPortInput.Text = viewModel.KimiProxyPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
            kimiProxyCookieInput.Text = viewModel.KimiProxyCookie;
            kimiStatusText.Text = $"Status: {viewModel.KimiStatus}";
        }
        finally
        {
            isUpdatingSettings = false;
        }

        UpdateProxyButtonsState();
    }

    private void ApplySettingsToViewModel()
    {
        viewModel.SelectedProfileIndex = profileCombo.SelectedIndex >= 0 ? profileCombo.SelectedIndex : 0;
        viewModel.SelectedProviderIndex = providerCombo.SelectedIndex >= 0 ? providerCombo.SelectedIndex : 0;
        viewModel.SelectedModelIndex = modelCombo.SelectedIndex >= 0 ? modelCombo.SelectedIndex : 0;
        viewModel.CustomModelName = customModelInput.Text ?? string.Empty;

        viewModel.SelectedTheme = themeCombo.SelectedItem?.ToString() ?? "system";
        viewModel.OverlayOpacity = opacitySlider.Value;
        viewModel.UiScale = uiScaleSlider.Value;
        viewModel.AlwaysOnTop = alwaysOnTopCheck.IsChecked ?? true;
        viewModel.ClickThroughMode = clickThroughCheck.IsChecked ?? false;
        viewModel.ShowConsole = showConsoleCheck.IsChecked ?? false;
        viewModel.HideSidebar = hideSidebarCheck.IsChecked ?? false;
        viewModel.CleanChatMode = cleanChatModeCheck.IsChecked ?? false;
        viewModel.SystemPrompt = systemPromptInput.Text ?? string.Empty;
        viewModel.DefaultFormat = formatCombo.SelectedItem is ScreenImageFormat fmt ? fmt : ScreenImageFormat.Png;
        viewModel.IncludeCursor = cursorCheck.IsChecked ?? true;
        viewModel.SilentMode = silentModeCheck.IsChecked ?? false;
        viewModel.DefaultPrompt = defaultPromptInput.Text ?? string.Empty;
        viewModel.WarnBeforeCloudUpload = false;
        viewModel.BlockedProcesses = blockedProcessInput.Text ?? string.Empty;
        viewModel.BlockedTitles = blockedTitleInput.Text ?? string.Empty;
        viewModel.RegionHotkeyText = regionHotkeyDisplay.Text ?? string.Empty;
        viewModel.ActiveWindowHotkeyText = activeWindowHotkeyDisplay.Text ?? string.Empty;
        viewModel.MonitorHotkeyText = monitorHotkeyDisplay.Text ?? string.Empty;
        viewModel.CleanChatHotkeyText = cleanChatHotkeyDisplay.Text ?? string.Empty;
        viewModel.ClickThroughHotkeyText = clickThroughHotkeyDisplay.Text ?? string.Empty;
        viewModel.EmergencyExitHotkeyText = emergencyExitHotkeyDisplay.Text ?? string.Empty;
        viewModel.QwenBaseUrl = qwenBaseUrlInput.Text ?? string.Empty;
        viewModel.QwenCookie = qwenCookieInput.Text ?? string.Empty;

        viewModel.KeepSessionHistory = keepSessionHistoryCheck.IsChecked ?? false;
        viewModel.PromptText1 = prompt1Input.Text ?? string.Empty;
        viewModel.Prompt1HotkeyText = prompt1HotkeyDisplay.Text ?? string.Empty;
        viewModel.PromptText2 = prompt2Input.Text ?? string.Empty;
        viewModel.Prompt2HotkeyText = prompt2HotkeyDisplay.Text ?? string.Empty;
        viewModel.PromptText3 = prompt3Input.Text ?? string.Empty;
        viewModel.Prompt3HotkeyText = prompt3HotkeyDisplay.Text ?? string.Empty;

        if (int.TryParse(payloadInput.Text, out int payloadLimit))
        {
            viewModel.MaxPayloadBytes = payloadLimit;
        }

        // Managed proxies
        viewModel.QwenProxyEnabled = qwenProxyCheck.IsChecked ?? false;
        viewModel.QwenProxyCookie = qwenProxyCookieInput.Text ?? string.Empty;
        if (int.TryParse(qwenProxyPortInput.Text, out int qwenPort)) viewModel.QwenProxyPort = qwenPort;

        viewModel.DeepseekProxyEnabled = deepseekProxyCheck.IsChecked ?? false;
        viewModel.DeepseekProxyCookie = deepseekProxyCookieInput.Text ?? string.Empty;
        if (int.TryParse(deepseekProxyPortInput.Text, out int dsPort)) viewModel.DeepseekProxyPort = dsPort;

        viewModel.KimiProxyEnabled = kimiProxyCheck.IsChecked ?? false;
        viewModel.KimiProxyCookie = kimiProxyCookieInput.Text ?? string.Empty;
        if (int.TryParse(kimiProxyPortInput.Text, out int kimiPort)) viewModel.KimiProxyPort = kimiPort;
    }

    private static StackPanel CreateSettingsPanel()
    {
        return new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(4, 16, 16, 20),
            MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    private static TabItem CreateSettingsTab(string title, Control content)
    {
        return new TabItem
        {
            Header = title,
            FontSize = 12,
            Content = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
    }

    private Button CreateButton(string text, ISolidColorBrush background, ISolidColorBrush foreground, bool isClose = false)
    {
        Button btn = new Button
        {
            Content = text,
            Foreground = foreground,
            Background = background,
            BorderThickness = new Thickness(0),
            FontSize = isClose ? 14 : 12,
            FontWeight = FontWeight.Medium,
            Padding = isClose ? new Thickness(0) : new Thickness(12, 6),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = isClose ? new CornerRadius(0) : new CornerRadius(9)
        };

        if (isClose)
        {
            btn.Width = 45;
            btn.Height = 45;
        }

        btn.PointerEntered += (s, e) =>
        {
            if (isClose)
            {
                btn.Background = new SolidColorBrush(Color.FromArgb(255, 220, 38, 38));
            }
            else
            {
                btn.Background = ReferenceEquals(background, AccentColor) ? AccentColorHover : BtnBgHover();
            }
        };

        btn.PointerExited += (s, e) =>
        {
            btn.Background = background;
        };

        return btn;
    }

    private static SolidColorBrush BtnBgNormal() => new(Color.Parse("#1A2134"));
    private static SolidColorBrush BtnBgHover() => new(Color.Parse("#252E47"));

    private static (string MediaType, ScreenImageFormat Format) DetectImageFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return ("image/png", ScreenImageFormat.Png);
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ("image/jpeg", ScreenImageFormat.Jpeg);

        throw new InvalidDataException("Unsupported image format. Use PNG or JPEG.");
    }
    private static SolidColorBrush TextMutedBrush() => new SolidColorBrush(Color.FromArgb(255, 160, 160, 170));

    private void UpdateProxyButtonsState()
    {
        // Qwen
        qwenInstallBtn.IsEnabled = !viewModel.IsQwenInstalled && !viewModel.IsQwenInstalling;
        qwenAuthBtn.IsEnabled = viewModel.IsQwenInstalled && !viewModel.IsQwenInstalling && !viewModel.IsQwenRunning && !viewModel.IsQwenStarting;
        qwenStartBtn.IsEnabled = viewModel.IsQwenInstalled && !viewModel.IsQwenRunning && !viewModel.IsQwenInstalling && !viewModel.IsQwenStarting;
        qwenStopBtn.IsEnabled = viewModel.IsQwenRunning && !viewModel.IsQwenInstalling && !viewModel.IsQwenStarting;

        // Deepseek
        deepseekInstallBtn.IsEnabled = !viewModel.IsDeepseekInstalled && !viewModel.IsDeepseekInstalling;
        deepseekAuthBtn.IsEnabled = viewModel.IsDeepseekInstalled && !viewModel.IsDeepseekInstalling && !viewModel.IsDeepseekRunning && !viewModel.IsDeepseekStarting;
        deepseekStartBtn.IsEnabled = viewModel.IsDeepseekInstalled && !viewModel.IsDeepseekRunning && !viewModel.IsDeepseekInstalling && !viewModel.IsDeepseekStarting;
        deepseekStopBtn.IsEnabled = viewModel.IsDeepseekRunning && !viewModel.IsDeepseekInstalling && !viewModel.IsDeepseekStarting;

        // Kimi
        kimiInstallBtn.IsEnabled = !viewModel.IsKimiInstalled && !viewModel.IsKimiInstalling;
        kimiAuthBtn.IsEnabled = viewModel.IsKimiInstalled && !viewModel.IsKimiInstalling && !viewModel.IsKimiRunning && !viewModel.IsKimiStarting;
        kimiStartBtn.IsEnabled = viewModel.IsKimiInstalled && !viewModel.IsKimiRunning && !viewModel.IsKimiInstalling && !viewModel.IsKimiStarting;
        kimiStopBtn.IsEnabled = viewModel.IsKimiRunning && !viewModel.IsKimiInstalling && !viewModel.IsKimiStarting;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            Rect bounds = (Rect)change.NewValue!;
            if (bounds.Width < 600 && sidebarGrid.IsVisible)
            {
                sidebarGrid.IsVisible = false;
                mainGrid.ColumnDefinitions[0] = new ColumnDefinition(0, GridUnitType.Pixel);
            }
        }
        else if (change.Property == WindowStateProperty)
        {
            WindowState newState = (WindowState)change.NewValue!;
            if (newState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                Hide();
            }
            else if (newState == WindowState.Normal)
            {
                bool desired = viewModel.AlwaysOnTop;
                Topmost = false;
                Topmost = desired;
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (capturingHotkeySlot is not null)
        {
            e.Handled = true;

            if (e.Key == Key.Escape)
            {
                CancelHotkeyCapture();
                return;
            }

            // Check if key is a modifier key
            bool isModifierKey = e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
            if (isModifierKey)
            {
                List<string> activeMods = new();
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) activeMods.Add("Ctrl");
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) activeMods.Add("Shift");
                if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) activeMods.Add("Alt");
                if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) activeMods.Add("Win");
                
                if (activeMods.Count > 0 && activeCaptureButton is not null)
                {
                    activeCaptureButton.Content = string.Join("+", activeMods) + "...";
                }
                return;
            }

            int? vk = GetVirtualKeyFromAvaloniaKey(e.Key);
            if (vk.HasValue)
            {
                HotkeyModifiers modifiers = HotkeyModifiers.NoRepeat;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= HotkeyModifiers.Control;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= HotkeyModifiers.Shift;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= HotkeyModifiers.Alt;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers |= HotkeyModifiers.Windows;

                Hotkey hotkey = new Hotkey(modifiers, vk.Value);
                string hotkeyText = ChatViewModel.FormatHotkey(hotkey);

                if (hotkeySlots.TryGetValue(capturingHotkeySlot, out var slot))
                {
                    slot.Setter(hotkeyText);
                    slot.Display.Text = hotkeyText;
                }

                CancelHotkeyCapture();
            }
            return;
        }

        base.OnKeyDown(e);
        if (e.Key == Key.B && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            sidebarGrid.IsVisible = !sidebarGrid.IsVisible;
            mainGrid.ColumnDefinitions[0] = new ColumnDefinition(sidebarGrid.IsVisible ? 292 : 0, GridUnitType.Pixel);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (capturingHotkeySlot is not null)
        {
            var properties = e.GetCurrentPoint(this).Properties;
            int? vk = null;
            if (properties.IsMiddleButtonPressed)
            {
                vk = 0x04; // VK_MBUTTON
            }
            else if (properties.PointerUpdateKind == PointerUpdateKind.XButton1Pressed)
            {
                vk = 0x05; // VK_XBUTTON1
            }
            else if (properties.PointerUpdateKind == PointerUpdateKind.XButton2Pressed)
            {
                vk = 0x06; // VK_XBUTTON2
            }

            if (vk.HasValue)
            {
                e.Handled = true;
                HotkeyModifiers modifiers = HotkeyModifiers.NoRepeat;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= HotkeyModifiers.Control;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= HotkeyModifiers.Shift;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= HotkeyModifiers.Alt;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers |= HotkeyModifiers.Windows;

                Hotkey hotkey = new Hotkey(modifiers, vk.Value);
                string hotkeyText = ChatViewModel.FormatHotkey(hotkey);

                if (hotkeySlots.TryGetValue(capturingHotkeySlot, out var slot))
                {
                    slot.Setter(hotkeyText);
                    slot.Display.Text = hotkeyText;
                }

                CancelHotkeyCapture();
                return;
            }
        }
        base.OnPointerPressed(e);
    }

    private void StartHotkeyCapture(string slotId, Button button)
    {
        CancelHotkeyCapture();

        capturingHotkeySlot = slotId;
        activeCaptureButton = button;

        button.Content = "Нажмите клавиши...";
        button.Background = new SolidColorBrush(Color.Parse("#DC2626")); // premium red recording background
    }

    private void CancelHotkeyCapture()
    {
        if (capturingHotkeySlot is not null && activeCaptureButton is not null)
        {
            activeCaptureButton.Content = "Задать";
            activeCaptureButton.Background = BtnBgNormal();
        }
        capturingHotkeySlot = null;
        activeCaptureButton = null;
    }

    private static int? GetVirtualKeyFromAvaloniaKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            return 0x41 + (key - Key.A);
        }
        if (key >= Key.D0 && key <= Key.D9)
        {
            return 0x30 + (key - Key.D0);
        }
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return 0x60 + (key - Key.NumPad0);
        }
        if (key >= Key.F1 && key <= Key.F24)
        {
            return 0x70 + (key - Key.F1);
        }
        return key switch
        {
            Key.Back => 0x08,
            Key.Tab => 0x09,
            Key.Enter => 0x0D,
            Key.Escape => 0x1B,
            Key.Space => 0x20,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.End => 0x23,
            Key.Home => 0x24,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            _ => null
        };
    }

    private Grid CreateHotkeyCaptureRow(
        string label,
        string slotId,
        TextBlock display,
        Func<string> getter,
        Action<string> setter)
    {
        hotkeySlots[slotId] = (display, getter, setter);

        Grid rowGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Margin = new Thickness(0, 4)
        };

        StackPanel infoPanel = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock labelText = new TextBlock
        {
            Text = label,
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeight.Medium
        };
        infoPanel.Children.Add(labelText);

        Border badgeBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1E293B")),
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        display.FontSize = 12;
        display.Foreground = new SolidColorBrush(Color.Parse("#38BDF8"));
        display.FontWeight = FontWeight.Bold;
        badgeBorder.Child = display;
        infoPanel.Children.Add(badgeBorder);

        rowGrid.Children.Add(infoPanel);
        Grid.SetColumn(infoPanel, 0);

        Button setBtn = CreateButton("Задать", BtnBgNormal(), Brushes.White);
        setBtn.Margin = new Thickness(8, 0, 0, 0);
        setBtn.VerticalAlignment = VerticalAlignment.Center;
        setBtn.Click += (s, e) =>
        {
            if (capturingHotkeySlot == slotId)
            {
                CancelHotkeyCapture();
            }
            else
            {
                StartHotkeyCapture(slotId, setBtn);
            }
        };
        rowGrid.Children.Add(setBtn);
        Grid.SetColumn(setBtn, 1);

        Button clearBtn = CreateButton("✖", BtnBgNormal(), Brushes.White);
        clearBtn.Margin = new Thickness(6, 0, 0, 0);
        clearBtn.VerticalAlignment = VerticalAlignment.Center;
        clearBtn.Width = 34;
        clearBtn.Height = 34;
        clearBtn.Click += (s, e) =>
        {
            setter("None");
            display.Text = "None";
        };
        rowGrid.Children.Add(clearBtn);
        Grid.SetColumn(clearBtn, 2);

        return rowGrid;
    }
}
