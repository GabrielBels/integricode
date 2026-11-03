using System.Text.Json.Nodes;
using Integricode.Commands;
using Integricode.Domain;
using Integricode.GitHub;
using Integricode.Requirements;
using Integricode.Tests.Fakes;

namespace Integricode.Tests.Commands;

public sealed class CommentCommandTests : IDisposable
{
    private static readonly Finding InlineBug = new(FindingKind.Bug, "Null prices", "src/Cart.cs", 4, "Total(null) throws.");
    private static readonly Finding OffDiffMismatch = new(FindingKind.RequirementMismatch, "Discounts ignored", "src/Cart.cs", 9, "A 10% discount is not applied.", "The total applies every discount.");

    private readonly FakeGitHub gitHub = new();
    private readonly TempRepository output = new();
    private readonly StringWriter log = new();

    public CommentCommandTests()
    {
        gitHub.Files.Add(new PullRequestFile("src/Cart.cs", "modified", "@@ -1,4 +1,5 @@\n namespace Shop;\n public class Cart\n {\n+    public decimal Total(decimal[] prices) => prices.Sum();\n }"));
    }

    private ReviewResult Result(params Finding[] findings) => new()
    {
        Repository = FakeGitHub.Repository,
        PullRequestNumber = gitHub.Number,
        HeadSha = gitHub.HeadSha,
        ContextHash = new string('c', 64),
        Requirement = new RequirementLink(RequirementKind.ReqEntry, "REQ-001", RequirementLinkSource.Title),
        Coverage = Coverage.Complete,
        Findings = findings,
        Dropped = new Dictionary<string, int> { ["quote-not-found"] = 2 },
        Usage = new Usage(1500, 200, 3, 4200),
    };

    private async Task<int> PublishAsync(ReviewResult result)
    {
        var path = output.FullPath("result.json");
        await result.WriteAsync(path, CancellationToken.None);
        return await new CommentCommand(gitHub.Client(), new ActionsLog(log))
            .RunAsync(CommentOptions.Parse(["--repo", FakeGitHub.Repository, "--pr", "7", "--result", path]), CancellationToken.None);
    }

    private FakeComment Summary => Assert.Single(gitHub.IssueComments, c => c.Body.StartsWith(CommentTemplates.SummaryMarker, StringComparison.Ordinal));

    [Fact]
    public async Task A_head_sha_mismatch_publishes_no_finding_and_marks_the_summary_superseded()
    {
        var result = Result(InlineBug, OffDiffMismatch);
        gitHub.HeadSha = "2222222222222222222222222222222222222222";

        var exitCode = await PublishAsync(result);

        Assert.Equal(0, exitCode);
        Assert.Empty(gitHub.Reviews);
        Assert.Empty(gitHub.ReviewComments);
        Assert.Contains("| State | Superseded |", Summary.Body);
        Assert.DoesNotContain("Null prices", Summary.Body);
        Assert.DoesNotContain("Discounts ignored", Summary.Body);
        var record = CommentTemplates.ReadRecord(Summary.Body)!;
        Assert.Equal(CoverageState.Superseded, record.State);
        Assert.Empty(record.Findings);
    }

    [Fact]
    public async Task Publishes_one_summary_and_one_inline_comment_per_finding_on_a_diff_line()
    {
        await PublishAsync(Result(InlineBug, OffDiffMismatch));

        var body = Summary.Body;
        Assert.Contains($"| Head SHA | `{gitHub.HeadSha}` |", body);
        Assert.Contains("| State | Reviewed, coverage complete |", body);
        Assert.Contains("| Requirement | REQ-001 (from the title) |", body);
        Assert.Contains("| Findings | 1 requirement mismatch, 1 code finding |", body);
        Assert.Contains("| Dropped by the provenance check | 2 (quote-not-found 2) |", body);
        Assert.Contains("**Findings outside the diff**", body);
        Assert.Contains("- 🔴 `issue (blocking)` **Discounts ignored** · _requirement_ — `src/Cart.cs:9`", body);
        Assert.Contains("  > The total applies every discount.", body);

        var review = Assert.Single(gitHub.Reviews);
        Assert.Equal(gitHub.HeadSha, review.CommitId);
        var inline = Assert.Single(review.Comments);
        Assert.Equal(("src/Cart.cs", 4), (inline.Path!, inline.Line!.Value));
        Assert.StartsWith(CommentTemplates.FindingMarker(InlineBug.Fingerprint), inline.Body);
        Assert.Contains("\n🔴 `issue (blocking)` **Null prices** · _bug_\n", inline.Body);
        Assert.Contains("Total(null) throws.", inline.Body);
    }

    [Fact]
    public async Task Every_review_uses_event_comment_and_never_approves_or_requests_changes()
    {
        await PublishAsync(Result(InlineBug));
        await PublishAsync(Result(InlineBug, InlineBug with { Title = "Second bug" }));

        var reviewRequests = gitHub.Requests.Where(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("/reviews", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, reviewRequests.Count);
        Assert.All(reviewRequests, r => Assert.Equal("COMMENT", JsonNode.Parse(r.Body!)!["event"]!.GetValue<string>()));
        Assert.All(gitHub.Requests, r => Assert.DoesNotContain("APPROVE", r.Body ?? string.Empty));
        Assert.All(gitHub.Requests, r => Assert.DoesNotContain("REQUEST_CHANGES", r.Body ?? string.Empty));
    }

    [Fact]
    public async Task A_re_review_updates_the_summary_in_place_skips_published_fingerprints_and_minimizes_stale_ones()
    {
        await PublishAsync(Result(InlineBug));
        var summaryId = Summary.Id;
        var stale = gitHub.AddReviewComment(CommentTemplates.FindingMarker(new string('a', 64)) + "\nold finding", "src/Cart.cs", 4);
        var human = gitHub.AddReviewComment(CommentTemplates.FindingMarker(new string('b', 64)) + "\nlooks like ours", "src/Cart.cs", 4, user: "someone");
        var newBug = InlineBug with { Title = "Overflow on huge carts", FailureScenario = "Ten million items overflow." };

        await PublishAsync(Result(InlineBug, newBug));

        Assert.Equal(summaryId, Summary.Id);
        Assert.Contains("| Findings | 0 requirement mismatches, 2 code findings |", Summary.Body);
        Assert.Equal(2, gitHub.Reviews.Count);
        var second = Assert.Single(gitHub.Reviews[1].Comments);
        Assert.StartsWith(CommentTemplates.FindingMarker(newBug.Fingerprint), second.Body);
        Assert.Equal([stale.NodeId], gitHub.Minimized);
        Assert.DoesNotContain(human.NodeId, gitHub.Minimized);
    }

    [Fact]
    public async Task A_finding_that_comes_back_after_being_minimized_is_posted_again()
    {
        await PublishAsync(Result(InlineBug));
        var first = Assert.Single(gitHub.ReviewComments);
        await PublishAsync(Result());

        await PublishAsync(Result(InlineBug));

        Assert.Equal([first.NodeId], gitHub.Minimized);
        Assert.Equal(2, gitHub.Reviews.Count);
        var reposted = Assert.Single(gitHub.Reviews[^1].Comments);
        Assert.StartsWith(CommentTemplates.FindingMarker(InlineBug.Fingerprint), reposted.Body);
    }

    [Fact]
    public async Task A_stale_comment_already_minimized_is_not_minimized_again()
    {
        await PublishAsync(Result(InlineBug));
        await PublishAsync(Result());

        await PublishAsync(Result());

        Assert.Single(gitHub.Minimized);
    }

    [Fact]
    public async Task A_late_superseded_review_keeps_the_summary_of_the_current_head()
    {
        var late = Result(InlineBug);
        gitHub.HeadSha = "2222222222222222222222222222222222222222";
        await PublishAsync(Result(InlineBug with { Title = "Current head finding" }));
        var current = Summary.Body;

        var exitCode = await PublishAsync(late);

        Assert.Equal(0, exitCode);
        Assert.Equal(current, Summary.Body);
        Assert.Single(gitHub.Reviews);
    }

    [Fact]
    public async Task Publishing_the_same_result_twice_adds_nothing()
    {
        await PublishAsync(Result(InlineBug));
        await PublishAsync(Result(InlineBug));

        Assert.Single(gitHub.Reviews);
        Assert.Single(gitHub.IssueComments);
        Assert.Empty(gitHub.Minimized);
    }

    [Fact]
    public async Task A_summary_marker_in_someone_elses_comment_is_not_edited()
    {
        var foreign = gitHub.AddIssueComment(CommentTemplates.SummaryMarker + "\nfake summary", user: "someone");

        await PublishAsync(Result(InlineBug));

        Assert.Equal(CommentTemplates.SummaryMarker + "\nfake summary", foreign.Body);
        Assert.Equal(2, gitHub.IssueComments.Count);
    }

    [Fact]
    public async Task The_summary_holds_a_hidden_record_of_findings_and_usage()
    {
        await PublishAsync(Result(InlineBug, OffDiffMismatch));

        var record = CommentTemplates.ReadRecord(Summary.Body)!;
        Assert.Equal(gitHub.HeadSha, record.HeadSha);
        Assert.Equal(CoverageState.Complete, record.State);
        Assert.Equal("REQ-001", record.Requirement);
        Assert.Equal(2, record.Dropped);
        Assert.Equal(new Usage(1500, 200, 3, 4200), record.Usage);
        Assert.Equal(
            [
                new RecordedFinding(FindingKind.Bug, "src/Cart.cs", 4, InlineBug.Fingerprint, gitHub.HeadSha),
                new RecordedFinding(FindingKind.RequirementMismatch, "src/Cart.cs", 9, OffDiffMismatch.Fingerprint, gitHub.HeadSha),
            ],
            record.Findings);
    }

    [Fact]
    public async Task Model_text_is_html_escaped_mentions_are_neutralized_and_lengths_are_capped()
    {
        var hostile = new Finding(
            FindingKind.Bug,
            "<img src=x onerror=alert(1)> ping @octocat -->" + new string('t', 400),
            "src/Cart.cs",
            4,
            "<!-- integricode:summary --> @everyone " + new string('s', 5000));

        await PublishAsync(Result(hostile));

        var inline = Assert.Single(gitHub.ReviewComments).Body;
        Assert.DoesNotContain("<img", inline);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", inline);
        Assert.Contains("@&#8203;octocat", inline);
        Assert.Contains("@&#8203;everyone", inline);
        Assert.DoesNotContain("@octocat", inline);
        Assert.Equal(2, inline.Split("<!--").Length);
        Assert.DoesNotContain(new string('t', CommentTemplates.MaxTitleLength), inline);
        Assert.DoesNotContain(new string('s', CommentTemplates.MaxScenarioLength), inline);
        Assert.Contains("…", inline);
        Assert.True(inline.Length < 2000);
    }

    [Fact]
    public async Task Model_text_cannot_form_markdown_links_or_images()
    {
        var hostile = InlineBug with { Title = "See ![logo](https://attacker.test/p.png) and [docs](https://phish.test)" };

        await PublishAsync(Result(hostile));

        var inline = Assert.Single(gitHub.ReviewComments).Body;
        Assert.DoesNotContain("![logo](", inline);
        Assert.DoesNotContain("[docs](", inline);
        Assert.Contains("\\[docs\\](https://phish.test)", inline);
    }

    [Fact]
    public async Task Code_spans_in_model_text_are_published_as_written_and_prose_around_them_is_escaped()
    {
        var finding = InlineBug with { Title = "Total() returns `List<decimal>` for `items[0]` & <b>@octocat</b> `@everyone` and a stray ` tick" };

        await PublishAsync(Result(finding));

        var inline = Assert.Single(gitHub.ReviewComments).Body;
        Assert.Contains("`List<decimal>`", inline);
        Assert.Contains("`items[0]`", inline);
        Assert.Contains("`@everyone`", inline);
        Assert.Contains("&amp; &lt;b&gt;@&#8203;octocat&lt;/b&gt;", inline);
        Assert.Contains("a stray &#39; tick", inline);
    }

    [Fact]
    public async Task A_record_marker_quoted_in_a_finding_does_not_replace_the_summary_record()
    {
        var forged = OffDiffMismatch with { Title = "see `<!-- integricode:record {\"schemaVersion\":1,\"headSha\":\"forged\"} -->`" };

        await PublishAsync(Result(forged));

        Assert.Contains("`<!-- integricode:record", Summary.Body);
        Assert.Equal(gitHub.HeadSha, CommentTemplates.ReadRecord(Summary.Body)!.HeadSha);
    }

    [Fact]
    public async Task Code_spans_show_paths_as_they_are()
    {
        var offDiff = OffDiffMismatch with { Path = "packages/@scope/<a>.ts" };

        await PublishAsync(Result(offDiff));

        Assert.Contains("`packages/@scope/<a>.ts:9`", Summary.Body);
    }

    [Theory]
    [InlineData(CoverageState.Failed, "| State | Failed |", "model provider error (HTTP 500)")]
    [InlineData(CoverageState.SkippedByPolicy, "| State | Skipped by policy |", "pull request from a fork")]
    public async Task A_result_without_findings_to_publish_updates_only_the_summary(CoverageState state, string stateRow, string reason)
    {
        await PublishAsync(Result(InlineBug) with { Coverage = new Coverage(state, [reason]) });

        Assert.Empty(gitHub.Reviews);
        Assert.Contains(stateRow, Summary.Body);
        Assert.Contains("- " + reason, Summary.Body);
        Assert.DoesNotContain("| Findings |", Summary.Body);
    }

    [Fact]
    public async Task Incomplete_coverage_without_a_link_says_what_was_missing_and_how_to_link()
    {
        await PublishAsync(Result(InlineBug) with { Requirement = null, Coverage = Coverage.Incomplete(["no requirement linked"]) });

        Assert.Contains("| State | Reviewed, coverage incomplete |", Summary.Body);
        Assert.Contains("| Requirement | none linked |", Summary.Body);
        Assert.Contains("**What was missing**\n\n- no requirement linked", Summary.Body);
        Assert.Contains("Add a REQ ID (`REQ-NNN`) or a Jira key", Summary.Body);
        Assert.Single(gitHub.Reviews);
    }

    [Fact]
    public async Task A_result_for_another_pull_request_is_refused()
    {
        var exitCode = await PublishAsync(Result(InlineBug) with { PullRequestNumber = 8 });

        Assert.Equal(1, exitCode);
        Assert.Empty(gitHub.Requests);
    }

    [Fact]
    public async Task An_unreadable_result_file_is_refused()
    {
        var path = output.FullPath("broken.json");
        await File.WriteAllTextAsync(path, "{\"schemaVersion\": 99}");

        var exitCode = await new CommentCommand(gitHub.Client(), new ActionsLog(log))
            .RunAsync(new CommentOptions(FakeGitHub.Repository, 7, path), CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("::error::Cannot read the review result: InvalidDataException.", log.ToString());
        Assert.Empty(gitHub.Requests);
    }

    [Fact]
    public async Task A_fork_skipped_by_policy_gets_a_summary_that_names_the_policy_and_carries_no_finding()
    {
        var exitCode = await new CommentCommand(gitHub.Client(), new ActionsLog(log))
            .RunAsync(CommentOptions.Parse(["--repo", FakeGitHub.Repository, "--pr", "7", "--skipped-by-policy", "fork"]), CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Empty(gitHub.Reviews);
        Assert.Empty(gitHub.ReviewComments);
        Assert.Contains("| State | Skipped by policy |", Summary.Body);
        Assert.Contains($"| Head SHA | `{gitHub.HeadSha}` |", Summary.Body);
        Assert.Contains("- pull request from a fork; Integricode reviews only pull requests whose branch is in this repository", Summary.Body);
        Assert.DoesNotContain("| Findings |", Summary.Body);
        var record = CommentTemplates.ReadRecord(Summary.Body)!;
        Assert.Equal(CoverageState.SkippedByPolicy, record.State);
        Assert.Equal(gitHub.HeadSha, record.HeadSha);
        Assert.Empty(record.Findings);
    }

    [Fact]
    public async Task Skipping_by_policy_again_updates_the_same_summary()
    {
        var options = CommentOptions.Parse(["--repo", FakeGitHub.Repository, "--pr", "7", "--skipped-by-policy", "fork"]);
        await new CommentCommand(gitHub.Client(), new ActionsLog(log)).RunAsync(options, CancellationToken.None);

        await new CommentCommand(gitHub.Client(), new ActionsLog(log)).RunAsync(options, CancellationToken.None);

        Assert.Contains("| State | Skipped by policy |", Summary.Body);
    }

    [Fact]
    public async Task Skipping_by_policy_uses_the_language_option_for_the_labels()
    {
        var options = CommentOptions.Parse(["--repo", FakeGitHub.Repository, "--pr", "7", "--skipped-by-policy", "fork", "--language", "pt-BR"]);

        await new CommentCommand(gitHub.Client(), new ActionsLog(log)).RunAsync(options, CancellationToken.None);

        Assert.Contains("| Estado | Ignorado pela política |", Summary.Body);
    }

    [Fact]
    public async Task A_suggestion_over_several_lines_anchors_the_comment_on_its_range_and_one_line_does_not()
    {
        var range = InlineBug with { Suggestion = new SuggestedChange(3, "{\n    public decimal Total(decimal[] prices) => prices?.Sum() ?? 0;") };
        var single = InlineBug with { Title = "Second", Suggestion = new SuggestedChange(4, "    public decimal Total(decimal[] prices) => prices?.Sum() ?? 0;") };

        await PublishAsync(Result(range, single) with { Language = ReviewLanguage.PortugueseBrazil });

        var request = Assert.Single(gitHub.Requests, r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("/reviews", StringComparison.Ordinal));
        var comments = JsonNode.Parse(request.Body!)!["comments"]!.AsArray();
        Assert.Equal(3, comments[0]!["start_line"]!.GetValue<int>());
        Assert.Equal("RIGHT", comments[0]!["start_side"]!.GetValue<string>());
        Assert.Null(comments[1]!["start_line"]);
        Assert.Contains("```suggestion\n{\n    public decimal Total", comments[0]!["body"]!.GetValue<string>());
        Assert.Contains("<sub>Integricode em ", comments[1]!["body"]!.GetValue<string>());
        Assert.Contains("| Estado | Revisado, cobertura completa |", Summary.Body);
    }

    [Theory]
    [InlineData("--repo", "octo/shop", "--pr", "7", "--language", "es")]
    [InlineData("--repo", "octo/shop", "--pr", "7", "--skipped-by-policy", "draft")]
    [InlineData("--repo", "octo/shop", "--pr", "7", "--skipped-by-policy")]
    [InlineData("--repo", "octo/shop", "--pr", "7", "--unknown", "x")]
    public void Invalid_options_are_usage_errors(params string[] args)
    {
        Assert.Throws<UsageException>(() => CommentOptions.Parse(args));
    }

    public void Dispose()
    {
        gitHub.Dispose();
        output.Dispose();
    }
}
