namespace Integricode.Domain;

public enum CoverageState
{
    Complete,
    Incomplete,
    SkippedByPolicy,
    Superseded,
    Failed,
}

/// <summary>How much of the review's required context was available, with the reasons when it was not complete.</summary>
public sealed record Coverage(CoverageState State, IReadOnlyList<string> Reasons)
{
    public static Coverage Complete { get; } = new(CoverageState.Complete, []);

    public static Coverage Incomplete(IEnumerable<string> reasons) => new(CoverageState.Incomplete, [.. reasons]);

    public static Coverage Failed(string reason) => new(CoverageState.Failed, [reason]);

    public static Coverage Superseded(string reason) => new(CoverageState.Superseded, [reason]);

    public static Coverage SkippedByPolicy(string reason) => new(CoverageState.SkippedByPolicy, [reason]);
}
