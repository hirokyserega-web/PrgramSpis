using ScreenMind.Core.Ai;

namespace ScreenMind.AI;

public sealed class ChatSessionManager : IChatSessionManager
{
    private readonly List<ChatSession> sessions = [];
    private readonly object gate = new();
    private ChatSession? currentSession;

    public ChatSession? CurrentSession
    {
        get
        {
            lock (gate)
            {
                return currentSession;
            }
        }
    }

    public IReadOnlyList<ChatSession> Sessions
    {
        get
        {
            lock (gate)
            {
                return sessions.ToArray();
            }
        }
    }

    public ChatSession CreateSession(AiProfile profile, ScreenMind.Core.Imaging.ScreenImage? image)
    {
        ArgumentNullException.ThrowIfNull(profile);

        lock (gate)
        {
            ChatSession session = new(profile, image);
            sessions.Add(session);
            currentSession = session;
            return session;
        }
    }

    public void ActivateSession(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        lock (gate)
        {
            ChatSession? session = sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session is not null)
            {
                currentSession = session;
            }
        }
    }

    public void DeleteSession(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        lock (gate)
        {
            ChatSession? session = sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session is not null)
            {
                sessions.Remove(session);
                if (currentSession == session)
                {
                    currentSession = sessions.LastOrDefault();
                }
                session.Dispose();
            }
        }
    }

    public void ClearSessions()
    {
        lock (gate)
        {
            foreach (var session in sessions)
            {
                session.Dispose();
            }
            sessions.Clear();
            currentSession = null;
        }
    }

    public void AddMessage(string sessionId, AiMessage message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(message);

        lock (gate)
        {
            ChatSession? session = sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session is not null)
            {
                session.Messages.Add(message);
            }
        }
    }
}
