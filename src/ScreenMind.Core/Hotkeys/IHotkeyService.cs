namespace ScreenMind.Core.Hotkeys;

public interface IHotkeyService : IAsyncDisposable
{
    event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    Task<HotkeyRegistrationResult> RegisterAsync(
        HotkeyRegistration registration,
        CancellationToken cancellationToken);

    Task<HotkeyRegistrationResult> ReassignAsync(
        string id,
        Hotkey hotkey,
        CancellationToken cancellationToken);

    Task UnregisterAsync(string id, CancellationToken cancellationToken);

    Task PauseAsync(CancellationToken cancellationToken);

    Task ResumeAsync(CancellationToken cancellationToken);
}

public sealed class HotkeyPressedEventArgs : EventArgs
{
    public HotkeyPressedEventArgs(string registrationId)
    {
        RegistrationId = registrationId;
    }

    public string RegistrationId { get; }
}

