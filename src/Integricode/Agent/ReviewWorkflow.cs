using System.Globalization;
using System.Text;
using System.Text.Json;
using Integricode.Domain;
using Integricode.GitHub;
using Integricode.Requirements;
using Integricode.Validation;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Integricode.Agent;

/// <summary>What the review starts from: the pull request and its files, read from GitHub.</summary>
public sealed record ReviewRequest(string Repository, PullRequest PullRequest, IReadOnlyList<PullRequestFile> Files);

/// <summary>
/// The fixed input of the review: the requirement, the diff map, the patches sent to the model (null when one was left
/// out), and the prompt holding them.
/// </summary>
public sealed record ReviewContext(
    ReviewRequest Request,
    RequirementLink? Link,
    string? RequirementText,
    IReadOnlyList<string> MissingReasons,
    DiffMap DiffMap,
    IReadOnlyList<DiffFile> SentDiff,
    string Prompt);

/// <summary>The review agent's answer, or why there is none, with the model usage.</summary>
public sealed record ReviewOutcome(ReviewContext Context, AgentReview? Review, string? FailureReason, long InputTokens, long OutputTokens);

/// <summary>
/// The review as a Microsoft Agent Framework workflow (ADR-004): a context executor gathers the fixed input, a review
/// executor runs a <see cref="ChatClientAgent"/> with the read-only repository tools, and a validation executor applies
/// the provenance check and yields the typed <see cref="ReviewResult"/>.
/// </summary>
public sealed class ReviewWorkflow
{
    public const string NoLinkReason =
        "no requirement linked: add a REQ ID (REQ-NNN) or a Jira key to the pull request title, branch name, or description";

    internal const string RepairRequest =
        "Your previous reply was not one JSON object matching the required schema. Reply again with only that JSON object.";

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    private static readonly Lazy<string> Instructions = new(LoadInstructions);

    private readonly IChatClient chatClient;
    private readonly PathGuard guard;
    private readonly RepositoryTools tools;
    private readonly RequirementSources requirementSources;

    public ReviewWorkflow(IChatClient chatClient, PathGuard guard, ContextBudget budget, RequirementSources requirementSources)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(requirementSources);
        this.chatClient = chatClient;
        this.guard = guard;
        this.requirementSources = requirementSources;
        tools = new RepositoryTools(guard, budget);
    }

    public RepositoryTools Tools => tools;

    /// <exception cref="InvalidOperationException">An executor failed unexpectedly or the workflow yielded no result.</exception>
    public async Task<ReviewResult> RunAsync(ReviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = ((Func<ReviewRequest, IWorkflowContext, CancellationToken, ValueTask<ReviewContext>>)GatherContextAsync).BindAsExecutor("context");
        var review = ((Func<ReviewContext, IWorkflowContext, CancellationToken, ValueTask<ReviewOutcome>>)ReviewAsync).BindAsExecutor("review");
        var validation = ((Func<ReviewOutcome, IWorkflowContext, CancellationToken, ValueTask<ReviewResult>>)ValidateAsync).BindAsExecutor("validation");
        var workflow = new WorkflowBuilder(context)
            .AddEdge(context, review)
            .AddEdge(review, validation)
            .WithOutputFrom(validation)
            .Build();

        await using var run = await InProcessExecution.RunAsync(workflow, request, cancellationToken: cancellationToken);
        foreach (var workflowEvent in run.NewEvents)
        {
            switch (workflowEvent)
            {
                case WorkflowOutputEvent output when output.Is<ReviewResult>(out var result):
                    return result;
                case ExecutorFailedEvent failed:
                    throw new InvalidOperationException($"The review workflow executor '{failed.ExecutorId}' failed.", failed.Data);
                case WorkflowErrorEvent error:
                    throw new InvalidOperationException("The review workflow failed.", error.Exception);
            }
        }

        throw new InvalidOperationException("The review workflow ended without a result.");
    }

    private async ValueTask<ReviewContext> GatherContextAsync(ReviewRequest request, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var pullRequest = request.PullRequest;
        var link = RequirementIdentifier.Identify(pullRequest.Title, pullRequest.HeadRef, pullRequest.Body);
        var missing = new List<string>();
        string? requirementText = null;
        if (link is null)
        {
            missing.Add(NoLinkReason);
        }
        else
        {
            var read = await requirementSources.ReadAsync(link, cancellationToken);
            requirementText = read.Text;
            if (read.UnreadableReason is { } reason)
            {
                missing.Add(reason);
            }
        }

        var diffMap = DiffMap.Build(request.Files);
        var sentDiff = request.Files.Select(file => new DiffFile(file.Path, SendablePatch(file))).ToList();
        var prompt = BuildPrompt(link, requirementText, missing, request.Files, sentDiff, diffMap);
        return new ReviewContext(request, link, requirementText, missing, diffMap, sentDiff, prompt);
    }

    /// <summary>
    /// The patch when it may go to the model: not excluded, and within the character budget, which it then uses up, so a
    /// large diff makes the coverage incomplete instead of overflowing the model's context.
    /// </summary>
    private string? SendablePatch(PullRequestFile file) =>
        file.Patch is not null && !guard.IsExcluded(file.Path) && tools.Budget.TryConsume(file.Patch.Length)
            ? file.Patch
            : null;

    private async ValueTask<ReviewOutcome> ReviewAsync(ReviewContext context, IWorkflowContext workflowContext, CancellationToken cancellationToken)
    {
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "integricode-reviewer",
            ChatOptions = new ChatOptions
            {
                Instructions = Instructions.Value,
                Tools = tools.AsAITools(),
                ResponseFormat = ChatResponseFormat.ForJsonSchema<AgentReview>(JsonOptions, "review", "The review findings."),
            },
        });

        long inputTokens = 0;
        long outputTokens = 0;
        try
        {
            var session = await agent.CreateSessionAsync(cancellationToken);
            foreach (var message in new[] { context.Prompt, RepairRequest })
            {
                var response = await agent.RunAsync(message, session, cancellationToken: cancellationToken);
                inputTokens += response.Usage?.InputTokenCount ?? 0;
                outputTokens += response.Usage?.OutputTokenCount ?? 0;
                if (TryParseReview(response.Text, out var review))
                {
                    return new ReviewOutcome(context, review, null, inputTokens, outputTokens);
                }
            }

            return new ReviewOutcome(context, null, "the model output did not match the result schema after one repair request", inputTokens, outputTokens);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Only the status or the exception type: provider messages can echo request content.
            var reason = ChatClientFactory.StatusOf(exception) is { } status
                ? $"model provider error (HTTP {status.ToString(CultureInfo.InvariantCulture)})"
                : $"model provider error ({exception.GetType().Name})";
            return new ReviewOutcome(context, null, reason, inputTokens, outputTokens);
        }
    }

    private ValueTask<ReviewResult> ValidateAsync(ReviewOutcome outcome, IWorkflowContext workflowContext, CancellationToken cancellationToken)
    {
        var context = outcome.Context;
        var request = context.Request;
        var validation = outcome.Review is null
            ? new ValidationOutcome([], new Dictionary<string, int>())
            : new FindingValidator(context.DiffMap, context.RequirementText, CountHeadLines).Validate(outcome.Review.Findings);

        var reasons = context.MissingReasons.ToList();
        if (tools.Budget.ReachedReason is { } budgetReason)
        {
            reasons.Add(budgetReason);
        }

        var coverage = outcome.FailureReason is not null ? Coverage.Failed(outcome.FailureReason)
            : reasons.Count > 0 ? Coverage.Incomplete(reasons)
            : Coverage.Complete;
        var snapshot = new ContextSnapshot(
            request.PullRequest.HeadSha,
            context.SentDiff,
            context.RequirementText,
            tools.Chunks);

        return ValueTask.FromResult(new ReviewResult
        {
            Repository = request.Repository,
            PullRequestNumber = request.PullRequest.Number,
            HeadSha = request.PullRequest.HeadSha,
            ContextHash = snapshot.ComputeHash(),
            Requirement = context.Link,
            Coverage = coverage,
            Findings = validation.Findings,
            Dropped = validation.Dropped,
            Usage = new Usage(outcome.InputTokens, outcome.OutputTokens, tools.Budget.ToolCalls, tools.Budget.CharactersRead),
        });
    }

    private int? CountHeadLines(string path)
    {
        var check = guard.Check(path, applyExclusions: false);
        return check.Allowed && File.Exists(check.FullPath) && TextFile.TryRead(check.FullPath, out var text)
            ? TextFile.SplitLines(text).Count
            : null;
    }

    private string BuildPrompt(
        RequirementLink? link,
        string? requirementText,
        IReadOnlyList<string> missing,
        IReadOnlyList<PullRequestFile> files,
        IReadOnlyList<DiffFile> sentDiff,
        DiffMap diffMap)
    {
        var prompt = new StringBuilder();
        if (requirementText is not null)
        {
            prompt.Append("<requirement id=\"").Append(link!.Id).Append("\">\n").Append(requirementText).Append("\n</requirement>\n\n");
        }
        else
        {
            prompt.Append("<requirement unavailable=\"true\">\n").Append(string.Join("\n", missing))
                .Append("\nNo requirement text is available: report only bugs.\n</requirement>\n\n");
        }

        prompt.Append("<diff>\n");
        foreach (var (file, sent) in files.Zip(sentDiff))
        {
            var changed = diffMap.Get(file.Path)?.ChangedLines ?? new SortedSet<int>();
            var patch = sent.Patch
                ?? (guard.IsExcluded(file.Path) ? "(not included: excluded from the review context)"
                    : file.Patch is null ? "(no patch: binary or too large)"
                    : "(not included: over the context budget)");
            prompt.Append("<file-diff path=\"").Append(file.Path).Append("\" status=\"").Append(file.Status)
                .Append("\" changed-lines=\"").Append(string.Join(',', changed)).Append("\">\n")
                .Append(patch)
                .Append("\n</file-diff>\n");
        }

        prompt.Append("</diff>\n\n<changed-files>\n");
        foreach (var file in files.Where(file => file.Status != "removed"))
        {
            var content = tools.ReadChangedFile(file.Path);
            prompt.Append("<file path=\"").Append(file.Path).Append("\">\n")
                .Append(content ?? "(not included: excluded, binary, missing, or over the context budget)")
                .Append("\n</file>\n");
        }

        return prompt.Append("</changed-files>\n").ToString();
    }

    /// <summary>Parses the model's reply, tolerating surrounding prose or a code fence; requires a findings list.</summary>
    internal static bool TryParseReview(string? text, out AgentReview review)
    {
        review = new AgentReview([]);
        var start = text?.IndexOf('{') ?? -1;
        var end = text?.LastIndexOf('}') ?? -1;
        if (start < 0 || end <= start)
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AgentReview>(text![start..(end + 1)], JsonOptions);
            if (parsed?.Findings is null)
            {
                return false;
            }

            review = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string LoadInstructions()
    {
        using var stream = typeof(ReviewWorkflow).Assembly.GetManifestResourceStream("Integricode.Agent.ReviewInstructions.md")
            ?? throw new InvalidOperationException("The embedded review instructions are missing.");
        using var reader = new StreamReader(stream);
        var schema = AIJsonUtilities.CreateJsonSchema(typeof(AgentReview), serializerOptions: JsonOptions);
        return reader.ReadToEnd().TrimEnd() + "\n\n" + schema.GetRawText() + "\n";
    }
}
