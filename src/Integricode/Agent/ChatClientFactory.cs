using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using Anthropic.Core;
using Anthropic.Exceptions;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Integricode.Agent;

/// <summary>Creates the <see cref="IChatClient"/> of the configured model provider (ADR-004).</summary>
public static class ChatClientFactory
{
    public const string OpenAi = "openai";
    public const string AzureOpenAi = "azure-openai";
    public const string Anthropic = "anthropic";

    /// <summary>The output limit sent to Anthropic, which requires one and whose adapter otherwise sends 1,024 tokens.</summary>
    public const int AnthropicMaxOutputTokens = 8192;

    /// <summary>Creates the provider's chat client with SDK retries off and Integricode's single retry on top.</summary>
    /// <param name="provider"><c>openai</c>, <c>azure-openai</c>, or <c>anthropic</c>.</param>
    /// <param name="model">The model, or the Azure deployment name.</param>
    /// <param name="endpoint">Optional for OpenAI and Anthropic; the Azure resource or v1 endpoint for Azure OpenAI.</param>
    /// <param name="apiKey">The model provider key.</param>
    /// <exception cref="ArgumentException">The provider is unknown, a required value is missing, or the endpoint is not an absolute URL.</exception>
    public static IChatClient Create(string provider, string model, string? endpoint, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var inner = provider.Trim().ToLowerInvariant() switch
        {
            OpenAi => CreateOpenAi(model, string.IsNullOrWhiteSpace(endpoint) ? null : ParseEndpoint(endpoint), apiKey),
            AzureOpenAi => CreateOpenAi(model, AzureV1Endpoint(endpoint), apiKey),
            Anthropic => CreateAnthropic(model, endpoint, apiKey),
            _ => throw new ArgumentException($"Unknown model provider '{provider}'; use {OpenAi}, {AzureOpenAi}, or {Anthropic}.", nameof(provider)),
        };
        return new RetryingChatClient(inner);
    }

    /// <summary>Azure OpenAI's OpenAI-compatible v1 endpoint: <c>https://{resource}.openai.azure.com/openai/v1/</c>.</summary>
    internal static Uri AzureV1Endpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("The azure-openai provider requires --model-endpoint.", nameof(endpoint));
        }

        var trimmed = ParseEndpoint(endpoint).AbsoluteUri.TrimEnd('/');
        return new Uri(trimmed.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase) ? trimmed + "/" : trimmed + "/openai/v1/");
    }

    /// <summary>Applies Integricode's default output limit when the caller sets none.</summary>
    internal static IChatClient WithAnthropicOutputLimit(IChatClient client) => client
        .AsBuilder()
        .ConfigureOptions(options => options.MaxOutputTokens ??= AnthropicMaxOutputTokens)
        .Build();

    private static Uri ParseEndpoint(string endpoint) =>
        Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : throw new ArgumentException("--model-endpoint must be an absolute http or https URL.", nameof(endpoint));

    private static IChatClient CreateOpenAi(string model, Uri? endpoint, string apiKey)
    {
        var options = new OpenAIClientOptions { RetryPolicy = new ClientRetryPolicy(maxRetries: 0) };
        if (endpoint is not null)
        {
            options.Endpoint = endpoint;
        }

        return new OpenAIClient(new ApiKeyCredential(apiKey), options).GetChatClient(model).AsIChatClient();
    }

    private static IChatClient CreateAnthropic(string model, string? endpoint, string apiKey)
    {
        var options = new ClientOptions { ApiKey = apiKey, MaxRetries = 0 };
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            options.BaseUrl = ParseEndpoint(endpoint).AbsoluteUri;
        }

        return WithAnthropicOutputLimit(new AnthropicClient(options).AsIChatClient(model));
    }

    /// <summary>The HTTP status of a provider failure, looking through the SDKs' exception types.</summary>
    internal static int? StatusOf(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: { } status } => (int)status,
        ClientResultException { Status: > 0 } result => result.Status,
        AnthropicApiException api => (int)api.StatusCode,
        { InnerException: { } inner } => StatusOf(inner),
        _ => null,
    };
}

/// <summary>Retries a model call once when the provider answers 429 or 5xx.</summary>
public sealed class RetryingChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var history = messages as IList<ChatMessage> ?? [.. messages];
        try
        {
            return await base.GetResponseAsync(history, options, cancellationToken);
        }
        catch (Exception exception) when (IsTransient(exception) && !cancellationToken.IsCancellationRequested)
        {
            return await base.GetResponseAsync(history, options, cancellationToken);
        }
    }

    private static bool IsTransient(Exception exception) =>
        ChatClientFactory.StatusOf(exception) is 429 or >= 500 and < 600;
}
