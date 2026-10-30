using Microsoft.Extensions.AI;

namespace Integricode.Tests.Fakes;

/// <summary>A fake model: each call takes the next scripted step, which answers or throws. Calls are recorded.</summary>
public sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse>> steps = new();

    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public ScriptedChatClient Then(Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse> step)
    {
        steps.Enqueue(step);
        return this;
    }

    public ScriptedChatClient ThenText(string text, long inputTokens = 100, long outputTokens = 10) =>
        Then((_, _) => Text(text, inputTokens, outputTokens));

    public ScriptedChatClient ThenToolCall(string name, Dictionary<string, object?> arguments, long inputTokens = 100, long outputTokens = 10) =>
        Then((_, _) => new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"call-{Calls.Count}", name, arguments)]))
        {
            Usage = new UsageDetails { InputTokenCount = inputTokens, OutputTokenCount = outputTokens },
        });

    public ScriptedChatClient ThenThrow(Exception exception) => Then((_, _) => throw exception);

    public static ChatResponse Text(string text, long inputTokens = 100, long outputTokens = 10) =>
        new(new ChatMessage(ChatRole.Assistant, text))
        {
            Usage = new UsageDetails { InputTokenCount = inputTokens, OutputTokenCount = outputTokens },
        };

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var history = messages.ToList();
        Calls.Add((history, options));
        if (steps.Count == 0)
        {
            throw new InvalidOperationException("The scripted model has no step left.");
        }

        return Task.FromResult(steps.Dequeue()(history, options));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The review does not stream.");

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
