using Integricode.Domain;
using Integricode.Validation;

namespace Integricode.Tests.Validation;

public class VerdictValidatorTests
{
    private const string Requirement = "## [REQ-001] Cart Totals\n\n**Acceptance Criteria**:\n- [ ] The total applies\n  every discount.\n- [ ] An empty cart totals zero.";

    private static readonly Finding BlockingMismatch = new(FindingKind.RequirementMismatch, "Discounts ignored", "src/Cart.cs", 4, "s", "The total applies every discount.");

    private static ProposedCriterion Row(string? quote = "An empty cart totals zero.", string? status = "covered", string? evidence = "src/Cart.cs:4") => new(quote, status, evidence);

    private static VerdictOutcome Validate(ProposedVerdict? verdict, string? requirement = Requirement, params Finding[] findings) =>
        new VerdictValidator(requirement).Validate(verdict, findings);

    [Fact]
    public void A_verdict_keeps_rows_whose_quotes_are_verbatim_requirement_text()
    {
        var outcome = Validate(new ProposedVerdict("Yes", "  Both criteria hold.  ", [Row(), Row("The total applies every discount.", "Covered", " src/Cart.cs:5 ")]));

        Assert.Equal(VerdictAnswer.Yes, outcome.Verdict!.Answer);
        Assert.Equal("Both criteria hold.", outcome.Verdict.Summary);
        Assert.Equal(
            [
                new CriterionCheck("An empty cart totals zero.", CriterionStatus.Covered, "src/Cart.cs:4"),
                new CriterionCheck("The total applies every discount.", CriterionStatus.Covered, "src/Cart.cs:5"),
            ],
            outcome.Verdict.Criteria);
        Assert.Empty(outcome.Dropped);
    }

    [Fact]
    public void Rows_with_invented_quotes_or_missing_fields_are_dropped_and_counted()
    {
        var outcome = Validate(new ProposedVerdict("partly", "One criterion fails.", [Row(), Row("Totals always apply discounts."), Row(status: "done"), Row(evidence: " "), Row(quote: "zero")]));

        Assert.Single(outcome.Verdict!.Criteria);
        Assert.Equal(
            new Dictionary<string, int> { [DropReason.CriterionMissingField] = 2, [DropReason.CriterionQuoteNotFound] = 2 },
            outcome.Dropped);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("partial")]
    [InlineData("unclear")]
    public void A_yes_that_a_row_contradicts_becomes_partly(string status)
    {
        var outcome = Validate(new ProposedVerdict("yes", "All good.", [Row(), Row("The total applies every discount.", status)]));

        Assert.Equal(VerdictAnswer.Partly, outcome.Verdict!.Answer);
    }

    [Fact]
    public void A_yes_that_a_blocking_requirement_mismatch_contradicts_becomes_partly()
    {
        var outcome = Validate(new ProposedVerdict("yes", "All good.", [Row()]), Requirement, BlockingMismatch);

        Assert.Equal(VerdictAnswer.Partly, outcome.Verdict!.Answer);
    }

    [Fact]
    public void A_no_stays_no_whatever_the_rows_say()
    {
        Assert.Equal(VerdictAnswer.No, Validate(new ProposedVerdict("no", "Nothing works.", [Row()])).Verdict!.Answer);
    }

    [Theory]
    [InlineData(null, "Why")]
    [InlineData("maybe", "Why")]
    [InlineData("yes", " ")]
    public void A_verdict_without_a_valid_answer_or_summary_is_dropped(string? answer, string summary)
    {
        var outcome = Validate(new ProposedVerdict(answer, summary, [Row()]));

        Assert.Null(outcome.Verdict);
        Assert.Equal(new Dictionary<string, int> { [DropReason.VerdictMissingField] = 1 }, outcome.Dropped);
    }

    [Fact]
    public void Without_requirement_text_or_a_proposed_verdict_there_is_no_verdict()
    {
        Assert.Null(Validate(new ProposedVerdict("yes", "All good.", [Row()]), requirement: null).Verdict);
        Assert.Null(Validate(null).Verdict);
    }
}
