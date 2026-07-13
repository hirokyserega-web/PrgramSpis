using ScreenMind.Core.Imaging;

namespace ScreenMind.Core.Capture;

public interface IChatWindowService
{
    void Show();
    void Hide();
    void AnalyzeImage(ScreenImage image);
}
