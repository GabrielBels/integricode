namespace Integricode.Domain;

/// <summary>The review agent's structured output: findings and the requirement verdict as the model proposed them, before validation.</summary>
public sealed record AgentReview(IReadOnlyList<ProposedFinding>? Findings, ProposedVerdict? RequirementVerdict = null);

/// <summary>A finding as proposed by the model; every field may be missing, which validation reports.</summary>
public sealed record ProposedFinding(
    string? Kind,
    string? Title,
    string? Path,
    int? Line,
    string? FailureScenario,
    string? RequirementQuote,
    string? Severity = null,
    string? Lens = null,
    string? Fix = null,
    int? SuggestionStartLine = null,
    string? Suggestion = null);

/// <summary>Whether the change does what the requirement asks, as the model proposed it.</summary>
public sealed record ProposedVerdict(string? Answer, string? Summary, IReadOnlyList<ProposedCriterion>? Criteria);

/// <summary>One acceptance criterion or clarification, as the model proposed its status.</summary>
public sealed record ProposedCriterion(string? Quote, string? Status, string? Evidence);
