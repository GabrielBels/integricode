using Integricode.Agent;
using Integricode.Commands;
using Integricode.GitHub;

var log = new ActionsLog(Console.Out);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    return args switch
    {
        ["review", .. var options] => await new ReviewCommand(CreateGitHubClient(), CreateChatClient, log)
            .RunAsync(ReviewOptions.Parse(options), cancellation.Token),
        ["comment", .. var options] => 0,
        _ => Usage(),
    };
}
catch (UsageException exception)
{
    Console.Error.WriteLine(exception.Message);
    return Usage();
}
catch (Exception exception) when (exception is not OperationCanceledException)
{
    // Only the type: messages from libraries can carry request or response content.
    log.Error($"Integricode failed: {exception.GetType().Name}.");
    return 1;
}

static GitHubClient CreateGitHubClient()
{
    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    if (string.IsNullOrWhiteSpace(token))
    {
        throw new UsageException("GITHUB_TOKEN is not set.");
    }

    var apiUrl = Environment.GetEnvironmentVariable("GITHUB_API_URL");
    return new GitHubClient(new HttpClient(), new Uri(string.IsNullOrWhiteSpace(apiUrl) ? "https://api.github.com/" : apiUrl), token);
}

static Microsoft.Extensions.AI.IChatClient CreateChatClient(ReviewOptions options) => ChatClientFactory.Create(
    options.ModelProvider,
    options.Model,
    options.ModelEndpoint,
    Environment.GetEnvironmentVariable("INTEGRICODE_MODEL_API_KEY") ?? string.Empty);

static int Usage()
{
    Console.Error.WriteLine("""
        Usage:
          integricode review --repo owner/name --pr N --model-provider openai|azure-openai|anthropic --model M
                             [--model-endpoint URL] [--workspace DIR] [--out result.json]
                             [--requirements-file docs/requirements.md] [--exclude GLOB]...
                             [--max-context-chars 400000] [--max-tool-calls 40]
          integricode comment --repo owner/name --pr N [--result result.json]
        Environment: GITHUB_TOKEN; INTEGRICODE_MODEL_API_KEY for review.
        """);
    return 2;
}
