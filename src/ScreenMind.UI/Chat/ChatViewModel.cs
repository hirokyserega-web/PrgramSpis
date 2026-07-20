using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

public sealed partial class ChatViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan StreamUiUpdateInterval = TimeSpan.FromMilliseconds(50);

    private readonly IChatSessionManager sessionManager;
    private readonly IAiOrchestrator orchestrator;
    private readonly ISettingsStore settingsStore;
    private readonly ISecretStore secretStore;
    private readonly IHotkeyService hotkeyService;
    private readonly HttpClient httpClient = new();

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
    private string cleanChatHotkeyText = string.Empty;

    [ObservableProperty]
    private string clickThroughHotkeyText = string.Empty;

    [ObservableProperty]
    private string emergencyExitHotkeyText = string.Empty;

    [ObservableProperty]
    private ChatSession? activeSession;

    private CancellationTokenSource? activeCancellationTokenSource;

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
    private double uiScale = 1d;

    [ObservableProperty]
    private bool alwaysOnTop = true;

    [ObservableProperty]
    private bool showConsole;

    [ObservableProperty]
    private bool hideSidebar;

    /// <summary>
    /// When true, chat shows only user messages and AI replies (no sidebar/status chrome).
    /// </summary>
    [ObservableProperty]
    private bool cleanChatMode;

    [ObservableProperty]
    private bool clickThroughMode;

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
    private bool keepSessionHistory;

    [ObservableProperty]
    private string prompt1HotkeyText = string.Empty;

    [ObservableProperty]
    private string promptText1 = string.Empty;

    [ObservableProperty]
    private string prompt2HotkeyText = string.Empty;

    [ObservableProperty]
    private string promptText2 = string.Empty;

    [ObservableProperty]
    private string prompt3HotkeyText = string.Empty;

    [ObservableProperty]
    private string promptText3 = string.Empty;

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
        "notion",
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
    private bool notionProxyEnabled;
    [ObservableProperty]
    private int notionProxyPort = 8088;
    [ObservableProperty]
    private string notionCookie = string.Empty;
    [ObservableProperty]
    private string notionSpaceId = string.Empty;
    [ObservableProperty]
    private string notionUserId = string.Empty;
    [ObservableProperty]
    private string notionUserName = string.Empty;
    [ObservableProperty]
    private string notionUserEmail = string.Empty;
    [ObservableProperty]
    private string notionBlockId = string.Empty;
    [ObservableProperty]
    private string notionApiMasterKey = string.Empty;

    private List<string> notionModels =
    [
        "opus-4.6",
        "sonnet-4.6",
        "haiku-4.5",
        "gpt-5.2",
        "gpt-5.4",
        "gemini-2.5-flash",
        "gemini-3-flash",
        "minimax-m2.5",
        "researcher",
        "fast-researcher",
    ];

    [ObservableProperty]
    private bool isNotionInstalled;
    [ObservableProperty]
    private bool isNotionRunning;
    [ObservableProperty]
    private string notionStatus = "Not Installed, Stopped";
    [ObservableProperty]
    private bool isNotionInstalling;
    [ObservableProperty]
    private bool isNotionStarting;

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
        foreach (var s in sessionManager.Sessions)
        {
            Sessions.Add(s);
        }
    }

    public ObservableCollection<ChatSession> Sessions { get; } = new();

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

        string notionStatus = IsNotionRunning ? "Running" : "Stopped";
        list.Add($"Notion AI (Local Proxy) - {notionStatus}");

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
                models.Add("qwen3.8-max-preview");
                models.Add("qwen3.7-max");
                models.Add("qwen3.7-plus");
                models.Add("qwen3.6-plus");
                models.Add("qwen3.5-plus");
                models.Add("qwen3.5-flash");
                models.Add("qwen3-max");
                models.Add("qwen3-vl-plus");
                models.Add("qwen3-coder-plus");
                models.Add("qwen3-omni-flash");
                models.Add("qwen3-235b-a22b");
                models.Add("qwq-32b");
                models.Add("qvq-72b-preview-0310");
                models.Add("qwen2.5-vl-32b-instruct");
                models.Add("qwen2.5-coder-32b-instruct");
                models.Add("qwen2.5-72b-instruct");
                models.Add("custom");
                break;
            case "deepseek":
                models.Add("deepseek-chat");
                models.Add("deepseek-reasoner");
                break;
            case "kimi":
                models.Add("kimi");
                break;
            case "notion":
                models.AddRange(notionModels);
                models.Add("custom");
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
                if (profile.ModelId.StartsWith("qwen", StringComparison.OrdinalIgnoreCase)
                    || profile.ModelId.StartsWith("qwq", StringComparison.OrdinalIgnoreCase)
                    || profile.ModelId.StartsWith("qvq", StringComparison.OrdinalIgnoreCase)
                    || profile.Id.StartsWith("qwen", StringComparison.OrdinalIgnoreCase))
                {
                    provIdx = 4;
                }
                else if (profile.ModelId.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase)) provIdx = 5;
                else if (profile.ModelId.StartsWith("kimi", StringComparison.OrdinalIgnoreCase) || profile.ModelId.StartsWith("glm", StringComparison.OrdinalIgnoreCase)) provIdx = 6;
                else if (profile.Id.StartsWith("notion", StringComparison.OrdinalIgnoreCase)) provIdx = 7;
                else provIdx = 8;
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

            // Update the active session's profile if it matches the one being edited
            if (ActiveSession is not null && ActiveSession.Profile.Id == oldProfile.Id)
            {
                ActiveSession.Profile = newProfile;
            }

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
            Sessions.Remove(session);
            ActiveSession = sessionManager.CurrentSession;
        }
    }

    [RelayCommand]
    public void StartNewChat()
    {
        ActiveSession = null;
        InputText = string.Empty;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(CanSend));
    }

    [RelayCommand]
    public void ClearAllHistory()
    {
        sessionManager.ClearSessions();
        Sessions.Clear();
        ActiveSession = null;
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    public async Task SendMessageAsync(ScreenImage? attachedImage = null)
    {
        if (string.IsNullOrWhiteSpace(InputText) || IsSending)
        {
            return;
        }

        // Auto-create a session if none exists
        if (ActiveSession is null)
        {
            // Prefer the currently selected profile from UI, fall back to saved settings
            AiProfile profile;
            if (SelectedProfileIndex >= 0 && SelectedProfileIndex < AvailableProfiles.Count)
            {
                profile = AvailableProfiles[SelectedProfileIndex];
            }
            else
            {
                ScreenMindSettings settings = await settingsStore.LoadAsync(CancellationToken.None);
                profile = settings.Profiles.Items.FirstOrDefault(p => p.Id == settings.Profiles.SelectedProfileId)
                    ?? settings.Profiles.Items.FirstOrDefault()
                    ?? new AiProfile("universal", "Universal", "openai", "gpt-4o-mini", "Analyze the screenshot and answer clearly.");
            }

            ChatSession createdSession = sessionManager.CreateSession(profile, null);
            Sessions.Add(createdSession);
            ActiveSession = createdSession;
        }

        string userText = InputText.Trim();
        InputText = string.Empty;
        IsSending = true;
        ErrorMessage = string.Empty;

        ChatSession session = ActiveSession ?? throw new InvalidOperationException("Active chat session is not available.");

        ScreenImage? messageImage = attachedImage;
        if (messageImage is null)
        {
            if (session.Messages.Count == 0)
            {
                messageImage = session.Image;
            }
            else
            {
                messageImage = null;
            }
        }
        else
        {
            if (session.Image is not null && !ReferenceEquals(session.Image, attachedImage))
            {
                session.Image.Dispose();
            }
            session.Image = attachedImage;
        }

        string providerId = session.Profile.ProviderId;
        if (session.ConversationState is null || session.ConversationState.ProviderId != providerId)
        {
            session.ConversationState = new ProviderConversationState(
                providerId,
                Guid.NewGuid().ToString());
        }

        ScreenImage? userMsgImage = messageImage?.Clone();
        AiMessage userMessage = new(AiMessageRole.User, userText, DateTimeOffset.UtcNow, userMsgImage);
        AiMessage assistantMessage = new(AiMessageRole.Assistant, string.Empty, DateTimeOffset.UtcNow);

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            session.Messages.Add(userMessage);
            session.Messages.Add(assistantMessage);
            OnPropertyChanged(nameof(ActiveMessages));
        });

        CancelActiveRequest();
        activeCancellationTokenSource = new CancellationTokenSource();
        CancellationTokenSource cts = activeCancellationTokenSource;
        try
        {
            AiProfile effectiveProfile = session.Profile;

            AiRequest request = new(
                effectiveProfile,
                messageImage?.Clone(),
                userText,
                session.Messages.Take(session.Messages.Count - 2).ToArray(),
                session.ConversationState);

            bool isQwen = providerId.Equals("qwen", StringComparison.OrdinalIgnoreCase)
                || (providerId.Equals("openai-compatible", StringComparison.OrdinalIgnoreCase)
                    && (session.Profile.ModelId.StartsWith("qwen", StringComparison.OrdinalIgnoreCase)
                        || session.Profile.ModelId.StartsWith("qwq", StringComparison.OrdinalIgnoreCase)
                        || session.Profile.ModelId.StartsWith("qvq", StringComparison.OrdinalIgnoreCase)
                        || session.Profile.Id.StartsWith("qwen", StringComparison.OrdinalIgnoreCase)));

            StreamingAnswerFilter answerFilter = new(suppressUntaggedReasoning: isQwen);
            StringBuilder visibleAnswer = new();
            DateTimeOffset lastUiUpdate = DateTimeOffset.MinValue;
            int assistantIndex = session.Messages.Count - 1;
            bool completed = false;

            void PublishAssistantMessage(bool force)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (!force && now - lastUiUpdate < StreamUiUpdateInterval)
                {
                    return;
                }

                lastUiUpdate = now;
                string text = visibleAnswer.ToString();
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (assistantIndex >= 0 && assistantIndex < session.Messages.Count)
                    {
                        session.Messages[assistantIndex] = new AiMessage(
                            AiMessageRole.Assistant,
                            text,
                            assistantMessage.CreatedAt);
                        OnPropertyChanged(nameof(ActiveMessages));
                    }
                });
            }

            await Task.Yield();
            await foreach (AiStreamEvent ev in orchestrator.AnalyzeAsync(request, cts.Token).ConfigureAwait(false))
            {
                if (ev is AiStreamEvent.ReasoningDelta)
                {
                    continue;
                }
                else if (ev is AiStreamEvent.TextDelta delta)
                {
                    string visibleDelta = answerFilter.Push(delta.Text);
                    if (!string.IsNullOrEmpty(visibleDelta))
                    {
                        visibleAnswer.Append(visibleDelta);
                        PublishAssistantMessage(force: false);
                    }
                }
                else if (ev is AiStreamEvent.Completed)
                {
                    if (!completed)
                    {
                        string remaining = answerFilter.Complete();
                        completed = true;
                        if (!string.IsNullOrEmpty(remaining))
                        {
                            visibleAnswer.Append(remaining);
                        }
                    }
                }
                else if (ev is AiStreamEvent.Failed failed)
                {
                    string remaining = answerFilter.Complete();
                    if (!string.IsNullOrEmpty(remaining))
                    {
                        visibleAnswer.Append(remaining);
                    }
                    PublishAssistantMessage(force: true);
                    throw new InvalidOperationException(failed.Error.Message);
                }
            }

            if (!completed)
            {
                string remaining = answerFilter.Complete();
                completed = true;
                if (!string.IsNullOrEmpty(remaining))
                {
                    visibleAnswer.Append(remaining);
                }
            }

            if (answerFilter.IsReasoningOnly && visibleAnswer.Length == 0)
            {
                visibleAnswer.Append("Ответ ИИ был прерван. Возможно, веб-поиск занял слишком много времени или локальное прокси-соединение было сброшено. Пожалуйста, повторите попытку.");
            }

            PublishAssistantMessage(force: true);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException || ex is TaskCanceledException)
            {
                return;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ErrorMessage = ex.Message;
                OnPropertyChanged(nameof(ActiveMessages));
            });
        }
        finally
        {
            if (activeCancellationTokenSource == cts)
            {
                activeCancellationTokenSource = null;
            }
            try
            {
                cts.Dispose();
            }
            catch { }
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsSending = false;
                OnPropertyChanged(nameof(CanSend));
            });
        }
    }

    [RelayCommand]
    public async Task LoadSettingsAsync()
    {
        ScreenMindSettings settings = await settingsStore.LoadAsync(CancellationToken.None);
        ApplyUiPreferences(settings);
        DefaultFormat = settings.Capture.DefaultFormat;
        MaxPayloadBytes = settings.Capture.MaxPayloadBytes;
        IncludeCursor = settings.Capture.IncludeCursor;
        SilentMode = settings.Capture.SilentMode;
        DefaultPrompt = settings.Capture.DefaultPrompt;
        if (string.IsNullOrWhiteSpace(settings.Capture.BaseDefaultPrompt))
        {
            settings.Capture.BaseDefaultPrompt = DefaultPrompt;
        }
        KeepSessionHistory = settings.Capture.KeepSessionHistory;
        Prompt1HotkeyText = FormatHotkey(settings.Hotkeys.PromptHotkey1);
        PromptText1 = settings.Hotkeys.PromptText1;
        Prompt2HotkeyText = FormatHotkey(settings.Hotkeys.PromptHotkey2);
        PromptText2 = settings.Hotkeys.PromptText2;
        Prompt3HotkeyText = FormatHotkey(settings.Hotkeys.PromptHotkey3);
        PromptText3 = settings.Hotkeys.PromptText3;
        WarnBeforeCloudUpload = settings.Privacy.WarnBeforeCloudUpload;

        BlockedProcesses = string.Join(Environment.NewLine, settings.Privacy.BlockedProcessNames);
        BlockedTitles = string.Join(Environment.NewLine, settings.Privacy.BlockedWindowTitleFragments);

        // Fetch secrets and proxy statuses in parallel
        var openAiKeyTask = secretStore.GetAsync("openai-api-key", CancellationToken.None);
        var anthropicKeyTask = secretStore.GetAsync("anthropic-api-key", CancellationToken.None);
        var geminiKeyTask = secretStore.GetAsync("gemini-api-key", CancellationToken.None);
        var qwenCookieTask = secretStore.GetAsync("qwen-cookie", CancellationToken.None);
        var qwenProxyCookieTask = secretStore.GetAsync("managed-qwen-cookie", CancellationToken.None);
        var deepseekProxyCookieTask = secretStore.GetAsync("managed-deepseek-cookie", CancellationToken.None);
        var kimiProxyCookieTask = secretStore.GetAsync("managed-kimi-cookie", CancellationToken.None);
        var notionCookieTask = secretStore.GetAsync("managed-notion-cookie", CancellationToken.None);
        var notionSpaceIdTask = secretStore.GetAsync("managed-notion-space-id", CancellationToken.None);
        var notionUserIdTask = secretStore.GetAsync("managed-notion-user-id", CancellationToken.None);
        var notionUserNameTask = secretStore.GetAsync("managed-notion-user-name", CancellationToken.None);
        var notionUserEmailTask = secretStore.GetAsync("managed-notion-user-email", CancellationToken.None);
        var notionBlockIdTask = secretStore.GetAsync("managed-notion-block-id", CancellationToken.None);
        var notionApiMasterKeyTask = secretStore.GetAsync("managed-notion-api-master-key", CancellationToken.None);

        var notionInstalledTask = proxyManager.IsInstalledAsync("notion-2api", CancellationToken.None);
        var notionRunningTask = proxyManager.IsRunningAsync("notion-2api", CancellationToken.None);
        var qwenInstalledTask = proxyManager.IsInstalledAsync("FreeQwenApi", CancellationToken.None);
        var qwenRunningTask = proxyManager.IsRunningAsync("FreeQwenApi", CancellationToken.None);
        var deepseekInstalledTask = proxyManager.IsInstalledAsync("FreeDeepseekAPI", CancellationToken.None);
        var deepseekRunningTask = proxyManager.IsRunningAsync("FreeDeepseekAPI", CancellationToken.None);
        var kimiInstalledTask = proxyManager.IsInstalledAsync("FreeGLMKimiAPI", CancellationToken.None);
        var kimiRunningTask = proxyManager.IsRunningAsync("FreeGLMKimiAPI", CancellationToken.None);

        await Task.WhenAll(
            openAiKeyTask, anthropicKeyTask, geminiKeyTask, qwenCookieTask,
            qwenProxyCookieTask, deepseekProxyCookieTask, kimiProxyCookieTask,
            notionCookieTask, notionSpaceIdTask, notionUserIdTask, notionUserNameTask, notionUserEmailTask, notionBlockIdTask, notionApiMasterKeyTask,
            notionInstalledTask, notionRunningTask, qwenInstalledTask, qwenRunningTask, deepseekInstalledTask, deepseekRunningTask,
            kimiInstalledTask, kimiRunningTask
        );

        OpenAiKey = await openAiKeyTask ?? string.Empty;
        AnthropicKey = await anthropicKeyTask ?? string.Empty;
        GeminiKey = await geminiKeyTask ?? string.Empty;

        // FreeQwenApi / OpenAI-Compatible settings
        if (settings.Providers.Providers.TryGetValue("openai-compatible", out ProviderEndpointSettings? compatProvider))
        {
            QwenBaseUrl = compatProvider.BaseUrl ?? string.Empty;
        }
        QwenCookie = await qwenCookieTask ?? string.Empty;

        // Load managed proxies settings
        QwenProxyEnabled = settings.ManagedProxies.Qwen.Enabled;
        QwenProxyPort = settings.ManagedProxies.Qwen.Port;
        QwenProxyCookie = await qwenProxyCookieTask ?? string.Empty;

        DeepseekProxyEnabled = settings.ManagedProxies.Deepseek.Enabled;
        DeepseekProxyPort = settings.ManagedProxies.Deepseek.Port;
        DeepseekProxyCookie = await deepseekProxyCookieTask ?? string.Empty;

        KimiProxyEnabled = settings.ManagedProxies.GlmKimi.Enabled;
        KimiProxyPort = settings.ManagedProxies.GlmKimi.Port;
        KimiProxyCookie = await kimiProxyCookieTask ?? string.Empty;
        NotionCookie = await notionCookieTask ?? string.Empty;
        NotionSpaceId = await notionSpaceIdTask ?? string.Empty;
        NotionUserId = await notionUserIdTask ?? string.Empty;
        NotionUserName = await notionUserNameTask ?? string.Empty;
        NotionUserEmail = await notionUserEmailTask ?? string.Empty;
        NotionBlockId = await notionBlockIdTask ?? string.Empty;
        NotionApiMasterKey = await notionApiMasterKeyTask ?? string.Empty;
        NotionProxyEnabled = settings.ManagedProxies.Notion.Enabled;
        NotionProxyPort = settings.ManagedProxies.Notion.Port;

        IsNotionInstalled = await notionInstalledTask;
        IsNotionRunning = await notionRunningTask;
        NotionStatus = $"{(IsNotionInstalled ? "Installed" : "Not Installed")}, {(IsNotionRunning ? "Running" : "Stopped")}";

        IsQwenInstalled = await qwenInstalledTask;
        IsQwenRunning = await qwenRunningTask;
        QwenStatus = $"{(IsQwenInstalled ? "Installed" : "Not Installed")}, {(IsQwenRunning ? "Running" : "Stopped")}";

        IsDeepseekInstalled = await deepseekInstalledTask;
        IsDeepseekRunning = await deepseekRunningTask;
        DeepseekStatus = $"{(IsDeepseekInstalled ? "Installed" : "Not Installed")}, {(IsDeepseekRunning ? "Running" : "Stopped")}";

        IsKimiInstalled = await kimiInstalledTask;
        IsKimiRunning = await kimiRunningTask;
        KimiStatus = $"{(IsKimiInstalled ? "Installed" : "Not Installed")}, {(IsKimiRunning ? "Running" : "Stopped")}";

        RegionHotkeyText = FormatHotkey(settings.Hotkeys.CaptureRegion);
        ActiveWindowHotkeyText = FormatHotkey(settings.Hotkeys.CaptureActiveWindow);
        MonitorHotkeyText = FormatHotkey(settings.Hotkeys.CaptureMonitor);
        CleanChatHotkeyText = FormatHotkey(settings.Hotkeys.ToggleCleanChat);
        ClickThroughHotkeyText = FormatHotkey(settings.Hotkeys.ToggleClickThrough);
        EmergencyExitHotkeyText = FormatHotkey(settings.Hotkeys.EmergencyExit);

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
        if (IsNotionRunning)
        {
            await RefreshNotionModelsAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task RefreshNotionModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"http://127.0.0.1:{NotionProxyPort}/v1/models");
            if (!string.IsNullOrWhiteSpace(NotionApiMasterKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", NotionApiMasterKey);
            }

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            List<string> discovered = data.EnumerateArray()
                .Select(item => item.TryGetProperty("id", out JsonElement id) ? id.GetString() : null)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (discovered.Count == 0)
            {
                return;
            }

            notionModels = discovered;
            if (SelectedProviderIndex >= 0 && SelectedProviderIndex < ProviderIds.Count && ProviderIds[SelectedProviderIndex] == "notion")
            {
                UpdateAvailableModelsList();
            }
        }
        catch
        {
        }
    }

    public async Task LoadWindowPreferencesAsync(CancellationToken cancellationToken)
    {
        ScreenMindSettings settings = await settingsStore.LoadAsync(cancellationToken);
        ApplyUiPreferences(settings);
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        try
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

            if (NotionProxyEnabled)
            {
                await proxyManager.StartAsync("notion-2api", NotionProxyPort, new ExternalProxyCredentials(NotionCookie, NotionApiMasterKey, NotionSpaceId, NotionUserId, NotionUserName, NotionUserEmail, NotionBlockId), CancellationToken.None);
            }
            else
            {
                await proxyManager.StopAsync("notion-2api", CancellationToken.None);
            }

            IsSettingsVisible = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save settings: {ex.Message}";
        }
    }

    private async Task<ScreenMindSettings> SaveSettingsStateAsync()
    {
        ScreenMindSettings settings = await settingsStore.LoadAsync(CancellationToken.None);
        settings.Ui.Theme = SelectedTheme;
        settings.Ui.OverlayOpacity = OverlayOpacity;
        settings.Ui.UiScale = UiScale;
        settings.Ui.AlwaysOnTop = AlwaysOnTop;
        settings.Ui.ShowConsole = ShowConsole;
        settings.Ui.HideSidebar = HideSidebar;
        settings.Ui.CleanChatMode = CleanChatMode;
        settings.Ui.ClickThroughMode = ClickThroughMode;
        settings.Capture.DefaultFormat = DefaultFormat;
        settings.Capture.MaxPayloadBytes = MaxPayloadBytes;
        settings.Capture.IncludeCursor = IncludeCursor;
        settings.Capture.SilentMode = SilentMode;
        settings.Capture.DefaultPrompt = DefaultPrompt;
        settings.Capture.BaseDefaultPrompt = DefaultPrompt;
        settings.Capture.KeepSessionHistory = KeepSessionHistory;
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

        Hotkey? parsedCleanChat = ParseHotkey(CleanChatHotkeyText);
        if (parsedCleanChat is not null)
        {
            settings.Hotkeys.ToggleCleanChat = parsedCleanChat.Value;
            await hotkeyService.ReassignAsync("clean_chat", parsedCleanChat.Value, CancellationToken.None);
        }

        Hotkey? parsedClickThrough = ParseHotkey(ClickThroughHotkeyText);
        if (parsedClickThrough is not null)
        {
            settings.Hotkeys.ToggleClickThrough = parsedClickThrough.Value;
            await hotkeyService.ReassignAsync("click_through", parsedClickThrough.Value, CancellationToken.None);
        }

        Hotkey? parsedEmergency = ParseHotkey(EmergencyExitHotkeyText);
        if (parsedEmergency is not null)
        {
            settings.Hotkeys.EmergencyExit = parsedEmergency.Value;
            await hotkeyService.ReassignAsync("emergency_exit", parsedEmergency.Value, CancellationToken.None);
        }

        Hotkey? parsedPrompt1 = ParseHotkey(Prompt1HotkeyText);
        if (parsedPrompt1 is not null)
        {
            settings.Hotkeys.PromptHotkey1 = parsedPrompt1.Value;
            await hotkeyService.ReassignAsync("prompt_1", parsedPrompt1.Value, CancellationToken.None);
        }
        else
        {
            settings.Hotkeys.PromptHotkey1 = new Hotkey(HotkeyModifiers.None, 0);
            await hotkeyService.UnregisterAsync("prompt_1", CancellationToken.None);
        }
        settings.Hotkeys.PromptText1 = PromptText1;

        Hotkey? parsedPrompt2 = ParseHotkey(Prompt2HotkeyText);
        if (parsedPrompt2 is not null)
        {
            settings.Hotkeys.PromptHotkey2 = parsedPrompt2.Value;
            await hotkeyService.ReassignAsync("prompt_2", parsedPrompt2.Value, CancellationToken.None);
        }
        else
        {
            settings.Hotkeys.PromptHotkey2 = new Hotkey(HotkeyModifiers.None, 0);
            await hotkeyService.UnregisterAsync("prompt_2", CancellationToken.None);
        }
        settings.Hotkeys.PromptText2 = PromptText2;

        Hotkey? parsedPrompt3 = ParseHotkey(Prompt3HotkeyText);
        if (parsedPrompt3 is not null)
        {
            settings.Hotkeys.PromptHotkey3 = parsedPrompt3.Value;
            await hotkeyService.ReassignAsync("prompt_3", parsedPrompt3.Value, CancellationToken.None);
        }
        else
        {
            settings.Hotkeys.PromptHotkey3 = new Hotkey(HotkeyModifiers.None, 0);
            await hotkeyService.UnregisterAsync("prompt_3", CancellationToken.None);
        }
        settings.Hotkeys.PromptText3 = PromptText3;

        settings.ManagedProxies.Qwen.Enabled = QwenProxyEnabled;
        settings.ManagedProxies.Qwen.Port = QwenProxyPort;
        settings.ManagedProxies.Deepseek.Enabled = DeepseekProxyEnabled;
        settings.ManagedProxies.Deepseek.Port = DeepseekProxyPort;
        settings.ManagedProxies.GlmKimi.Enabled = KimiProxyEnabled;
        settings.ManagedProxies.GlmKimi.Port = KimiProxyPort;
        settings.ManagedProxies.Notion.Enabled = NotionProxyEnabled;
        settings.ManagedProxies.Notion.Port = NotionProxyPort;

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
        if (string.IsNullOrWhiteSpace(NotionApiMasterKey) && !string.IsNullOrWhiteSpace(NotionCookie))
        {
            NotionApiMasterKey = DeriveNotionApiKey(ExtractToken(NotionCookie));
        }

        await SaveSecretIfChangedAsync("managed-notion-cookie", NotionCookie);
        await SaveSecretIfChangedAsync("managed-notion-space-id", NotionSpaceId);
        await SaveSecretIfChangedAsync("managed-notion-user-id", NotionUserId);
        await SaveSecretIfChangedAsync("managed-notion-user-name", NotionUserName);
        await SaveSecretIfChangedAsync("managed-notion-user-email", NotionUserEmail);
        await SaveSecretIfChangedAsync("managed-notion-block-id", NotionBlockId);
        await SaveSecretIfChangedAsync("managed-notion-api-master-key", NotionApiMasterKey);

        return settings;
    }

    private void ApplyUiPreferences(ScreenMindSettings settings)
    {
        SelectedTheme = settings.Ui.Theme;
        OverlayOpacity = settings.Ui.OverlayOpacity;
        UiScale = settings.Ui.UiScale;
        AlwaysOnTop = settings.Ui.AlwaysOnTop;
        ShowConsole = settings.Ui.ShowConsole;
        HideSidebar = settings.Ui.HideSidebar;
        CleanChatMode = settings.Ui.CleanChatMode;
        ClickThroughMode = settings.Ui.ClickThroughMode;
        RegionHotkeyText = FormatHotkey(settings.Hotkeys.CaptureRegion);
        ActiveWindowHotkeyText = FormatHotkey(settings.Hotkeys.CaptureActiveWindow);
        MonitorHotkeyText = FormatHotkey(settings.Hotkeys.CaptureMonitor);
        CleanChatHotkeyText = FormatHotkey(settings.Hotkeys.ToggleCleanChat);
        ClickThroughHotkeyText = FormatHotkey(settings.Hotkeys.ToggleClickThrough);
        EmergencyExitHotkeyText = FormatHotkey(settings.Hotkeys.EmergencyExit);
        Prompt1HotkeyText = FormatHotkey(settings.Hotkeys.PromptHotkey1);
        Prompt2HotkeyText = FormatHotkey(settings.Hotkeys.PromptHotkey2);
        Prompt3HotkeyText = FormatHotkey(settings.Hotkeys.PromptHotkey3);
        KeepSessionHistory = settings.Capture.KeepSessionHistory;
    }

    [RelayCommand]
    public async Task ToggleCleanChatModeAsync()
    {
        CleanChatMode = !CleanChatMode;
        try
        {
            ScreenMindSettings settings = await settingsStore.LoadAsync(CancellationToken.None);
            settings.Ui.CleanChatMode = CleanChatMode;
            await settingsStore.SaveAsync(settings, CancellationToken.None);
        }
        catch
        {
            // UI toggle still applies for this session even if persistence fails.
        }
    }

    [RelayCommand]
    public async Task ToggleClickThroughModeAsync()
    {
        ClickThroughMode = !ClickThroughMode;
        try
        {
            ScreenMindSettings settings = await settingsStore.LoadAsync(CancellationToken.None);
            settings.Ui.ClickThroughMode = ClickThroughMode;
            await settingsStore.SaveAsync(settings, CancellationToken.None);
        }
        catch
        {
            // UI toggle still applies for this session even if persistence fails.
        }
    }

    private static string ExtractToken(string cookie)
    {
        foreach (string part in cookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("token_v2=", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(part[8..]);
            }
        }

        return cookie.Trim();
    }

    private static string DeriveNotionApiKey(string token)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return "sm-notion-" + Convert.ToHexString(digest)[..32].ToLowerInvariant();
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
            else if (proxyName == "notion-2api") { IsNotionInstalling = true; NotionStatus = "Installing... Please wait (cloning and building)"; }

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
            else if (proxyName == "notion-2api")
            {
                IsNotionInstalled = true;
                IsNotionRunning = await proxyManager.IsRunningAsync("notion-2api", CancellationToken.None);
                NotionStatus = $"Installed, {(IsNotionRunning ? "Running" : "Stopped")}";
            }
        }
        catch (Exception ex)
        {
            string err = $"Install failed: {ex.Message}";
            if (proxyName == "FreeQwenApi") QwenStatus = err;
            else if (proxyName == "FreeDeepseekAPI") DeepseekStatus = err;
            else if (proxyName == "FreeGLMKimiAPI") KimiStatus = err;
            else if (proxyName == "notion-2api") NotionStatus = err;
            ErrorMessage = err;
        }
        finally
        {
            if (proxyName == "FreeQwenApi") IsQwenInstalling = false;
            else if (proxyName == "FreeDeepseekAPI") IsDeepseekInstalling = false;
            else if (proxyName == "FreeGLMKimiAPI") IsKimiInstalling = false;
            else if (proxyName == "notion-2api") IsNotionInstalling = false;
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
            else if (proxyName == "notion-2api")
            {
                IsNotionStarting = true;
                NotionStatus = "Starting...";
                NotionProxyEnabled = true;
                ExternalProxyCredentials credentials = new(NotionCookie, NotionApiMasterKey, NotionSpaceId, NotionUserId, NotionUserName, NotionUserEmail, NotionBlockId);
                await proxyManager.StartAsync("notion-2api", NotionProxyPort, credentials, CancellationToken.None);
                IsNotionRunning = await proxyManager.IsRunningAsync("notion-2api", CancellationToken.None);
                NotionStatus = $"Installed, {(IsNotionRunning ? "Running" : "Stopped")}";
                await SaveSettingsStateAsync();
                if (IsNotionRunning)
                {
                    await RefreshNotionModelsAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            string err = $"Start failed: {ex.Message}";
            if (proxyName == "FreeQwenApi") QwenStatus = err;
            else if (proxyName == "FreeDeepseekAPI") DeepseekStatus = err;
            else if (proxyName == "FreeGLMKimiAPI") KimiStatus = err;
            else if (proxyName == "notion-2api") NotionStatus = err;
            ErrorMessage = err;
        }
        finally
        {
            if (proxyName == "FreeQwenApi") IsQwenStarting = false;
            else if (proxyName == "FreeDeepseekAPI") IsDeepseekStarting = false;
            else if (proxyName == "FreeGLMKimiAPI") IsKimiStarting = false;
            else if (proxyName == "notion-2api") IsNotionStarting = false;
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
            else if (proxyName == "notion-2api")
            {
                NotionStatus = "Stopping...";
                NotionProxyEnabled = false;
                await proxyManager.StopAsync("notion-2api", CancellationToken.None);
                IsNotionRunning = await proxyManager.IsRunningAsync("notion-2api", CancellationToken.None);
                NotionStatus = $"Installed, {(IsNotionRunning ? "Running" : "Stopped")}";
                await SaveSettingsStateAsync();
            }
        }
        catch (Exception ex)
        {
            string err = $"Stop failed: {ex.Message}";
            if (proxyName == "FreeQwenApi") QwenStatus = err;
            else if (proxyName == "FreeDeepseekAPI") DeepseekStatus = err;
            else if (proxyName == "FreeGLMKimiAPI") KimiStatus = err;
            else if (proxyName == "notion-2api") NotionStatus = err;
            ErrorMessage = err;
        }
        finally
        {
            UpdateProviderList();
        }
    }

    [RelayCommand]
    public async Task FixQwenProxyModelsAsync()
    {
        try
        {
            await proxyManager.FixQwenProxyModelsAsync(CancellationToken.None);
            QwenStatus = "Model list fixed successfully! Restart the proxy.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to fix models: {ex.Message}";
            QwenStatus = $"Error: {ex.Message}";
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

    public async Task CreateSessionFromImageAsync(ScreenImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);

        ErrorMessage = string.Empty;
        ScreenMindSettings settings = await settingsStore.LoadAsync(cancellationToken);
        AiProfile profile = settings.Profiles.Items.FirstOrDefault(p => p.Id == settings.Profiles.SelectedProfileId)
            ?? settings.Profiles.Items.FirstOrDefault()
            ?? new AiProfile("universal", "Universal", "openai", "gpt-4o-mini", "Analyze the screenshot and answer clearly.");

        ChatSession session = sessionManager.CreateSession(profile, image);
        Sessions.Add(session);
        ActiveSession = session;
        OnPropertyChanged(nameof(ActiveMessages));
        OnPropertyChanged(nameof(CanSend));
    }

    public void CreateSessionFromImage(ScreenImage image)
    {
        CreateSessionFromImageAsync(image, CancellationToken.None).GetAwaiter().GetResult();
    }

    public void CancelActiveRequest()
    {
        if (activeCancellationTokenSource is not null)
        {
            var cts = activeCancellationTokenSource;
            activeCancellationTokenSource = null;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                catch { }
            });
        }
        IsSending = false;
    }

    public async Task AnalyzeImageSilentlyAsync(ScreenImage image, string? promptOverride = null)
    {
        CancelActiveRequest();
        try
        {
            ScreenMindSettings settings = await settingsStore.LoadAsync(CancellationToken.None);
            if (settings.Capture.KeepSessionHistory && ActiveSession is not null)
            {
                if (ActiveSession.Image is not null && !ReferenceEquals(ActiveSession.Image, image))
                {
                    ActiveSession.Image.Dispose();
                }
                ActiveSession.Image = image;
            }
            else
            {
                await CreateSessionFromImageAsync(image, CancellationToken.None);
            }

            string prompt = !string.IsNullOrWhiteSpace(promptOverride)
                ? promptOverride
                : (!string.IsNullOrWhiteSpace(settings.Capture.DefaultPrompt)
                    ? settings.Capture.DefaultPrompt
                    : "What is on my screen?");

            InputText = prompt;
            await SendMessageAsync(image);
        }
        catch (Exception exception)
        {
            bool isOwned = false;
            foreach (var s in sessionManager.Sessions)
            {
                if (ReferenceEquals(s.Image, image))
                {
                    isOwned = true;
                    break;
                }
            }
            if (!isOwned)
            {
                image.Dispose();
            }

            ErrorMessage = $"Screenshot failed: {exception.Message}";
        }
    }

    public static Hotkey? ParseHotkey(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
            else if (trimmed.Equals("Mouse3", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Mouse 3", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("MButton", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("MiddleClick", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x04;
            }
            else if (trimmed.Equals("Mouse4", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Mouse 4", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("XButton1", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x05;
            }
            else if (trimmed.Equals("Mouse5", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Mouse 5", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("XButton2", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x06;
            }
            else if (trimmed.Length == 1)
            {
                keyCode = char.ToUpper(trimmed[0], System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (trimmed.StartsWith("F", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(trimmed[1..], out int fNum)
                     && fNum is >= 1 and <= 24)
            {
                keyCode = 0x70 + (fNum - 1);
            }
            else if (trimmed.Equals("Esc", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Escape", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x1B;
            }
            else if (trimmed.Equals("Space", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Spacebar", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x20;
            }
            else if (trimmed.Equals("Tab", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x09;
            }
            else if (trimmed.Equals("Enter", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Return", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x0D;
            }
            else if (trimmed.Equals("Backspace", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x08;
            }
            else if (trimmed.Equals("Delete", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Del", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x2E;
            }
            else if (trimmed.Equals("Insert", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Ins", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x2D;
            }
            else if (trimmed.Equals("Home", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x24;
            }
            else if (trimmed.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x23;
            }
            else if (trimmed.Equals("PageUp", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("PgUp", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x21;
            }
            else if (trimmed.Equals("PageDown", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("PgDn", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x22;
            }
            else if (trimmed.Equals("Left", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x25;
            }
            else if (trimmed.Equals("Up", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x26;
            }
            else if (trimmed.Equals("Right", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x27;
            }
            else if (trimmed.Equals("Down", StringComparison.OrdinalIgnoreCase))
            {
                keyCode = 0x28;
            }
            else if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out int hexVal))
            {
                keyCode = hexVal;
            }
            else if (int.TryParse(trimmed, out int intVal))
            {
                keyCode = intVal;
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

        parts.Add(FormatVirtualKey(hotkey.VirtualKey));
        return string.Join("+", parts);
    }

    public static string FormatVirtualKey(int virtualKey)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKey - 0x6F}";
        }

        return virtualKey switch
        {
            0x04 => "Mouse 3",
            0x05 => "Mouse 4",
            0x06 => "Mouse 5",
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            _ => $"0x{virtualKey:X}",
        };
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

    partial void OnAlwaysOnTopChanged(bool value)
    {
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                ScreenMindSettings settings = await settingsStore.LoadAsync(CancellationToken.None);
                settings.Ui.AlwaysOnTop = value;
                await settingsStore.SaveAsync(settings, CancellationToken.None);
            }
            catch { }
        });
    }

    public void Dispose()
    {
        CancelActiveRequest();
    }
}
