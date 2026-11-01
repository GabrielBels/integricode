using Integricode.Commands;
using Integricode.GitHub;
using Microsoft.Extensions.AI;

namespace Integricode;

/// <summary>
/// The command line: dispatches <c>review</c> and <c>comment</c>, reads the environment, and maps the outcome to an
/// exit code: 0 done, 1 failed, 2 usage error. <c>Program</c> only wires it to the console and the network.
/// </summary>
/// <param name="httpHandler">The handler every GitHub call goes through.</param>
/// <param name="createChatClient">Creates the model client from the provider, model, endpoint, and API key.</param>
public sealed class App(HttpMessageHandler httpHandler, Func<string, string, string?, string, IChatClient> createChatClient)
{
    public const string ModelApiKeyVariable = "INTEGRICODE_MODEL_API_KEY";

    /// <param name="args">The command-line arguments.</param>
    /// <param name="environment">Reads an environment variable, or null when it is not set.</param>
    /// <param name="output">Receives the GitHub Actions log.</param>
    /// <param name="error">Receives usage errors.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunAsync(string[] args, Func<string, string?> environment, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);
        var log = new ActionsLog(output);
        try
        {
            return args switch
            {
                ["review", .. var options] => await new ReviewCommand(CreateGitHubClient(environment), CreateChatClient(environment), log)
                    .RunAsync(ReviewOptions.Parse(options), cancellationToken),
                ["comment", .. var options] => await new CommentCommand(CreateGitHubClient(environment), log)
                    .RunAsync(CommentOptions.Parse(options), cancellationToken),
                _ => await UsageAsync(error),
            };
        }
        catch (UsageException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return await UsageAsync(error);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Only the type: messages from libraries can carry request or response content.
            log.Error($"Integricode failed: {exception.GetType().Name}.");
            return 1;
        }
    }

    private GitHubClient CreateGitHubClient(Func<string, string?> environment)
    {
        var token = environment("GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new UsageException("GITHUB_TOKEN is not set.");
        }

        var apiUrl = environment("GITHUB_API_URL");
        var graphqlUrl = environment("GITHUB_GRAPHQL_URL");
        return new GitHubClient(
            new HttpClient(httpHandler, disposeHandler: false),
            new Uri(string.IsNullOrWhiteSpace(apiUrl) ? "https://api.github.com/" : apiUrl),
            token,
            string.IsNullOrWhiteSpace(graphqlUrl) ? null : new Uri(graphqlUrl));
    }

    private Func<ReviewOptions, IChatClient> CreateChatClient(Func<string, string?> environment) => options => createChatClient(
        options.ModelProvider,
        options.Model,
        options.ModelEndpoint,
        environment(ModelApiKeyVariable) ?? string.Empty);

    private static async Task<int> UsageAsync(TextWriter error)
    {
        await error.WriteLineAsync($"""
            Usage:
              integricode review --repo owner/name --pr N --model-provider openai|azure-openai|anthropic --model M
                                 [--model-endpoint URL] [--workspace DIR] [--out result.json]
                                 [--requirements-file docs/requirements.md] [--exclude GLOB]...
                                 [--max-context-chars 400000] [--max-tool-calls 40]
              integricode comment --repo owner/name --pr N [--result result.json | --skipped-by-policy fork]
            Environment: GITHUB_TOKEN; {ModelApiKeyVariable} for review; GITHUB_API_URL and GITHUB_GRAPHQL_URL when set.
            """);
        return 2;
    }
}
