namespace Integricode.Commands;

/// <summary>
/// Writes GitHub Actions workflow commands. Callers pass only identifiers, counts, states, and hashes: never
/// requirement text, code, model output, or secrets.
/// </summary>
public sealed class ActionsLog(TextWriter writer)
{
    public void Info(string message) => writer.WriteLine(Escape(message));

    public void Notice(string message) => writer.WriteLine("::notice::" + Escape(message));

    public void Warning(string message) => writer.WriteLine("::warning::" + Escape(message));

    public void Error(string message) => writer.WriteLine("::error::" + Escape(message));

    private static string Escape(string message) => message
        .Replace("%", "%25", StringComparison.Ordinal)
        .Replace("\r", "%0D", StringComparison.Ordinal)
        .Replace("\n", "%0A", StringComparison.Ordinal);
}
