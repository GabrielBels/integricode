using System.Text.Json.Nodes;
using Integricode.GitHub;
using Integricode.Tests.Fakes;

namespace Integricode.Tests.GitHub;

public class GitHubClientGraphQlTests
{
    private static GitHubClient Client(FakeHttpHandler handler) =>
        new(new HttpClient(handler), new Uri("https://api.github.test"), "test-token");

    [Fact]
    public async Task Minimizing_sends_the_outdated_classifier_for_the_node()
    {
        var handler = new FakeHttpHandler().OnJson(HttpMethod.Post, "/graphql", """{"data": {"minimizeComment": {"minimizedComment": {"isMinimized": true}}}}""");

        await Client(handler).MinimizeAsOutdatedAsync("NODE_1", CancellationToken.None);

        var body = JsonNode.Parse(handler.Requests.Single().Body!)!;
        Assert.Contains("classifier: OUTDATED", body["query"]!.GetValue<string>());
        Assert.Equal("NODE_1", body["variables"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task GraphQL_errors_throw()
    {
        var handler = new FakeHttpHandler().OnJson(HttpMethod.Post, "/graphql", """{"data": null, "errors": [{"message": "Resource not accessible"}]}""");

        await Assert.ThrowsAsync<GitHubApiException>(() => Client(handler).MinimizeAsOutdatedAsync("NODE_1", CancellationToken.None));
    }

    [Fact]
    public async Task Minimized_state_is_read_for_the_given_nodes()
    {
        var handler = new FakeHttpHandler().OnJson(HttpMethod.Post, "/graphql", """
            {"data": {"nodes": [{"id": "NODE_1", "isMinimized": true}, {"id": "NODE_2", "isMinimized": false}, null]}}
            """);

        var minimized = await Client(handler).GetMinimizedAsync(["NODE_1", "NODE_2", "NODE_GONE"], CancellationToken.None);

        Assert.Equal(["NODE_1"], minimized);
        Assert.Equal(["NODE_1", "NODE_2", "NODE_GONE"], JsonNode.Parse(handler.Requests.Single().Body!)!["variables"]!["ids"]!.AsArray().Select(id => id!.GetValue<string>()));
    }
}
