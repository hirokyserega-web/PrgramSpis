using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenMind.Core.Ai;
using ScreenMind.Core.Capture;
using ScreenMind.Core.Hotkeys;
using ScreenMind.Core.Imaging;
using ScreenMind.Core.Privacy;
using ScreenMind.Core.Settings;
using ScreenMind.Core.State;

namespace ScreenMind.UI.Chat;

public sealed partial class ChatViewModel : ObservableObject
{
    private static readonly TimeSpan StreamUiUpdateInterval = TimeSpan.FromMilliseconds(50);

    private readonly IChatSessionManager sessionManager;
    private readonly IAiOrchestrator orchestrator;
    private readonly ISettingsStore settingsStore;
    private readonly ISecretStore secretStore;
    private readonly IHotkeyService hotkeyService;

    public event EventHandler? NewCaptureRequested;
    public event EventHandler? ActiveWindowCaptureRequested;
    public event EventHandler? MonitorCaptureRequested;

    [ObservableProperty]
    private string regionHotkeyText = string.Empty;

    [ObservableProperty]
    private string activeWindowHotkeyText = string.Empty;

    [ObservableProperty]
    private string monitorHotkeyText = string.Empty;

    [ObservableProperty]
    private ChatSession? activeSession;

    [ObservableProperty]
    private string inputText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private bool isSending;

    [ObservableProperty]
    private bool isSettingsVisible;

    // Settings properties bound to UI
    [ObservableProperty]
    private string selectedTheme = "system";

    [ObservableProperty]
    private double overlayOpacity = 0.96d;

    [ObservableProperty]
    private bool alwaysOnTop = true;

    [ObservableProperty]
    private string systemPrompt = string.Empty;

    [ObservableProperty]
    private ScreenImageFormat defaultFormat = ScreenImageFormat.Png;

    [ObservableProperty]
    private int maxPayloadBytes = 8 * 1024 * 1024;

    [ObservableProperty]
    private bool includeCursor = true;

    [ObservableProperty]
    private bool silentMode;

    [ObservableProperty]
    private string defaultPrompt = string.Empty;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool isPrivacyWarningActive;

    [ObservableProperty]
    private string openAiKey = string.Empty;

    [ObservableProperty]
    private string anthropicKey = string.Empty;

    [ObservableProperty]
    private string geminiKey = string.Empty;

    [ObservableProperty]
    private bool warnBeforeCloudUpload = true;

    [ObservableProperty]
    private string blockedProcesses = string.Empty;

    [ObservableProperty]
    private string blockedTitles = string.Empty;

    [ObservableProperty]
    private int selectedProfileIndex;

    [ObservableProperty]
    private int selectedProviderIndex;

    [ObservableProperty]
    private int selectedModelIndex;

    [ObservableProperty]
    private string customModelName = string.Empty;

    [ObservableProperty]
    private bool isCustomModelVisible;

    private List<string> availableProviders = new();
    public List<string> AvailableProviders
    {
        get => availableProviders;
        private set => SetProperty(ref availableProviders, value);
    }

    private List<string> availableModels = new();
    public List<string> AvailableModels
    {
        get => availableModels;
        private set => SetProperty(ref availableModels, value);
    }

    private static readonly List<string> ProviderIds = new()
    {
        "openai",
        "anthropic",
        "gemini",
        "ollama",
        "qwen",
        "deepseek",
        "kimi",
        "openai-compatible"
    };

    private bool isSyncingSelection;

    [ObservableProperty]
    private string qwenBaseUrl = string.Empty;

    [ObservableProperty]
    private string qwenCookie = string.Empty;

    // Managed proxies properties
    [ObservableProperty]
    private bool qwenProxyEnabled;
    [ObservableProperty]
    private int qwenProxyPort = 3264;
    [ObservableProperty]
    private string qwenProxyCookie = string.Empty;

    [ObservableProperty]
    private bool deepseekProxyEnabled;
    [ObservableProperty]
    private int deepseekProxyPort = 9655;
    [ObservableProperty]
    private string deepseekProxyCookie = string.Empty;

    [ObservableProperty]
    private bool kimiProxyEnabled;
    [ObservableProperty]
    private int kimiProxyPort = 3265;
    [ObservableProperty]
    private string kimiProxyCookie = string.Empty;

    [ObservableProperty]
    private bool isQwenInstalled;
    [ObservableProperty]
    private bool isQwenRunning;

    [ObservableProperty]
    private bool isDeepseekInstalled;
    [ObservableProperty]
    private bool isDeepseekRunning;

    [ObservableProperty]
    private bool isKimiInstalled;
    [ObservableProperty]
    private bool isKimiRunning;

    [ObservableProperty]
    private string qwenStatus = "Not Installed, Stopped";
    [ObservableProperty]
    private string deepseekStatus = "Not Installed, Stopped";
    [ObservableProperty]
    private string kimiStatus = "Not Installed, Stopped";

    [ObservableProperty]
    private bool isQwenInstalling;
    [ObservableProperty]
    private bool isQwenStarting;

    [ObservableProperty]
    private bool isDeepseekInstalling;
    [ObservableProperty]
    private bool isDeepseekStarting;

    [ObservableProperty]
    private bool isKimiInstalling;
    [ObservableProperty]
    private bool isKimiStarting;

    private readonly IExternalProxyManager proxyManager;

    public List<string> ProfileNames { get; private set; } = new();
    public List<AiProfile> AvailableProfiles { get; private set; } = new();

    public ChatViewModel(
        IChatSessionManager sessionManager,
        IAiOrchestrator orchestrator,
        ISettingsStore settingsStore,
        ISecretStore secretStore,
        IHotkeyService hotkeyService,
        IExternalProxyManager proxyManager)
    {
        this.sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        this.orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        this.hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
        this.proxyManager = proxyManager ?? throw new ArgumentNullException(nameof(proxyManager));

        ActiveSession = sessionManager.CurrentSession;
    }

    public ObservableCollection<ChatSession> Sessions => new(sessionManager.Sessions);

    public IReadOnlyList<AiMessage> ActiveMessages =>
        ActiveSession is not null
            ? ActiveSession.Messages.ToArray()
            : Array.Empty<AiMessage>();

    public bool CanSend => !IsSending && !string.IsNullOrWhiteSpace(InputText);

    public event EventHandler? CloseRequested;

    partial void OnInputTextChanged(string value)
    {
        OnPropertyChanged(nameof(CanSend));
    }

    partial void OnActiveSessionChanged(ChatSession? value)
    {
        OnPropertyChanged(nameof(ActiveMessages));
        OnPropertyChanged(nameof(CanSend));
    }

    partial void OnSelectedProfileIndexChanged(int value)
    {
        if (isSyncingSelection) return;
        SyncDropdownsFromProfile();
        SystemPrompt = value >= 0 && value < AvailableProfiles.Count
            ? AvailableProfiles[value].SystemPrompt
            : string.Empty;
    }

    partial void OnSystemPromptChanged(string value)
    {
        if (isSyncingSelection || SelectedProfileIndex < 0 || SelectedProfileIndex >= AvailableProfiles.Count) return;
        AvailableProfiles[SelectedProfileIndex] = AvailableProfiles[SelectedProfileIndex] with { SystemPrompt = value.Trim() };
    }

    partial void OnSelectedProviderIndexChanged(int value)
    {
        if (isSyncingSelection) return;
        UpdateAvailableModelsList();
        SelectedModelIndex = 0;
        IsCustomModelVisible = AvailableModels.Count > 0 && AvailableModels[SelectedModelIndex] == "custom";
        SyncProfileFromDropdowns();
    }

    partial void OnSelectedModelIndexChanged(int value)
    {
        if (isSyncingSelection) return;
        IsCustomModelVisible = value >= 0 && value < AvailableModels.Count && AvailableModels[value] == "custom";
        SyncProfileFromDropdowns();
    }

    partial void OnCustomModelNameChanged(string value)
    {
        if (isSyncingSelection) return;
        SyncProfileFromDropdowns();
    }

    public void UpdateProviderList()
    {
        List<string> list = new List<string>();

        string openAiStatus = !string.IsNullOrWhiteSpace(OpenAiKey) ? "Connected" : "Not Configured";
        list.Add($"OpenAI (Cloud) - {openAiStatus}");

        string anthropicStatus = !string.IsNullOrWhiteSpace(AnthropicKey) ? "Connected" : "Not Configured";
        list.Add($"Anthropic (Cloud) - {anthropicStatus}");

        string geminiStatus = !string.IsNullOrWhiteSpace(GeminiKey) ? "Connected" : "Not Configured";
        list.Add($"Gemini (Cloud) - {geminiStatus}");

        list.Add("Ollama (Local)");

        string qwenStatus = IsQwenRunning ? "Running" : "Stopped";
        list.Add($"Qwen (Local Proxy) - {qwenStatus}");

        string deepseekStatus = IsDeepseekRunning ? "Running" : "Stopped";
        list.Add($"Deepseek (Local Proxy) - {deepseekStatus}");

        string kimiStatus = IsKimiRunning ? "Running" : "Stopped";
        list.Add($"Kimi (Local Proxy) - {kimiStatus}");

        list.Add("Custom OpenAI-Compatible");

        AvailableProviders = list;
    }

    private void UpdateAvailableModelsList()
    {
        if (SelectedProviderIndex < 0 || SelectedProviderIndex >= ProviderIds.Count) return;

        string prov = ProviderIds[SelectedProviderIndex];
        List<string> models = new List<string>();
        switch (prov)
        {
            case "openai":
                models.Add("gpt-4o-mini");
                models.Add("gpt-4o");
                break;
            case "anthropic":
                models.Add("claude-3-5-sonnet");
                models.Add("claude-3-haiku");
                break;
            case "gemini":
                models.Add("gemini-1.5-flash");
                models.Add("gemini-1.5-pro");
                break;
            case "ollama":
                models.Add("llama3");
                models.Add("mistral");
                models.Add("phi3");
                models.Add("custom");
                break;
            case "qwen":
                models.Add("qwen3.7-max");
                models.Add("qwen3-vl-plus");
                break;
            case "deepseek":
                models.Add("deepseek-chat");
                models.Add("deepseek-reasoner");
                break;
            case "kimi":
                models.Add("kimi");
                break;
            case "openai-compatible":
                models.Add("custom");
                break;
        }
        AvailableModels = models;
    }

    private void SyncDropdownsFromProfile()
    {
        if (isSyncingSelection) return;
        if (SelectedProfileIndex < 0 || SelectedProfileIndex >= AvailableProfiles.Count) return;

        isSyncingSelection = true;
        try
        {
            AiProfile profile = AvailableProfiles[SelectedProfileIndex];

            // Determine provider index
            int provIdx = 0;
            if (profile.ProviderId == "openai-compatible")
            {
                if (profile.ModelId.StartsWith("qwen", StringComparison.OrdinalIgnoreCase)) provIdx = 4;
                else if (profile.ModelId.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase)) provIdx = 5;
                else if (profile.ModelId.StartsWith("kimi", StringComparison.OrdinalIgnoreCase) || profile.ModelId.StartsWith("glm", StringComparison.OrdinalIgnoreCase)) provIdx = 6;
                else provIdx = 7;
            }
            else
            {
                provIdx = ProviderIds.IndexOf(profile.ProviderId);
                if (provIdx < 0) provIdx = 0;
            }

            SelectedProviderIndex = provIdx;

            // Populate models list
            UpdateAvailableModelsList();

            // Find matching model index
            int modelIdx = AvailableModels.IndexOf(profile.ModelId);
            if (modelIdx >= 0)
            {
                SelectedModelIndex = modelIdx;
                CustomModelName = string.Empty;
                IsCustomModelVisible = false;
            }
            else
            {
                // It's a custom model name
                int customIdx = AvailableModels.IndexOf("custom");
                if (customIdx >= 0)
                {
                    SelectedModelIndex = customIdx;
                    CustomModelName = profile.ModelId;
                    IsCustomModelVisible = true;
                }
                else
                {
                    SelectedModelIndex = 0;
                    CustomModelName = string.Empty;
                    IsCustomModelVisible = false;
                }
            }
        }
        finally
        {
            isSyncingSelection = false;
        }
    }

    private void SyncProfileFromDropdowns()
    {
        if (isSyncingSelection) return;
        if (SelectedProfileIndex < 0 || SelectedProfileIndex >= AvailableProfiles.Count) return;

        isSyncingSelection = true;
        try
        {
            AiProfile oldProfile = AvailableProfiles[SelectedProfileIndex];
            string providerId = oldProfile.ProviderId;
            string modelId = oldProfile.ModelId;

            // Map provider index to string
            if (SelectedProviderIndex >= 0 && SelectedProviderIndex < ProviderIds.Count)
            {
                string prov = ProviderIds[SelectedProviderIndex];
                if (prov == "qwen" || prov == "deepseek" || prov == "kimi")
                {
                    providerId = "openai-compatible";
                }
                else
                {
                    providerId = prov;
                }
            }

            // Map model index to string
            if (SelectedModelIndex >= 0 && SelectedModelIndex < AvailableModels.Count)
            {
                string model = AvailableModels[SelectedModelIndex];
                if (model == "custom")
                {
                    modelId = CustomModelName.Trim();
                }
                else
                {
                    modelId = model;
                }
            }

            AiProfile newProfile = oldProfile with { ProviderId = providerId, ModelId = modelId };
            AvailableProfiles[SelectedProfileIndex] = newProfile;

            // Re-create the profile names list to update display
            ProfileNames = AvailableProfiles.Select(p => p.DisplayName).ToList();
            OnPropertyChanged(nameof(ProfileNames));
        }
        finally
        {
            isSyncingSelection = false;
        }
    }

    [RelayCommand]
    public void SelectSession(ChatSession session)
    {
        if (session is not null)
        {
            sessionManager.ActivateSession(session.Id);
            ActiveSession = sessionManager.CurrentSession;
        }
    }

    [RelayCommand]
    public void DeleteSession(ChatSession session)
    {
        if (session is not null)
        {
            sessionManager.DeleteSession(session.Id);
            ActiveSession = sessionManager.CurrentSession;
            OnPropertyChanged(nameof(Sessions));
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    public async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText) || IsSending)
        {
            return;
        }

        // Auto-create a session if none exists
        if (ActiveSession is null)
        {
            ScreenMindSettings settings = await settingsStore.LoadAsync(CancellationToken.None);
            AiProfile profile = settings.Profiles.Items.FirstOrDefault(p => p.Id == settings.Profiles.SelectedProfileId)
                ?? settings.Profiles.Items.FirstOrDefault()
                ?? new AiProfile("universal", "Universal", "openai", "gpt-4o-mini", "Analyze the screenshot and answer clearly.");

            // Create a text-only session with a 1x1 valid transparent PNG placeholder image
            byte[] pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");
            ScreenImage placeholder = new(pngBytes, "image/png", ScreenImageFormat.Png, 1, 1, DateTimeOffset.UtcNow);
            ChatSession createdSession = sessionManager.CreateSession(profile, placeholder);
            ActiveSession = createdSession;
            OnPropertyChanged(nameof(Sessions));
        }

        string userText = InputText.Trim();
        InputText = string.Empty;
        IsSending = true;
        ErrorMessage = string.Empty;

        ChatSession session = ActiveSession ?? throw new InvalidOperationException("Active chat session is not available.");

        AiMessage userMessage = new(AiMessageRole.User, userText, DateTimeOffset.UtcNow);
        session.Messages.Add(userMessage);
        OnPropertyChanged(nameof(ActiveMessages));

        AiMessage assistantMessage = new(AiMessageRole.Assistant, string.Empty, DateTimeOffset.UtcNow);
        session.Messages.Add(assistantMessage);
        OnPropertyChanged(nameof(ActiveMessages));

        CancellationTokenSource cts = new();
        try
        {
            AiRequest request = new(
                session.Profile,
                session.Image,
                userText,
                session.Messages.Take(session.Messages.Count - 2).ToArray());

            StringBuilder accumulatedText = new();
            DateTimeOffset lastUiUpdate = DateTimeOffset.MinValue;
            int assistantIndex = session.Messages.Count - 1;

            void PublishAssistantMessage(bool force)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (!force && now - lastUiUpdate < StreamUiUpdateInterval)
                {
                    return;
                }

                lastUiUpdate = now;
                session.Messages[assistantIndex] = new AiMessage(
                    AiMessageRole.Assistant,
                    accumulatedText.ToString(),
                    assistantMessage.CreatedAt);
                OnPropertyChanged(nameof(ActiveMessages));
            }

            await Task.Yield();
            await foreach (AiStreamEvent ev in orchestrator.AnalyzeAsync(request, cts.Token).ConfigureAwait(false))
            {
                if (ev is AiStreamEvent.TextDelta delta)
                {
                    accumulatedText.Append(delta.Text);
                    PublishAssistantMessage(force: false);
                }
                else if (ev is AiStreamEvent.Failed failed)
                {
                    throw new InvalidOperationException(failed.Error.Message);
                }
            }

            PublishAssistantMessage(force: true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            int index = session.Messages.Count - 1;
            session.Messages[index] = new AiMessage(AiMessageRole.Assistant, $"Error: {ex.Message}", assistantMessage.CreatedAt);
            OnPropertyChanged(nameof(ActiveMessages));
        }
        finally
        {
            IsSending = false;
            OnPropertyChanged(nameof(CanSend));
        }
    }

    [RelayCommand]
    public async Task LoadSettingsAsync()
    {
        ScreenMindSettings settings = await settingsStore.LoadAsync(CancellationToken.None);
        SelectedTheme = settings.Ui.Theme;
        OverlayOpacity = settings.Ui.OverlayOpacity;
        AlwaysOnTop = settings.Ui.AlwaysOnTop;
        DefaultFormat = settings.Capture.DefaultFormat;
        MaxPayloadBytes = settings.Capture.MaxPayloadBytes;
        IncludeCursor = settings.Capture.IncludeCursor;
        SilentMode = settings.Capture.SilentMode;
        DefaultPrompt = settings.Capture.DefaultPrompt;
        WarnBeforeCloudUpload = settings.Privacy.WarnBeforeCloudUpload;

        BlockedProcesses = string.Join(Environment.NewLine, settings.Privacy.BlockedProcessNames);
        BlockedTitles = string.Join(Environment.NewLine, settings.Privacy.BlockedWindowTitleFragments);

        OpenAiKey = await secretStore.GetAsync("openai-api-key", CancellationToken.None) ?? string.Empty;
        AnthropicKey = await secretStore.GetAsync("anthropic-api-key", CancellationToken.None) ?? string.Empty;
        GeminiKey = await secretStore.GetAsync("gemini-api-key", CancellationToken.None) ?? string.Empty;

        // FreeQwenApi / OpenAI-Compatible settings
        if (settings.Providers.Providers.TryGetValue("openai-compatible", out ProviderEndpointSettings? compatProvider))
        {
            QwenBaseUrl = compatProvider.BaseUrl ?? string.Empty;
        }
        QwenCookie = await secretStore.GetAsync("qwen-cookie", CancellationToken.None) ?? string.Empty;

        // Load managed proxies settings
        QwenProxyEnabled = settings.ManagedProxies.Qwen.Enabled;
        QwenProxyPort = settings.ManagedProxies.Qwen.Port;
        QwenProxyCookie = await secretStore.GetAsync("managed-qwen-cookie", CancellationToken.None) ?? string.Empty;

        DeepseekProxyEnabled = settings.ManagedProxies.Deepseek.Enabled;
        DeepseekProxyPort = settings.ManagedProxies.Deepseek.Port;
        DeepseekProxyCookie = await secretStore.GetAsync("managed-deepseek-cookie", CancellationToken.None) ?? string.Empty;

        KimiProxyEnabled = settings.ManagedProxies.GlmKimi.Enabled;
        KimiProxyPort = settings.ManagedProxies.GlmKimi.Port;
        KimiProxyCookie = await secretStore.GetAsync("managed-kimi-cookie", CancellationToken.None) ?? string.Empty;

        // Load installation and running statuses
        IsQwenInstalled = await proxyManager.IsInstalledAsync("FreeQwenApi", CancellationToken.None);
        IsQwenRunning = await proxyManager.IsRunningAsync("FreeQwenApi", CancellationToken.None);
        QwenStatus = $"{(IsQwenInstalled ? "Installed" : "Not Installed")}, {(IsQwenRunning ? "Running" : "Stopped")}";

        IsDeepseekInstalled = await proxyManager.IsInstalledAsync("FreeDeepseekAPI", CancellationToken.None);
        IsDeepseekRunning = await proxyManager.IsRunningAsync("FreeDeepseekAPI", CancellationToken.None);
        DeepseekStatus = $"{(IsDeepseekInstalled ? "Installed" : "Not Installed")}, {(IsDeepseekRunning ? "Running" : "Stopped")}";

        IsKimiInstalled = await proxyManager.IsInstalledAsync("FreeGLMKimiAPI", CancellationToken.None);
        IsKimiRunning = await proxyManager.IsRunningAsync("FreeGLMKimiAPI", CancellationToken.None);
        KimiStatus = $"{(IsKimiInstalled ? "Installed" : "Not Installed")}, {(IsKimiRunning ? "Running" : "Stopped")}";

        RegionHotkeyText = FormatHotkey(settings.Hotkeys.CaptureRegion);
        ActiveWindowHotkeyText = FormatHotkey(settings.Hotkeys.CaptureActiveWindow);
        MonitorHotkeyText = FormatHotkey(settings.Hotkeys.CaptureMonitor);

        AvailableProfiles = settings.Profiles.Items.ToList();
        ProfileNames = AvailableProfiles.Select(p => p.DisplayName).ToList();
        OnPropertyChanged(nameof(ProfileNames));
        SelectedProfileIndex = Math.Max(0, AvailableProfiles.FindIndex(p => p.Id == settings.Profiles.SelectedProfileId));

        UpdateProviderList();
        SyncDropdownsFromProfile();
        SystemPrompt = AvailableProfiles.Count > SelectedProfileIndex
            ? AvailableProfiles[SelectedProfileIndex].SystemPrompt
            : string.Empty;

        IsSettingsVisible = true;
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        await SaveSettingsStateAsync();

        // Control process execution state based on enabled flags
        if (QwenProxyEnabled)
        {
            await proxyManager.StartAsync("FreeQwenApi", QwenProxyPort, QwenProxyCookie, CancellationToken.None);
        }
        else
        {
            await proxyManager.StopAsync("FreeQwenApi", CancellationToken.None);
        }

        if (DeepseekProxyEnabled)
        {
            await proxyManager.StartAsync("FreeDeepseekAPI", DeepseekProxyPort, DeepseekProxyCookie, CancellationToken.None);
        }
        else
        {
            await proxyManager.StopAsync("FreeDeepseekAPI", CancellationToken.None);
        }

        if (KimiProxyEnabled)
        {
            await proxyManager.StartAsync("FreeGLMKimiAPI", KimiProxyPort, KimiProxyCookie, CancellationToken.None);
        }
        else
        {
            await proxyManager.StopAsync("FreeGLMKimiAPI", CancellationToken.None);
        }

        IsSettingsVisible = false;
    }

    private async Task<ScreenMindSettings> SaveSettingsStateAsync()
    {
        ScreenMindSettings settings = await settingsStore.LoadAsync(CancellationToken.None);
        settings.Ui.Theme = SelectedTheme;
        settings.Ui.OverlayOpacity = OverlayOpacity;
        settings.Ui.AlwaysOnTop = AlwaysOnTop;
        settings.Capture.DefaultFormat = DefaultFormat;
        settings.Capture.MaxPayloadBytes = MaxPayloadBytes;
        settings.Capture.IncludeCursor = IncludeCursor;
        settings.Capture.SilentMode = SilentMode;
        settings.Capture.DefaultPrompt = DefaultPrompt;
        settings.Privacy.WarnBeforeCloudUpload = false;

        settings.Profiles.Items = AvailableProfiles;
        if (SelectedProfileIndex >= 0 && SelectedProfileIndex < AvailableProfiles.Count)
        {
            AiProfile selectedProfile = AvailableProfiles[SelectedProfileIndex];
            settings.Profiles.SelectedProfileId = selectedProfile.Id;
            settings.Providers.SelectedProviderId = selectedProfile.ProviderId;
        }

        settings.Privacy.BlockedProcessNames = BlockedProcesses
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        settings.Privacy.BlockedWindowTitleFragments = BlockedTitles
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        Hotkey? parsedRegion = ParseHotkey(RegionHotkeyText);
        if (parsedRegion is not null)
        {
            settings.Hotkeys.CaptureRegion = parsedRegion.Value;
            await hotkeyService.ReassignAsync("region", parsedRegion.Value, CancellationToken.None);
        }

        Hotkey? parsedActive = ParseHotkey(ActiveWindowHotkeyText);
        if (parsedActive is not null)
        {
            settings.Hotkeys.CaptureActiveWindow = parsedActive.Value;
            await hotkeyService.ReassignAsync("active_window", parsedActive.Value, CancellationToken.None);
        }

        Hotkey? parsedMonitor = ParseHotkey(MonitorHotkeyText);
        if (parsedMonitor is not null)
        {
            settings.Hotkeys.CaptureMonitor = parsedMonitor.Value;
            await hotkeyService.ReassignAsync("monitor", parsedMonitor.Value, CancellationToken.None);
        }

        settings.ManagedProxies.Qwen.Enabled = QwenProxyEnabled;
        settings.ManagedProxies.Qwen.Port = QwenProxyPort;
        settings.ManagedProxies.Deepseek.Enabled = DeepseekProxyEnabled;
        settings.ManagedProxies.Deepseek.Port = DeepseekProxyPort;
        settings.ManagedProxies.GlmKimi.Enabled = KimiProxyEnabled;
        settings.ManagedProxies.GlmKimi.Port = KimiProxyPort;

        if (!string.IsNullOrWhiteSpace(QwenBaseUrl))
        {
            if (settings.Providers.Providers.TryGetValue("openai-compatible", out ProviderEndpointSettings? compat))
            {
                compat.BaseUrl = QwenBaseUrl;
            }
            else
            {
                settings.Providers.Providers["openai-compatible"] = new ProviderEndpointSettings { BaseUrl = QwenBaseUrl };
            }
        }

        await settingsStore.SaveAsync(settings, CancellationToken.None);

        await SaveSecretIfChangedAsync("openai-api-key", OpenAiKey);
        await SaveSecretIfChangedAsync("anthropic-api-key", AnthropicKey);
        await SaveSecretIfChangedAsync("gemini-api-key", GeminiKey);
        await SaveSecretIfChangedAsync("qwen-cookie", QwenCookie);
        await SaveSecretIfChangedAsync("managed-qwen-cookie", QwenProxyCookie);
        await SaveSecretIfChangedAsync("managed-deepseek-cookie", DeepseekProxyCookie);
        await SaveSecretIfChangedAsync("managed-kimi-cookie", KimiProxyCookie);

        return settings;
    }

    private async Task SaveSecretIfChangedAsync(string keyName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            await secretStore.DeleteAsync(keyName, CancellationToken.None);
        }
        else
        {
            await secretStore.SaveAsync(keyName, value, CancellationToken.None);
        }
    }

    [RelayCommand]
    public async Task InstallProxyAsync(string proxyName)
    {
        try
        {
            if (proxyName == "FreeQwenApi") { IsQwenInstalling = true; QwenStatus = "Installing... Please wait (cloning and building)"; }
            else if (proxyName == "FreeDeepseekAPI") { IsDeepseekInstalling = true; DeepseekStatus = "Installing... Please wait (cloning and building)"; }
            else if (proxyName == "FreeGLMKimiAPI") { IsKimiInstalling = true; KimiStatus = "Installing... Please wait (cloning and building)"; }

            await proxyManager.InstallAsync(proxyName, CancellationToken.None);

            // Refresh status
            if (proxyName == "FreeQwenApi")
            {
                IsQwenInstalled = true;
                IsQwenRunning = await proxyManager.IsRunningAsync("FreeQwenApi", CancellationToken.None);
                QwenStatus = $"Installed, {(IsQwenRunning ? "Running" : "Stopped")}";
            }
            else if (proxyName == "FreeDeepseekAPI")
            {
                IsDeepseekInstalled = true;
                IsDeepseekRunning = await proxyManager.IsRunningAsync("FreeDeepseekAPI", CancellationToken.None);
                DeepseekStatus = $"Installed, {(IsDeepseekRunning ? "Running" : "Stopped")}";
            }
            else if (proxyName == "FreeGLMKimiAPI")
            {
                IsKimiInstalled = true;
                IsKimiRunning = await proxyManager.IsRunningAsync("FreeGLMKimiAPI", CancellationToken.None);
                KimiStatus = $"Installed, {(IsKimiRunning ? "Running" : "Stopped")}";
            }
        }
        catch (Exception ex)
        {
            string err = $"Install failed: {ex.Message}";
            if (proxyName == "FreeQwenApi") QwenStatus = err;
            else if (proxyName == "FreeDeepseekAPI") DeepseekStatus = err;
            else if (proxyName == "FreeGLMKimiAPI") KimiStatus = err;
            ErrorMessage = err;
        }
        finally
        {
            if (proxyName == "FreeQwenApi") IsQwenInstalling = false;
            else if (proxyName == "FreeDeepseekAPI") IsDeepseekInstalling = false;
            else if (proxyName == "FreeGLMKimiAPI") IsKimiInstalling = false;
            UpdateProviderList();
        }
    }

    [RelayCommand]
    public async Task AuthenticateProxyAsync(string proxyName)
    {
        try
        {
            await proxyManager.AuthenticateAsync(proxyName, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Authentication failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task StartProxyManualAsync(string proxyName)
    {
        try
        {
            if (proxyName == "FreeQwenApi")
            {
                IsQwenStarting = true;
                QwenStatus = "Starting...";
                QwenProxyEnabled = true;
                await proxyManager.StartAsync("FreeQwenApi", QwenProxyPort, QwenProxyCookie, CancellationToken.None);
                IsQwenRunning = await proxyManager.IsRunningAsync("FreeQwenApi", CancellationToken.None);
                QwenStatus = $"Installed, {(IsQwenRunning ? "Running" : "Stopped")}";
                await SaveSettingsStateAsync();
            }
            else if (proxyName == "FreeDeepseekAPI")
            {
                IsDeepseekStarting = true;
                DeepseekStatus = "Starting...";
                DeepseekProxyEnabled = true;
                await proxyManager.StartAsync("FreeDeepseekAPI", DeepseekProxyPort, DeepseekProxyCookie, CancellationToken.None);
                IsDeepseekRunning = await proxyManager.IsRunningAsync("FreeDeepseekAPI", CancellationToken.None);
                DeepseekStatus = $"Installed, {(IsDeepseekRunning ? "Running" : "Stopped")}";
                await SaveSettingsStateAsync();
            }
            else if (proxyName == "FreeGLMKimiAPI")
            {
                IsKimiStarting = true;
                KimiStatus = "Starting...";
                KimiProxyEnabled = true;
                await proxyManager.StartAsync("FreeGLMKimiAPI", KimiProxyPort, KimiProxyCookie, CancellationToken.None);
                IsKimiRunning = await proxyManager.IsRunningAsync("FreeGLMKimiAPI", CancellationToken.None);
                KimiStatus = $"Installed, {(IsKimiRunning ? "Running" : "Stopped")}";
                await SaveSettingsStateAsync();
            }
        }
        catch (Exception ex)
        {
            string err = $"Start failed: {ex.Message}";
            if (proxyName == "FreeQwenApi") QwenStatus = err;
            else if (proxyName == "FreeDeepseekAPI") DeepseekStatus = err;
            else if (proxyName == "FreeGLMKimiAPI") KimiStatus = err;
            ErrorMessage = err;
        }
        finally
        {
            if (proxyName == "FreeQwenApi") IsQwenStarting = false;
            else if (proxyName == "FreeDeepseekAPI") IsDeepseekStarting = false;
            else if (proxyName == "FreeGLMKimiAPI") IsKimiStarting = false;
            UpdateProviderList();
        }
    }

    [RelayCommand]
    public async Task StopProxyManualAsync(string proxyName)
    {
        try
        {
            if (proxyName == "FreeQwenApi")
            {
                QwenStatus = "Stopping...";
                QwenProxyEnabled = false;
                await proxyManager.StopAsync("FreeQwenApi", CancellationToken.None);
                IsQwenRunning = await proxyManager.IsRunningAsync("FreeQwenApi", CancellationToken.None);
                QwenStatus = $"Installed, {(IsQwenRunning ? "Running" : "Stopped")}";
                await SaveSettingsStateAsync();
            }
            else if (proxyName == "FreeDeepseekAPI")
            {
                DeepseekStatus = "Stopping...";
                DeepseekProxyEnabled = false;
                await proxyManager.StopAsync("FreeDeepseekAPI", CancellationToken.None);
                IsDeepseekRunning = await proxyManager.IsRunningAsync("FreeDeepseekAPI", CancellationToken.None);
                DeepseekStatus = $"Installed, {(IsDeepseekRunning ? "Running" : "Stopped")}";
                await SaveSettingsStateAsync();
            }
            else if (proxyName == "FreeGLMKimiAPI")
            {
                KimiStatus = "Stopping...";
                KimiProxyEnabled = false;
                await proxyManager.StopAsync("FreeGLMKimiAPI", CancellationToken.None);
                IsKimiRunning = await proxyManager.IsRunningAsync("FreeGLMKimiAPI", CancellationToken.None);
                KimiStatus = $"Installed, {(IsKimiRunning ? "Running" : "Stopped")}";
                await SaveSettingsStateAsync();
            }
        }
        catch (Exception ex)
        {
            string err = $"Stop failed: {ex.Message}";
            if (proxyName == "FreeQwenApi") QwenStatus = err;
            else if (proxyName == "FreeDeepseekAPI") DeepseekStatus = err;
            else if (proxyName == "FreeGLMKimiAPI") KimiStatus = err;
            ErrorMessage = err;
        }
        finally
        {
            UpdateProviderList();
        }
    }

    [RelayCommand]
    public void ApprovePrivacyWarning()
    {
        IsPrivacyWarningActive = false;
    }

    [RelayCommand]
    public void RequestNewCapture() => NewCaptureRequested?.Invoke(this, EventArgs.Empty);

    public void RequestActiveWindowCapture() => ActiveWindowCaptureRequested?.Invoke(this, EventArgs.Empty);

    public void RequestMonitorCapture() => MonitorCaptureRequested?.Invoke(this, EventArgs.Empty);

    public void CreateSessionFromImage(ScreenImage image)
    {
        ErrorMessage = string.Empty;
        ScreenMindSettings settings = settingsStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        AiProfile profile = settings.Profiles.Items.FirstOrDefault(p => p.Id == settings.Profiles.SelectedProfileId)
            ?? settings.Profiles.Items.FirstOrDefault()
            ?? new AiProfile("universal", "Universal", "openai", "gpt-4o-mini", "Analyze the screenshot and answer clearly.");

        ChatSession session = sessionManager.CreateSession(profile, image);
        ActiveSession = session;
        OnPropertyChanged(nameof(Sessions));
        OnPropertyChanged(nameof(ActiveMessages));
        OnPropertyChanged(nameof(CanSend));
    }

    public async Task AnalyzeImageSilentlyAsync(ScreenImage image)
    {
        CreateSessionFromImage(image);

        ScreenMindSettings settings = await settingsStore.LoadAsync(CancellationToken.None);
        string prompt = !string.IsNullOrWhiteSpace(settings.Capture.DefaultPrompt)
            ? settings.Capture.DefaultPrompt
            : "What is on my screen?";

        InputText = prompt;
        await SendMessageAsync();
    }

    public static Hotkey? ParseHotkey(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string[] parts = text.Split('+');
        HotkeyModifiers modifiers = HotkeyModifiers.NoRepeat;
        int keyCode = 0;
        foreach (string part in parts)
        {
            string trimmed = part.Trim();
            if (trimmed.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Control;
            }
            else if (trimmed.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Shift;
            }
            else if (trimmed.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Alt;
            }
            else if (trimmed.Equals("Win", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= HotkeyModifiers.Windows;
            }
            else if (trimmed.Length == 1)
            {
                keyCode = char.ToUpper(trimmed[0], System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out int hexVal))
                    {
                        keyCode = hexVal;
                    }
                }
                else if (int.TryParse(trimmed, out int intVal))
                {
                    keyCode = intVal;
                }
            }
        }
        return keyCode > 0 ? new Hotkey(modifiers, keyCode) : null;
    }

    public static string FormatHotkey(Hotkey hotkey)
    {
        List<string> parts = new List<string>();
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");

        if (hotkey.VirtualKey >= 0x41 && hotkey.VirtualKey <= 0x5A)
        {
            parts.Add(((char)hotkey.VirtualKey).ToString());
        }
        else
        {
            parts.Add($"0x{hotkey.VirtualKey:X}");
        }
        return string.Join("+", parts);
    }

    [RelayCommand]
    public void RejectPrivacyWarning()
    {
        IsPrivacyWarningActive = false;
    }

    [RelayCommand]
    public void CloseSettings()
    {
        IsSettingsVisible = false;
    }

    [RelayCommand]
    public void CloseChat()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
