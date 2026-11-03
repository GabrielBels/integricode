using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Integricode.GitHub;

/// <summary>Calls the GitHub REST API with the job's token.</summary>
public sealed partial class GitHubClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private const string MinimizeOutdated =
        "mutation($id: ID!) { minimizeComment(input: {subjectId: $id, classifier: OUTDATED}) { minimizedComment { isMinimized } } }";

    private const string MinimizedState =
        "query($ids: [ID!]!) { nodes(ids: $ids) { ... on PullRequestReviewComment { id isMinimized } } }";

    private const int MaxNodesPerQuery = 100;

    private readonly HttpClient http;
    private readonly Uri apiBaseUrl;
    private readonly Uri graphqlUrl;
    private readonly string token;

    /// <param name="http">The HTTP client; tests pass one built on a fake handler.</param>
    /// <param name="apiBaseUrl">The REST base URL, for example <c>https://api.github.com/</c>.</param>
    /// <param name="token">The job's <c>GITHUB_TOKEN</c>.</param>
    /// <param name="graphqlUrl">The GraphQL URL; defaults to <c>graphql</c> under <paramref name="apiBaseUrl"/>.</param>
    public GitHubClient(HttpClient http, Uri apiBaseUrl, string token, Uri? graphqlUrl = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(apiBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        this.http = http;
        this.apiBaseUrl = apiBaseUrl.AbsoluteUri.EndsWith('/') ? apiBaseUrl : new Uri(apiBaseUrl.AbsoluteUri + "/");
        this.graphqlUrl = graphqlUrl ?? new Uri(this.apiBaseUrl, "graphql");
        this.token = token;
    }

    public async Task<PullRequest> GetPullRequestAsync(string repository, int number, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, Relative($"repos/{repository}/pulls/{number}"), null, cancellationToken);
        var dto = await ReadAsync<PullRequestDto>(response, cancellationToken);
        return new PullRequest(
            dto.Number,
            dto.Title ?? string.Empty,
            dto.Body,
            dto.Head.Ref,
            dto.Head.Sha,
            dto.Base.Repo?.FullName ?? repository,
            dto.Head.Repo?.FullName,
            dto.Draft);
    }

    /// <summary>Lists every file of the pull request, following the <c>Link</c> header across pages.</summary>
    public async Task<IReadOnlyList<PullRequestFile>> GetPullRequestFilesAsync(string repository, int number, CancellationToken cancellationToken)
    {
        var files = new List<PullRequestFile>();
        await foreach (var dto in GetPagesAsync<FileDto>(Relative($"repos/{repository}/pulls/{number}/files?per_page=100"), cancellationToken))
        {
            files.Add(new PullRequestFile(dto.Filename, dto.Status ?? "modified", dto.Patch, dto.Additions));
        }

        return files;
    }

    public Task<IReadOnlyList<GitHubComment>> ListIssueCommentsAsync(string repository, int number, CancellationToken cancellationToken) =>
        ListCommentsAsync(Relative($"repos/{repository}/issues/{number}/comments?per_page=100"), cancellationToken);

    public Task<IReadOnlyList<GitHubComment>> ListReviewCommentsAsync(string repository, int number, CancellationToken cancellationToken) =>
        ListCommentsAsync(Relative($"repos/{repository}/pulls/{number}/comments?per_page=100"), cancellationToken);

    public async Task<GitHubComment> CreateIssueCommentAsync(string repository, int number, string body, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(new CommentBody(body), options: Json);
        using var response = await SendAsync(HttpMethod.Post, Relative($"repos/{repository}/issues/{number}/comments"), content, cancellationToken);
        return ToComment(await ReadAsync<CommentDto>(response, cancellationToken));
    }

    public async Task UpdateIssueCommentAsync(string repository, long commentId, string body, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(new CommentBody(body), options: Json);
        using var response = await SendAsync(HttpMethod.Patch, Relative($"repos/{repository}/issues/comments/{commentId}"), content, cancellationToken);
    }

    /// <summary>
    /// Creates one pull-request review holding the inline comments. Its event is always <c>COMMENT</c>: Integricode never
    /// approves or requests changes (FR-15).
    /// </summary>
    public async Task CreateCommentReviewAsync(string repository, int number, string commitId, string body, IReadOnlyList<InlineComment> comments, CancellationToken cancellationToken)
    {
        var review = new ReviewBody(commitId, body, "COMMENT", [.. comments.Select(c => new ReviewCommentBody(c.Path, c.Line, "RIGHT", c.Body, c.StartLine, c.StartLine is null ? null : "RIGHT"))]);
        using var content = JsonContent.Create(review, options: Json);
        using var response = await SendAsync(HttpMethod.Post, Relative($"repos/{repository}/pulls/{number}/reviews"), content, cancellationToken);
    }

    /// <summary>Minimizes a comment as outdated through GraphQL <c>minimizeComment</c>.</summary>
    /// <exception cref="GitHubApiException">The call failed or GraphQL returned errors.</exception>
    public async Task MinimizeAsOutdatedAsync(string nodeId, CancellationToken cancellationToken)
    {
        await GraphQlAsync<IdVariables, MinimizeData>(new GraphQlRequest<IdVariables>(MinimizeOutdated, new IdVariables(nodeId)), cancellationToken);
    }

    /// <summary>The node IDs, among the given review comments, that are minimized.</summary>
    /// <exception cref="GitHubApiException">The call failed or GraphQL returned errors.</exception>
    public async Task<IReadOnlySet<string>> GetMinimizedAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        var minimized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in nodeIds.Chunk(MaxNodesPerQuery))
        {
            var data = await GraphQlAsync<IdsVariables, NodesData>(new GraphQlRequest<IdsVariables>(MinimizedState, new IdsVariables(chunk)), cancellationToken);
            minimized.UnionWith(data.Nodes.Where(node => node is { IsMinimized: true, Id: not null }).Select(node => node!.Id!));
        }

        return minimized;
    }

    private async Task<TData> GraphQlAsync<TVariables, TData>(GraphQlRequest<TVariables> request, CancellationToken cancellationToken)
        where TData : class
    {
        using var content = JsonContent.Create(request, options: Json);
        using var response = await SendAsync(HttpMethod.Post, graphqlUrl, content, cancellationToken);
        var result = await ReadAsync<GraphQlResponse<TData>>(response, cancellationToken);
        return result.Errors is { Count: > 0 } || result.Data is null
            ? throw new GitHubApiException("POST", graphqlUrl.AbsolutePath, HttpStatusCode.UnprocessableEntity)
            : result.Data;
    }

    private async Task<IReadOnlyList<GitHubComment>> ListCommentsAsync(Uri firstPage, CancellationToken cancellationToken)
    {
        var comments = new List<GitHubComment>();
        await foreach (var dto in GetPagesAsync<CommentDto>(firstPage, cancellationToken))
        {
            comments.Add(ToComment(dto));
        }

        return comments;
    }

    private static GitHubComment ToComment(CommentDto dto) =>
        new(dto.Id, dto.NodeId, dto.Body ?? string.Empty, dto.User?.Login ?? string.Empty, dto.Path, dto.Line);

    private async IAsyncEnumerable<T> GetPagesAsync<T>(Uri firstPage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Uri? next = firstPage;
        while (next is not null)
        {
            using var response = await SendAsync(HttpMethod.Get, next, null, cancellationToken);
            foreach (var item in await ReadAsync<List<T>>(response, cancellationToken))
            {
                yield return item;
            }

            next = NextPage(response);
        }
    }

    private Uri Relative(string path) => new(apiBaseUrl, path);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("integricode", "1"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // The body is not included: it can echo request content.
            var status = response.StatusCode;
            response.Dispose();
            throw new GitHubApiException(method.Method, uri.AbsolutePath, status);
        }

        return response;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, Json, cancellationToken)
            ?? throw new InvalidDataException($"GitHub returned an empty body for {response.RequestMessage?.RequestUri?.AbsolutePath}.");
    }

    private static Uri? NextPage(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            var match = NextLink().Match(value);
            if (match.Success)
            {
                return new Uri(match.Groups["url"].Value);
            }
        }

        return null;
    }

    [GeneratedRegex("<(?<url>[^>]+)>;\\s*rel=\"next\"")]
    private static partial Regex NextLink();

    private sealed record PullRequestDto(int Number, string? Title, string? Body, bool Draft, RefDto Head, RefDto Base);

    private sealed record RefDto(string Ref, string Sha, RepoDto? Repo);

    private sealed record RepoDto(string FullName);

    private sealed record FileDto(string Filename, string? Status, string? Patch, int? Additions);

    private sealed record CommentDto(long Id, string NodeId, string? Body, UserDto? User, string? Path, int? Line);

    private sealed record UserDto(string Login);

    private sealed record CommentBody(string Body);

    private sealed record ReviewBody(string CommitId, string Body, string Event, IReadOnlyList<ReviewCommentBody> Comments);

    private sealed record ReviewCommentBody(
        string Path,
        int Line,
        string Side,
        string Body,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? StartLine,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StartSide);

    // GraphQL names are camelCase, so they are spelled out instead of following the REST snake_case policy.
    private sealed record GraphQlRequest<TVariables>(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("variables")] TVariables Variables);

    private sealed record IdVariables([property: JsonPropertyName("id")] string Id);

    private sealed record IdsVariables([property: JsonPropertyName("ids")] IReadOnlyList<string> Ids);

    private sealed record GraphQlResponse<TData>(
        [property: JsonPropertyName("data")] TData? Data,
        [property: JsonPropertyName("errors")] IReadOnlyList<JsonElement>? Errors);

    private sealed record MinimizeData([property: JsonPropertyName("minimizeComment")] JsonElement MinimizeComment);

    private sealed record NodesData([property: JsonPropertyName("nodes")] IReadOnlyList<MinimizableNode?> Nodes);

    private sealed record MinimizableNode(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("isMinimized")] bool IsMinimized);
}

/// <summary>An issue or review comment; <see cref="Path"/> and <see cref="Line"/> are set for review comments.</summary>
public sealed record GitHubComment(long Id, string NodeId, string Body, string UserLogin, string? Path, int? Line);

/// <summary>An inline comment on a right-side line of the pull request's diff, or on lines <see cref="StartLine"/> to <see cref="Line"/>.</summary>
public sealed record InlineComment(string Path, int Line, string Body, int? StartLine = null);

/// <summary>A GitHub API call that returned a non-success status; carries no response body.</summary>
public sealed class GitHubApiException(string method, string path, HttpStatusCode statusCode)
    : Exception($"GitHub API {method} {path} returned {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
