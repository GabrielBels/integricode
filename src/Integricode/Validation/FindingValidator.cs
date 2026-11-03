using Integricode.Domain;
using Integricode.GitHub;

namespace Integricode.Validation;

/// <summary>The reasons a proposed finding is dropped before publishing (FR-9 to FR-11).</summary>
public static class DropReason
{
    public const string MissingField = "missing-field";
    public const string PathNotInPullRequest = "path-not-in-pull-request";
    public const string LineNotFound = "line-not-found";
    public const string BugNotOnChangedLine = "bug-not-on-changed-line";
    public const string QuoteNotFound = "quote-not-found";
    public const string VerdictMissingField = "verdict-missing-field";
    public const string CriterionMissingField = "criterion-missing-field";
    public const string CriterionQuoteNotFound = "criterion-quote-not-found";
}

public sealed record ValidationOutcome(IReadOnlyList<Finding> Findings, IReadOnlyDictionary<string, int> Dropped);

/// <summary>
/// The provenance check: keeps a proposed finding only when its fields are present, its path is a pull-request file,
/// its line exists at the head SHA, a bug sits on a changed line, and a mismatch quotes the requirement verbatim
/// (whitespace-normalized, at least <see cref="MinQuoteLength"/> characters). Findings with the same fingerprint collapse
/// into the first. A suggested change is kept only when every line it replaces is a diff line and it stays within
/// <see cref="MaxSuggestionLines"/> lines and <see cref="MaxSuggestionLength"/> characters; otherwise the finding keeps
/// its fix in prose.
/// </summary>
/// <param name="diff">The pull request's diff map.</param>
/// <param name="requirementText">The requirement text read, or null when none was.</param>
/// <param name="headLineCount">The number of lines of a pull-request file at the head SHA, or null when it does not exist.</param>
public sealed class FindingValidator(DiffMap diff, string? requirementText, Func<string, int?> headLineCount)
{
    /// <summary>A shorter quote, such as "the", is found in any requirement and proves nothing about where it came from.</summary>
    public const int MinQuoteLength = 12;

    /// <summary>A one-click change the author may apply unread stays small enough to read in the comment.</summary>
    public const int MaxSuggestionLines = 20;

    public const int MaxSuggestionLength = 2000;

    private readonly string? normalizedRequirement = requirementText is null ? null : Fingerprint.NormalizeWhitespace(requirementText);

    /// <summary>Whether the quote, whitespace-normalized, is at least <see cref="MinQuoteLength"/> characters and appears in the requirement.</summary>
    /// <param name="normalizedRequirement">The whitespace-normalized requirement text, or null when none was read.</param>
    /// <param name="quote">The whitespace-normalized quote.</param>
    /// <returns>True when the quote is verbatim requirement text.</returns>
    internal static bool IsVerbatim(string? normalizedRequirement, string quote) =>
        normalizedRequirement is not null && quote.Length >= MinQuoteLength && normalizedRequirement.Contains(quote, StringComparison.Ordinal);

    public ValidationOutcome Validate(IEnumerable<ProposedFinding>? proposed)
    {
        var kept = new List<Finding>();
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        var dropped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var candidate in proposed ?? [])
        {
            var (finding, reason) = Check(candidate);
            if (finding is null)
            {
                dropped[reason!] = dropped.GetValueOrDefault(reason!) + 1;
            }
            else if (fingerprints.Add(finding.Fingerprint))
            {
                kept.Add(finding);
            }
        }

        return new ValidationOutcome(kept, dropped);
    }

    private (Finding? Finding, string? Reason) Check(ProposedFinding? candidate)
    {
        var kind = ParseKind(candidate?.Kind);
        var severity = ParseSeverity(candidate?.Severity);
        if (candidate is null
            || kind is null
            || severity is null
            || string.IsNullOrWhiteSpace(candidate.Title)
            || string.IsNullOrWhiteSpace(candidate.Path)
            || candidate.Line is not > 0
            || string.IsNullOrWhiteSpace(candidate.FailureScenario)
            || kind == FindingKind.RequirementMismatch && string.IsNullOrWhiteSpace(candidate.RequirementQuote))
        {
            return (null, DropReason.MissingField);
        }

        var path = NormalizePath(candidate.Path);
        var line = candidate.Line.Value;
        if (!diff.Contains(path))
        {
            return (null, DropReason.PathNotInPullRequest);
        }

        if (headLineCount(path) is not { } lineCount || line > lineCount)
        {
            return (null, DropReason.LineNotFound);
        }

        if (kind == FindingKind.Bug && !diff.IsChangedLine(path, line))
        {
            return (null, DropReason.BugNotOnChangedLine);
        }

        string? quote = null;
        if (kind == FindingKind.RequirementMismatch)
        {
            quote = Fingerprint.NormalizeWhitespace(candidate.RequirementQuote!);
            if (!IsVerbatim(normalizedRequirement, quote))
            {
                return (null, DropReason.QuoteNotFound);
            }
        }

        var finding = new Finding(kind.Value, candidate.Title.Trim(), path, line, candidate.FailureScenario.Trim(), quote)
        {
            Severity = severity.Value,
            Lens = ParseLens(candidate.Lens),
            Fix = string.IsNullOrWhiteSpace(candidate.Fix) ? null : candidate.Fix.Trim(),
            Suggestion = CheckSuggestion(candidate, path, line),
        };
        return (finding, null);
    }

    /// <summary>The suggested change when it replaces only diff lines and stays small; null otherwise.</summary>
    /// <param name="candidate">The proposed finding.</param>
    /// <param name="path">The finding's normalized path.</param>
    /// <param name="line">The finding's line, the last line the change replaces.</param>
    /// <returns>The change to publish, or null to publish the finding without one.</returns>
    private SuggestedChange? CheckSuggestion(ProposedFinding candidate, string path, int line)
    {
        if (string.IsNullOrWhiteSpace(candidate.Suggestion) || diff.Get(path) is not { } file)
        {
            return null;
        }

        var start = candidate.SuggestionStartLine ?? line;
        if (start < 1 || start > line || line - start + 1 > MaxSuggestionLines || Enumerable.Range(start, line - start + 1).Any(n => !file.DiffLines.Contains(n)))
        {
            return null;
        }

        // In Markdown a fence can be the very text the suggestion replaces, so only a markdown-tagged wrapper goes there.
        var suggestion = candidate.Suggestion.Replace("\r\n", "\n", StringComparison.Ordinal);
        var code = (IsMarkdown(path) && !IsMarkdownWrapper(suggestion) ? suggestion : WithoutFence(suggestion)).TrimEnd('\n');
        return code.Trim().Length == 0 || code.Length > MaxSuggestionLength || code.Split('\n').Length > MaxSuggestionLines
            ? null
            : new SuggestedChange(start, code);
    }

    private static bool IsMarkdown(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase);

    // A fence of three or more backticks tagged markdown or md around the whole suggestion wraps prose; a fence with
    // another tag is content.
    private static bool IsMarkdownWrapper(string code)
    {
        var first = code.TrimStart('\n').Split('\n', 2)[0].Trim();
        var ticks = first.Length - first.TrimStart('`').Length;
        var info = first[ticks..].Trim().Split(' ', 2)[0];
        return ticks >= 3 && (info.Equals("markdown", StringComparison.OrdinalIgnoreCase) || info.Equals("md", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Removes a Markdown code fence the model wrapped the code in, despite the instructions.</summary>
    /// <param name="code">The suggested code with LF line breaks.</param>
    /// <returns>The code without an enclosing fence.</returns>
    private static string WithoutFence(string code)
    {
        var lines = code.Trim('\n').Split('\n');
        return lines.Length >= 2 && lines[0].TrimStart().StartsWith("```", StringComparison.Ordinal) && lines[^1].Trim().StartsWith("```", StringComparison.Ordinal)
            ? string.Join('\n', lines[1..^1])
            : code;
    }

    private static FindingSeverity? ParseSeverity(string? severity) => Letters(severity) switch
    {
        "blocking" or "issueblocking" or "issue" => FindingSeverity.Blocking,
        "suggestion" => FindingSeverity.Suggestion,
        "nitpick" or "nit" => FindingSeverity.Nitpick,
        "note" => FindingSeverity.Note,
        _ => null,
    };

    // A lens such as "security (OWASP A01)" keeps its first word; an unknown one falls back to the generic lens.
    private static string ParseLens(string? lens)
    {
        var first = (lens ?? string.Empty).Trim().Split([' ', '(', ':', '/'], 2)[0].ToLowerInvariant();
        return FindingLens.All.Contains(first, StringComparer.Ordinal) ? first : FindingLens.Bug;
    }

    internal static string Letters(string? text) => new string((text ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();

    private static FindingKind? ParseKind(string? kind)
    {
        return Letters(kind) switch
        {
            "bug" => FindingKind.Bug,
            "requirementmismatch" or "mismatch" => FindingKind.RequirementMismatch,
            _ => null,
        };
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }
}
