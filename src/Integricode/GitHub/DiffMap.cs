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

        foreach (var line in RightSideLines(patch))
        {
            shown.Add(line.Number);
            if (line.Added)
            {
                changed.Add(line.Number);
            }
        }

        return new FileDiff(changed, shown, HasPatch: true);
    }

    /// <summary>The lines a patch adds or modifies, with their right-side line numbers and text without the <c>+</c>.</summary>
    /// <param name="patch">A unified-diff patch as GitHub returns it.</param>
    /// <returns>The added lines in patch order.</returns>
    public static IEnumerable<PatchLine> AddedLines(string patch) => RightSideLines(patch).Where(line => line.Added);

    private static IEnumerable<PatchLine> RightSideLines(string patch)
    {
        var rightLine = 0;
        var inHunk = false;
        var body = patch.EndsWith('\n') ? patch[..^1] : patch;
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var header = HunkHeader().Match(line);
            if (header.Success)
            {
                rightLine = int.Parse(header.Groups["start"].Value, CultureInfo.InvariantCulture);
                inHunk = true;
                continue;
            }

            if (!inHunk)
            {
                continue;
            }

            // An empty line inside a hunk is a blank context line whose leading space was stripped.
            switch (line.Length == 0 ? ' ' : line[0])
            {
                case '+':
                    yield return new PatchLine(rightLine++, line[1..], Added: true);
                    break;
                case ' ':
                    yield return new PatchLine(rightLine++, line.Length == 0 ? line : line[1..], Added: false);
                    break;
                default:
                    // '-' lines exist only on the left side; '\' marks "No newline at end of file".
                    break;
            }
        }
    }

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(?<start>\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();
}

public sealed record FileDiff(IReadOnlySet<int> ChangedLines, IReadOnlySet<int> DiffLines, bool HasPatch);

/// <summary>A right-side line of a patch: its head-commit line number, its text, and whether the patch adds it.</summary>
public sealed record PatchLine(int Number, string Text, bool Added);
