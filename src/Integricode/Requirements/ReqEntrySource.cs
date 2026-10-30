using System.Text.RegularExpressions;
using Integricode.Agent;

namespace Integricode.Requirements;

/// <summary>
/// Reads a REQ entry (FR-6): the <c>## [REQ-NNN]</c> section of the requirements file in the head-SHA checkout, up to
/// the next level-two heading, acceptance criteria included.
/// </summary>
public sealed class ReqEntrySource(PathGuard guard, string requirementsFile) : IRequirementSource
{
    public RequirementKind Kind => RequirementKind.ReqEntry;

    public async Task<RequirementReadResult> ReadAsync(RequirementLink link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        var check = guard.Check(requirementsFile, applyExclusions: false);
        if (!check.Allowed)
        {
            return RequirementReadResult.Unreadable($"requirements file {requirementsFile} refused: {check.Refusal}");
        }

        if (!File.Exists(check.FullPath))
        {
            return RequirementReadResult.Unreadable($"requirements file {requirementsFile} not found at the head SHA");
        }

        var lines = await File.ReadAllLinesAsync(check.FullPath, cancellationToken);
        var section = ExtractSection(lines, link.Id);
        return section is null
            ? RequirementReadResult.Unreadable($"{link.Id} not found in {requirementsFile}")
            : RequirementReadResult.Read(section);
    }

    internal static string? ExtractSection(IReadOnlyList<string> lines, string requirementId)
    {
        var heading = new Regex($@"^##\s+\[{Regex.Escape(requirementId)}\]", RegexOptions.IgnoreCase);
        var start = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (heading.IsMatch(lines[i]))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        var end = lines.Count;
        for (var i = start + 1; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("## ", StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }

        return string.Join('\n', lines.Skip(start).Take(end - start)).TrimEnd();
    }
}
