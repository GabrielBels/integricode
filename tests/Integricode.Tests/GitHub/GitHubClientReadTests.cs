using System.Net;
using Integricode.GitHub;
using Integricode.Tests.Fakes;

namespace Integricode.Tests.GitHub;

public class GitHubClientReadTests
{
    private static GitHubClient Client(FakeHttpHandler handler) =>
        new(new HttpClient(handler), new Uri("https://api.github.test"), "test-token");

    [Fact]
    public async Task Reads_the_pull_request_fields()
    {
        var handler = new FakeHttpHandler().OnJson(HttpMethod.Get, "/repos/octo/shop/pulls/7", """
            {"number": 7, "title": "REQ-001: cart", "body": "Implements the cart.", "draft": true,
             "head": {"ref": "feat/cart", "sha": "abc123", "repo": {"full_name": "fork/shop"}},
             "base": {"ref": "main", "sha": "def456", "repo": {"full_name": "octo/shop"}}}
            """);

        var pullRequest = await Client(handler).GetPullRequestAsync("octo/shop", 7, CancellationToken.None);

        Assert.Equal(new PullRequest(7, "REQ-001: cart", "Implements the cart.", "feat/cart", "abc123", "octo/shop", "fork/shop", true), pullRequest);
        Assert.Equal("Bearer test-token", handler.Requests.Single().Authorization);
    }

    [Fact]
    public async Task Follows_pagination_and_keeps_files_without_a_patch()
    {
        var handler = new FakeHttpHandler()
            .OnJson(HttpMethod.Get, "/repos/octo/shop/pulls/7/files?per_page=100", """
                [{"filename": "src/A.cs", "status": "modified", "patch": "@@ -1 +1 @@\n-a\n+b"}]
                """, link: "<https://api.github.test/repos/octo/shop/pulls/7/files?per_page=100&page=2>; rel=\"next\", <https://api.github.test/repos/octo/shop/pulls/7/files?per_page=100&page=2>; rel=\"last\"")
            .OnJson(HttpMethod.Get, "/repos/octo/shop/pulls/7/files?per_page=100&page=2", """
                [{"filename": "img/logo.png", "status": "added"}]
                """);

        var files = await Client(handler).GetPullRequestFilesAsync("octo/shop", 7, CancellationToken.None);

        Assert.Equal(
            [new PullRequestFile("src/A.cs", "modified", "@@ -1 +1 @@\n-a\n+b"), new PullRequestFile("img/logo.png", "added", null)],
            files);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_failed_call_throws_with_the_status_and_without_the_body()
    {
        var handler = new FakeHttpHandler().On(HttpMethod.Get, "/repos/octo/shop/pulls/7", _ => FakeHttpHandler.Json("{\"message\":\"secret detail\"}", HttpStatusCode.Forbidden));

        var exception = await Assert.ThrowsAsync<GitHubApiException>(() => Client(handler).GetPullRequestAsync("octo/shop", 7, CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.DoesNotContain("secret detail", exception.Message);
    }
}
