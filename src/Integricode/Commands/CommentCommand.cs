using System.Text.Json;
using Integricode.Domain;
using Integricode.GitHub;

namespace Integricode.Commands;

/// <summary>The <c>comment</c> command's options.</summary>
/// <param name="SkippedByPolicy">The policy that stopped the review (only <c>fork</c>); set, no result file is read.</param>
/// <param name="Language">
/// The labels' language when no result file is read and the pull request's description and commit messages do not
/// tell; a result carries its own.
/// </param>
public sealed record CommentOptions(string Repository, int PullRequest, string ResultPath, string? SkippedByPolicy = null, string Language = ReviewLanguage.English)
{
    public const string ForkPolicy = "fork";

    private static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal) { "repo", "pr", "result", "skipped-by-policy", "language" };

    /// <exception cref="UsageException">An option is missing or invalid.</exception>
    public static CommentOptions Parse(IReadOnlyList<string> args)
    {
        var commandLine = CommandLine.Parse(args, Allowed);
        var policy = commandLine.Optional("skipped-by-policy");
        if (policy is not (null or ForkPolicy))
        {
            throw new UsageException($"Option --skipped-by-policy must be {ForkPolicy}.");
        }

        return new CommentOptions(
            commandLine.Repository(),
            commandLine.PullRequestNumber(),
            commandLine.Optional("result") ?? "result.json",
            policy,
            ReviewOptions.ParseLanguage(commandLine.Optional("language")));
    }
}

/// <summary>
/// Publishes a review result on the pull request (FR-12 to FR-15, FR-17): one summary comment, updated in place, and
/// one review with event <c>COMMENT</c> holding an inline comment per new finding on a diff line. Earlier Integricode
/// findings the result no longer reports are minimized as outdated. Nothing is published but what the result carries,
/// through <see cref="CommentTemplates"/>. A pull request the workflow's gate stopped by policy gets only a summary
/// that says so (FR-3).
/// </summary>
/// <param name="publisherLogin">The account the comment job's token posts as; only its comments are updated or minimized.</param>
public sealed class CommentCommand(GitHubClient gitHub, ActionsLog log, string publisherLogin = CommentCommand.GitHubActionsLogin)
{
    public const string GitHubActionsLogin = "github-actions[bot]";
    public const string ForkReason = "pull request from a fork; Integricode reviews only pull requests whose branch is in this repository";

    /// <returns>0 when published, 1 when the result file is unreadable or does not belong to the pull request.</returns>
    public async Task<int> RunAsync(CommentOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.SkippedByPolicy is not null)
        {
            await PublishSkippedByPolicyAsync(options, cancellationToken);
            return 0;
        }

        ReviewResult result;
        try
        {
            result = await ReviewResult.ReadAsync(options.ResultPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            log.Error($"Cannot read the review result: {exception.GetType().Name}.");
            return 1;
        }

        if (!string.Equals(result.Repository, options.Repository, StringComparison.OrdinalIgnoreCase) || result.PullRequestNumber != options.PullRequest)
        {
            log.Error($"The review result is for another pull request than {options.Repository}#{options.PullRequest}.");
            return 1;
        }

        var pullRequest = await gitHub.GetPullRequestAsync(options.Repository, options.PullRequest, cancellationToken);
        if (!string.Equals(pullRequest.HeadSha, result.HeadSha, StringComparison.OrdinalIgnoreCase))
        {
            var summary = await FindSummaryAsync(options, cancellationToken);
            if (summary is not null && string.Equals(CommentTemplates.ReadRecord(summary.Body)?.HeadSha, pullRequest.HeadSha, StringComparison.OrdinalIgnoreCase))
            {
                // A review of the current head already published; a late, older review must not replace it.
                log.Notice($"Superseded: the review of {result.HeadSha} published nothing; the summary of {pullRequest.HeadSha} is kept.");
                return 0;
            }

            await UpsertSummaryAsync(options, summary, CommentTemplates.Superseded(result, pullRequest.HeadSha), cancellationToken);
            log.Notice($"Superseded: the review of {result.HeadSha} published no findings; the head is {pullRequest.HeadSha}.");
            return 0;
        }

        if (result.Coverage.State is not (CoverageState.Complete or CoverageState.Incomplete))
        {
            await UpsertSummaryAsync(options, await FindSummaryAsync(options, cancellationToken), CommentTemplates.Summary(result, []), cancellationToken);
            log.Info($"Published the summary ({JsonNamingPolicy.KebabCaseLower.ConvertName(result.Coverage.State.ToString())}); no findings.");
            return 0;
        }

        await PublishFindingsAsync(options, result, cancellationToken);
        return 0;
    }

    private async Task PublishSkippedByPolicyAsync(CommentOptions options, CancellationToken cancellationToken)
    {
        var pullRequest = await gitHub.GetPullRequestAsync(options.Repository, options.PullRequest, cancellationToken);
        var result = new ReviewResult
        {
            Repository = options.Repository,
            PullRequestNumber = options.PullRequest,
            HeadSha = pullRequest.HeadSha,
            Coverage = Coverage.SkippedByPolicy(ForkReason),
            Language = await PullRequestLanguage.ResolveAsync(gitHub, options.Repository, pullRequest, options.Language, log, cancellationToken),
        };

        await UpsertSummaryAsync(options, await FindSummaryAsync(options, cancellationToken), CommentTemplates.Summary(result, []), cancellationToken);
        log.Notice($"Skipped by policy ({options.SkippedByPolicy}): published the summary; no findings.");
    }

    private async Task PublishFindingsAsync(CommentOptions options, ReviewResult result, CancellationToken cancellationToken)
    {
        var diff = DiffMap.Build(await gitHub.GetPullRequestFilesAsync(options.Repository, options.PullRequest, cancellationToken));
        var inline = result.Findings.Where(f => diff.IsDiffLine(f.Path, f.Line)).ToList();
        var offDiff = result.Findings.Where(f => !diff.IsDiffLine(f.Path, f.Line)).ToList();

        var published = (await gitHub.ListReviewCommentsAsync(options.Repository, options.PullRequest, cancellationToken))
            .Where(comment => comment.UserLogin == publisherLogin)
            .Select(comment => (Comment: comment, Fingerprint: CommentTemplates.FingerprintOf(comment.Body)))
            .Where(item => item.Fingerprint is not null)
            .ToList();

        // Only visible comments count as published: a finding that comes back after being minimized is posted again.
        var hidden = published.Count == 0
            ? new HashSet<string>()
            : await gitHub.GetMinimizedAsync([.. published.Select(item => item.Comment.NodeId)], cancellationToken);
        var visible = published.Where(item => !hidden.Contains(item.Comment.NodeId)).ToList();
        var visibleFingerprints = visible.Select(item => item.Fingerprint!).ToHashSet(StringComparer.Ordinal);
        var currentFingerprints = result.Findings.Select(f => f.Fingerprint).ToHashSet(StringComparer.Ordinal);

        var minimized = 0;
        foreach (var (comment, _) in visible.Where(item => !currentFingerprints.Contains(item.Fingerprint!)))
        {
            try
            {
                await gitHub.MinimizeAsOutdatedAsync(comment.NodeId, cancellationToken);
                minimized++;
            }
            catch (GitHubApiException exception)
            {
                // A stale comment left visible is not worth failing the publication for.
                log.Warning($"Could not minimize comment {comment.Id} as outdated: HTTP {(int)exception.StatusCode}.");
            }
        }

        var fresh = inline.Where(f => !visibleFingerprints.Contains(f.Fingerprint)).ToList();
        if (fresh.Count > 0)
        {
            var comments = fresh
                .Select(f => new InlineComment(f.Path, f.Line, CommentTemplates.InlineComment(f, result.HeadSha, result.Language), StartLineOf(f)))
                .ToList();
            await gitHub.CreateCommentReviewAsync(options.Repository, options.PullRequest, result.HeadSha, CommentTemplates.ReviewBody(result, fresh.Count), comments, cancellationToken);
        }

        await UpsertSummaryAsync(options, await FindSummaryAsync(options, cancellationToken), CommentTemplates.Summary(result, offDiff), cancellationToken);
        log.Info($"Published {fresh.Count} inline findings ({inline.Count - fresh.Count} already published), {offDiff.Count} in the summary; minimized {minimized} outdated.");
    }

    // A suggestion over several lines anchors the comment on its whole range, so GitHub replaces exactly those lines.
    private static int? StartLineOf(Finding finding) =>
        finding.Suggestion is { } suggestion && suggestion.StartLine < finding.Line ? suggestion.StartLine : null;

    private async Task<GitHubComment?> FindSummaryAsync(CommentOptions options, CancellationToken cancellationToken) =>
        (await gitHub.ListIssueCommentsAsync(options.Repository, options.PullRequest, cancellationToken))
            .FirstOrDefault(comment => comment.UserLogin == publisherLogin && comment.Body.StartsWith(CommentTemplates.SummaryMarker, StringComparison.Ordinal));

    private async Task UpsertSummaryAsync(CommentOptions options, GitHubComment? existing, string body, CancellationToken cancellationToken)
    {
        if (existing is null)
        {
            await gitHub.CreateIssueCommentAsync(options.Repository, options.PullRequest, body, cancellationToken);
        }
        else
        {
            await gitHub.UpdateIssueCommentAsync(options.Repository, existing.Id, body, cancellationToken);
        }
    }
}
