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
}

public sealed record ValidationOutcome(IReadOnlyList<Finding> Findings, IReadOnlyDictionary<string, int> Dropped);

/// <summary>
/// The provenance check: keeps a proposed finding only when its fields are present, its path is a pull-request file,
/// its line exists at the head SHA, a bug sits on a changed line, and a mismatch quotes the requirement verbatim
/// (whitespace-normalized). Findings with the same fingerprint collapse into the first.
/// </summary>
/// <param name="diff">The pull request's diff map.</param>
/// <param name="requirementText">The requirement text read, or null when none was.</param>
/// <param name="headLineCount">The number of lines of a pull-request file at the head SHA, or null when it does not exist.</param>
public sealed class FindingValidator(DiffMap diff, string? requirementText, Func<string, int?> headLineCount)
{
    private readonly string? normalizedRequirement = requirementText is null ? null : Fingerprint.NormalizeWhitespace(requirementText);

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
        if (candidate is null
            || kind is null
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
            if (normalizedRequirement is null || !normalizedRequirement.Contains(quote, StringComparison.Ordinal))
            {
                return (null, DropReason.QuoteNotFound);
            }
        }

        return (new Finding(kind.Value, candidate.Title.Trim(), path, line, candidate.FailureScenario.Trim(), quote), null);
    }

    private static FindingKind? ParseKind(string? kind)
    {
        var letters = new string((kind ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();
        return letters switch
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
