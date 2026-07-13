namespace ScreenMind.AI;

public static class NdjsonStreamReader
{
    public static async IAsyncEnumerable<string> ReadLinesAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using StreamReader reader = new(stream, leaveOpen: false);
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return line;
            }
        }
    }
}

