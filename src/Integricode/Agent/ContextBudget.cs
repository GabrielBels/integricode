using System.Globalization;

namespace Integricode.Agent;

/// <summary>
/// Limits what the repository tools return per review (ADR-002). The call or read that would exceed a limit is refused
/// and marks the budget reached, which makes the coverage incomplete.
/// </summary>
public sealed class ContextBudget
{
    public const int DefaultMaxToolCalls = 40;
    public const long DefaultMaxCharacters = 400_000;

    private readonly Lock gate = new();

    public ContextBudget(int maxToolCalls = DefaultMaxToolCalls, long maxCharacters = DefaultMaxCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxToolCalls);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCharacters);
        MaxToolCalls = maxToolCalls;
        MaxCharacters = maxCharacters;
    }

    public int MaxToolCalls { get; }

    public long MaxCharacters { get; }

    public int ToolCalls { get; private set; }

    public long CharactersRead { get; private set; }

    /// <summary>Why the budget was reached, or null while it was not.</summary>
    public string? ReachedReason { get; private set; }

    public bool Reached => ReachedReason is not null;

    public bool TryStartCall()
    {
        lock (gate)
        {
            if (ToolCalls >= MaxToolCalls)
            {
                ReachedReason ??= $"context budget reached ({MaxToolCalls.ToString(CultureInfo.InvariantCulture)} tool calls)";
                return false;
            }

            ToolCalls++;
            return true;
        }
    }

    public bool TryConsume(long characters)
    {
        lock (gate)
        {
            if (CharactersRead + characters > MaxCharacters)
            {
                ReachedReason ??= $"context budget reached ({MaxCharacters.ToString(CultureInfo.InvariantCulture)} characters)";
                return false;
            }

            CharactersRead += characters;
            return true;
        }
    }
}
