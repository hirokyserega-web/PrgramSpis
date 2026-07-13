namespace ScreenMind.AI;

public static class SseStreamReader
{
    public static async IAsyncEnumerable<string> ReadDataAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using StreamReader reader = new(stream, leaveOpen: false);
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                yield break;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                string data = line[5..].Trim();
                if (data.Length > 0)
                {
                    yield return data;
                }
            }
        }
    }
}

