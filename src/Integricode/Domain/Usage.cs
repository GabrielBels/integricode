namespace Integricode.Domain;

/// <summary>Model and tool usage of one review run (FR-17).</summary>
public sealed record Usage(long InputTokens = 0, long OutputTokens = 0, int ToolCalls = 0, long CharactersRead = 0);
