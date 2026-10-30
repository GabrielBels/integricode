using System.Text.RegularExpressions;

namespace Integricode.Requirements;

/// <summary>
/// Finds the pull request's requirement (FR-4): the first REQ ID (<c>REQ-NNN</c>) or Jira Cloud key in the title, then
/// the branch name, then the description.
/// </summary>
public static partial class RequirementIdentifier
{
    public static RequirementLink? Identify(string? title, string? branch, string? description)
    {
        foreach (var (text, source) in new[]
                 {
                     (title, RequirementLinkSource.Title),
                     (branch, RequirementLinkSource.Branch),
                     (description, RequirementLinkSource.Description),
                 })
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var match = Link().Match(text);
            if (match.Success)
            {
                return match.Groups["req"].Success
                    ? new RequirementLink(RequirementKind.ReqEntry, match.Groups["req"].Value.ToUpperInvariant(), source)
                    : new RequirementLink(RequirementKind.JiraIssue, match.Groups["jira"].Value, source);
            }
        }

        return null;
    }

    // The REQ prefix matches in any case because branch names are usually lowercase (feat/req-001-...);
    // Jira keys stay uppercase so ordinary lowercase words with a number are not taken for keys.
    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:(?<req>(?i:req)-\d{3})(?![0-9])|(?<jira>[A-Z][A-Z0-9]{1,9}-\d+)(?![0-9]))")]
    private static partial Regex Link();
}
