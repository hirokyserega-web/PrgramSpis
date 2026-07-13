using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Controls.Templates;
using Avalonia.Animation;
using Avalonia.Threading;
using ScreenMind.Core.Ai;
using ScreenMind.Core.Imaging;

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
    private Bitmap? activePreview;
    private string? activePreviewSessionId;
    private string? renderedSessionId;
    private int renderedMessageCount;

    // Settings panel controls
    private readonly Grid mainGrid;
    private readonly Border settingsOverlay;
    private readonly Border privacyWarningOverlay;
    private readonly ComboBox themeCombo;
    private readonly Slider opacitySlider;
    private readonly CheckBox alwaysOnTopCheck;
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
    private readonly TextBox regionHotkeyInput;
    private readonly TextBox activeWindowHotkeyInput;
    private readonly TextBox monitorHotkeyInput;
    private readonly ComboBox profileCombo;
    private readonly ComboBox providerCombo;
    private readonly ComboBox modelCombo;
    private readonly TextBox customModelInput;
    private readonly TextBlock customModelLabel;
    private readonly TextBox qwenBaseUrlInput;
    private readonly TextBox qwenCookieInput;
    private readonly CheckBox silentModeCheck;
    private readonly TextBox defaultPromptInput;

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

    // Deep-space palette: calm contrast, restrained violet accent, layered glass surfaces.
    private static readonly ISolidColorBrush WindowBg = new SolidColorBrush(Color.FromArgb(244, 8, 11, 20));
    private static readonly ISolidColorBrush SidebarBg = new SolidColorBrush(Color.FromArgb(220, 12, 16, 29));
    private static readonly ISolidColorBrush BorderColor = new SolidColorBrush(Color.Parse("#27304A"));
    private static readonly ISolidColorBrush HeaderBg = new SolidColorBrush(Color.FromArgb(210, 13, 17, 30));
    private static readonly ISolidColorBrush AccentColor = new SolidColorBrush(Color.Parse("#7168F6"));
    private static readonly ISolidColorBrush AccentColorHover = new SolidColorBrush(Color.Parse("#887FFF"));
    private static readonly ISolidColorBrush UserBubbleBg = new SolidColorBrush(Color.Parse("#5B54D8"));
    private static readonly ISolidColorBrush AssistantBubbleBg = new SolidColorBrush(Color.Parse("#171D2E"));
    private static readonly ISolidColorBrush InputBg = new SolidColorBrush(Color.Parse("#111729"));

    public ChatViewModel ViewModel => viewModel;

    public void Dispose()
    {
        activePreview?.Dispose();
        activePreview = null;
        GC.SuppressFinalize(this);
    }

    public ChatWindow(ChatViewModel viewModel)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;

        // Window config
        Title = "ScreenMind — visual AI workspace";
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = true;
        ShowInTaskbar = false;
        Width = 900;
        Height = 650;
        MinWidth = 680;
        MinHeight = 500;
        Topmost = viewModel.AlwaysOnTop;
        Background = Brushes.Transparent;
        TransparencyLevelHint =
        [
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Transparent,
            WindowTransparencyLevel.None,
        ];

        // Root Border
        Border rootBorder = new Border
        {
            Background = WindowBg,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            ClipToBounds = true
        };

        mainGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("252,*,*")
        };
        rootBorder.Child = mainGrid;
        Content = rootBorder;

        // Left Panel: Sidebar
        Grid sidebarGrid = new Grid
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
                var border = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6),
                    Margin = new Thickness(0, 1),
                    Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255))
                };
                
                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
                };

                var icon = new TextBlock
                {
                    Text = "💬",
                    Foreground = AccentColorHover,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12
                };
                Grid.SetColumn(icon, 0);
                grid.Children.Add(icon);

                var title = new TextBlock
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

                var deleteBtn = new Button
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

        Button settingsBtn = CreateButton("⚙ Settings", BtnBgNormal(), Brushes.White);
        settingsBtn.Margin = new Thickness(8);
        settingsBtn.Click += async (s, e) => await viewModel.LoadSettingsAsync();
        sidebarGrid.Children.Add(settingsBtn);
        Grid.SetRow(settingsBtn, 3);

        mainGrid.Children.Add(sidebarGrid);
        Grid.SetColumn(sidebarGrid, 0);

        // Right Panel: Active Chat
        Grid chatGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("68,Auto,*,Auto")
        };
        Grid.SetColumnSpan(chatGrid, 2); // spans center and right initially

        // Chat Header
        Grid headerGrid = new Grid
        {
            Background = HeaderBg,
            ColumnDefinitions = new ColumnDefinitions("*,45")
        };
        headerGrid.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };

        TextBlock titleText = new TextBlock
        {
            Text = "Screen assistant",
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            FontSize = 16,
            Margin = new Thickness(24, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        headerGrid.Children.Add(titleText);
        Grid.SetColumn(titleText, 0);

        Button windowCloseBtn = CreateButton("✕", BtnBgNormal(), Brushes.White, isClose: true);
        windowCloseBtn.Click += (s, e) => viewModel.CloseChat();
        headerGrid.Children.Add(windowCloseBtn);
        Grid.SetColumn(windowCloseBtn, 1);

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
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
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
                viewModel.CreateSessionFromImage(screenImage);
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

        mainGrid.Children.Add(chatGrid);
        Grid.SetColumn(chatGrid, 1);

        // Settings Overlay panel
        settingsOverlay = new Border
        {
            Background = WindowBg,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1, 0, 0, 0),
            IsVisible = false
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
            if (providerCombo.SelectedIndex >= 0)
            {
                viewModel.SelectedProviderIndex = providerCombo.SelectedIndex;
                modelCombo.ItemsSource = viewModel.AvailableModels;
                modelCombo.SelectedIndex = viewModel.SelectedModelIndex;
            }
        };

        modelCombo.SelectionChanged += (s, e) =>
        {
            if (modelCombo.SelectedIndex >= 0)
            {
                viewModel.SelectedModelIndex = modelCombo.SelectedIndex;
                customModelLabel.IsVisible = viewModel.IsCustomModelVisible;
                customModelInput.IsVisible = viewModel.IsCustomModelVisible;
            }
        };

        customModelInput.TextChanged += (s, e) =>
        {
            viewModel.CustomModelName = customModelInput.Text ?? string.Empty;
        };

        profileCombo.SelectionChanged += (s, e) =>
        {
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
        themeCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        themeCombo.ItemsSource = new[] { "system", "dark", "light" };
        scrollPanel.Children.Add(themeCombo);

        // Opacity
        scrollPanel.Children.Add(CreateHeaderLabel("Overlay Opacity"));
        opacitySlider = new Slider { Minimum = 0.5, Maximum = 1.0, TickFrequency = 0.05, IsSnapToTickEnabled = true };
        scrollPanel.Children.Add(opacitySlider);

        alwaysOnTopCheck = new CheckBox { Content = "Keep ScreenMind above other windows", Foreground = Brushes.White };
        alwaysOnTopCheck.IsCheckedChanged += (_, _) => Topmost = alwaysOnTopCheck.IsChecked ?? true;
        scrollPanel.Children.Add(alwaysOnTopCheck);

        scrollPanel = aiPanel;
        scrollPanel.Children.Add(CreateHeaderLabel("System prompt for selected profile"));
        systemPromptInput = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 110,
            MaxHeight = 220,
            Watermark = "Instructions applied to text and screenshot requests",
        };
        scrollPanel.Children.Add(systemPromptInput);

        scrollPanel = capturePanel;

        // Capture Format
        scrollPanel.Children.Add(CreateHeaderLabel("Default Image Format"));
        formatCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        formatCombo.ItemsSource = Enum.GetValues<ScreenImageFormat>();
        scrollPanel.Children.Add(formatCombo);

        // Max Payload Size
        scrollPanel.Children.Add(CreateHeaderLabel("Max Payload Limit (bytes)"));
        payloadInput = new TextBox { Watermark = "8388608" };
        scrollPanel.Children.Add(payloadInput);

        // Include Cursor Check
        cursorCheck = new CheckBox { Content = "Include mouse cursor in captures", Foreground = Brushes.White };
        scrollPanel.Children.Add(cursorCheck);

        // Silent Capture Mode Check
        silentModeCheck = new CheckBox { Content = "Silent capture mode (send straight to chat via hotkeys without overlay)", Foreground = Brushes.White };
        scrollPanel.Children.Add(silentModeCheck);

        // Default Prompt for Silent Capture
        scrollPanel.Children.Add(CreateHeaderLabel("Default Prompt for Silent Captures (e.g. Что на экране? / Explain this)"));
        defaultPromptInput = new TextBox { Watermark = "What is on my screen?" };
        scrollPanel.Children.Add(defaultPromptInput);

        scrollPanel = providersPanel;

        // API Keys (Secrets)
        scrollPanel.Children.Add(CreateHeaderLabel("OpenAI API Key"));
        openAiKeyInput = new TextBox { PasswordChar = '*', Watermark = "sk-..." };
        scrollPanel.Children.Add(openAiKeyInput);

        scrollPanel.Children.Add(CreateHeaderLabel("Anthropic API Key"));
        anthropicKeyInput = new TextBox { PasswordChar = '*', Watermark = "sk-ant-..." };
        scrollPanel.Children.Add(anthropicKeyInput);

        scrollPanel.Children.Add(CreateHeaderLabel("Gemini API Key"));
        geminiKeyInput = new TextBox { PasswordChar = '*', Watermark = "AIzaSy..." };
        scrollPanel.Children.Add(geminiKeyInput);

        scrollPanel.Children.Add(new Border
        {
            Height = 1,
            Background = BorderColor,
            Margin = new Thickness(0, 8, 0, 4)
        });

        // FreeQwenApi / OpenAI-Compatible section
        scrollPanel.Children.Add(CreateHeaderLabel("🌐 FreeQwenApi / OpenAI-Compatible Base URL"));
        qwenBaseUrlInput = new TextBox { Watermark = "http://localhost:3264/api" };
        scrollPanel.Children.Add(qwenBaseUrlInput);

        scrollPanel.Children.Add(CreateHeaderLabel("FreeQwenApi Cookie (optional, from browser DevTools)"));
        qwenCookieInput = new TextBox { PasswordChar = '*', Watermark = "cna=...; acw_tc=...", AcceptsReturn = false };
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

        // Customizable Hotkeys
        scrollPanel.Children.Add(CreateHeaderLabel("Capture Region Hotkey (e.g. Ctrl+Shift+A)"));
        regionHotkeyInput = new TextBox { Watermark = "Ctrl+Shift+A" };
        scrollPanel.Children.Add(regionHotkeyInput);

        scrollPanel.Children.Add(CreateHeaderLabel("Capture Active Window Hotkey (e.g. Ctrl+Shift+S)"));
        activeWindowHotkeyInput = new TextBox { Watermark = "Ctrl+Shift+S" };
        scrollPanel.Children.Add(activeWindowHotkeyInput);

        scrollPanel.Children.Add(CreateHeaderLabel("Capture Monitor Hotkey (e.g. Ctrl+Shift+D)"));
        monitorHotkeyInput = new TextBox { Watermark = "Ctrl+Shift+D" };
        scrollPanel.Children.Add(monitorHotkeyInput);

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
        Closed += (_, _) => Dispose();
        viewModel.CloseRequested += (s, e) => Close();

        UpdateSessionsView();
        UpdateMessagesView();
        UpdateSettingsView();
        UpdateErrorBanner();
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
                settingsOverlay.IsVisible = viewModel.IsSettingsVisible;
                if (viewModel.IsSettingsVisible)
                {
                    UpdateSettingsView();
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
                Opacity = viewModel.OverlayOpacity;
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

            sendBtn.IsEnabled = viewModel.CanSend;
        });
    }

    private void UpdateErrorBanner()
    {
        errorText.Text = viewModel.ErrorMessage;
        errorBanner.IsVisible = !string.IsNullOrWhiteSpace(viewModel.ErrorMessage);
    }

    private void UpdateSessionsView()
    {
        sessionsListBox.ItemsSource = viewModel.Sessions;
        sessionsListBox.SelectedItem = viewModel.ActiveSession;
    }

    private void UpdateMessagesView()
    {
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
            if (!string.Equals(activePreviewSessionId, viewModel.ActiveSession.Id, StringComparison.Ordinal))
            {
                activePreview?.Dispose();
                using MemoryStream stream = new(viewModel.ActiveSession.Image.Bytes.ToArray(), writable: false);
                activePreview = new Bitmap(stream);
                activePreviewSessionId = viewModel.ActiveSession.Id;
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
        profileCombo.ItemsSource = viewModel.ProfileNames;
        profileCombo.SelectedIndex = viewModel.SelectedProfileIndex;

        providerCombo.ItemsSource = viewModel.AvailableProviders;
        providerCombo.SelectedIndex = viewModel.SelectedProviderIndex;

        modelCombo.ItemsSource = viewModel.AvailableModels;
        modelCombo.SelectedIndex = viewModel.SelectedModelIndex;

        customModelInput.Text = viewModel.CustomModelName;
        customModelLabel.IsVisible = viewModel.IsCustomModelVisible;
        customModelInput.IsVisible = viewModel.IsCustomModelVisible;

        themeCombo.SelectedItem = viewModel.SelectedTheme;
        opacitySlider.Value = viewModel.OverlayOpacity;
        alwaysOnTopCheck.IsChecked = viewModel.AlwaysOnTop;
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
        regionHotkeyInput.Text = viewModel.RegionHotkeyText;
        activeWindowHotkeyInput.Text = viewModel.ActiveWindowHotkeyText;
        monitorHotkeyInput.Text = viewModel.MonitorHotkeyText;

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
        viewModel.AlwaysOnTop = alwaysOnTopCheck.IsChecked ?? true;
        viewModel.SystemPrompt = systemPromptInput.Text ?? string.Empty;
        viewModel.DefaultFormat = formatCombo.SelectedItem is ScreenImageFormat fmt ? fmt : ScreenImageFormat.Png;
        viewModel.IncludeCursor = cursorCheck.IsChecked ?? true;
        viewModel.SilentMode = silentModeCheck.IsChecked ?? false;
        viewModel.DefaultPrompt = defaultPromptInput.Text ?? string.Empty;
        viewModel.WarnBeforeCloudUpload = false;
        viewModel.BlockedProcesses = blockedProcessInput.Text ?? string.Empty;
        viewModel.BlockedTitles = blockedTitleInput.Text ?? string.Empty;
        viewModel.RegionHotkeyText = regionHotkeyInput.Text ?? string.Empty;
        viewModel.ActiveWindowHotkeyText = activeWindowHotkeyInput.Text ?? string.Empty;
        viewModel.MonitorHotkeyText = monitorHotkeyInput.Text ?? string.Empty;
        viewModel.QwenBaseUrl = qwenBaseUrlInput.Text ?? string.Empty;
        viewModel.QwenCookie = qwenCookieInput.Text ?? string.Empty;

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

    private static Button CreateButton(string text, ISolidColorBrush background, ISolidColorBrush foreground, bool isClose = false)
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
}
