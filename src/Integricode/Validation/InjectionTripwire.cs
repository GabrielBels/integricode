using System.Text.RegularExpressions;
using Integricode.Domain;
using Integricode.GitHub;

namespace Integricode.Validation;

/// <summary>
/// Scans the text the pull request controls (the requirement and the lines the diff adds) for phrases that address an
/// AI reviewer, in English and Portuguese. It runs outside the model, so a review the data talked into silence still
/// warns; the phrases are few and specific, because a hit only adds a warning to the summary.
/// </summary>
public static partial class InjectionTripwire
{
    public const int MaxWarnings = 10;

    /// <summary>Finds the lines that read like instructions to an AI reviewer.</summary>
    /// <param name="requirementText">The requirement text sent to the model, or null.</param>
    /// <param name="sentDiff">
    /// The patches to scan, in diff order: those sent to the model, and whole files sent without their patch; null
    /// patches were not sent and are skipped.
    /// </param>
    /// <returns>At most <see cref="MaxWarnings"/> warnings, requirement first, then the files in diff order.</returns>
    public static IReadOnlyList<InjectionWarning> Scan(string? requirementText, IReadOnlyList<DiffFile> sentDiff)
    {
        ArgumentNullException.ThrowIfNull(sentDiff);
        var warnings = new List<InjectionWarning>();
        foreach (var line in (requirementText ?? string.Empty).Split('\n').Where(text => Phrase().IsMatch(text)))
        {
            warnings.Add(new InjectionWarning(InjectionWarning.RequirementSource, null, null, Fingerprint.NormalizeWhitespace(line)));
        }

        foreach (var file in sentDiff.Where(file => file.Patch is not null))
        {
            foreach (var line in DiffMap.AddedLines(file.Patch!).Where(line => Phrase().IsMatch(line.Text)))
            {
                warnings.Add(new InjectionWarning(InjectionWarning.DiffSource, file.Path, line.Number, Fingerprint.NormalizeWhitespace(line.Text)));
            }
        }

        return [.. warnings.Take(MaxWarnings)];
    }

    [GeneratedRegex(
        @"\b(ignore|disregard|forget|override)\s+(all\s+|any\s+|the\s+|your\s+|of\s+)*(previous|prior|above|earlier|preceding|system)\s+(instructions?|rules|prompts?|guidelines)"
        + @"|\b(system|developer)\s+prompt\b"
        + @"|\byou\s+are\s+(now\s+)?(an?\s+|the\s+)?(ai|assistant|language\s+model|llm|chatbot|code\s+reviewer)\b"
        + @"|\bas\s+an?\s+(ai|language\s+model|llm)\b"
        + @"|\bnew\s+instructions\s*:"
        + @"|\b(note|message|instructions?)\s+(to|for)\s+(the\s+|any\s+)?(ai|llm|bots?|assistants?|automated\s+reviewers?|ai\s+reviewers?)\b"
        + @"|\b(report|return|output)\s+(no|zero)\s+(findings|issues|problems)\b"
        + @"|\bapprove\s+this\s+(pull\s+request|pr|change)\b"
        + @"|<\s*/?\s*(system|assistant|instructions?)\s*>"
        + @"|\b(ignore|desconsidere|esque[cç]a)\s+(todas\s+)?(as\s+)?(instru[cç][oõ]es|regras|orienta[cç][oõ]es)\s+(anteriores|acima|do\s+sistema)"
        + @"|\bprompt\s+do\s+sistema\b"
        + @"|\bvoc[eê]\s+(agora\s+)?[eé]\s+(um|uma|o|a)\s+(ia|assistente|modelo\s+de\s+linguagem|revisor)\b"
        + @"|\bnovas\s+instru[cç][oõ]es\s*:"
        + @"|\b(nota|mensagem|instru[cç][oõ]es)\s+(para|ao|à|aos)\s+(o\s+|a\s+)?(ia|llm|bots?|assistentes?|revisor(es)?\s+(de\s+ia|autom[aá]ticos?))\b"
        + @"|\bn[aã]o\s+(reporte|aponte|relate)\s+(nenhum|nenhuma|este|esse|isso|achados?)\b"
        + @"|\baprove\s+(este|esse)\s+(pull\s+request|pr)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Phrase();
}
