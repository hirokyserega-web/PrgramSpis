using ScreenMind.Core.Imaging;

namespace ScreenMind.Core.Ai;

public sealed class ChatSession
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public AiProfile Profile { get; set; }
    public ScreenImage Image { get; set; }
    public List<AiMessage> Messages { get; } = [];

    public ChatSession(AiProfile profile, ScreenImage image)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Image = image ?? throw new ArgumentNullException(nameof(image));
    }
}
