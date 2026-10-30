using System.Globalization;
using System.Text.RegularExpressions;

namespace Integricode.Commands;

/// <summary>A wrong or missing command-line argument; the message names the option, never a value.</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>Parses <c>--name value</c> and <c>--name=value</c> options; an option may repeat.</summary>
public sealed partial class CommandLine
{
    private readonly Dictionary<string, List<string>> values = new(StringComparer.Ordinal);

    private CommandLine()
    {
    }

    /// <exception cref="UsageException">An argument is not an allowed option or has no value.</exception>
    public static CommandLine Parse(IReadOnlyList<string> args, IReadOnlySet<string> allowed)
    {
        var commandLine = new CommandLine();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new UsageException($"Unexpected argument at position {i + 1}.");
            }

            var separator = arg.IndexOf('=');
            var name = separator < 0 ? arg[2..] : arg[2..separator];
            if (!allowed.Contains(name))
            {
                throw new UsageException($"Unknown option --{name}.");
            }

            string value;
            if (separator >= 0)
            {
                value = arg[(separator + 1)..];
            }
            else if (i + 1 < args.Count)
            {
                value = args[++i];
            }
            else
            {
                throw new UsageException($"Option --{name} needs a value.");
            }

            if (!commandLine.values.TryGetValue(name, out var list))
            {
                commandLine.values[name] = list = [];
            }

            list.Add(value);
        }

        return commandLine;
    }

    public string? Optional(string name) =>
        values.TryGetValue(name, out var list) && !string.IsNullOrWhiteSpace(list[^1]) ? list[^1].Trim() : null;

    public string Required(string name) => Optional(name) ?? throw new UsageException($"Option --{name} is required.");

    public IReadOnlyList<string> All(string name) => values.TryGetValue(name, out var list) ? list : [];

    /// <summary>The <c>--repo</c> option as <c>owner/name</c>; it becomes part of API paths, so nothing else is accepted.</summary>
    public string Repository()
    {
        var repository = Required("repo");
        return RepositoryName().IsMatch(repository) && !repository.Split('/').Any(part => part is "." or "..")
            ? repository
            : throw new UsageException("Option --repo must be owner/name.");
    }

    public int PullRequestNumber()
    {
        Required("pr");
        return (int)Number("pr", defaultValue: 0, minimum: 1, maximum: int.MaxValue);
    }

    public long Number(string name, long defaultValue, long minimum, long maximum = long.MaxValue)
    {
        var text = Optional(name);
        if (text is null)
        {
            return defaultValue;
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum && number <= maximum
            ? number
            : throw new UsageException($"Option --{name} must be a whole number from {minimum.ToString(CultureInfo.InvariantCulture)} to {maximum.ToString(CultureInfo.InvariantCulture)}.");
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepositoryName();
}
