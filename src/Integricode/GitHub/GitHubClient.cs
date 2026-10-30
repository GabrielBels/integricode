using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Integricode.GitHub;

/// <summary>Calls the GitHub REST API with the job's token.</summary>
public sealed partial class GitHubClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient http;
    private readonly Uri apiBaseUrl;
    private readonly string token;

    /// <param name="http">The HTTP client; tests pass one built on a fake handler.</param>
    /// <param name="apiBaseUrl">The REST base URL, for example <c>https://api.github.com/</c>.</param>
    /// <param name="token">The job's <c>GITHUB_TOKEN</c>.</param>
    public GitHubClient(HttpClient http, Uri apiBaseUrl, string token)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(apiBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        this.http = http;
        this.apiBaseUrl = apiBaseUrl.AbsoluteUri.EndsWith('/') ? apiBaseUrl : new Uri(apiBaseUrl.AbsoluteUri + "/");
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
            files.Add(new PullRequestFile(dto.Filename, dto.Status ?? "modified", dto.Patch));
        }

        return files;
    }

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

    private sealed record FileDto(string Filename, string? Status, string? Patch);
}

/// <summary>A GitHub API call that returned a non-success status; carries no response body.</summary>
public sealed class GitHubApiException(string method, string path, HttpStatusCode statusCode)
    : Exception($"GitHub API {method} {path} returned {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
