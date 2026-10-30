using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Integricode.Domain;

namespace Integricode.Commands;

/// <summary>The hidden record a summary carries (FR-17): each published finding and the run's usage.</summary>
public sealed record SummaryRecord(
    int SchemaVersion,
    string HeadSha,
    CoverageState State,
    string? Requirement,
    IReadOnlyList<RecordedFinding> Findings,
    int Dropped,
    Usage Usage);

public sealed record RecordedFinding(FindingKind Kind, string Path, int Line, string Fingerprint, string HeadSha);

/// <summary>
/// The fixed templates of everything Integricode publishes. Text that came from the model or the repository is
/// length-capped, HTML-escaped, and has its <c>@</c> mentions neutralized before it enters a template.
/// </summary>
public static partial class CommentTemplates
{
    public const string SummaryMarker = "<!-- integricode:summary -->";
    public const int MaxTitleLength = 200;
    public const int MaxScenarioLength = 1000;
    public const int MaxQuoteLength = 500;
    public const int MaxPathLength = 300;
    public const int MaxReasonLength = 300;

    private const string RecordStart = "<!-- integricode:record";

    private static readonly JsonSerializerOptions RecordJson = new(ReviewResult.JsonOptions) { WriteIndented = false };
    private const string HowToLink = "Add a REQ ID (`REQ-NNN`) or a Jira key to the pull request title, branch name, or description, then comment `@integricode review`.";

    public static string FindingMarker(string fingerprint) => $"<!-- integricode:finding:{fingerprint} -->";

    /// <summary>The fingerprint of an Integricode inline comment, or null for any other comment.</summary>
    public static string? FingerprintOf(string body)
    {
        var match = FindingMarkerPattern().Match(body);
        return match.Success ? match.Groups["fingerprint"].Value : null;
    }

    public static string InlineComment(Finding finding, string headSha)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var body = new StringBuilder(FindingMarker(finding.Fingerprint)).Append('\n');
        body.Append("**").Append(KindLabel(finding.Kind)).Append(":** ").Append(Sanitize(finding.Title, MaxTitleLength)).Append("\n\n");
        if (finding.RequirementQuote is { } quote)
        {
            body.Append("> ").Append(Sanitize(quote, MaxQuoteLength)).Append("\n\n");
        }

        body.Append(Sanitize(finding.FailureScenario, MaxScenarioLength)).Append("\n\n");
        return body.Append("<sub>Integricode at ").Append(Sha(Short(headSha))).Append("</sub>").ToString();
    }

    public static string ReviewBody(int findings, string headSha) =>
        $"Integricode found {findings.ToString(CultureInfo.InvariantCulture)} new {(findings == 1 ? "finding" : "findings")} at {Sha(Short(headSha))}; the summary comment has the coverage.";

    /// <summary>The summary of a result whose findings are published (or that has none to publish).</summary>
    /// <param name="offDiff">Findings outside the diff's lines, listed in the summary instead of inline.</param>
    public static string Summary(ReviewResult result, IReadOnlyList<Finding> offDiff)
    {
        ArgumentNullException.ThrowIfNull(result);
        var publishes = result.Coverage.State is CoverageState.Complete or CoverageState.Incomplete;
        var body = Header(result.HeadSha, result, StateLabel(result.Coverage.State));
        if (publishes)
        {
            body.Append("| Findings | ").Append(Counts(result.Findings)).Append(" |\n");
            body.Append("| Dropped by the provenance check | ").Append(Dropped(result)).Append(" |\n");
        }

        body.Append('\n');
        AppendReasons(body, result);
        if (publishes && offDiff.Count > 0)
        {
            body.Append("**Findings outside the diff**\n\n");
            foreach (var finding in offDiff)
            {
                body.Append("- **").Append(KindLabel(finding.Kind)).Append("** at ")
                    .Append(Code(finding.Path + ":" + finding.Line.ToString(CultureInfo.InvariantCulture), MaxPathLength))
                    .Append(": ").Append(Sanitize(finding.Title, MaxTitleLength)).Append('\n');
                if (finding.RequirementQuote is { } quote)
                {
                    body.Append("  > ").Append(Sanitize(quote, MaxQuoteLength)).Append('\n');
                }

                body.Append("  ").Append(Sanitize(finding.FailureScenario, MaxScenarioLength)).Append('\n');
            }

            body.Append('\n');
        }

        return AppendRecord(body, result, publishes ? result.Findings : []).ToString();
    }

    /// <summary>The summary of a result whose head SHA is no longer the pull request's head (FR-12).</summary>
    public static string Superseded(ReviewResult result, string currentHeadSha)
    {
        ArgumentNullException.ThrowIfNull(result);
        var body = Header(result.HeadSha, result, "Superseded");
        body.Append('\n').Append("The pull request moved to ").Append(Sha(Short(currentHeadSha)))
            .Append(" before this review of ").Append(Sha(Short(result.HeadSha)))
            .Append(" was published, so no findings were published. Comment `@integricode review` to review the current head.\n\n");
        return AppendRecord(body, result with { Coverage = Coverage.Superseded("head SHA changed") }, []).ToString();
    }

    /// <summary>Reads the hidden record of a summary, or null when it has none.</summary>
    public static SummaryRecord? ReadRecord(string summary)
    {
        var start = summary.IndexOf(RecordStart, StringComparison.Ordinal);
        var end = start < 0 ? -1 : summary.IndexOf("-->", start, StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SummaryRecord>(summary[(start + RecordStart.Length)..end], ReviewResult.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Collapses whitespace, caps the length, escapes HTML, neutralizes <c>@</c> mentions, and escapes square brackets
    /// so the text cannot form Markdown links or images.
    /// </summary>
    public static string Sanitize(string text, int maxLength)
    {
        // A zero-width space after @ keeps the text readable without notifying anyone.
        return WebUtility.HtmlEncode(Cap(text, maxLength))
            .Replace("@", "@&#8203;", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
    }

    private static string Cap(string text, int maxLength)
    {
        var single = Fingerprint.NormalizeWhitespace(text);
        return single.Length > maxLength ? single[..(maxLength - 1)] + "…" : single;
    }

    private static StringBuilder Header(string headSha, ReviewResult result, string state)
    {
        var body = new StringBuilder(SummaryMarker).Append("\n### Integricode review\n\n| | |\n|---|---|\n");
        body.Append("| State | ").Append(state).Append(" |\n");
        body.Append("| Head SHA | ").Append(Sha(headSha)).Append(" |\n");
        body.Append("| Requirement | ").Append(result.Requirement is { } link
            ? $"{Sanitize(link.Id, 40)} (from the {SourceLabel(link.Source)})"
            : "none linked").Append(" |\n");
        return body;
    }

    private static void AppendReasons(StringBuilder body, ReviewResult result)
    {
        if (result.Coverage.Reasons.Count == 0)
        {
            return;
        }

        body.Append(result.Coverage.State == CoverageState.Incomplete ? "**What was missing**\n\n" : "**Reason**\n\n");
        foreach (var reason in result.Coverage.Reasons)
        {
            body.Append("- ").Append(Sanitize(reason, MaxReasonLength)).Append('\n');
        }

        if (result.Coverage.State == CoverageState.Incomplete && result.Requirement is null)
        {
            body.Append('\n').Append(HowToLink).Append('\n');
        }

        body.Append('\n');
    }

    private static StringBuilder AppendRecord(StringBuilder body, ReviewResult result, IReadOnlyList<Finding> published)
    {
        var record = new SummaryRecord(
            ReviewResult.CurrentSchemaVersion,
            result.HeadSha,
            result.Coverage.State,
            result.Requirement?.Id,
            [.. published.Select(f => new RecordedFinding(f.Kind, f.Path, f.Line, f.Fingerprint, result.HeadSha))],
            result.DroppedCount,
            result.Usage);

        // The default encoder escapes < and >, so the JSON can never close the HTML comment early.
        var json = JsonSerializer.Serialize(record, RecordJson);
        return body.Append(RecordStart).Append('\n').Append(json).Append("\n-->\n");
    }

    private static string Counts(IReadOnlyList<Finding> findings)
    {
        var mismatches = findings.Count(f => f.Kind == FindingKind.RequirementMismatch);
        var bugs = findings.Count(f => f.Kind == FindingKind.Bug);
        return $"{mismatches.ToString(CultureInfo.InvariantCulture)} requirement {(mismatches == 1 ? "mismatch" : "mismatches")}, {bugs.ToString(CultureInfo.InvariantCulture)} {(bugs == 1 ? "bug" : "bugs")}";
    }

    private static string Dropped(ReviewResult result) => result.DroppedCount == 0
        ? "0"
        : $"{result.DroppedCount.ToString(CultureInfo.InvariantCulture)} ({string.Join(", ", result.Dropped.Select(pair => $"{Sanitize(pair.Key, 40)} {pair.Value.ToString(CultureInfo.InvariantCulture)}"))})";

    private static string StateLabel(CoverageState state) => state switch
    {
        CoverageState.Complete => "Reviewed, coverage complete",
        CoverageState.Incomplete => "Reviewed, coverage incomplete",
        CoverageState.SkippedByPolicy => "Skipped by policy",
        CoverageState.Superseded => "Superseded",
        _ => "Failed",
    };

    private static string KindLabel(FindingKind kind) => kind == FindingKind.Bug ? "Bug" : "Requirement mismatch";

    private static string SourceLabel(Requirements.RequirementLinkSource source) => source switch
    {
        Requirements.RequirementLinkSource.Title => "title",
        Requirements.RequirementLinkSource.Branch => "branch name",
        _ => "description",
    };

    private static string Short(string sha) => sha.Length > 12 ? sha[..12] : sha;

    private static string Sha(string sha) => Code(sha, 64);

    // Markdown shows a code span's content literally, so HTML and mentions are inert there and are not escaped.
    private static string Code(string text, int maxLength) => "`" + Cap(text, maxLength).Replace("`", "'", StringComparison.Ordinal) + "`";

    [GeneratedRegex(@"^<!-- integricode:finding:(?<fingerprint>[0-9a-f]{64}) -->")]
    private static partial Regex FindingMarkerPattern();
}
