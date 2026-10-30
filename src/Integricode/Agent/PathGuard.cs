using System.Text;
using System.Text.RegularExpressions;

namespace Integricode.Agent;

/// <summary>The outcome of a path check: the resolved path when allowed, otherwise the refusal reason.</summary>
public sealed record PathCheck(bool Allowed, string FullPath, string RelativePath, string? Refusal)
{
    public static PathCheck Refuse(string reason) => new(false, string.Empty, string.Empty, reason);
}

/// <summary>
/// Keeps every read inside the head-SHA checkout (ADR-002): refuses absolute paths, <c>..</c> segments, symbolic links
/// that leave the checkout, and excluded paths.
/// </summary>
public sealed class PathGuard
{
    public static IReadOnlyList<string> DefaultExclusions { get; } =
    [
        ".git/",
        ".env*",
        "*.pem", "*.key", "*.pfx", "*.p12", "id_rsa*",
        "*.lock", "package-lock.json", "npm-shrinkwrap.json", "pnpm-lock.yaml", "packages.lock.json", "go.sum",
        "*.min.*",
        "*.dll", "*.exe", "*.so", "*.dylib", "*.bin", "*.o", "*.a", "*.lib", "*.class", "*.jar", "*.nupkg", "*.snk",
        "*.zip", "*.gz", "*.tar", "*.7z", "*.rar",
        "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.ico", "*.webp", "*.pdf",
        "*.woff", "*.woff2", "*.ttf", "*.otf", "*.eot", "*.mp3", "*.mp4", "*.mov", "*.wasm", "*.db", "*.sqlite",
    ];

    private readonly IReadOnlyList<Regex> segmentPatterns;
    private readonly IReadOnlyList<Regex> pathPatterns;

    /// <param name="root">The checkout root.</param>
    /// <param name="exclusions">Globs refused in addition to <see cref="DefaultExclusions"/>.</param>
    public PathGuard(string root, IEnumerable<string>? exclusions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var segments = new List<Regex>();
        var paths = new List<Regex>();
        foreach (var glob in DefaultExclusions.Concat(exclusions ?? []).Select(g => g.Trim()).Where(g => g.Length > 0))
        {
            var pattern = glob.Trim('/');
            if (pattern.Contains('/'))
            {
                // A glob with a slash is anchored at the root and also covers everything under a matching directory.
                paths.Add(GlobToRegex(pattern, "(?:/.*)?"));
            }
            else
            {
                // A glob without a slash matches a file or directory name at any depth, like .gitignore.
                segments.Add(GlobToRegex(pattern, string.Empty));
            }
        }

        segmentPatterns = segments;
        pathPatterns = paths;
    }

    public string Root { get; }

    /// <summary>Resolves a checkout-relative path; <c>""</c> or <c>.</c> is the root itself.</summary>
    public PathCheck Check(string? relativePath, bool applyExclusions = true)
    {
        var path = (relativePath ?? string.Empty).Trim();
        if (Path.IsPathRooted(path) || path.StartsWith('/') || path.StartsWith('\\') || path.Contains(':'))
        {
            return PathCheck.Refuse("absolute paths are not allowed");
        }

        var segments = path.Split('/', '\\').Where(s => s.Length > 0 && s != ".").ToArray();
        if (segments.Contains(".."))
        {
            return PathCheck.Refuse("parent directory segments are not allowed");
        }

        var normalized = string.Join('/', segments);
        var fullPath = segments.Length == 0 ? Root : Path.GetFullPath(Path.Combine(Root, Path.Combine(segments)));
        if (!IsInside(fullPath))
        {
            return PathCheck.Refuse("the path leaves the checkout");
        }

        var (linkRefusal, targetPath) = ResolveLinks(segments);
        if (linkRefusal is not null)
        {
            return PathCheck.Refuse(linkRefusal);
        }

        // Both the requested path and the path a link leads to must be allowed: notes.txt -> .env is still .env.
        if (applyExclusions && (IsExcluded(normalized) || IsExcluded(targetPath)))
        {
            return PathCheck.Refuse("the path is excluded from the review context");
        }

        return new PathCheck(true, fullPath, normalized, null);
    }

    /// <summary>Whether a normalized checkout-relative path (forward slashes) matches an exclusion.</summary>
    public bool IsExcluded(string relativePath)
    {
        if (relativePath.Length == 0)
        {
            return false;
        }

        var segments = relativePath.Split('/');
        return segments.Any(segment => segmentPatterns.Any(pattern => pattern.IsMatch(segment)))
            || pathPatterns.Any(pattern => pattern.IsMatch(relativePath));
    }

    /// <summary>Follows every symbolic link on the path; returns a refusal, or the checkout-relative path it leads to.</summary>
    private (string? Refusal, string TargetPath) ResolveLinks(string[] segments)
    {
        var current = Root;
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (!info.Exists || info.LinkTarget is null)
            {
                continue;
            }

            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is null || !IsInside(Path.GetFullPath(target.FullName)))
            {
                return ("a symbolic link leaves the checkout", string.Empty);
            }

            current = Path.GetFullPath(target.FullName);
        }

        var relative = Path.GetRelativePath(Root, current).Replace('\\', '/');
        return (null, relative == "." ? string.Empty : relative);
    }

    private bool IsInside(string fullPath)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(fullPath, Root, comparison)
            || fullPath.StartsWith(Root + Path.DirectorySeparatorChar, comparison);
    }

    private static Regex GlobToRegex(string glob, string suffix)
    {
        var pattern = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                var slash = i + 2 < glob.Length && glob[i + 2] == '/';
                pattern.Append(slash ? "(?:.*/)?" : ".*");
                i += slash ? 2 : 1;
            }
            else
            {
                pattern.Append(c switch
                {
                    '*' => "[^/]*",
                    '?' => "[^/]",
                    _ => Regex.Escape(c.ToString()),
                });
            }
        }

        // Exclusions err on the side of refusing: .ENV is as secret as .env.
        return new Regex(pattern.Append(suffix).Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
