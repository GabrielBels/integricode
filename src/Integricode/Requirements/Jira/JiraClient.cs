using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Integricode.Requirements.Jira;

/// <summary>A Jira Cloud issue's requirement fields; the rich-text ones stay JSON for <see cref="AdfText"/>, null when empty.</summary>
public sealed record JiraIssue(string Summary, JsonElement? Description, JsonElement? AcceptanceCriteria);

/// <summary>
/// Reads Jira Cloud issues through REST API v3 with basic authentication: the e-mail and API token of a read-only
/// account. Every request is a GET, so the review never changes anything in Jira (FR-5).
/// </summary>
public sealed class JiraClient
{
    /// <summary>How long one Jira request may take before the read counts as timed out.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private const int CommentsPageSize = 100;

    private readonly HttpClient http;
    private readonly Uri baseUrl;
    private readonly AuthenticationHeaderValue authorization;

    /// <param name="http">The HTTP client, whose timeout bounds each request; tests pass one built on a fake handler.</param>
    /// <param name="baseUrl">The Jira Cloud site, for example <c>https://example.atlassian.net</c>.</param>
    /// <param name="email">The read-only account's e-mail (<c>JIRA_EMAIL</c>).</param>
    /// <param name="apiToken">The read-only account's API token (<c>JIRA_API_TOKEN</c>).</param>
    public JiraClient(HttpClient http, Uri baseUrl, string email, string apiToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiToken);
        this.http = http;
        this.baseUrl = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
        authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{apiToken}")));
    }

    /// <summary>Reads the issue's summary, description, and, when one is named, the acceptance-criteria field.</summary>
    /// <exception cref="JiraApiException">Jira answered with a non-success status.</exception>
    /// <exception cref="JsonException">The response is not an issue.</exception>
    public async Task<JiraIssue> GetIssueAsync(string key, string? acceptanceCriteriaField, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        string[] fields = acceptanceCriteriaField is null ? ["summary", "description"] : ["summary", "description", acceptanceCriteriaField];
        using var document = await GetAsync($"rest/api/3/issue/{Uri.EscapeDataString(key)}?fields={string.Join(',', fields.Select(Uri.EscapeDataString))}", cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("fields", out var values) || values.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The Jira issue response has no fields object.");
        }

        return new JiraIssue(
            Field(values, "summary") is { ValueKind: JsonValueKind.String } summary ? summary.GetString()! : string.Empty,
            Field(values, "description"),
            acceptanceCriteriaField is null ? null : Field(values, acceptanceCriteriaField));
    }

    /// <summary>
    /// Reads the body of every comment, oldest first, page after page until Jira's total is reached. Only bodies are
    /// read: authors never leave this method.
    /// </summary>
    /// <exception cref="JiraApiException">Jira answered with a non-success status.</exception>
    /// <exception cref="JsonException">A response is not a comment page, or has no total.</exception>
    public async Task<IReadOnlyList<JsonElement>> GetCommentBodiesAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var bodies = new List<JsonElement>();
        while (true)
        {
            var startAt = bodies.Count.ToString(CultureInfo.InvariantCulture);
            using var document = await GetAsync($"rest/api/3/issue/{Uri.EscapeDataString(key)}/comment?startAt={startAt}&maxResults={CommentsPageSize}&orderBy=created", cancellationToken);
            var page = document.RootElement;
            if (page.ValueKind != JsonValueKind.Object || !page.TryGetProperty("comments", out var comments) || comments.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("The Jira comments response has no comments array.");
            }

            var count = 0;
            foreach (var comment in comments.EnumerateArray())
            {
                bodies.Add(comment.ValueKind == JsonValueKind.Object && comment.TryGetProperty("body", out var body) ? body.Clone() : default);
                count++;
            }

            // Without the total, a partial read could not be told from a complete one.
            if (Field(page, "total") is not { ValueKind: JsonValueKind.Number } total || !total.TryGetInt32(out var totalCount))
            {
                throw new JsonException("The Jira comments response has no total.");
            }

            if (count == 0 || bodies.Count >= totalCount)
            {
                return bodies;
            }
        }
    }

    private static JsonElement? Field(JsonElement values, string name) =>
        values.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.Clone() : null;

    private async Task<JsonDocument> GetAsync(string relative, CancellationToken cancellationToken)
    {
        var uri = new Uri(baseUrl, relative);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = authorization;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("integricode", "1"));
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // The body is not included: it can echo request content.
            throw new JiraApiException(uri.AbsolutePath, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}

/// <summary>A Jira API call that returned a non-success status; carries no response body.</summary>
public sealed class JiraApiException(string path, HttpStatusCode statusCode)
    : Exception($"Jira API GET {path} returned {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
