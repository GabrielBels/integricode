using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Integricode.Agent;
using Integricode.Domain;
using Integricode.GitHub;
using Integricode.Requirements;
using Integricode.Requirements.Jira;
using Microsoft.Extensions.AI;

namespace Integricode.Commands;

/// <summary>The <c>review</c> command's options (FR-16 inputs reach the tool as arguments).</summary>
public sealed partial record ReviewOptions(
    string Repository,
    int PullRequest,
    string Workspace,
    string Out,
    string ModelProvider,
    string Model,
    string? ModelEndpoint,
    string RequirementsFile,
    IReadOnlyList<string> Exclude,
    long MaxContextChars,
    int MaxToolCalls,
    Uri? JiraBaseUrl = null,
    string? JiraAcceptanceCriteriaField = null,
    string Language = ReviewLanguage.English)
{
    public const string DefaultRequirementsFile = "docs/requirements.md";

    private static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "repo", "pr", "workspace", "out", "model-provider", "model", "model-endpoint", "requirements-file",
        "exclude", "max-context-chars", "max-tool-calls", "jira-base-url", "jira-acceptance-criteria-field", "language",
    };

    /// <exception cref="UsageException">An option is missing or invalid.</exception>
    public static ReviewOptions Parse(IReadOnlyList<string> args)
    {
        var commandLine = CommandLine.Parse(args, Allowed);
        return new ReviewOptions(
            commandLine.Repository(),
            commandLine.PullRequestNumber(),
            commandLine.Optional("workspace") ?? Directory.GetCurrentDirectory(),
            commandLine.Optional("out") ?? "result.json",
            commandLine.Required("model-provider"),
            commandLine.Required("model"),
            commandLine.Optional("model-endpoint"),
            commandLine.Optional("requirements-file") ?? DefaultRequirementsFile,
            [.. commandLine.All("exclude").SelectMany(SplitGlobs)],
            commandLine.Number("max-context-chars", ContextBudget.DefaultMaxCharacters, minimum: 0),
            (int)commandLine.Number("max-tool-calls", ContextBudget.DefaultMaxToolCalls, minimum: 0, maximum: int.MaxValue),
            JiraSite(commandLine.Optional("jira-base-url")),
            JiraField(commandLine.Optional("jira-acceptance-criteria-field")),
            ParseLanguage(commandLine.Optional("language")));
    }

    /// <summary>The supported language an option names, or English when it is not set.</summary>
    /// <param name="text">The option's value, or null.</param>
    /// <returns>The language name as <see cref="ReviewLanguage"/> spells it.</returns>
    /// <exception cref="UsageException">The value is not a supported language.</exception>
    internal static string ParseLanguage(string? text) => text is null
        ? ReviewLanguage.English
        : ReviewLanguage.Find(text) ?? throw new UsageException($"Option --language must be one of {string.Join(", ", ReviewLanguage.All)}.");

    // The site receives the Jira credentials, so only an https origin is accepted.
    private static Uri? JiraSite(string? text) => text is null ? null
        : Uri.TryCreate(text, UriKind.Absolute, out var site) && site.Scheme == Uri.UriSchemeHttps && site.UserInfo.Length == 0 && site.Query.Length == 0 && site.Fragment.Length == 0
            ? site
            : throw new UsageException("Option --jira-base-url must be an https URL, such as https://example.atlassian.net.");

    // The field ID becomes part of the issue request's field list, so nothing but a field ID is accepted.
    private static string? JiraField(string? text) => text is null || JiraFieldId().IsMatch(text)
        ? text
        : throw new UsageException("Option --jira-acceptance-criteria-field must be a Jira field ID, such as customfield_10042.");

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex JiraFieldId();

    // A workflow passes a multi-line input; blank lines and # comments are skipped.
    private static IEnumerable<string> SplitGlobs(string value) => value
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith('#'));
}

/// <summary>
/// Reviews a pull request and writes <c>result.json</c> (FR-7, FR-8, FR-17). A result is written for every reviewed
/// state, failed included, so the comment job can report it; only an unreadable pull request ends without one.
/// </summary>
/// <param name="createJiraSource">Creates the Jira source from the options, or null when the Jira source is off.</param>
public sealed class ReviewCommand(
    GitHubClient gitHub,
    Func<ReviewOptions, IChatClient> createChatClient,
    ActionsLog log,
    Func<ReviewOptions, IRequirementSource?>? createJiraSource = null)
{
    public const string ProviderConfigurationError =
        "model provider configuration error: check --model-provider, --model, --model-endpoint, and INTEGRICODE_MODEL_API_KEY";

    /// <returns>0 when the result file was written, 1 when the pull request could not be read.</returns>
    public async Task<int> RunAsync(ReviewOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        PullRequest pullRequest;
        IReadOnlyList<PullRequestFile> files;
        try
        {
            pullRequest = await gitHub.GetPullRequestAsync(options.Repository, options.PullRequest, cancellationToken);
            files = await gitHub.GetPullRequestFilesAsync(options.Repository, options.PullRequest, cancellationToken);
        }
        catch (GitHubApiException exception)
        {
            log.Error($"Cannot read pull request {options.Repository}#{options.PullRequest}: GitHub returned HTTP {(int)exception.StatusCode}.");
            return 1;
        }

        log.Info($"Reviewing {options.Repository}#{options.PullRequest} at {pullRequest.HeadSha} ({files.Count} files).");
        var result = await ReviewAsync(options, pullRequest, files, cancellationToken);
        await result.WriteAsync(options.Out, cancellationToken);
        Report(result);
        return 0;
    }

    private async Task<ReviewResult> ReviewAsync(ReviewOptions options, PullRequest pullRequest, IReadOnlyList<PullRequestFile> files, CancellationToken cancellationToken)
    {
        IChatClient chatClient;
        try
        {
            chatClient = createChatClient(options);
        }
        catch (ArgumentException)
        {
            return new ReviewResult
            {
                Repository = options.Repository,
                PullRequestNumber = pullRequest.Number,
                HeadSha = pullRequest.HeadSha,
                Requirement = RequirementIdentifier.Identify(pullRequest.Title, pullRequest.HeadRef, pullRequest.Body),
                Coverage = Coverage.Failed(ProviderConfigurationError),
                Language = options.Language,
            };
        }

        using (chatClient)
        {
            var guard = new PathGuard(options.Workspace, options.Exclude);
            List<IRequirementSource> sources = [new ReqEntrySource(guard, options.RequirementsFile)];
            if (createJiraSource?.Invoke(options) is { } jira)
            {
                sources.Add(jira);
            }

            var workflow = new ReviewWorkflow(chatClient, guard, new ContextBudget(options.MaxToolCalls, options.MaxContextChars), new RequirementSources(sources), options.Language);
            return await workflow.RunAsync(new ReviewRequest(options.Repository, pullRequest, files), cancellationToken);
        }
    }

    private void Report(ReviewResult result)
    {
        log.Info(result.Requirement is { } link
            ? $"Requirement: {link.Id} (from the {Name(link.Source)})."
            : "Requirement: none linked.");
        log.Info($"Coverage: {Name(result.Coverage.State)}.");
        foreach (var reason in result.Coverage.Reasons)
        {
            log.Warning($"Coverage {Name(result.Coverage.State)}: {reason}.");
        }

        var mismatches = result.Findings.Count(f => f.Kind == FindingKind.RequirementMismatch);
        var bugs = result.Findings.Count(f => f.Kind == FindingKind.Bug);
        var dropped = string.Join(", ", result.Dropped.Select(pair => $"{pair.Key} {pair.Value.ToString(CultureInfo.InvariantCulture)}"));
        log.Info($"Findings: {mismatches} requirement mismatches, {bugs} bugs; dropped {result.DroppedCount}{(dropped.Length > 0 ? $" ({dropped})" : string.Empty)}.");
        log.Info($"Severity: {string.Join(", ", Enum.GetValues<FindingSeverity>().Select(severity => $"{Name(severity)} {result.Findings.Count(f => f.Severity == severity)}"))}; verdict: {(result.Verdict is { } verdict ? Name(verdict.Answer) : "none")}; lenses: {(result.Lenses.Count > 0 ? string.Join(", ", result.Lenses) : "none")}.");
        foreach (var warning in result.InjectionWarnings)
        {
            log.Warning($"Possible prompt injection in the {warning.Source}{(warning.Path is null ? string.Empty : $" at {warning.Path}:{warning.Line}")}.");
        }
        log.Info($"Context hash: {result.ContextHash ?? "none"}.");
        log.Info($"Usage: {result.Usage.InputTokens} input tokens, {result.Usage.OutputTokens} output tokens, {result.Usage.ToolCalls} tool calls, {result.Usage.CharactersRead} characters read.");
    }

    private static string Name<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.KebabCaseLower.ConvertName(value.ToString());
}
