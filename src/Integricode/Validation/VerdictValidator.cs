using Integricode.Domain;

namespace Integricode.Validation;

public sealed record VerdictOutcome(RequirementVerdict? Verdict, IReadOnlyDictionary<string, int> Dropped);

/// <summary>
/// The provenance check of the requirement verdict: keeps a criterion row only when its fields are present and its
/// quote is verbatim requirement text, like a mismatch quote. An answer of yes that the kept rows or a blocking
/// requirement mismatch contradict becomes partly, so data that talked the model into "yes" cannot publish it.
/// </summary>
/// <param name="requirementText">The requirement text read, or null when none was; without it no verdict is kept.</param>
public sealed class VerdictValidator(string? requirementText)
{
    private readonly string? normalizedRequirement = requirementText is null ? null : Fingerprint.NormalizeWhitespace(requirementText);

    /// <summary>Validates the proposed verdict against the requirement and the validated findings.</summary>
    /// <param name="proposed">The verdict the model proposed, or null.</param>
    /// <param name="findings">The findings that passed the provenance check.</param>
    /// <returns>The verdict to publish, or null, and the rows dropped by reason.</returns>
    public VerdictOutcome Validate(ProposedVerdict? proposed, IReadOnlyList<Finding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var dropped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (proposed is null || normalizedRequirement is null)
        {
            return new VerdictOutcome(null, dropped);
        }

        var answer = ParseAnswer(proposed.Answer);
        if (answer is null || string.IsNullOrWhiteSpace(proposed.Summary))
        {
            dropped[DropReason.VerdictMissingField] = 1;
            return new VerdictOutcome(null, dropped);
        }

        var criteria = new List<CriterionCheck>();
        foreach (var row in proposed.Criteria ?? [])
        {
            var status = ParseStatus(row?.Status);
            if (row is null || status is null || string.IsNullOrWhiteSpace(row.Quote) || string.IsNullOrWhiteSpace(row.Evidence))
            {
                dropped[DropReason.CriterionMissingField] = dropped.GetValueOrDefault(DropReason.CriterionMissingField) + 1;
                continue;
            }

            var quote = Fingerprint.NormalizeWhitespace(row.Quote);
            if (!FindingValidator.IsVerbatim(normalizedRequirement, quote))
            {
                dropped[DropReason.CriterionQuoteNotFound] = dropped.GetValueOrDefault(DropReason.CriterionQuoteNotFound) + 1;
                continue;
            }

            criteria.Add(new CriterionCheck(quote, status.Value, row.Evidence.Trim()));
        }

        var contradicted = criteria.Any(row => row.Status != CriterionStatus.Covered)
            || findings.Any(f => f.Kind == FindingKind.RequirementMismatch && f.Severity == FindingSeverity.Blocking);
        var kept = answer == VerdictAnswer.Yes && contradicted ? VerdictAnswer.Partly : answer.Value;
        return new VerdictOutcome(new RequirementVerdict(kept, proposed.Summary.Trim(), criteria), dropped);
    }

    private static VerdictAnswer? ParseAnswer(string? answer) => FindingValidator.Letters(answer) switch
    {
        "yes" => VerdictAnswer.Yes,
        "partly" or "partial" or "partially" => VerdictAnswer.Partly,
        "no" => VerdictAnswer.No,
        _ => null,
    };

    private static CriterionStatus? ParseStatus(string? status) => FindingValidator.Letters(status) switch
    {
        "covered" => CriterionStatus.Covered,
        "partial" or "partly" => CriterionStatus.Partial,
        "missing" or "notcovered" => CriterionStatus.Missing,
        "unclear" => CriterionStatus.Unclear,
        _ => null,
    };
}
