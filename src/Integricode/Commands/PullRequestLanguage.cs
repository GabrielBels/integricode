using Integricode.Domain;
using Integricode.GitHub;

namespace Integricode.Commands;

/// <summary>
/// Picks the language Integricode writes in for a pull request: the language of its description and commit subject
/// lines, detected here and never by the model, or the <c>--language</c> option when they do not tell.
/// </summary>
internal static class PullRequestLanguage
{
    /// <summary>Reads the pull request's commit messages and picks the language from their subjects and its description.</summary>
    /// <param name="gitHub">The client that reads the commit messages.</param>
    /// <param name="repository">The repository, as <c>owner/name</c>.</param>
    /// <param name="pullRequest">The pull request, with its description.</param>
    /// <param name="fallback">The <c>--language</c> option, used when the texts do not tell.</param>
    /// <param name="log">The log that records the language and where it came from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The language name as <see cref="ReviewLanguage"/> spells it.</returns>
    public static async Task<string> ResolveAsync(
        GitHubClient gitHub,
        string repository,
        PullRequest pullRequest,
        string fallback,
        ActionsLog log,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> messages;
        try
        {
            messages = await gitHub.GetPullRequestCommitMessagesAsync(repository, pullRequest.Number, cancellationToken);
        }
        catch (GitHubApiException exception)
        {
            // The language is not worth losing the review over; the description and --language still decide it.
            log.Warning($"Cannot list the commits: GitHub returned HTTP {(int)exception.StatusCode}; picking the language without them.");
            messages = [];
        }

        if (ReviewLanguage.Detect([pullRequest.Body, .. messages.Select(Subject)]) is { } detected)
        {
            log.Info($"Language: {detected}, from the description and {messages.Count} commit subjects.");
            return detected;
        }

        log.Info($"Language: {fallback}, from --language; the description and {messages.Count} commit subjects do not tell.");
        return fallback;
    }

    /// <summary>
    /// The first line of a commit message, which its author writes; the body also carries trailers, tool footers, and
    /// lines git adds (<c>This reverts commit ...</c>, <c>(cherry picked from commit ...)</c>).
    /// </summary>
    /// <param name="message">The commit message.</param>
    /// <returns>The message up to its first line break.</returns>
    private static string Subject(string message) => message.Split('\n', 2)[0];
}
