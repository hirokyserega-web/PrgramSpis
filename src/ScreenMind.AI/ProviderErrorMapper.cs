using System.Net;
using ScreenMind.Core.Ai;

namespace ScreenMind.AI;

public static class ProviderErrorMapper
{
    public static AiError FromHttpStatus(HttpStatusCode statusCode, string providerId)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new AiError(
                AiErrorKind.Auth,
                $"{providerId} authentication failed.",
                ((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            HttpStatusCode.TooManyRequests => new AiError(
                AiErrorKind.RateLimit,
                $"{providerId} rate limit reached.",
                ((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture),
                IsRetryable: true),
            HttpStatusCode.RequestEntityTooLarge => new AiError(
                AiErrorKind.PayloadTooLarge,
                $"{providerId} rejected image payload as too large.",
                ((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            HttpStatusCode.NotFound => new AiError(
                AiErrorKind.UnsupportedModel,
                $"{providerId} model was not found or is unsupported.",
                ((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout => new AiError(
                AiErrorKind.ServiceUnavailable,
                $"{providerId} service is temporarily unavailable.",
                ((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture),
                IsRetryable: true),
            _ when (int)statusCode >= 500 => new AiError(
                AiErrorKind.ServiceUnavailable,
                $"{providerId} server error.",
                ((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture),
                IsRetryable: true),
            _ => new AiError(
                AiErrorKind.Unknown,
                $"{providerId} request failed.",
                ((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
    }

    public static AiError FromException(Exception exception, string providerId)
    {
        return exception switch
        {
            TaskCanceledException => new AiError(AiErrorKind.Timeout, $"{providerId} request timed out.", IsRetryable: true),
            OperationCanceledException => new AiError(AiErrorKind.Cancelled, $"{providerId} request was cancelled."),
            TimeoutException => new AiError(AiErrorKind.Timeout, $"{providerId} request timed out.", IsRetryable: true),
            HttpRequestException => new AiError(AiErrorKind.Network, $"{providerId} network error.", IsRetryable: true),
            _ => new AiError(AiErrorKind.Unknown, $"{providerId} request failed."),
        };
    }
}
