namespace Integricode.Domain;

public enum FindingKind
{
    RequirementMismatch,
    Bug,
}

/// <summary>The traffic light of a finding: 🔴 blocking, 🟡 suggestion, 🟢 nitpick or note (Conventional Comments labels).</summary>
public enum FindingSeverity
{
    Blocking,
    Suggestion,
    Nitpick,
    Note,
}

/// <summary>The review angles a finding can come from, as the model names them.</summary>
public static class FindingLens
{
    public const string Bug = "bug";

    public static IReadOnlyList<string> All { get; } = [Bug, "security", "tests", "conventions", "docs", "performance", "ci", "containers", "contracts"];
}

/// <summary>A one-click change that replaces lines <see cref="StartLine"/> to the finding's line with <see cref="Code"/>.</summary>
public sealed record SuggestedChange(int StartLine, string Code);

/// <summary>
/// A validated finding: a code location, a failure scenario, and for a mismatch the requirement quote it cites, with its
/// light, lens, fix, and optional suggested change.
/// </summary>
public sealed record Finding(
    FindingKind Kind,
    string Title,
    string Path,
    int Line,
    string FailureScenario,
    string? RequirementQuote = null)
{
    public FindingSeverity Severity { get; init; } = FindingSeverity.Blocking;

    public string Lens { get; init; } = FindingLens.Bug;

    public string? Fix { get; init; }

    public SuggestedChange? Suggestion { get; init; }

    public string Fingerprint => Domain.Fingerprint.Compute(Path, Line, Title, FailureScenario);
}
