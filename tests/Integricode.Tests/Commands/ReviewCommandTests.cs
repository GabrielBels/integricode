using System.Net;
using Integricode.Agent;
using Integricode.Commands;
using Integricode.Domain;
using Integricode.GitHub;
using Integricode.Requirements;
using Integricode.Requirements.Jira;
using Integricode.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace Integricode.Tests.Commands;

public sealed class ReviewCommandTests : IDisposable
{
    private const string RequirementMarker = "SYNTHETIC-REQUIREMENT-TEXT-7f3a";
    private const string CodeMarker = "SYNTHETIC-CODE-TEXT-9c1d";
    private const string SecretMarker = "sk-SYNTHETIC-SECRET-4b2e";
    private const string Findings = """
        {"findings": [
          {"severity": "blocking", "kind": "requirement-mismatch", "title": "Discounts ignored", "path": "src/Cart.cs", "line": 4,
           "failureScenario": "A 10% discount is not applied.", "requirementQuote": "The total applies every discount."},
          {"severity": "blocking", "kind": "bug", "title": "Null prices", "path": "src/Cart.cs", "line": 4, "failureScenario": "Total(null) throws."}
        ]}
        """;

    private readonly TempRepository workspace = new();
    private readonly TempRepository output = new();
    private readonly FakeGitHub gitHub = new();
    private readonly FakeJira jira = new();
    private readonly StringWriter log = new();

    public ReviewCommandTests()
    {
        workspace
            .Write("src/Cart.cs", $"namespace Shop;\npublic class Cart\n{{\n    public decimal Total(decimal[] prices) => prices.Sum(); // {CodeMarker}\n}}\n")
            .Write("docs/requirements.md", $"# Requirements\n\n## [REQ-001] Cart Totals\n\n{RequirementMarker}\n\n**Acceptance Criteria**:\n- [ ] The total applies every discount.\n");
        gitHub.Files.Add(new PullRequestFile("src/Cart.cs", "modified", $"@@ -1,4 +1,5 @@\n namespace Shop;\n public class Cart\n {{\n+    public decimal Total(decimal[] prices) => prices.Sum(); // {CodeMarker}\n }}"));
    }

    private string ResultPath => output.FullPath("out/result.json");

    private async Task<(int ExitCode, ReviewResult? Result)> RunAsync(IChatClient model, params string[] extraArgs) =>
        await RunAsync(_ => model, extraArgs);

    private async Task<(int ExitCode, ReviewResult? Result)> RunAsync(Func<ReviewOptions, IChatClient> createChatClient, params string[] extraArgs)
    {
        var options = ReviewOptions.Parse(
        [
            "--repo", FakeGitHub.Repository, "--pr", "7", "--workspace", workspace.Root, "--out", ResultPath,
            "--model-provider", "openai", "--model", "gpt-test", .. extraArgs,
        ]);
        // The Jira inputs from the environment are absent, as in a repository that does not use Jira.
        var exitCode = await new ReviewCommand(
                gitHub.Client(),
                createChatClient,
                new ActionsLog(log),
                o => JiraRequirementSource.Create(jira.HttpClient(), o.JiraBaseUrl, o.JiraAcceptanceCriteriaField, email: null, apiToken: null))
            .RunAsync(options, CancellationToken.None);
        return (exitCode, File.Exists(ResultPath) ? await ReviewResult.ReadAsync(ResultPath, CancellationToken.None) : null);
    }

    [Fact]
    public async Task A_linked_req_entry_gives_complete_coverage()
    {
        var (exitCode, result) = await RunAsync(new ScriptedChatClient().ThenText(Findings));

        Assert.Equal(0, exitCode);
        Assert.Equal(CoverageState.Complete, result!.Coverage.State);
        Assert.Equal(new RequirementLink(RequirementKind.ReqEntry, "REQ-001", RequirementLinkSource.Title), result.Requirement);
        Assert.Equal(gitHub.HeadSha, result.HeadSha);
        Assert.Equal(FakeGitHub.Repository, result.Repository);
        Assert.Equal(7, result.PullRequestNumber);
        Assert.Equal(2, result.Findings.Count);
        Assert.Matches("^[0-9a-f]{64}$", result.ContextHash);
    }

    [Fact]
    public async Task No_link_keeps_bug_findings_and_reports_incomplete_coverage()
    {
        gitHub.Title = "Cart totals";
        gitHub.HeadRef = "feature/cart";
        gitHub.Body = "No requirement here.";

        var (_, result) = await RunAsync(new ScriptedChatClient().ThenText(Findings));

        Assert.Equal(CoverageState.Incomplete, result!.Coverage.State);
        Assert.Equal([ReviewWorkflow.NoLinkReason], result.Coverage.Reasons);
        Assert.Null(result.Requirement);
        Assert.Equal(FindingKind.Bug, Assert.Single(result.Findings).Kind);
        Assert.Equal(1, result.Dropped["quote-not-found"]);
    }

    [Theory]
    [InlineData("REQ-009: checkout", null, "REQ-009 not found in docs/requirements.md")]
    [InlineData("REQ-001: cart", "docs/missing.md", "requirements file docs/missing.md not found at the head SHA")]
    public async Task An_unreadable_req_entry_keeps_bug_findings_and_names_what_was_missing(string title, string? requirementsFile, string reason)
    {
        gitHub.Title = title;

        var (_, result) = await RunAsync(new ScriptedChatClient().ThenText(Findings), requirementsFile is null ? [] : ["--requirements-file", requirementsFile]);

        Assert.Equal(CoverageState.Incomplete, result!.Coverage.State);
        Assert.Equal([reason], result.Coverage.Reasons);
        Assert.Equal(FindingKind.Bug, Assert.Single(result.Findings).Kind);
    }

    [Fact]
    public async Task A_jira_key_without_a_jira_source_keeps_bug_findings_and_reports_incomplete_coverage()
    {
        gitHub.Title = "SHOP-9 cart totals";

        var (_, result) = await RunAsync(new ScriptedChatClient().ThenText(Findings));

        Assert.Equal(CoverageState.Incomplete, result!.Coverage.State);
        Assert.Equal(["Jira source not configured for SHOP-9"], result.Coverage.Reasons);
        Assert.Equal(new RequirementLink(RequirementKind.JiraIssue, "SHOP-9", RequirementLinkSource.Title), result.Requirement);
        Assert.Equal(FindingKind.Bug, Assert.Single(result.Findings).Kind);
        Assert.Empty(jira.Requests);
    }

    [Fact]
    public async Task Reaching_the_tool_call_budget_reports_incomplete_coverage_and_keeps_findings()
    {
        var model = new ScriptedChatClient()
            .ThenToolCall("read_file", new() { ["path"] = "src/Cart.cs" })
            .ThenToolCall("list_dir", new() { ["path"] = "." })
            .ThenText(Findings);

        var (_, result) = await RunAsync(model, "--max-tool-calls", "1");

        Assert.Equal(CoverageState.Incomplete, result!.Coverage.State);
        Assert.Equal(["context budget reached (1 tool calls)"], result.Coverage.Reasons);
        Assert.Equal(2, result.Findings.Count);
        Assert.Equal(1, result.Usage.ToolCalls);
    }

    [Fact]
    public async Task Reaching_the_character_budget_reports_incomplete_coverage_and_keeps_findings()
    {
        var (_, result) = await RunAsync(new ScriptedChatClient().ThenText(Findings), "--max-context-chars", "10");

        Assert.Equal(CoverageState.Incomplete, result!.Coverage.State);
        Assert.Equal(["context budget reached (10 characters)"], result.Coverage.Reasons);
        Assert.Equal(2, result.Findings.Count);
    }

    [Fact]
    public async Task A_provider_failure_writes_a_failed_result()
    {
        var model = new ScriptedChatClient()
            .ThenThrow(new HttpRequestException("boom", null, HttpStatusCode.BadGateway))
            .ThenThrow(new HttpRequestException("boom", null, HttpStatusCode.BadGateway));

        var (exitCode, result) = await RunAsync(new RetryingChatClient(model));

        Assert.Equal(0, exitCode);
        Assert.Equal(CoverageState.Failed, result!.Coverage.State);
        Assert.Equal(["model provider error (HTTP 502)"], result.Coverage.Reasons);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("azure-openai", null)]
    [InlineData("azure-openai", "shop.openai.azure.com")]
    [InlineData("gemini", null)]
    public async Task A_provider_configuration_error_writes_a_failed_result(string provider, string? endpoint)
    {
        var (exitCode, result) = await RunAsync(options => ChatClientFactory.Create(provider, options.Model, endpoint, "key"));

        Assert.Equal(0, exitCode);
        Assert.Equal([ReviewCommand.ProviderConfigurationError], result!.Coverage.Reasons);
        Assert.Equal(CoverageState.Failed, result.Coverage.State);
    }

    [Fact]
    public async Task An_unreadable_pull_request_exits_with_an_error_and_no_result()
    {
        gitHub.PullRequestStatus = HttpStatusCode.NotFound;

        var (exitCode, result) = await RunAsync(new ScriptedChatClient());

        Assert.Equal(1, exitCode);
        Assert.Null(result);
        Assert.Contains("::error::Cannot read pull request octo/shop#7: GitHub returned HTTP 404.", log.ToString());
    }

    [Fact]
    public async Task The_log_holds_identifiers_counts_states_and_hashes_but_no_requirement_text_code_or_secret()
    {
        var (_, complete) = await RunAsync(new ScriptedChatClient()
            .ThenToolCall("read_file", new() { ["path"] = "src/Cart.cs" })
            .ThenText(Findings));
        await RunAsync(new ScriptedChatClient()
            .ThenThrow(new InvalidOperationException($"invalid key {SecretMarker} for prompt {RequirementMarker} {CodeMarker}")));

        var text = log.ToString();
        Assert.Contains("Requirement: REQ-001 (from the title).", text);
        Assert.Contains("Coverage: complete.", text);
        Assert.Contains("Findings: 1 requirement mismatches, 1 bugs; dropped 0.", text);
        Assert.Contains($"Context hash: {complete!.ContextHash}.", text);
        Assert.Contains("::warning::Coverage failed: model provider error (InvalidOperationException).", text);
        Assert.DoesNotContain(RequirementMarker, text);
        Assert.DoesNotContain("The total applies every discount", text);
        Assert.DoesNotContain(CodeMarker, text);
        Assert.DoesNotContain("prices.Sum", text);
        Assert.DoesNotContain(SecretMarker, text);
        Assert.DoesNotContain("fake-token", text);
        Assert.DoesNotContain("Discounts ignored", text);
        Assert.DoesNotContain("Total(null) throws", text);
    }

    [Fact]
    public void Options_have_defaults_and_split_multi_line_exclusions()
    {
        var options = ReviewOptions.Parse(["--repo", "octo/shop", "--pr", "7", "--model-provider", "anthropic", "--model", "claude-test", "--exclude", "secrets/**\n\n# comment\n*.sql", "--exclude=docs/private/"]);

        Assert.Equal("docs/requirements.md", options.RequirementsFile);
        Assert.Equal("result.json", options.Out);
        Assert.Equal(400_000, options.MaxContextChars);
        Assert.Equal(40, options.MaxToolCalls);
        Assert.Equal(["secrets/**", "*.sql", "docs/private/"], options.Exclude);
        Assert.Null(options.JiraBaseUrl);
        Assert.Null(options.JiraAcceptanceCriteriaField);
    }

    [Fact]
    public void Options_read_the_jira_site_and_acceptance_criteria_field()
    {
        var options = ReviewOptions.Parse(["--repo", "octo/shop", "--pr", "7", "--model-provider", "openai", "--model", "m", "--jira-base-url", "https://shop.atlassian.test", "--jira-acceptance-criteria-field", "customfield_10042"]);

        Assert.Equal(new Uri("https://shop.atlassian.test"), options.JiraBaseUrl);
        Assert.Equal("customfield_10042", options.JiraAcceptanceCriteriaField);
    }

    [Theory]
    [InlineData(null, "en")]
    [InlineData("pt-br", "pt-BR")]
    [InlineData("EN", "en")]
    public void The_language_option_names_a_supported_language_and_defaults_to_english(string? language, string expected)
    {
        string[] args = ["--repo", "octo/shop", "--pr", "7", "--model-provider", "openai", "--model", "m"];

        var options = ReviewOptions.Parse(language is null ? args : [.. args, "--language", language]);

        Assert.Equal(expected, options.Language);
    }

    [Theory]
    [InlineData("--pr", "7", "--model-provider", "openai", "--model", "m")]
    [InlineData("--repo", "octo/shop/../x", "--pr", "7", "--model-provider", "openai", "--model", "m")]
    [InlineData("--repo", "../..", "--pr", "7", "--model-provider", "openai", "--model", "m")]
    [InlineData("--repo", "octo/..", "--pr", "7", "--model-provider", "openai", "--model", "m")]
    [InlineData("--repo", "octo/shop", "--pr", "seven", "--model-provider", "openai", "--model", "m")]
    [InlineData("--repo", "octo/shop", "--pr", "99999999999", "--model-provider", "openai", "--model", "m")]
    [InlineData("--repo", "octo/shop", "--pr", "7", "--model", "m")]
    [InlineData("--repo", "octo/shop", "--pr", "7", "--model-provider", "openai", "--model", "m", "--unknown", "x")]
    [InlineData("--repo", "octo/shop", "--pr", "7", "--model-provider", "openai", "--model")]
    [InlineData("--repo", "octo/shop", "--pr", "7", "--model-provider", "openai", "--model", "m", "--jira-base-url", "http://shop.atlassian.test")]
    [InlineData("--repo", "octo/shop", "--pr", "7", "--model-provider", "openai", "--model", "m", "--jira-base-url", "shop.atlassian.test")]
    [InlineData("--repo", "octo/shop", "--pr", "7", "--model-provider", "openai", "--model", "m", "--jira-base-url", "https://user:pass@shop.atlassian.test")]
    [InlineData("--repo", "octo/shop", "--pr", "7", "--model-provider", "openai", "--model", "m", "--jira-base-url", "https://shop.atlassian.test/?a=b")]
    [InlineData("--repo", "octo/shop", "--pr", "7", "--model-provider", "openai", "--model", "m", "--jira-acceptance-criteria-field", "customfield_1,reporter")]
    [InlineData("--repo", "octo/shop", "--pr", "7", "--model-provider", "openai", "--model", "m", "--language", "fr")]
    public void Invalid_options_are_usage_errors(params string[] args)
    {
        Assert.Throws<UsageException>(() => ReviewOptions.Parse(args));
    }

    public void Dispose()
    {
        workspace.Dispose();
        output.Dispose();
        gitHub.Dispose();
        jira.Dispose();
    }
}
