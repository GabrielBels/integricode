using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Integricode.Requirements.Jira;

/// <summary>
/// Reads a linked Jira Cloud issue (FR-5) as requirement text: its summary, description, acceptance criteria (from the
/// configured field, else the description), and every comment as a numbered clarification, without author names. A
/// Jira failure makes the requirement unreadable with its cause, which the review reports as incomplete coverage.
/// </summary>
public sealed class JiraRequirementSource(JiraClient client, string? acceptanceCriteriaField) : IRequirementSource
{
    public const string BaseUrlOption = "--jira-base-url";
    public const string EmailVariable = "JIRA_EMAIL";
    public const string ApiTokenVariable = "JIRA_API_TOKEN";

    private const string None = "(none)";

    public RequirementKind Kind => RequirementKind.JiraIssue;

    /// <summary>
    /// The Jira source for the given inputs: null when none is set, which leaves Jira keys "not configured"; a source
    /// that names the missing inputs when only some are set; and a source reading Jira when the base URL, e-mail, and
    /// API token are all set.
    /// </summary>
    /// <param name="http">The HTTP client for Jira; its timeout bounds each request.</param>
    /// <param name="baseUrl">The <c>--jira-base-url</c> option.</param>
    /// <param name="acceptanceCriteriaField">The <c>--jira-acceptance-criteria-field</c> option.</param>
    /// <param name="email">The <c>JIRA_EMAIL</c> environment variable.</param>
    /// <param name="apiToken">The <c>JIRA_API_TOKEN</c> environment variable.</param>
    public static IRequirementSource? Create(HttpClient http, Uri? baseUrl, string? acceptanceCriteriaField, string? email, string? apiToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        apiToken = string.IsNullOrWhiteSpace(apiToken) ? null : apiToken.Trim();
        if (baseUrl is not null && email is not null && apiToken is not null)
        {
            return new JiraRequirementSource(new JiraClient(http, baseUrl, email, apiToken), acceptanceCriteriaField);
        }

        if (baseUrl is null && email is null && apiToken is null && acceptanceCriteriaField is null)
        {
            return null;
        }

        var missing = new List<string>();
        if (baseUrl is null)
        {
            missing.Add(BaseUrlOption);
        }

        if (email is null)
        {
            missing.Add(EmailVariable);
        }

        if (apiToken is null)
        {
            missing.Add(ApiTokenVariable);
        }

        return new MissingInputs(missing);
    }

    public async Task<RequirementReadResult> ReadAsync(RequirementLink link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        JiraIssue issue;
        IReadOnlyList<JsonElement> comments;
        try
        {
            issue = await client.GetIssueAsync(link.Id, acceptanceCriteriaField, cancellationToken);
            comments = await client.GetCommentBodiesAsync(link.Id, cancellationToken);
        }
        catch (Exception exception) when (Cause(exception, cancellationToken) is { } cause)
        {
            return RequirementReadResult.Unreadable($"Jira issue {link.Id} unreadable: {cause}");
        }

        var description = Text(issue.Description);
        var acceptanceCriteria = acceptanceCriteriaField is null ? description : Text(issue.AcceptanceCriteria);
        var text = Format(link.Id, issue.Summary, description, acceptanceCriteria, comments);
        return acceptanceCriteria.Length == 0
            ? RequirementReadResult.ReadIncomplete(text, $"Jira issue {link.Id} has no acceptance criteria in {acceptanceCriteriaField ?? "the description"}")
            : RequirementReadResult.Read(text);
    }

    /// <summary>
    /// The requirement text as Markdown sections. Without a configured field the description holds the acceptance
    /// criteria, so it is not repeated; comments are numbered oldest first.
    /// </summary>
    private string Format(string key, string summary, string description, string acceptanceCriteria, IReadOnlyList<JsonElement> comments)
    {
        var text = new StringBuilder()
            .Append("# ").Append(key).Append(": ").Append(summary.Trim()).Append("\n\n")
            .Append(acceptanceCriteriaField is null ? "## Description and Acceptance Criteria" : "## Description").Append("\n\n")
            .Append(OrNone(description));
        if (acceptanceCriteriaField is not null)
        {
            text.Append("\n\n## Acceptance Criteria\n\n").Append(OrNone(acceptanceCriteria));
        }

        text.Append("\n\n## Comments");
        if (comments.Count == 0)
        {
            text.Append("\n\n").Append(None);
        }

        for (var i = 0; i < comments.Count; i++)
        {
            text.Append("\n\n### Comment ").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("\n\n").Append(OrNone(Text(comments[i])));
        }

        return text.ToString();
    }

    /// <summary>Why a Jira read failed, or null for a failure that is not Jira's, such as the review being cancelled.</summary>
    /// <remarks>Only the status or the exception type: Jira's messages can echo request content.</remarks>
    private static string? Cause(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        JiraApiException { StatusCode: HttpStatusCode.Unauthorized } => $"authentication failed (HTTP 401), check {EmailVariable} and {ApiTokenVariable}",
        JiraApiException { StatusCode: HttpStatusCode.Forbidden } => "access denied (HTTP 403) for the Jira account",
        JiraApiException { StatusCode: HttpStatusCode.NotFound } => "not found or not visible to the Jira account (HTTP 404)",
        JiraApiException failed => $"HTTP {((int)failed.StatusCode).ToString(CultureInfo.InvariantCulture)}",
        OperationCanceledException when !cancellationToken.IsCancellationRequested => "the request timed out",
        HttpRequestException => "Jira unreachable (HttpRequestException)",
        JsonException => "invalid response",
        _ => null,
    };

    /// <summary>A field's text: rich text converted from ADF, plain text as written, anything else empty.</summary>
    private static string Text(JsonElement? value) => value switch
    {
        { ValueKind: JsonValueKind.Object } adf => AdfText.ToText(adf).Trim(),
        { ValueKind: JsonValueKind.String } plain => plain.GetString()!.Trim(),
        _ => string.Empty,
    };

    private static string OrNone(string text) => text.Length == 0 ? None : text;

    /// <summary>The Jira source when only some of its inputs are set: each Jira key is unreadable, naming what is missing.</summary>
    private sealed class MissingInputs(IReadOnlyList<string> missing) : IRequirementSource
    {
        public RequirementKind Kind => RequirementKind.JiraIssue;

        public Task<RequirementReadResult> ReadAsync(RequirementLink link, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(link);
            return Task.FromResult(RequirementReadResult.Unreadable($"Jira source not configured for {link.Id}: missing {string.Join(", ", missing)}"));
        }
    }
}
