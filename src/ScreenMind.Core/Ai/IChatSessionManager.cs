using ScreenMind.Core.Imaging;

namespace ScreenMind.Core.Ai;

public interface IChatSessionManager
{
    ChatSession? CurrentSession { get; }
    IReadOnlyList<ChatSession> Sessions { get; }
    ChatSession CreateSession(AiProfile profile, ScreenImage? image);
    void ActivateSession(string sessionId);
    void DeleteSession(string sessionId);
    void ClearSessions();
    void AddMessage(string sessionId, AiMessage message);
}
