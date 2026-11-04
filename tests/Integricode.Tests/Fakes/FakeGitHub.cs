using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Integricode.GitHub;

namespace Integricode.Tests.Fakes;

public sealed class FakeComment
{
    public required long Id { get; init; }

    public required string NodeId { get; init; }

    public required string Body { get; set; }

    public required string UserLogin { get; init; }

    public string? Path { get; init; }

    public int? Line { get; init; }
}

public sealed record FakeReview(string CommitId, string Event, string? Body, IReadOnlyList<FakeComment> Comments);

/// <summary>
/// An in-memory GitHub for one pull request: serves the pull request and its files, stores issue comments, review
/// comments, and reviews, and records GraphQL minimizations. Every request is recorded; nothing reaches a network.
/// </summary>
public sealed partial class FakeGitHub : HttpMessageHandler
{
    public const string Repository = "octo/shop";
    public const string Bot = "github-actions[bot]";

    private long nextId = 1000;

    public int Number { get; set; } = 7;

    public string Title { get; set; } = "REQ-001: cart totals";

    public string? Body { get; set; } = "Implements the cart totals.";

    public string HeadRef { get; set; } = "feat/cart";

    public string HeadSha { get; set; } = "1111111111111111111111111111111111111111";

    public List<PullRequestFile> Files { get; } = [];

    public List<string> CommitMessages { get; } = [];

    public List<FakeComment> IssueComments { get; } = [];

    public List<FakeComment> ReviewComments { get; } = [];

    public List<FakeReview> Reviews { get; } = [];

    public List<string> Minimized { get; } = [];

    public List<RecordedRequest> Requests { get; } = [];

    public HttpStatusCode? PullRequestStatus { get; set; }

    public GitHubClient Client() => new(new HttpClient(this), new Uri("https://api.github.test/"), "fake-token");

    public FakeComment AddIssueComment(string body, string user = Bot) => Add(IssueComments, body, user, null, null);

    public FakeComment AddReviewComment(string body, string path, int line, string user = Bot) => Add(ReviewComments, body, user, path, line);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body, request.Headers.Authorization?.ToString()));
        var path = request.RequestUri!.AbsolutePath;
        var pulls = $"/repos/{Repository}/pulls/{Number}";
        var issues = $"/repos/{Repository}/issues/{Number}";
        var response = (request.Method.Method, path) switch
        {
            ("GET", _) when path == pulls => PullRequestStatus is { } status ? new HttpResponseMessage(status) : Json(PullRequestJson()),
            ("GET", _) when path == pulls + "/files" => Json(new JsonArray([.. Files.Select(FileJson)])),
            ("GET", _) when path == pulls + "/commits" => Json(new JsonArray([.. CommitMessages.Select(CommitJson)])),
            ("GET", _) when path == issues + "/comments" => Json(new JsonArray([.. IssueComments.Select(CommentJson)])),
            ("POST", _) when path == issues + "/comments" => Json(CommentJson(AddIssueComment(Read(body)["body"]!.GetValue<string>())), HttpStatusCode.Created),
            ("PATCH", _) when IssueCommentPath().Match(path) is { Success: true } match => UpdateIssueComment(long.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture), Read(body)),
            ("GET", _) when path == pulls + "/comments" => Json(new JsonArray([.. ReviewComments.Select(CommentJson)])),
            ("POST", _) when path == pulls + "/reviews" => CreateReview(Read(body)),
            ("POST", "/graphql") => GraphQl(Read(body)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        response.RequestMessage = request;
        return response;
    }

    private FakeComment Add(List<FakeComment> list, string body, string user, string? path, int? line)
    {
        var id = nextId++;
        var comment = new FakeComment { Id = id, NodeId = $"NODE_{id}", Body = body, UserLogin = user, Path = path, Line = line };
        list.Add(comment);
        return comment;
    }

    private HttpResponseMessage UpdateIssueComment(long id, JsonNode body)
    {
        var comment = IssueComments.SingleOrDefault(c => c.Id == id);
        if (comment is null)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        comment.Body = body["body"]!.GetValue<string>();
        return Json(CommentJson(comment));
    }

    private HttpResponseMessage CreateReview(JsonNode body)
    {
        var comments = body["comments"]?.AsArray()
            .Select(c => AddReviewComment(c!["body"]!.GetValue<string>(), c["path"]!.GetValue<string>(), c["line"]!.GetValue<int>()))
            .ToList() ?? [];
        Reviews.Add(new FakeReview(body["commit_id"]!.GetValue<string>(), body["event"]!.GetValue<string>(), body["body"]?.GetValue<string>(), comments));
        return Json(new JsonObject { ["id"] = nextId++ });
    }

    private HttpResponseMessage GraphQl(JsonNode body)
    {
        var query = body["query"]!.GetValue<string>();
        if (query.Contains("minimizeComment", StringComparison.Ordinal))
        {
            Minimized.Add(body["variables"]!["id"]!.GetValue<string>());
            return Json(new JsonObject { ["data"] = new JsonObject { ["minimizeComment"] = new JsonObject { ["minimizedComment"] = new JsonObject { ["isMinimized"] = true } } } });
        }

        if (query.Contains("nodes(ids:", StringComparison.Ordinal))
        {
            var nodes = body["variables"]!["ids"]!.AsArray()
                .Select(id => (JsonNode?)new JsonObject { ["id"] = id!.GetValue<string>(), ["isMinimized"] = Minimized.Contains(id.GetValue<string>()) });
            return Json(new JsonObject { ["data"] = new JsonObject { ["nodes"] = new JsonArray([.. nodes]) } });
        }

        return Json(new JsonObject { ["errors"] = new JsonArray(new JsonObject { ["message"] = "unknown query" }) });
    }

    private JsonObject PullRequestJson() => new()
    {
        ["number"] = Number,
        ["title"] = Title,
        ["body"] = Body,
        ["draft"] = false,
        ["head"] = new JsonObject { ["ref"] = HeadRef, ["sha"] = HeadSha, ["repo"] = new JsonObject { ["full_name"] = Repository } },
        ["base"] = new JsonObject { ["ref"] = "main", ["sha"] = "0000000000000000000000000000000000000000", ["repo"] = new JsonObject { ["full_name"] = Repository } },
    };

    private static JsonObject FileJson(PullRequestFile file) => new()
    {
        ["filename"] = file.Path,
        ["status"] = file.Status,
        ["patch"] = file.Patch,
        ["additions"] = file.Additions,
    };

    private static JsonObject CommitJson(string message) => new()
    {
        ["sha"] = "2222222222222222222222222222222222222222",
        ["commit"] = new JsonObject { ["message"] = message },
    };

    private static JsonObject CommentJson(FakeComment comment) => new()
    {
        ["id"] = comment.Id,
        ["node_id"] = comment.NodeId,
        ["body"] = comment.Body,
        ["user"] = new JsonObject { ["login"] = comment.UserLogin },
        ["path"] = comment.Path,
        ["line"] = comment.Line,
    };

    private static JsonNode Read(string? body) => JsonNode.Parse(body ?? "{}")!;

    private static HttpResponseMessage Json(JsonNode node, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };

    [GeneratedRegex(@"^/repos/octo/shop/issues/comments/(?<id>\d+)$")]
    private static partial Regex IssueCommentPath();
}
