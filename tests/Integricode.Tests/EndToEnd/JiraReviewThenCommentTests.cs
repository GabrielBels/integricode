using System.Text.Json.Nodes;
using Integricode.Commands;
using Integricode.Domain;
using Integricode.GitHub;
using Integricode.Requirements;
using Integricode.Requirements.Jira;
using Integricode.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace Integricode.Tests.EndToEnd;

/// <summary>
/// Runs <c>review</c> then <c>comment</c> on the fixture repository for a pull request linked to a synthetic Jira issue,
/// with a fake model, a fake GitHub, and a fake Jira: requirement quotes taken from the Jira text pass the provenance
/// check, and an altered quote is dropped.
/// </summary>
public sealed class JiraReviewThenCommentTests : IDisposable
{
    private const string Field = "customfield_10042";

    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "shop");

    private readonly TempRepository workspace = new();
    private readonly TempRepository output = new();
    private readonly FakeGitHub gitHub = new() { Title = "SHOP-12: cart totals", Body = "Totals for the cart." };
    private readonly FakeJira jira = new();
    private readonly StringWriter log = new();

    public JiraReviewThenCommentTests()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(FixtureRoot, "repo", "src")))
        {
            workspace.Write(Path.Combine("src", Path.GetFileName(file)), File.ReadAllText(file));
        }

        gitHub.Files.Add(new PullRequestFile("src/Cart.cs", "modified", File.ReadAllText(Path.Combine(FixtureRoot, "cart.patch"))));
        jira.Description = Adf.Doc(
            Adf.Node("heading", Adf.Text("Cart totals")),
            Adf.Paragraph("The cart shows a total for the items in it."));
        jira.CustomFields[Field] = Adf.Doc(Adf.BulletList("The total applies every discount in the cart.", "An empty cart averages zero."));
        jira.AddComment(Adf.Paragraph("Discounts are percentages, not flat amounts."));
        jira.AddComment(Adf.Paragraph("Averages round to cents."));
        jira.AddComment(Adf.Paragraph("Free items still count as items."));
    }

    private string ResultPath => output.FullPath("result.json");

    private static JsonObject Finding(string kind, string title, int line, string scenario, string? quote = null) => new()
    {
        ["kind"] = kind,
        ["title"] = title,
        ["path"] = "src/Cart.cs",
        ["line"] = line,
        ["failureScenario"] = scenario,
        ["requirementQuote"] = quote,
    };

    private static ScriptedChatClient Model() => new ScriptedChatClient().ThenText(new JsonObject
    {
        ["findings"] = new JsonArray(
            Finding("requirement-mismatch", "Total subtracts a flat amount", 10, "A 10% discount on a 50.00 cart subtracts 10.00 instead of 5.00.", "Discounts are percentages,\n  not flat amounts."),
            Finding("requirement-mismatch", "Average of an empty cart throws", 12, "Average() on an empty cart throws DivideByZeroException instead of returning zero.", "An empty cart averages zero."),
            Finding("requirement-mismatch", "Coupons are ignored", 10, "A coupon is not applied.", "The total applies every coupon in the cart."),
            Finding("bug", "Average divides by zero", 12, "Average() on an empty cart throws DivideByZeroException.")),
    }.ToJsonString());

    private async Task<ReviewResult> ReviewAsync(IChatClient model)
    {
        var options = ReviewOptions.Parse(
        [
            "--repo", FakeGitHub.Repository, "--pr", "7", "--workspace", workspace.Root, "--out", ResultPath,
            "--model-provider", "anthropic", "--model", "claude-test",
            "--jira-base-url", FakeJira.BaseUrl, "--jira-acceptance-criteria-field", Field,
        ]);
        var command = new ReviewCommand(
            gitHub.Client(),
            _ => model,
            new ActionsLog(log),
            o => JiraRequirementSource.Create(jira.HttpClient(), o.JiraBaseUrl, o.JiraAcceptanceCriteriaField, FakeJira.Email, FakeJira.ApiToken));
        Assert.Equal(0, await command.RunAsync(options, CancellationToken.None));
        return await ReviewResult.ReadAsync(ResultPath, CancellationToken.None);
    }

    [Fact]
    public async Task Quotes_from_the_jira_issue_pass_the_provenance_check_and_an_altered_quote_is_dropped()
    {
        var model = Model();

        var result = await ReviewAsync(model);
        Assert.Equal(0, await new CommentCommand(gitHub.Client(), new ActionsLog(log))
            .RunAsync(new CommentOptions(FakeGitHub.Repository, 7, ResultPath), CancellationToken.None));

        Assert.Equal(CoverageState.Complete, result.Coverage.State);
        Assert.Equal(new RequirementLink(RequirementKind.JiraIssue, "SHOP-12", RequirementLinkSource.Title), result.Requirement);
        Assert.Equal(
            ["Discounts are percentages, not flat amounts.", "An empty cart averages zero.", null],
            result.Findings.Select(f => f.RequirementQuote));
        Assert.Equal(new Dictionary<string, int> { ["quote-not-found"] = 1 }, result.Dropped);

        // The model saw the issue's text as the requirement, without any author.
        var prompt = string.Join("\n", model.Calls[0].Messages.SelectMany(m => m.Contents).OfType<TextContent>().Select(t => t.Text));
        Assert.Contains("<requirement id=\"SHOP-12\">\n# SHOP-12: Cart totals apply discounts\n\n## Description\n\n# Cart totals\n\nThe cart shows a total for the items in it.", prompt);
        Assert.Contains("## Acceptance Criteria\n\n- The total applies every discount in the cart.\n- An empty cart averages zero.", prompt);
        Assert.Contains("### Comment 3\n\nFree items still count as items.\n</requirement>", prompt);
        Assert.DoesNotContain(FakeJira.AuthorName, prompt);
        Assert.DoesNotContain(FakeJira.AuthorEmail, prompt);

        // Jira was only read; the summary names the issue and the inline comments carry the validated quotes.
        Assert.Equal(3, jira.Requests.Count);
        Assert.All(jira.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        var summary = Assert.Single(gitHub.IssueComments).Body;
        Assert.Contains("SHOP-12", summary);
        Assert.Contains("| Findings | 2 requirement mismatches, 1 bug |", summary);
        var inline = Assert.Single(gitHub.Reviews).Comments.Select(c => c.Body).ToList();
        Assert.Equal(3, inline.Count);
        Assert.Contains(inline, body => body.Contains("Discounts are percentages, not flat amounts.", StringComparison.Ordinal));
        Assert.DoesNotContain(inline, body => body.Contains("every coupon", StringComparison.Ordinal));
        var writes = gitHub.Requests.Where(r => r.Method != HttpMethod.Get).Select(r => r.Body ?? string.Empty).ToList();
        Assert.All(writes, body => Assert.DoesNotContain(FakeJira.AuthorName, body));
        Assert.All(writes, body => Assert.DoesNotContain(FakeJira.ApiToken, body));
        Assert.DoesNotContain(FakeJira.ApiToken, log.ToString());
    }

    [Fact]
    public async Task The_same_issue_gives_the_same_context_hash()
    {
        var first = await ReviewAsync(Model());
        var second = await ReviewAsync(Model());

        Assert.Equal(first.ContextHash, second.ContextHash);
    }

    public void Dispose()
    {
        workspace.Dispose();
        output.Dispose();
        gitHub.Dispose();
        jira.Dispose();
    }
}
