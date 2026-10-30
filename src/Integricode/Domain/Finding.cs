namespace Integricode.Domain;

public enum FindingKind
{
    RequirementMismatch,
    Bug,
}

/// <summary>A validated finding: a code location, a failure scenario, and for a mismatch the requirement quote it cites.</summary>
public sealed record Finding(
    FindingKind Kind,
    string Title,
    string Path,
    int Line,
    string FailureScenario,
    string? RequirementQuote = null)
{
    public string Fingerprint => Domain.Fingerprint.Compute(Path, Line, Title, FailureScenario);
}
