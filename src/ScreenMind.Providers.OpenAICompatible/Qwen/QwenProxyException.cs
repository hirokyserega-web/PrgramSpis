using System;

namespace ScreenMind.Providers.OpenAICompatible.Qwen;

public sealed class QwenProxyException : Exception
{
    public QwenProxyException(string message) : base(message) { }
    public QwenProxyException(string message, Exception innerException) : base(message, innerException) { }
}
