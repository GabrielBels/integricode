using Integricode.Domain;
using Integricode.GitHub;
using Integricode.Validation;

namespace Integricode.Tests.Validation;

public class FindingValidatorTests
{
    private const string Requirement = "## [REQ-001] Cart Totals\n\n**Acceptance Criteria**:\n- [ ] The total applies\n  every discount.\n- [ ] An empty cart totals zero.";

    // src/Cart.cs has 10 lines at the head SHA; lines 4 and 5 are changed, line 3 is a context line.
    private static readonly DiffMap Diff = DiffMap.Build(
    [
        new PullRequestFile("src/Cart.cs", "modified", "@@ -3,2 +3,3 @@\n context\n+added\n+added2\n"),
        new PullRequestFile("src/Removed.cs", "removed", "@@ -1 +0,0 @@\n-gone"),
        new PullRequestFile("README.md", "modified", "@@ -1,3 +1,3 @@\n+```bash\n+npm install\n+```"),
    ]);

    private static FindingValidator Validator(string? requirement = Requirement) =>
        new(Diff, requirement, path => path is "src/Cart.cs" or "README.md" ? 10 : null);

    private static ProposedFinding Bug(string? path = "src/Cart.cs", int? line = 4, string? scenario = "Total(null) throws.", string? title = "Null prices") =>
        new("bug", title, path, line, scenario, null, "blocking");

    private static ProposedFinding Mismatch(string? quote = "The total applies every discount.", int? line = 3) =>
        new("requirement-mismatch", "Discounts ignored", "src/Cart.cs", line, "A 10% discount is not applied.", quote, "blocking");

    [Fact]
    public void A_valid_bug_and_a_valid_mismatch_are_kept()
    {
        var outcome = Validator().Validate([Bug(), Mismatch()]);

        Assert.Equal(
            [
                new Finding(FindingKind.Bug, "Null prices", "src/Cart.cs", 4, "Total(null) throws."),
                new Finding(FindingKind.RequirementMismatch, "Discounts ignored", "src/Cart.cs", 3, "A 10% discount is not applied.", "The total applies every discount."),
            ],
            outcome.Findings);
        Assert.Empty(outcome.Dropped);
    }

    [Fact]
    public void A_mismatch_may_point_at_an_unchanged_line_of_a_pull_request_file()
    {
        var outcome = Validator().Validate([Mismatch(line: 9)]);

        Assert.Single(outcome.Findings);
    }

    [Theory]
    [InlineData("The total applies every discounts.")]
    [InlineData("the total applies every discount.")]
    [InlineData("Totals apply discounts.")]
    [InlineData("the total")]
    [InlineData("  The  ")]
    public void A_quote_not_in_the_requirement_or_too_short_to_place_is_dropped(string quote)
    {
        AssertDropped(Validator().Validate([Mismatch(quote)]), DropReason.QuoteNotFound);
    }

    [Fact]
    public void A_mismatch_without_a_requirement_text_is_dropped_as_quote_not_found()
    {
        AssertDropped(Validator(requirement: null).Validate([Mismatch()]), DropReason.QuoteNotFound);
    }

    [Theory]
    [InlineData("src/Other.cs")]
    [InlineData("docs/requirements.md")]
    public void A_path_outside_the_pull_request_is_dropped(string path)
    {
        AssertDropped(Validator().Validate([Bug(path: path)]), DropReason.PathNotInPullRequest);
    }

    [Theory]
    [InlineData("src/Cart.cs", 11)]
    [InlineData("src/Removed.cs", 1)]
    public void A_line_that_does_not_exist_at_the_head_sha_is_dropped(string path, int line)
    {
        AssertDropped(Validator().Validate([new ProposedFinding("requirement-mismatch", "t", path, line, "s", "An empty cart totals zero.", "blocking")]), DropReason.LineNotFound);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(9)]
    public void A_bug_not_on_a_changed_line_is_dropped(int line)
    {
        AssertDropped(Validator().Validate([Bug(line: line)]), DropReason.BugNotOnChangedLine);
    }

    public static TheoryData<ProposedFinding> MissingFields() =>
    [
        new ProposedFinding(null, "t", "src/Cart.cs", 4, "s", null, "blocking"),
        new ProposedFinding("nitpick", "t", "src/Cart.cs", 4, "s", null, "blocking"),
        new ProposedFinding("bug", " ", "src/Cart.cs", 4, "s", null, "blocking"),
        new ProposedFinding("bug", "t", null, 4, "s", null, "blocking"),
        new ProposedFinding("bug", "t", "src/Cart.cs", null, "s", null, "blocking"),
        new ProposedFinding("bug", "t", "src/Cart.cs", 0, "s", null, "blocking"),
        new ProposedFinding("bug", "t", "src/Cart.cs", 4, "", null, "blocking"),
        new ProposedFinding("requirement-mismatch", "t", "src/Cart.cs", 4, "s", null, "blocking"),
        new ProposedFinding("bug", "t", "src/Cart.cs", 4, "s", null),
        new ProposedFinding("bug", "t", "src/Cart.cs", 4, "s", null, "critical"),
    ];

    [Theory]
    [MemberData(nameof(MissingFields))]
    public void A_finding_missing_a_required_field_is_dropped(ProposedFinding finding)
    {
        AssertDropped(Validator().Validate([finding]), DropReason.MissingField);
    }

    [Fact]
    public void Duplicate_fingerprints_collapse_into_the_first()
    {
        var outcome = Validator().Validate([Bug(), Bug(scenario: "Total(null)   throws."), Bug(title: " Null prices ")]);

        Assert.Single(outcome.Findings);
        Assert.Empty(outcome.Dropped);
    }

    [Fact]
    public void Dropped_counts_are_kept_by_reason()
    {
        var outcome = Validator().Validate([Bug(), Bug(line: 3), Bug(line: 9), Mismatch("invented"), Bug(path: "x.cs")]);

        Assert.Single(outcome.Findings);
        Assert.Equal(
            new Dictionary<string, int> { [DropReason.BugNotOnChangedLine] = 2, [DropReason.PathNotInPullRequest] = 1, [DropReason.QuoteNotFound] = 1 },
            outcome.Dropped);
    }

    [Fact]
    public void Paths_and_kinds_are_normalized()
    {
        var outcome = Validator().Validate([new ProposedFinding("Bug", "t", "./src\\Cart.cs", 4, "s", null, "blocking"), new ProposedFinding("RequirementMismatch", "t", "/src/Cart.cs", 5, "s", "An empty cart totals zero.", "blocking")]);

        Assert.Equal(["src/Cart.cs", "src/Cart.cs"], outcome.Findings.Select(f => f.Path));
        Assert.Equal([FindingKind.Bug, FindingKind.RequirementMismatch], outcome.Findings.Select(f => f.Kind));
    }

    [Theory]
    [InlineData("blocking", "bug", FindingSeverity.Blocking, "bug")]
    [InlineData("issue (blocking)", "security (OWASP A03)", FindingSeverity.Blocking, "security")]
    [InlineData("suggestion", "performance", FindingSeverity.Suggestion, "performance")]
    [InlineData("nit", "Tests", FindingSeverity.Nitpick, "tests")]
    [InlineData("note", "style", FindingSeverity.Note, "bug")]
    [InlineData("Note", null, FindingSeverity.Note, "bug")]
    public void A_finding_keeps_its_light_and_lens_and_an_unknown_lens_becomes_bug(string severity, string? lens, FindingSeverity expectedSeverity, string expectedLens)
    {
        var finding = Assert.Single(Validator().Validate([Bug() with { Severity = severity, Lens = lens, Fix = "  Guard against null.  " }]).Findings);

        Assert.Equal(expectedSeverity, finding.Severity);
        Assert.Equal(expectedLens, finding.Lens);
        Assert.Equal("Guard against null.", finding.Fix);
    }

    [Theory]
    [InlineData(4, "        if (prices is null) return 0;")]
    [InlineData(3, "context\r\n        if (prices is null) return 0;")]
    [InlineData(null, "```csharp\n        if (prices is null) return 0;\n```")]
    public void A_suggestion_over_diff_lines_is_kept_without_a_fence(int? startLine, string code)
    {
        var finding = Assert.Single(Validator().Validate([Bug() with { SuggestionStartLine = startLine, Suggestion = code }]).Findings);

        Assert.Equal(startLine ?? 4, finding.Suggestion!.StartLine);
        Assert.EndsWith("        if (prices is null) return 0;", finding.Suggestion.Code, StringComparison.Ordinal);
        Assert.DoesNotContain("```", finding.Suggestion.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void A_suggestion_for_markdown_keeps_the_fences_it_replaces()
    {
        var finding = Assert.Single(Validator().Validate([Bug(path: "README.md", line: 3) with { SuggestionStartLine = 1, Suggestion = "```bash\nnpm ci\n```" }]).Findings);

        Assert.Equal("```bash\nnpm ci\n```", finding.Suggestion!.Code);
    }

    public static TheoryData<int?, string> UnpublishableSuggestions() => new()
    {
        { 2, "a\nb\nc" },
        { 5, "a" },
        { 0, "a" },
        { 4, "   \n  " },
        { 4, new string('x', FindingValidator.MaxSuggestionLength + 1) },
        { 4, string.Join('\n', Enumerable.Repeat("x", FindingValidator.MaxSuggestionLines + 1)) },
    };

    [Theory]
    [MemberData(nameof(UnpublishableSuggestions))]
    public void A_suggestion_off_the_diff_empty_or_too_large_is_left_out_and_the_finding_kept(int? startLine, string code)
    {
        var finding = Assert.Single(Validator().Validate([Bug() with { SuggestionStartLine = startLine, Suggestion = code }]).Findings);

        Assert.Null(finding.Suggestion);
    }

    private static void AssertDropped(ValidationOutcome outcome, string reason)
    {
        Assert.Empty(outcome.Findings);
        Assert.Equal(new Dictionary<string, int> { [reason] = 1 }, outcome.Dropped);
    }
}
