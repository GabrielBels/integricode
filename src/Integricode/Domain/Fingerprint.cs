using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Integricode.Domain;

/// <summary>Identifies a finding across runs: SHA-256 of its path, line, and whitespace-normalized title and scenario.</summary>
public static partial class Fingerprint
{
    public static string Compute(string path, int line, string title, string failureScenario)
    {
        var canonical = new StringBuilder();
        foreach (var field in new[] { path, line.ToString(CultureInfo.InvariantCulture), NormalizeWhitespace(title), NormalizeWhitespace(failureScenario) })
        {
            canonical.Append(field.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(field).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    /// <summary>Trims the text and collapses every run of whitespace into one space.</summary>
    public static string NormalizeWhitespace(string text) => Whitespace().Replace(text.Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
