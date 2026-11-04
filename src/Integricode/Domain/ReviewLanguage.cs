using System.Text.RegularExpressions;

namespace Integricode.Domain;

/// <summary>The languages Integricode writes in: the model's text and the labels of everything it publishes.</summary>
public static partial class ReviewLanguage
{
    public const string English = "en";
    public const string PortugueseBrazil = "pt-BR";

    // Words only one of the languages uses; words both use ("a", "do", "no", "as", "os", and "for", the Portuguese
    // future subjunctive of "ser") would tell nothing.
    private static readonly IReadOnlySet<string> EnglishWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "the", "and", "of", "to", "is", "are", "was", "were", "that", "this", "these", "with", "from", "when",
        "which", "should", "would", "will", "not", "it", "its", "in", "on", "at", "an", "be", "been", "has", "have",
        "into", "only", "if", "but", "does", "can", "there", "their", "by", "or",
    };

    private static readonly IReadOnlySet<string> PortugueseWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "de", "da", "das", "dos", "que", "não", "para", "com", "uma", "um", "em", "na", "nas", "nos", "ao", "aos",
        "pela", "pelo", "por", "mais", "é", "está", "são", "foi", "ser", "como", "quando", "também", "já", "sem",
        "isso", "este", "esta", "essa", "esse", "ou", "mas", "porque", "onde", "deve", "seu", "sua", "se", "até", "só",
    };

    public static IReadOnlyList<string> All { get; } = [English, PortugueseBrazil];

    /// <summary>The supported language with this name, ignoring case, or null.</summary>
    public static string? Find(string? name) => All.FirstOrDefault(language => string.Equals(language, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The supported language most of these texts are written in, counting the words only one language uses; HTML
    /// comments, code, links, and e-mail addresses are not counted, and an identifier such as <c>is_valid</c> is one
    /// word that counts for neither.
    /// </summary>
    /// <param name="texts">The pull request's description and commit subject lines; null or empty ones are skipped.</param>
    /// <returns>The language with more words, or null when no word tells them apart or both have as many.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="texts"/> is null.</exception>
    public static string? Detect(IEnumerable<string?> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var english = 0;
        var portuguese = 0;
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            foreach (Match word in Word().Matches(NotProse().Replace(text, " ")))
            {
                var lower = word.Value.ToLowerInvariant();
                english += EnglishWords.Contains(lower) ? 1 : 0;
                portuguese += PortugueseWords.Contains(lower) ? 1 : 0;
            }
        }

        return english == portuguese ? null : english > portuguese ? English : PortugueseBrazil;
    }

    [GeneratedRegex(@"<!--[\s\S]*?-->|```[\s\S]*?```|`[^`\n]*`|https?://\S+|\S+@\S+", RegexOptions.IgnoreCase)]
    private static partial Regex NotProse();

    // Digits and underscores keep an identifier whole, so is_valid does not count as "is".
    [GeneratedRegex(@"[\p{L}\p{N}_]+")]
    private static partial Regex Word();
}
