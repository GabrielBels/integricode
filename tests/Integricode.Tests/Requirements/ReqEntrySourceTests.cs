using Integricode.Agent;
using Integricode.Requirements;
using Integricode.Tests.Fakes;

namespace Integricode.Tests.Requirements;

public sealed class ReqEntrySourceTests : IDisposable
{
    private const string Requirements = """
        # Requirements

        ## [REQ-001] Cart Totals

        **Description**: The cart shows a total.

        **Acceptance Criteria**:
        - [ ] The total applies every discount.
        - [ ] An empty cart totals zero.

        ### Notes

        Synthetic test data.

        ---

        ## [REQ-002] Checkout

        **Acceptance Criteria**:
        - [ ] Checkout requires a signed-in customer.
        """;

    private readonly TempRepository repository = new();

    private static RequirementLink Link(string id) => new(RequirementKind.ReqEntry, id, RequirementLinkSource.Title);

    private ReqEntrySource Source(string file = "docs/requirements.md") => new(new PathGuard(repository.Root), file);

    [Fact]
    public async Task Reads_the_section_with_its_acceptance_criteria_up_to_the_next_entry()
    {
        repository.Write("docs/requirements.md", Requirements);

        var result = await Source().ReadAsync(Link("REQ-001"), CancellationToken.None);

        Assert.True(result.IsRead);
        Assert.StartsWith("## [REQ-001] Cart Totals", result.Text);
        Assert.Contains("- [ ] The total applies every discount.", result.Text);
        Assert.Contains("### Notes", result.Text);
        Assert.DoesNotContain("REQ-002", result.Text);
    }

    [Fact]
    public async Task The_last_section_runs_to_the_end_of_the_file()
    {
        repository.Write("docs/requirements.md", Requirements);

        var result = await Source().ReadAsync(Link("REQ-002"), CancellationToken.None);

        Assert.EndsWith("- [ ] Checkout requires a signed-in customer.", result.Text);
    }

    [Fact]
    public async Task Reads_the_configured_file_in_the_checkout()
    {
        repository.Write("specs/reqs.md", Requirements);

        var result = await Source("specs/reqs.md").ReadAsync(Link("REQ-002"), CancellationToken.None);

        Assert.True(result.IsRead);
    }

    [Fact]
    public async Task A_missing_file_is_unreadable_with_its_reason()
    {
        var result = await Source().ReadAsync(Link("REQ-001"), CancellationToken.None);

        Assert.False(result.IsRead);
        Assert.Equal("requirements file docs/requirements.md not found at the head SHA", result.UnreadableReason);
    }

    [Fact]
    public async Task A_missing_section_is_unreadable_with_its_reason()
    {
        repository.Write("docs/requirements.md", Requirements);

        var result = await Source().ReadAsync(Link("REQ-009"), CancellationToken.None);

        Assert.False(result.IsRead);
        Assert.Equal("REQ-009 not found in docs/requirements.md", result.UnreadableReason);
    }

    [Fact]
    public async Task A_requirements_file_outside_the_checkout_is_refused()
    {
        var result = await Source("../outside.md").ReadAsync(Link("REQ-001"), CancellationToken.None);

        Assert.False(result.IsRead);
        Assert.Contains("refused", result.UnreadableReason);
    }

    [Fact]
    public async Task A_jira_key_without_a_jira_source_is_unreadable()
    {
        var sources = new RequirementSources([Source()]);

        var result = await sources.ReadAsync(new RequirementLink(RequirementKind.JiraIssue, "SHOP-9", RequirementLinkSource.Title), CancellationToken.None);

        Assert.Equal("Jira source not configured for SHOP-9", result.UnreadableReason);
    }

    [Fact]
    public async Task Sources_dispatch_a_req_id_to_the_req_entry_source()
    {
        repository.Write("docs/requirements.md", Requirements);
        var sources = new RequirementSources([Source()]);

        var result = await sources.ReadAsync(Link("REQ-001"), CancellationToken.None);

        Assert.True(result.IsRead);
    }

    public void Dispose() => repository.Dispose();
}
