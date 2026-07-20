using System;

namespace ScreenMind.Providers.OpenAICompatible.Qwen;

public sealed class QwenProxyException : Exception
{
    public QwenProxyException(string message) : base(message) { }
    public QwenProxyException(string message, Exception innerException) : base(message, innerException) { }
    public QwenProxyException(string message, int statusCode) : base(message) => StatusCode = statusCode;

    public int? StatusCode { get; }
}
