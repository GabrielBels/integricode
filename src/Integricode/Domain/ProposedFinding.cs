namespace Integricode.Domain;

/// <summary>The review agent's structured output: findings as the model proposed them, before validation.</summary>
public sealed record AgentReview(IReadOnlyList<ProposedFinding>? Findings);

/// <summary>A finding as proposed by the model; every field may be missing, which validation reports.</summary>
public sealed record ProposedFinding(
    string? Kind,
    string? Title,
    string? Path,
    int? Line,
    string? FailureScenario,
    string? RequirementQuote);
