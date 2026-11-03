namespace Integricode.Domain;

/// <summary>Text in the review's input that reads like instructions to an AI reviewer, found by the injection tripwire.</summary>
/// <param name="Source">Where it was found: <see cref="RequirementSource"/>, or <see cref="DiffSource"/> for an added line.</param>
/// <param name="Path">The file of an added line; null for the requirement.</param>
/// <param name="Line">The head-commit line of an added line; null for the requirement.</param>
/// <param name="Excerpt">The matched text.</param>
public sealed record InjectionWarning(string Source, string? Path, int? Line, string Excerpt)
{
    public const string RequirementSource = "requirement";
    public const string DiffSource = "diff";
}
