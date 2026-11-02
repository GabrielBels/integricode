using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Integricode.Requirements.Jira;

namespace Integricode.Tests.Fakes;

/// <summary>
/// An in-memory Jira Cloud site for one issue: serves the requested fields and the comments a page at a time, answers
/// 401 to other credentials and 405 to anything but GET, and records every request. Nothing reaches a network, and
/// every value is synthetic.
/// </summary>
public sealed class FakeJira : HttpMessageHandler
{
    public const string BaseUrl = "https://shop.atlassian.test/";
    public const string Email = "integricode-reader@example.test";
    public const string ApiToken = "synthetic-jira-token-3c9a";
    public const string AuthorName = "Synthetic Author Name";
    public const string AuthorEmail = "synthetic.author@example.test";

    public static readonly string Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Email}:{ApiToken}"));

    public string Key { get; set; } = "SHOP-12";

    public string Summary { get; set; } = "Cart totals apply discounts";

    public JsonNode? Description { get; set; } = Adf.Doc(Adf.Paragraph("The cart shows a total."));

    /// <summary>Custom fields by ID; a field is returned only when requested, as Jira does.</summary>
    public Dictionary<string, JsonNode?> CustomFields { get; } = [];

    /// <summary>The comment bodies (ADF), oldest first; each is served with a synthetic author.</summary>
    public List<JsonNode> Comments { get; } = [];

    /// <summary>The most comments one page holds, whatever <c>maxResults</c> asks for.</summary>
    public int MaxPageSize { get; set; } = 2;

    public HttpStatusCode? IssueStatus { get; set; }

    public HttpStatusCode? CommentsStatus { get; set; }

    /// <summary>When set, every request waits until it is cancelled, as a Jira that never answers.</summary>
    public bool Hang { get; set; }

    public List<RecordedRequest> Requests { get; } = [];

    public HttpClient HttpClient(TimeSpan? timeout = null) =>
        new(this, disposeHandler: false) { Timeout = timeout ?? JiraClient.DefaultTimeout };

    public JiraClient Client(TimeSpan? timeout = null) => new(HttpClient(timeout), new Uri(BaseUrl), Email, ApiToken);

    public FakeJira AddComment(params JsonNode[] blocks)
    {
        Comments.Add(Adf.Doc(blocks));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body, request.Headers.Authorization?.ToString()));
        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        var path = request.RequestUri!.AbsolutePath;
        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
        var issue = $"/rest/api/3/issue/{Key}";
        var response = request.Method != HttpMethod.Get ? new HttpResponseMessage(HttpStatusCode.MethodNotAllowed)
            : request.Headers.Authorization?.ToString() != Authorization ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : path == issue ? IssueStatus is { } issueStatus ? new HttpResponseMessage(issueStatus) : Json(IssueJson(query["fields"]))
            : path == issue + "/comment" ? CommentsStatus is { } commentsStatus ? new HttpResponseMessage(commentsStatus) : Json(CommentsJson(Number(query["startAt"]), Number(query["maxResults"])))
            : new HttpResponseMessage(HttpStatusCode.NotFound);
        response.RequestMessage = request;
        return response;
    }

    private JsonObject IssueJson(string? requested)
    {
        var all = new Dictionary<string, JsonNode?>(CustomFields)
        {
            ["summary"] = Summary,
            ["description"] = Description?.DeepClone(),
            ["reporter"] = Person(),
        };
        var fields = new JsonObject();
        foreach (var name in (requested ?? string.Join(',', all.Keys)).Split(','))
        {
            if (all.TryGetValue(name, out var value))
            {
                fields[name] = value?.DeepClone();
            }
        }

        return new JsonObject { ["id"] = "10100", ["key"] = Key, ["fields"] = fields };
    }

    private JsonObject CommentsJson(int startAt, int maxResults)
    {
        var page = Comments.Skip(startAt).Take(Math.Min(maxResults, MaxPageSize)).Select((comment, i) => (JsonNode)new JsonObject
        {
            ["id"] = (20000 + startAt + i).ToString(CultureInfo.InvariantCulture),
            ["author"] = Person(),
            ["updateAuthor"] = Person(),
            ["body"] = comment.DeepClone(),
            ["created"] = "2026-10-01T10:00:00.000+0000",
        });
        return new JsonObject
        {
            ["startAt"] = startAt,
            ["maxResults"] = MaxPageSize,
            ["total"] = Comments.Count,
            ["comments"] = new JsonArray([.. page]),
        };
    }

    private static JsonObject Person() => new()
    {
        ["accountId"] = "synthetic-account-0001",
        ["displayName"] = AuthorName,
        ["emailAddress"] = AuthorEmail,
    };

    private static int Number(string? text) => int.Parse(text ?? "0", CultureInfo.InvariantCulture);

    private static HttpResponseMessage Json(JsonNode node) =>
        new(HttpStatusCode.OK) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };
}

/// <summary>Builds synthetic Atlassian Document Format nodes.</summary>
public static class Adf
{
    public static JsonObject Doc(params JsonNode[] content) => new()
    {
        ["type"] = "doc",
        ["version"] = 1,
        ["content"] = new JsonArray([.. content]),
    };

    public static JsonObject Paragraph(params string[] texts) => Node("paragraph", [.. texts.Select(Text)]);

    public static JsonObject Text(string text) => new() { ["type"] = "text", ["text"] = text };

    public static JsonObject BulletList(params string[] items) =>
        Node("bulletList", [.. items.Select(item => Node("listItem", Paragraph(item)))]);

    public static JsonObject Node(string type, params JsonNode[] content) => new()
    {
        ["type"] = type,
        ["content"] = new JsonArray([.. content]),
    };
}
