using Integricode.Domain;
using Integricode.GitHub;

namespace Integricode.Commands;

/// <summary>
/// Picks the language Integricode writes in for a pull request: the language of its description and commit messages,
/// detected here and never by the model, or the <c>--language</c> option when they do not tell.
/// </summary>
internal static class PullRequestLanguage
{
    /// <summary>Reads the pull request's commit messages and picks the language from them and its description.</summary>
    /// <param name="gitHub">The client that reads the commit messages.</param>
    /// <param name="repository">The repository, as <c>owner/name</c>.</param>
    /// <param name="pullRequest">The pull request, with its description.</param>
    /// <param name="fallback">The <c>--language</c> option, used when the texts do not tell.</param>
    /// <param name="log">The log that records the language and where it came from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The language name as <see cref="ReviewLanguage"/> spells it.</returns>
    /// <exception cref="GitHubApiException">GitHub refused to list the commits.</exception>
    public static async Task<string> ResolveAsync(
        GitHubClient gitHub,
        string repository,
        PullRequest pullRequest,
        string fallback,
        ActionsLog log,
        CancellationToken cancellationToken)
    {
        var messages = await gitHub.GetPullRequestCommitMessagesAsync(repository, pullRequest.Number, cancellationToken);
        if (ReviewLanguage.Detect([pullRequest.Body, .. messages]) is { } detected)
        {
            log.Info($"Language: {detected}, from the description and {messages.Count} commit messages.");
            return detected;
        }

        log.Info($"Language: {fallback}, from --language; the description and {messages.Count} commit messages do not tell.");
        return fallback;
    }
}
