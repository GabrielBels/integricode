using System.Net;
using Integricode.Commands;
using Integricode.Domain;
using Integricode.GitHub;
using Integricode.Requirements;
using Integricode.Requirements.Jira;
using Integricode.Tests.Fakes;

namespace Integricode.Tests.Requirements.Jira;

/// <summary>Runs <c>review</c> on a Jira-linked pull request: a Jira failure is incomplete coverage, never a lost review.</summary>
public sealed class JiraReviewCoverageTests : IDisposable
{
    private const string Findings = """
        {"findings": [
          {"kind": "requirement-mismatch", "title": "Discounts ignored", "path": "src/Cart.cs", "line": 4,
           "failureScenario": "A 10% discount is not applied.", "requirementQuote": "The total applies every discount in the cart."},
          {"kind": "bug", "title": "Null prices", "path": "src/Cart.cs", "line": 4, "failureScenario": "Total(null) throws."}
        ]}
        """;

    private readonly TempRepository workspace = new();
    private readonly FakeGitHub gitHub = new() { Title = "SHOP-12: cart totals" };
    private readonly FakeJira jira = new();
    private readonly StringWriter log = new();

    public JiraReviewCoverageTests()
    {
        workspace.Write("src/Cart.cs", "namespace Shop;\npublic class Cart\n{\n    public decimal Total(decimal[] prices) => prices.Sum();\n}\n");
        gitHub.Files.Add(new PullRequestFile("src/Cart.cs", "modified", "@@ -1,4 +1,5 @@\n namespace Shop;\n public class Cart\n {\n+    public decimal Total(decimal[] prices) => prices.Sum();\n }"));
        jira.CustomFields["customfield_10042"] = Adf.Doc(Adf.BulletList("The total applies every discount in the cart."));
    }

    private async Task<ReviewResult> ReviewAsync(string? email = FakeJira.Email, string? apiToken = FakeJira.ApiToken, TimeSpan? timeout = null)
    {
        var resultPath = workspace.FullPath("out/result.json");
        var options = ReviewOptions.Parse(
        [
            "--repo", FakeGitHub.Repository, "--pr", "7", "--workspace", workspace.Root, "--out", resultPath,
            "--model-provider", "openai", "--model", "gpt-test",
            "--jira-base-url", FakeJira.BaseUrl, "--jira-acceptance-criteria-field", "customfield_10042",
        ]);
        var command = new ReviewCommand(
            gitHub.Client(),
            _ => new ScriptedChatClient().ThenText(Findings),
            new ActionsLog(log),
            o => JiraRequirementSource.Create(jira.HttpClient(timeout), o.JiraBaseUrl, o.JiraAcceptanceCriteriaField, email, apiToken));
        Assert.Equal(0, await command.RunAsync(options, CancellationToken.None));
        return await ReviewResult.ReadAsync(resultPath, CancellationToken.None);
    }

    [Fact]
    public async Task A_readable_issue_gives_complete_coverage_and_its_quotes_validate()
    {
        var result = await ReviewAsync();

        Assert.Equal(CoverageState.Complete, result.Coverage.State);
        Assert.Equal(new RequirementLink(RequirementKind.JiraIssue, "SHOP-12", RequirementLinkSource.Title), result.Requirement);
        Assert.Equal([FindingKind.RequirementMismatch, FindingKind.Bug], result.Findings.Select(f => f.Kind));
        Assert.All(jira.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Jira issue SHOP-12 unreadable: authentication failed (HTTP 401), check JIRA_EMAIL and JIRA_API_TOKEN")]
    [InlineData(HttpStatusCode.Forbidden, "Jira issue SHOP-12 unreadable: access denied (HTTP 403) for the Jira account")]
    [InlineData(HttpStatusCode.NotFound, "Jira issue SHOP-12 unreadable: not found or not visible to the Jira account (HTTP 404)")]
    public async Task A_jira_error_keeps_bug_findings_and_reports_incomplete_coverage(HttpStatusCode status, string reason)
    {
        jira.IssueStatus = status;

        var result = await ReviewAsync();

        AssertIncompleteWithBugs(result, reason);
    }

    [Fact]
    public async Task Wrong_credentials_are_an_authentication_failure()
    {
        var result = await ReviewAsync(apiToken: "synthetic-wrong-token");

        AssertIncompleteWithBugs(result, "Jira issue SHOP-12 unreadable: authentication failed (HTTP 401), check JIRA_EMAIL and JIRA_API_TOKEN");
        Assert.DoesNotContain("synthetic-wrong-token", log.ToString());
    }

    [Fact]
    public async Task A_jira_timeout_keeps_bug_findings_and_reports_incomplete_coverage()
    {
        jira.Hang = true;

        var result = await ReviewAsync(timeout: TimeSpan.FromMilliseconds(20));

        AssertIncompleteWithBugs(result, "Jira issue SHOP-12 unreadable: the request timed out");
    }

    [Theory]
    [InlineData(null, FakeJira.ApiToken, "Jira source not configured for SHOP-12: missing JIRA_EMAIL")]
    [InlineData(FakeJira.Email, null, "Jira source not configured for SHOP-12: missing JIRA_API_TOKEN")]
    public async Task A_missing_jira_input_keeps_bug_findings_and_reports_incomplete_coverage(string? email, string? apiToken, string reason)
    {
        var result = await ReviewAsync(email, apiToken);

        AssertIncompleteWithBugs(result, reason);
        Assert.Empty(jira.Requests);
    }

    [Fact]
    public async Task An_issue_without_acceptance_criteria_is_reviewed_with_incomplete_coverage()
    {
        jira.CustomFields.Remove("customfield_10042");
        jira.Description = Adf.Doc(Adf.Paragraph("The total applies every discount in the cart."));

        var result = await ReviewAsync();

        Assert.Equal(CoverageState.Incomplete, result.Coverage.State);
        Assert.Equal(["Jira issue SHOP-12 has no acceptance criteria in customfield_10042"], result.Coverage.Reasons);
        Assert.Equal([FindingKind.RequirementMismatch, FindingKind.Bug], result.Findings.Select(f => f.Kind));
    }

    private void AssertIncompleteWithBugs(ReviewResult result, string reason)
    {
        Assert.Equal(CoverageState.Incomplete, result.Coverage.State);
        Assert.Equal([reason], result.Coverage.Reasons);
        Assert.Equal(FindingKind.Bug, Assert.Single(result.Findings).Kind);
        Assert.Equal(1, result.Dropped["quote-not-found"]);
        Assert.Contains($"::warning::Coverage incomplete: {reason}.", log.ToString());
        Assert.DoesNotContain(FakeJira.ApiToken, log.ToString());
    }

    public void Dispose()
    {
        workspace.Dispose();
        gitHub.Dispose();
        jira.Dispose();
    }
}
