using ScreenMind.Core.Imaging;

namespace ScreenMind.Core.Capture;

public interface IChatWindowService
{
    void Show();
    void Hide();

    /// <summary>
    /// Ensures capture exclusion is applied without hiding the window or stealing focus.
    /// Preferred over <see cref="Hide"/> for stealth screenshots.
    /// </summary>
    void PrepareForStealthCapture();

    /// <summary>
    /// Toggles clean chat mode (only messages + AI reply) without stealing focus.
    /// </summary>
    void ToggleCleanChatMode();

    /// <summary>
    /// Toggles click-through/ghost mode without stealing focus.
    /// </summary>
    void ToggleClickThroughMode();

    /// <summary>
    /// Starts image analysis without activating the chat window or changing the foreground app.
    /// </summary>
    void AnalyzeImage(ScreenImage image, string? promptOverride = null);

    void SetDefaultPrompt(string promptText);
}
