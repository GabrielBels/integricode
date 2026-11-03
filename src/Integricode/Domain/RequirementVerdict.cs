namespace Integricode.Domain;

public enum VerdictAnswer
{
    Yes,
    Partly,
    No,
}

public enum CriterionStatus
{
    Covered,
    Partial,
    Missing,
    Unclear,
}

/// <summary>Whether the change does what the requirement asks: an answer, why, and the criteria rows that passed the provenance check.</summary>
public sealed record RequirementVerdict(VerdictAnswer Answer, string Summary, IReadOnlyList<CriterionCheck> Criteria);

/// <summary>One acceptance criterion or clarification, quoted verbatim from the requirement, with its status and evidence.</summary>
public sealed record CriterionCheck(string Quote, CriterionStatus Status, string Evidence);
