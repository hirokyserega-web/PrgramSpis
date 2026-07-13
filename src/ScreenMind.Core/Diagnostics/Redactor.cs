namespace ScreenMind.Core.Diagnostics;

public static class Redactor
{
    public static string Redact(string input, IReadOnlyList<string> secretsToRedact)
    {
        if (string.IsNullOrEmpty(input) || secretsToRedact is null || secretsToRedact.Count == 0)
        {
            return input;
        }

        string redacted = input;
        foreach (string secret in secretsToRedact)
        {
            if (!string.IsNullOrEmpty(secret) && secret.Length > 4)
            {
                redacted = redacted.Replace(secret, "[REDACTED]");
            }
        }

        return redacted;
    }
}
