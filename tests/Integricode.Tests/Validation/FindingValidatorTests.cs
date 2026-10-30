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
    ]);

    private static FindingValidator Validator(string? requirement = Requirement) =>
        new(Diff, requirement, path => path == "src/Cart.cs" ? 10 : null);

    private static ProposedFinding Bug(string? path = "src/Cart.cs", int? line = 4, string? scenario = "Total(null) throws.", string? title = "Null prices") =>
        new("bug", title, path, line, scenario, null);

    private static ProposedFinding Mismatch(string? quote = "The total applies every discount.", int? line = 3) =>
        new("requirement-mismatch", "Discounts ignored", "src/Cart.cs", line, "A 10% discount is not applied.", quote);

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
    public void A_quote_not_in_the_requirement_is_dropped(string quote)
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
        AssertDropped(Validator().Validate([new ProposedFinding("requirement-mismatch", "t", path, line, "s", "An empty cart totals zero.")]), DropReason.LineNotFound);
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
        new ProposedFinding(null, "t", "src/Cart.cs", 4, "s", null),
        new ProposedFinding("nitpick", "t", "src/Cart.cs", 4, "s", null),
        new ProposedFinding("bug", " ", "src/Cart.cs", 4, "s", null),
        new ProposedFinding("bug", "t", null, 4, "s", null),
        new ProposedFinding("bug", "t", "src/Cart.cs", null, "s", null),
        new ProposedFinding("bug", "t", "src/Cart.cs", 0, "s", null),
        new ProposedFinding("bug", "t", "src/Cart.cs", 4, "", null),
        new ProposedFinding("requirement-mismatch", "t", "src/Cart.cs", 4, "s", null),
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
        var outcome = Validator().Validate([new ProposedFinding("Bug", "t", "./src\\Cart.cs", 4, "s", null), new ProposedFinding("RequirementMismatch", "t", "/src/Cart.cs", 5, "s", "An empty cart totals zero.")]);

        Assert.Equal(["src/Cart.cs", "src/Cart.cs"], outcome.Findings.Select(f => f.Path));
        Assert.Equal([FindingKind.Bug, FindingKind.RequirementMismatch], outcome.Findings.Select(f => f.Kind));
    }

    private static void AssertDropped(ValidationOutcome outcome, string reason)
    {
        Assert.Empty(outcome.Findings);
        Assert.Equal(new Dictionary<string, int> { [reason] = 1 }, outcome.Dropped);
    }
}
