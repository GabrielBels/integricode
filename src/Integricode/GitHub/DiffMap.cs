using System.Globalization;
using System.Text.RegularExpressions;

namespace Integricode.GitHub;

/// <summary>
/// Maps each pull-request file's patch to right-side line numbers: <see cref="FileDiff.ChangedLines"/> are the added or
/// modified lines, <see cref="FileDiff.DiffLines"/> every right-side line shown in a hunk (where an inline comment fits).
/// </summary>
public sealed partial class DiffMap
{
    private readonly Dictionary<string, FileDiff> files;

    private DiffMap(Dictionary<string, FileDiff> files) => this.files = files;

    public IReadOnlyCollection<string> Paths => files.Keys;

    public static DiffMap Build(IEnumerable<PullRequestFile> pullRequestFiles)
    {
        ArgumentNullException.ThrowIfNull(pullRequestFiles);
        var map = new Dictionary<string, FileDiff>(StringComparer.Ordinal);
        foreach (var file in pullRequestFiles)
        {
            map[file.Path] = Parse(file.Patch);
        }

        return new DiffMap(map);
    }

    public bool Contains(string path) => files.ContainsKey(path);

    public bool IsChangedLine(string path, int line) => files.TryGetValue(path, out var diff) && diff.ChangedLines.Contains(line);

    public bool IsDiffLine(string path, int line) => files.TryGetValue(path, out var diff) && diff.DiffLines.Contains(line);

    public FileDiff? Get(string path) => files.GetValueOrDefault(path);

    /// <summary>Parses a unified-diff patch as GitHub returns it (hunks only, no file headers).</summary>
    public static FileDiff Parse(string? patch)
    {
        var changed = new SortedSet<int>();
        var shown = new SortedSet<int>();
        if (patch is null)
        {
            return new FileDiff(changed, shown, HasPatch: false);
        }

        var rightLine = 0;
        var inHunk = false;
        foreach (var rawLine in patch.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var header = HunkHeader().Match(line);
            if (header.Success)
            {
                rightLine = int.Parse(header.Groups["start"].Value, CultureInfo.InvariantCulture);
                inHunk = true;
                continue;
            }

            // An empty line is the split artifact of a trailing newline; an empty context line is a single space.
            if (!inHunk || line.Length == 0)
            {
                continue;
            }

            switch (line[0])
            {
                case '+':
                    changed.Add(rightLine);
                    shown.Add(rightLine);
                    rightLine++;
                    break;
                case ' ':
                    shown.Add(rightLine);
                    rightLine++;
                    break;
                default:
                    // '-' lines exist only on the left side; '\' marks "No newline at end of file".
                    break;
            }
        }

        return new FileDiff(changed, shown, HasPatch: true);
    }

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(?<start>\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();
}

public sealed record FileDiff(IReadOnlySet<int> ChangedLines, IReadOnlySet<int> DiffLines, bool HasPatch);
