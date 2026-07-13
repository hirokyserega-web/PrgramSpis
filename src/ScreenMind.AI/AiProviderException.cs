using ScreenMind.Core.Ai;

namespace ScreenMind.AI;

public sealed class AiProviderException : Exception
{
    public AiProviderException(AiError error, Exception? innerException = null)
        : base(error.Message, innerException)
    {
        Error = error;
    }

    public AiError Error { get; }
}

