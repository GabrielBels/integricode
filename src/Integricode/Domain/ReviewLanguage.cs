namespace Integricode.Domain;

/// <summary>The languages Integricode writes in: the model's text and the labels of everything it publishes.</summary>
public static class ReviewLanguage
{
    public const string English = "en";
    public const string PortugueseBrazil = "pt-BR";

    public static IReadOnlyList<string> All { get; } = [English, PortugueseBrazil];

    /// <summary>The supported language with this name, ignoring case, or null.</summary>
    public static string? Find(string? name) => All.FirstOrDefault(language => string.Equals(language, name, StringComparison.OrdinalIgnoreCase));
}
