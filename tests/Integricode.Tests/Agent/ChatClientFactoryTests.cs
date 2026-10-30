using System.Net;
using Anthropic.Exceptions;
using Integricode.Agent;
using Integricode.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace Integricode.Tests.Agent;

public class ChatClientFactoryTests
{
    [Fact]
    public void Openai_uses_the_openai_client()
    {
        var metadata = ChatClientFactory.Create("openai", "gpt-test", null, "test-key").GetService<ChatClientMetadata>();

        Assert.Equal("openai", metadata?.ProviderName);
        Assert.Equal("gpt-test", metadata?.DefaultModelId);
    }

    [Theory]
    [InlineData("https://shop.openai.azure.com")]
    [InlineData("https://shop.openai.azure.com/")]
    [InlineData("https://shop.openai.azure.com/openai/v1")]
    [InlineData("https://shop.openai.azure.com/openai/v1/")]
    public void Azure_openai_uses_the_v1_endpoint(string endpoint)
    {
        var metadata = ChatClientFactory.Create("azure-openai", "my-deployment", endpoint, "test-key").GetService<ChatClientMetadata>();

        Assert.Equal(new Uri("https://shop.openai.azure.com/openai/v1/"), metadata?.ProviderUri);
        Assert.Equal("my-deployment", metadata?.DefaultModelId);
    }

    [Fact]
    public void Azure_openai_requires_an_endpoint()
    {
        Assert.Throws<ArgumentException>(() => ChatClientFactory.Create("azure-openai", "my-deployment", null, "test-key"));
    }

    [Fact]
    public void Anthropic_uses_the_anthropic_client()
    {
        var metadata = ChatClientFactory.Create("Anthropic", "claude-test", null, "test-key").GetService<ChatClientMetadata>();

        Assert.Equal("anthropic", metadata?.ProviderName);
        Assert.Equal("claude-test", metadata?.DefaultModelId);
    }

    [Fact]
    public void Every_provider_is_wrapped_in_the_single_retry()
    {
        foreach (var (provider, endpoint) in new[] { ("openai", (string?)null), ("azure-openai", "https://x.openai.azure.com"), ("anthropic", null) })
        {
            Assert.NotNull(ChatClientFactory.Create(provider, "m", endpoint, "k").GetService<RetryingChatClient>());
        }
    }

    [Fact]
    public void An_unknown_provider_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => ChatClientFactory.Create("gemini", "m", null, "k"));

        Assert.Contains("gemini", exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_429_or_5xx_is_retried_once(HttpStatusCode status)
    {
        var inner = new ScriptedChatClient()
            .ThenThrow(new HttpRequestException("transient", null, status))
            .ThenText("ok");

        var response = await new RetryingChatClient(inner).GetResponseAsync("hi");

        Assert.Equal("ok", response.Text);
        Assert.Equal(2, inner.Calls.Count);
    }

    [Fact]
    public async Task A_second_transient_failure_is_not_retried_again()
    {
        var inner = new ScriptedChatClient()
            .ThenThrow(new HttpRequestException("transient", null, HttpStatusCode.BadGateway))
            .ThenThrow(new HttpRequestException("transient", null, HttpStatusCode.BadGateway))
            .ThenText("never");

        await Assert.ThrowsAsync<HttpRequestException>(() => new RetryingChatClient(inner).GetResponseAsync("hi"));
        Assert.Equal(2, inner.Calls.Count);
    }

    [Fact]
    public async Task Other_failures_are_not_retried()
    {
        var inner = new ScriptedChatClient()
            .ThenThrow(new HttpRequestException("bad request", null, HttpStatusCode.BadRequest))
            .ThenText("never");

        await Assert.ThrowsAsync<HttpRequestException>(() => new RetryingChatClient(inner).GetResponseAsync("hi"));
        Assert.Single(inner.Calls);
    }

    [Fact]
    public void Statuses_are_read_from_anthropic_and_wrapped_exceptions()
    {
        var anthropic = new AnthropicApiException("overloaded", new HttpRequestException()) { StatusCode = (HttpStatusCode)529, ResponseBody = string.Empty };

        Assert.Equal(529, ChatClientFactory.StatusOf(anthropic));
        Assert.Equal(503, ChatClientFactory.StatusOf(new InvalidOperationException("wrapped", new HttpRequestException(null, null, HttpStatusCode.ServiceUnavailable))));
        Assert.Null(ChatClientFactory.StatusOf(new TimeoutException()));
    }
}
