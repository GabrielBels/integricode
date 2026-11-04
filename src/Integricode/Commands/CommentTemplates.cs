using System.Globalization;
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

public sealed record RecordedFinding(FindingKind Kind, string Path, int Line, string Fingerprint, string HeadSha)
{
    public FindingSeverity Severity { get; init; } = FindingSeverity.Blocking;
}

/// <summary>
/// The fixed templates of everything Integricode publishes, in the format of the dev-flow <c>pr-review</c> skill: a
/// traffic-light scoreboard, Conventional Comments labels, and one-click suggestions, with labels in the result's
/// language. Text that came from the model or the repository is length-capped and, outside code spans, HTML-escaped,
/// with <c>@</c> mentions and Markdown links neutralized; suggested code sits in a fence it cannot close.
/// </summary>
public static partial class CommentTemplates
{
    public const string SummaryMarker = "<!-- integricode:summary -->";
    public const int MaxTitleLength = 200;
    public const int MaxScenarioLength = 1000;
    public const int MaxQuoteLength = 500;
    public const int MaxPathLength = 300;
    public const int MaxReasonLength = 300;
    public const int MaxEvidenceLength = 300;
    public const int MaxExcerptLength = 200;

    private const string RecordStart = "<!-- integricode:record";

    private static readonly JsonSerializerOptions RecordJson = new(ReviewResult.JsonOptions) { WriteIndented = false };

    public static string FindingMarker(string fingerprint) => $"<!-- integricode:finding:{fingerprint} -->";

    /// <summary>The fingerprint of an Integricode inline comment, or null for any other comment.</summary>
    public static string? FingerprintOf(string body)
    {
        var match = FindingMarkerPattern().Match(body);
        return match.Success ? match.Groups["fingerprint"].Value : null;
    }

    /// <summary>One inline comment: the light and label, the headline and lens, the scenario, the fix, and the suggestion.</summary>
    /// <param name="finding">A finding on a diff line.</param>
    /// <param name="headSha">The head SHA the finding was reviewed at.</param>
    /// <param name="language">The labels' language.</param>
    /// <returns>The comment body.</returns>
    public static string InlineComment(Finding finding, string headSha, string language = ReviewLanguage.English)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var labels = LabelsFor(language);
        var body = new StringBuilder(FindingMarker(finding.Fingerprint)).Append('\n');
        body.Append(Headline(finding, labels)).Append("\n\n");
        if (finding.RequirementQuote is { } quote)
        {
            body.Append("> ").Append(Sanitize(quote, MaxQuoteLength)).Append("\n\n");
        }

        body.Append(Sanitize(finding.FailureScenario, MaxScenarioLength)).Append("\n\n");
        if (finding.Fix is { } fix)
        {
            body.Append(Sanitize(fix, MaxScenarioLength)).Append("\n\n");
        }

        if (finding.Suggestion is { } suggestion)
        {
            var fence = new string('`', Math.Max(3, LongestBacktickRun(suggestion.Code) + 1));
            body.Append(fence).Append("suggestion\n").Append(suggestion.Code).Append('\n').Append(fence).Append("\n\n");
        }

        return body.Append("<sub>").Append(Format(labels.Footer, Sha(Short(headSha)))).Append("</sub>").ToString();
    }

    /// <summary>The body of the review that holds the inline comments: the scoreboard and how many are new.</summary>
    /// <param name="result">The published result.</param>
    /// <param name="fresh">The number of inline comments the review posts.</param>
    /// <returns>The review body.</returns>
    public static string ReviewBody(ReviewResult result, int fresh)
    {
        ArgumentNullException.ThrowIfNull(result);
        var labels = LabelsFor(result.Language);
        return Scoreboard(result, labels) + "\n\n" + Format(fresh == 1 ? labels.ReviewBodyOne : labels.ReviewBodyMany, Count(fresh), Sha(Short(result.HeadSha)));
    }

    /// <summary>The summary of a result whose findings are published (or that has none to publish).</summary>
    /// <param name="offDiff">Findings outside the diff's lines, listed in the summary instead of inline.</param>
    public static string Summary(ReviewResult result, IReadOnlyList<Finding> offDiff)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(offDiff);
        var labels = LabelsFor(result.Language);
        var publishes = result.Coverage.State is CoverageState.Complete or CoverageState.Incomplete;
        var body = new StringBuilder(SummaryMarker).Append("\n### 🚦 ").Append(labels.Title).Append("\n\n");
        if (publishes)
        {
            body.Append(Scoreboard(result, labels)).Append("\n\n");
            AppendVerdict(body, result, labels);
            AppendOffDiff(body, offDiff, labels);
            AppendInjectionWarnings(body, result, labels);
        }

        AppendReasons(body, result, labels);
        AppendFacts(body, result, labels, StateLabel(result.Coverage.State, labels), publishes);
        return AppendRecord(body, result, publishes ? result.Findings : []).ToString();
    }

    /// <summary>The summary of a result whose head SHA is no longer the pull request's head (FR-12).</summary>
    public static string Superseded(ReviewResult result, string currentHeadSha)
    {
        ArgumentNullException.ThrowIfNull(result);
        var labels = LabelsFor(result.Language);
        var body = new StringBuilder(SummaryMarker).Append("\n### 🚦 ").Append(labels.Title).Append("\n\n");
        body.Append(Format(labels.SupersededText, Sha(Short(currentHeadSha)), Sha(Short(result.HeadSha)))).Append("\n\n");
        AppendFacts(body, result, labels, labels.StateSuperseded, publishes: false);
        return AppendRecord(body, result with { Coverage = Coverage.Superseded("head SHA changed") }, []).ToString();
    }

    /// <summary>Reads the hidden record of a summary, or null when it has none.</summary>
    public static SummaryRecord? ReadRecord(string summary)
    {
        // The record is always last; a code span in a finding above it may quote the marker.
        var start = summary.LastIndexOf(RecordStart, StringComparison.Ordinal);
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
    /// Collapses whitespace and caps the length. Outside code spans it also escapes HTML, neutralizes <c>@</c> mentions,
    /// and escapes square brackets so the text cannot form Markdown links or images. Code spans are kept as written,
    /// because Markdown shows their content literally; a stray backtick becomes <c>'</c> so no span swallows the rest.
    /// </summary>
    public static string Sanitize(string text, int maxLength) =>
        CodeSpanOrProse().Replace(
            Cap(text, maxLength),
            match => match.Value.Length > 1 && match.Value[0] == '`'
                ? match.Value
                : EscapeProse(match.Value.Replace('`', '\'')));

    private static string EscapeProse(string text) =>
        // Only the HTML-significant characters are escaped, so accented text stays readable in the raw Markdown.
        // A backslash is escaped first: unescaped before a backtick, it would turn the next code span into prose.
        // A zero-width space after @ keeps the text readable without notifying anyone.
        text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal)
            .Replace("@", "@&#8203;", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);

    private static string Cap(string text, int maxLength)
    {
        var single = Fingerprint.NormalizeWhitespace(text);
        return single.Length > maxLength ? single[..(maxLength - 1)] + "…" : single;
    }

    /// <summary>The first line of a finding: light, Conventional Comments label, bold headline, and lens.</summary>
    private static string Headline(Finding finding, Labels labels)
    {
        var lens = finding.Kind == FindingKind.RequirementMismatch ? labels.RequirementLens : finding.Lens;
        return $"{SeverityLabel(finding.Severity)} **{Sanitize(finding.Title, MaxTitleLength)}** · _{Sanitize(lens, 40)}_";
    }

    private static string SeverityLabel(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Blocking => "🔴 `issue (blocking)`",
        FindingSeverity.Suggestion => "🟡 `suggestion`",
        FindingSeverity.Nitpick => "🟢 `nitpick`",
        _ => "🟢 `note`",
    };

    /// <summary>
    /// The counts by light and the verdict on merging, as the first line of the summary and the review. It says "good to
    /// merge" only when nothing blocks, the requirement verdict (when a requirement is linked) is yes, no injection
    /// warning was raised, and the coverage is complete.
    /// </summary>
    private static string Scoreboard(ReviewResult result, Labels labels)
    {
        var red = result.Findings.Count(f => f.Severity == FindingSeverity.Blocking);
        var yellow = result.Findings.Count(f => f.Severity == FindingSeverity.Suggestion);
        var green = result.Findings.Count - red - yellow;
        var verdict = red > 0 ? labels.VerdictBlocking
            : result.InjectionWarnings.Count > 0 ? labels.VerdictInjection
            : result.Verdict?.Answer is VerdictAnswer.No or VerdictAnswer.Partly ? labels.VerdictRequirementUnmet
            : result.Coverage.State == CoverageState.Incomplete ? labels.VerdictIncomplete
            : result.Requirement is not null && result.Verdict is null ? labels.VerdictUnjudged
            : yellow > 0 ? labels.VerdictYellow
            : green > 0 ? labels.VerdictGreen
            : labels.VerdictClear;
        return $"🔴 {Count(red)} · 🟡 {Count(yellow)} · 🟢 {Count(green)} — {verdict}";
    }

    private static void AppendVerdict(StringBuilder body, ReviewResult result, Labels labels)
    {
        if (result.Verdict is not { } verdict || result.Requirement is not { } link)
        {
            return;
        }

        var answer = verdict.Answer switch
        {
            VerdictAnswer.Yes => labels.Yes,
            VerdictAnswer.Partly => labels.Partly,
            _ => labels.No,
        };
        body.Append("**").Append(Format(labels.Solves, Sanitize(link.Id, 40))).Append("** ").Append(answer).Append(" — ")
            .Append(Sanitize(verdict.Summary, MaxScenarioLength)).Append('\n');
        foreach (var row in verdict.Criteria.Where(row => row.Status != CriterionStatus.Covered))
        {
            var mark = row.Status switch
            {
                CriterionStatus.Partial => "⚠️",
                CriterionStatus.Missing => "❌",
                _ => "❓",
            };
            body.Append("- ").Append(mark).Append(" \"").Append(Sanitize(row.Quote, MaxQuoteLength)).Append("\" — ")
                .Append(Sanitize(row.Evidence, MaxEvidenceLength)).Append('\n');
        }

        var covered = verdict.Criteria.Count(row => row.Status == CriterionStatus.Covered);
        if (verdict.Criteria.Count > 0)
        {
            body.Append(covered == verdict.Criteria.Count ? "- ✅ " : "- ⚠️ ")
                .Append(Format(labels.CoveredCount, Count(covered), Count(verdict.Criteria.Count))).Append('\n');
        }

        body.Append('\n');
    }

    private static void AppendOffDiff(StringBuilder body, IReadOnlyList<Finding> offDiff, Labels labels)
    {
        if (offDiff.Count == 0)
        {
            return;
        }

        body.Append("**").Append(labels.OutsideDiff).Append("**\n\n");
        foreach (var finding in offDiff)
        {
            body.Append("- ").Append(Headline(finding, labels)).Append(" — ")
                .Append(Code(finding.Path + ":" + finding.Line.ToString(CultureInfo.InvariantCulture), MaxPathLength)).Append('\n');
            if (finding.RequirementQuote is { } quote)
            {
                body.Append("  > ").Append(Sanitize(quote, MaxQuoteLength)).Append('\n');
            }

            body.Append("  ").Append(Sanitize(finding.FailureScenario, MaxScenarioLength)).Append('\n');
            if (finding.Fix is { } fix)
            {
                body.Append("  ").Append(Sanitize(fix, MaxScenarioLength)).Append('\n');
            }
        }

        body.Append('\n');
    }

    private static void AppendInjectionWarnings(StringBuilder body, ReviewResult result, Labels labels)
    {
        if (result.InjectionWarnings.Count == 0)
        {
            return;
        }

        body.Append("**⚠️ ").Append(labels.Injection).Append("**\n\n").Append(labels.InjectionNote).Append("\n\n");
        foreach (var warning in result.InjectionWarnings)
        {
            var where = warning.Path is null
                ? labels.InRequirement
                : Code(warning.Path + ":" + (warning.Line ?? 0).ToString(CultureInfo.InvariantCulture), MaxPathLength);
            body.Append("- ").Append(where).Append(": \"").Append(Sanitize(warning.Excerpt, MaxExcerptLength)).Append("\"\n");
        }

        body.Append('\n');
    }

    private static void AppendReasons(StringBuilder body, ReviewResult result, Labels labels)
    {
        if (result.Coverage.Reasons.Count == 0)
        {
            return;
        }

        body.Append("**").Append(result.Coverage.State == CoverageState.Incomplete ? labels.Missing : labels.Reason).Append("**\n\n");
        foreach (var reason in result.Coverage.Reasons)
        {
            body.Append("- ").Append(Sanitize(CoverageReasonText.Localize(reason, result.Language), MaxReasonLength)).Append('\n');
        }

        if (result.Coverage.State == CoverageState.Incomplete && result.Requirement is null)
        {
            body.Append('\n').Append(labels.HowToLink).Append('\n');
        }

        body.Append('\n');
    }

    /// <summary>The facts table: state, head SHA, requirement and, for a published review, counts, drops, and lenses.</summary>
    private static void AppendFacts(StringBuilder body, ReviewResult result, Labels labels, string state, bool publishes)
    {
        body.Append("| | |\n|---|---|\n");
        body.Append("| ").Append(labels.State).Append(" | ").Append(state).Append(" |\n");
        body.Append("| Head SHA | ").Append(Sha(result.HeadSha)).Append(" |\n");
        body.Append("| ").Append(labels.Requirement).Append(" | ").Append(result.Requirement is { } link
            ? $"{Sanitize(link.Id, 40)} ({SourceLabel(link.Source, labels)})"
            : labels.NoneLinked).Append(" |\n");
        if (publishes)
        {
            body.Append("| ").Append(labels.Findings).Append(" | ").Append(KindCounts(result.Findings, labels)).Append(" |\n");
            body.Append("| ").Append(labels.Dropped).Append(" | ").Append(Dropped(result)).Append(" |\n");
            body.Append("| ").Append(labels.Lenses).Append(" | ")
                .Append(result.Lenses.Count == 0 ? "—" : string.Join(", ", result.Lenses.Select(lens => Sanitize(lens, 40)))).Append(" |\n");
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
            [.. published.Select(f => new RecordedFinding(f.Kind, f.Path, f.Line, f.Fingerprint, result.HeadSha) { Severity = f.Severity })],
            result.DroppedCount,
            result.Usage);

        // The default encoder escapes < and >, so the JSON can never close the HTML comment early.
        var json = JsonSerializer.Serialize(record, RecordJson);
        return body.Append(RecordStart).Append('\n').Append(json).Append("\n-->\n");
    }

    private static string KindCounts(IReadOnlyList<Finding> findings, Labels labels)
    {
        var mismatches = findings.Count(f => f.Kind == FindingKind.RequirementMismatch);
        var code = findings.Count(f => f.Kind == FindingKind.Bug);
        return Format(labels.KindCounts, Count(mismatches), mismatches == 1 ? labels.MismatchOne : labels.MismatchMany, Count(code), code == 1 ? labels.CodeOne : labels.CodeMany);
    }

    private static string Dropped(ReviewResult result) => result.DroppedCount == 0
        ? "0"
        : $"{Count(result.DroppedCount)} ({string.Join(", ", result.Dropped.Select(pair => $"{Sanitize(pair.Key, 40)} {Count(pair.Value)}"))})";

    private static string StateLabel(CoverageState state, Labels labels) => state switch
    {
        CoverageState.Complete => labels.StateComplete,
        CoverageState.Incomplete => labels.StateIncomplete,
        CoverageState.SkippedByPolicy => labels.StateSkipped,
        CoverageState.Superseded => labels.StateSuperseded,
        _ => labels.StateFailed,
    };

    private static string SourceLabel(Requirements.RequirementLinkSource source, Labels labels) => source switch
    {
        Requirements.RequirementLinkSource.Title => labels.FromTitle,
        Requirements.RequirementLinkSource.Branch => labels.FromBranch,
        _ => labels.FromDescription,
    };

    private static int LongestBacktickRun(string code)
    {
        var longest = 0;
        var run = 0;
        foreach (var character in code)
        {
            run = character == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return longest;
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Format(string template, params object[] values) => string.Format(CultureInfo.InvariantCulture, template, values);

    private static string Short(string sha) => sha.Length > 12 ? sha[..12] : sha;

    private static string Sha(string sha) => Code(sha, 64);

    // Markdown shows a code span's content literally, so HTML and mentions are inert there and are not escaped.
    private static string Code(string text, int maxLength) => "`" + Cap(text, maxLength).Replace("`", "'", StringComparison.Ordinal) + "`";

    private static Labels LabelsFor(string language) => language == ReviewLanguage.PortugueseBrazil ? Labels.PortugueseBrazil : Labels.English;

    [GeneratedRegex(@"^<!-- integricode:finding:(?<fingerprint>[0-9a-f]{64}) -->")]
    private static partial Regex FindingMarkerPattern();

    [GeneratedRegex("`[^`]+`|[^`]+|`")]
    private static partial Regex CodeSpanOrProse();

    /// <summary>The fixed words of the templates in one language; <c>{n}</c> placeholders take escaped values.</summary>
    private sealed record Labels
    {
        public required string Title { get; init; }
        public required string VerdictBlocking { get; init; }
        public required string VerdictRequirementUnmet { get; init; }
        public required string VerdictInjection { get; init; }
        public required string VerdictIncomplete { get; init; }
        public required string VerdictUnjudged { get; init; }
        public required string VerdictYellow { get; init; }
        public required string VerdictGreen { get; init; }
        public required string VerdictClear { get; init; }
        public required string Solves { get; init; }
        public required string Yes { get; init; }
        public required string Partly { get; init; }
        public required string No { get; init; }
        public required string CoveredCount { get; init; }
        public required string RequirementLens { get; init; }
        public required string OutsideDiff { get; init; }
        public required string Injection { get; init; }
        public required string InjectionNote { get; init; }
        public required string InRequirement { get; init; }
        public required string Missing { get; init; }
        public required string Reason { get; init; }
        public required string HowToLink { get; init; }
        public required string State { get; init; }
        public required string Requirement { get; init; }
        public required string NoneLinked { get; init; }
        public required string FromTitle { get; init; }
        public required string FromBranch { get; init; }
        public required string FromDescription { get; init; }
        public required string Findings { get; init; }
        public required string KindCounts { get; init; }
        public required string MismatchOne { get; init; }
        public required string MismatchMany { get; init; }
        public required string CodeOne { get; init; }
        public required string CodeMany { get; init; }
        public required string Dropped { get; init; }
        public required string Lenses { get; init; }
        public required string StateComplete { get; init; }
        public required string StateIncomplete { get; init; }
        public required string StateSkipped { get; init; }
        public required string StateSuperseded { get; init; }
        public required string StateFailed { get; init; }
        public required string SupersededText { get; init; }
        public required string ReviewBodyOne { get; init; }
        public required string ReviewBodyMany { get; init; }
        public required string Footer { get; init; }

        public static Labels English { get; } = new()
        {
            Title = "Integricode review",
            VerdictBlocking = "blocking issues to fix before merge.",
            VerdictRequirementUnmet = "no blockers found, but the change does not fully solve the requirement.",
            VerdictInjection = "no blockers found, but the input has text that may be a prompt injection; check it by hand.",
            VerdictIncomplete = "no blockers found, but the coverage is incomplete.",
            VerdictUnjudged = "no blockers found, but the review gave no verdict on the requirement.",
            VerdictYellow = "no blockers — good to merge after a look at the yellow ones.",
            VerdictGreen = "no blockers — good to merge.",
            VerdictClear = "all clear — good to merge.",
            Solves = "Solves {0}?",
            Yes = "Yes",
            Partly = "Partly",
            No = "No",
            CoveredCount = "{0} of {1} checked criteria covered",
            RequirementLens = "requirement",
            OutsideDiff = "Findings outside the diff",
            Injection = "Possible prompt injection",
            InjectionNote = "This text reads like instructions to an AI reviewer. The review treated it as data; check it and the findings by hand.",
            InRequirement = "requirement",
            Missing = "What was missing",
            Reason = "Reason",
            HowToLink = "Add a REQ ID (`REQ-NNN`) or a Jira key to the pull request title, branch name, or description, then comment `@integricode review`.",
            State = "State",
            Requirement = "Requirement",
            NoneLinked = "none linked",
            FromTitle = "from the title",
            FromBranch = "from the branch name",
            FromDescription = "from the description",
            Findings = "Findings",
            KindCounts = "{0} {1}, {2} {3}",
            MismatchOne = "requirement mismatch",
            MismatchMany = "requirement mismatches",
            CodeOne = "code finding",
            CodeMany = "code findings",
            Dropped = "Dropped by the provenance check",
            Lenses = "Lenses",
            StateComplete = "Reviewed, coverage complete",
            StateIncomplete = "Reviewed, coverage incomplete",
            StateSkipped = "Skipped by policy",
            StateSuperseded = "Superseded",
            StateFailed = "Failed",
            SupersededText = "The pull request moved to {0} before this review of {1} was published, so no findings were published. Comment `@integricode review` to review the current head.",
            ReviewBodyOne = "{0} new finding at {1}; the summary comment has the requirement verdict and the coverage.",
            ReviewBodyMany = "{0} new findings at {1}; the summary comment has the requirement verdict and the coverage.",
            Footer = "Integricode at {0}",
        };

        public static Labels PortugueseBrazil { get; } = new()
        {
            Title = "Revisão do Integricode",
            VerdictBlocking = "há bloqueios para corrigir antes do merge.",
            VerdictRequirementUnmet = "nenhum bloqueio encontrado, mas a mudança não resolve o requisito por completo.",
            VerdictInjection = "nenhum bloqueio encontrado, mas a entrada tem texto que pode ser prompt injection; confira à mão.",
            VerdictIncomplete = "nenhum bloqueio encontrado, mas a cobertura está incompleta.",
            VerdictUnjudged = "nenhum bloqueio encontrado, mas a revisão não deu veredito sobre o requisito.",
            VerdictYellow = "sem bloqueios — pode fazer o merge depois de olhar os amarelos.",
            VerdictGreen = "sem bloqueios — pode fazer o merge.",
            VerdictClear = "tudo certo — pode fazer o merge.",
            Solves = "Resolve o {0}?",
            Yes = "Sim",
            Partly = "Parcialmente",
            No = "Não",
            CoveredCount = "{0} de {1} critérios avaliados cobertos",
            RequirementLens = "requisito",
            OutsideDiff = "Achados fora do diff",
            Injection = "Possível prompt injection",
            InjectionNote = "Este texto parece instrução para um revisor de IA. A revisão o tratou como dado; confira-o e os achados à mão.",
            InRequirement = "requisito",
            Missing = "O que faltou",
            Reason = "Motivo",
            HowToLink = "Coloque um REQ (`REQ-NNN`) ou uma chave do Jira no título, na branch ou na descrição do pull request e comente `@integricode review`.",
            State = "Estado",
            Requirement = "Requisito",
            NoneLinked = "nenhum vinculado",
            FromTitle = "do título",
            FromBranch = "da branch",
            FromDescription = "da descrição",
            Findings = "Achados",
            KindCounts = "{0} {1}, {2} {3}",
            MismatchOne = "desvio de requisito",
            MismatchMany = "desvios de requisito",
            CodeOne = "achado de código",
            CodeMany = "achados de código",
            Dropped = "Descartados pela checagem de proveniência",
            Lenses = "Lentes",
            StateComplete = "Revisado, cobertura completa",
            StateIncomplete = "Revisado, cobertura incompleta",
            StateSkipped = "Ignorado pela política",
            StateSuperseded = "Substituído",
            StateFailed = "Falhou",
            SupersededText = "O pull request foi para {0} antes de esta revisão de {1} ser publicada, então nenhum achado foi publicado. Comente `@integricode review` para revisar o head atual.",
            ReviewBodyOne = "{0} achado novo em {1}; o comentário de resumo tem o veredito do requisito e a cobertura.",
            ReviewBodyMany = "{0} achados novos em {1}; o comentário de resumo tem o veredito do requisito e a cobertura.",
            Footer = "Integricode em {0}",
        };
    }
}
