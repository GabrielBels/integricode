using System.Text.Json.Nodes;
using Integricode.Agent;
using Integricode.Commands;
using Integricode.Domain;
using Integricode.GitHub;
using Integricode.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace Integricode.Tests;

public sealed class AppTests : IDisposable
{
    private readonly TempRepository workspace = new();
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();
    private readonly Dictionary<string, string?> environment = new() { ["GITHUB_TOKEN"] = "fake-token", ["GITHUB_API_URL"] = "https://api.github.test/" };

    private Task<int> RunAsync(HttpMessageHandler handler, params string[] args) =>
        RunAsync(handler, ChatClientFactory.Create, args);

    private Task<int> RunAsync(HttpMessageHandler handler, Func<string, string, string?, string, IChatClient> createChatClient, params string[] args) =>
        new App(handler, createChatClient).RunAsync(args, name => environment.GetValueOrDefault(name), output, error, CancellationToken.None);

    [Theory]
    [InlineData]
    [InlineData("publish")]
    public async Task An_unknown_command_prints_the_usage_and_exits_2(params string[] args)
    {
        var exitCode = await RunAsync(new FakeHttpHandler(), args);

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage:", error.ToString());
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("review")]
    [InlineData("comment")]
    public async Task A_missing_github_token_is_a_usage_error(string command)
    {
        environment.Remove("GITHUB_TOKEN");
        var handler = new FakeHttpHandler();

        var exitCode = await RunAsync(handler, command, "--repo", "octo/shop", "--pr", "7");

        Assert.Equal(2, exitCode);
        Assert.StartsWith("GITHUB_TOKEN is not set.", error.ToString());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task An_invalid_option_is_a_usage_error()
    {
        var exitCode = await RunAsync(new FakeHttpHandler(), "comment", "--repo", "octo/shop", "--pr", "7", "--verbose", "true");

        Assert.Equal(2, exitCode);
        Assert.StartsWith("Unknown option --verbose.", error.ToString());
    }

    [Fact]
    public async Task An_unexpected_failure_exits_1_and_logs_only_its_type()
    {
        var handler = new ThrowingHandler(new InvalidOperationException("response echoed sk-SYNTHETIC-SECRET"));

        var exitCode = await RunAsync(handler, "review", "--repo", "octo/shop", "--pr", "7", "--model-provider", "openai", "--model", "m");

        Assert.Equal(1, exitCode);
        Assert.Equal("::error::Integricode failed: InvalidOperationException." + Environment.NewLine, output.ToString());
        Assert.DoesNotContain("SYNTHETIC-SECRET", output.ToString() + error.ToString());
    }

    [Fact]
    public async Task Review_reads_the_model_key_from_the_environment()
    {
        using var gitHub = new FakeGitHub();
        gitHub.Files.Add(new PullRequestFile("src/A.cs", "added", "@@ -0,0 +1 @@\n+class A {}"));
        workspace.Write("src/A.cs", "class A {}\n");
        environment[App.ModelApiKeyVariable] = "key-from-environment";
        string? receivedKey = null;
        var resultPath = workspace.FullPath("out/result.json");

        var exitCode = await RunAsync(
            gitHub,
            (_, _, _, apiKey) =>
            {
                receivedKey = apiKey;
                return new ScriptedChatClient().ThenText("{\"findings\": []}");
            },
            "review", "--repo", FakeGitHub.Repository, "--pr", "7", "--workspace", workspace.Root, "--out", resultPath, "--model-provider", "openai", "--model", "m");

        Assert.Equal(0, exitCode);
        Assert.Equal("key-from-environment", receivedKey);
        Assert.NotEqual(CoverageState.Failed, (await ReviewResult.ReadAsync(resultPath, CancellationToken.None)).Coverage.State);
    }

    [Fact]
    public async Task Review_reads_the_jira_credentials_from_the_environment()
    {
        using var gitHub = new FakeGitHub { Title = "SHOP-12: cart totals" };
        using var jira = new FakeJira();
        gitHub.Files.Add(new PullRequestFile("src/A.cs", "added", "@@ -0,0 +1 @@\n+class A {}"));
        workspace.Write("src/A.cs", "class A {}\n");
        environment["JIRA_EMAIL"] = FakeJira.Email;
        environment["JIRA_API_TOKEN"] = FakeJira.ApiToken;
        var resultPath = workspace.FullPath("out/result.json");

        var exitCode = await RunAsync(
            new HostRouter(new Dictionary<string, HttpMessageHandler> { ["api.github.test"] = gitHub, ["shop.atlassian.test"] = jira }),
            (_, _, _, _) => new ScriptedChatClient().ThenText("{\"findings\": []}"),
            "review", "--repo", FakeGitHub.Repository, "--pr", "7", "--workspace", workspace.Root, "--out", resultPath, "--model-provider", "openai", "--model", "m",
            "--jira-base-url", FakeJira.BaseUrl);

        Assert.Equal(0, exitCode);
        Assert.Equal(CoverageState.Complete, (await ReviewResult.ReadAsync(resultPath, CancellationToken.None)).Coverage.State);
        Assert.Equal(2, jira.Requests.Count);
        Assert.All(jira.Requests, r => Assert.Equal(FakeJira.Authorization, r.Authorization));
        Assert.DoesNotContain(FakeJira.ApiToken, output.ToString() + error.ToString());
    }

    [Fact]
    public async Task A_missing_model_key_writes_a_failed_result()
    {
        using var gitHub = new FakeGitHub();
        var resultPath = workspace.FullPath("result.json");

        var exitCode = await RunAsync(gitHub, "review", "--repo", FakeGitHub.Repository, "--pr", "7", "--workspace", workspace.Root, "--out", resultPath, "--model-provider", "anthropic", "--model", "m");

        var result = await ReviewResult.ReadAsync(resultPath, CancellationToken.None);
        Assert.Equal(0, exitCode);
        Assert.Equal(Coverage.Failed(ReviewCommand.ProviderConfigurationError).Reasons, result.Coverage.Reasons);
    }

    [Fact]
    public async Task Comment_uses_the_rest_and_graphql_urls_from_the_environment()
    {
        environment["GITHUB_API_URL"] = "https://ghe.test/api/v3";
        environment["GITHUB_GRAPHQL_URL"] = "https://ghe.test/api/graphql";
        const string head = "1111111111111111111111111111111111111111";
        var stale = CommentTemplates.FindingMarker(new string('a', 64)) + "\nold";
        var handler = new FakeHttpHandler()
            .OnJson(HttpMethod.Get, "/api/v3/repos/octo/shop/pulls/7", $$$"""{"number": 7, "title": "t", "draft": false, "head": {"ref": "b", "sha": "{{{head}}}"}, "base": {"ref": "main", "sha": "0"}}""")
            .OnJson(HttpMethod.Get, "/api/v3/repos/octo/shop/pulls/7/files?per_page=100", "[]")
            .OnJson(HttpMethod.Get, "/api/v3/repos/octo/shop/pulls/7/comments?per_page=100", new JsonArray(new JsonObject { ["id"] = 5, ["node_id"] = "NODE_5", ["body"] = stale, ["user"] = new JsonObject { ["login"] = CommentCommand.GitHubActionsLogin }, ["path"] = "src/A.cs", ["line"] = 1 }).ToJsonString())
            .OnJson(HttpMethod.Post, "/api/graphql", """{"data": {"nodes": [{"id": "NODE_5", "isMinimized": false}], "minimizeComment": {"minimizedComment": {"isMinimized": true}}}}""")
            .OnJson(HttpMethod.Get, "/api/v3/repos/octo/shop/issues/7/comments?per_page=100", "[]")
            .OnJson(HttpMethod.Post, "/api/v3/repos/octo/shop/issues/7/comments", """{"id": 9, "node_id": "NODE_9", "body": "s", "user": {"login": "github-actions[bot]"}}""");
        var resultPath = workspace.FullPath("result.json");
        await new ReviewResult { Repository = "octo/shop", PullRequestNumber = 7, HeadSha = head, Coverage = Coverage.Complete }.WriteAsync(resultPath, CancellationToken.None);

        var exitCode = await RunAsync(handler, "comment", "--repo", "octo/shop", "--pr", "7", "--result", resultPath);

        Assert.Equal(0, exitCode);
        Assert.All(handler.Requests, r => Assert.Equal("ghe.test", r.Uri.Host));
        Assert.Equal(2, handler.Requests.Count(r => r.Uri.AbsolutePath == "/api/graphql"));
        Assert.All(handler.Requests, r => Assert.Equal("Bearer fake-token", r.Authorization));
    }

    public void Dispose()
    {
        workspace.Dispose();
        output.Dispose();
        error.Dispose();
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }

    /// <summary>Sends each request to the fake that serves its host, as one real handler serves GitHub and Jira.</summary>
    private sealed class HostRouter(IReadOnlyDictionary<string, HttpMessageHandler> handlers) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var invoker = new HttpMessageInvoker(handlers[request.RequestUri!.Host], disposeHandler: false);
            return await invoker.SendAsync(request, cancellationToken);
        }
    }
}
