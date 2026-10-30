namespace Integricode.Requirements;

/// <summary>The registered requirement sources; a link whose kind has no source is unreadable.</summary>
public sealed class RequirementSources(IEnumerable<IRequirementSource> sources)
{
    private readonly IReadOnlyList<IRequirementSource> sources = [.. sources];

    public Task<RequirementReadResult> ReadAsync(RequirementLink link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        var source = sources.FirstOrDefault(candidate => candidate.Kind == link.Kind);
        if (source is not null)
        {
            return source.ReadAsync(link, cancellationToken);
        }

        var reason = link.Kind == RequirementKind.JiraIssue
            ? $"Jira source not configured for {link.Id}"
            : $"no requirement source configured for {link.Id}";
        return Task.FromResult(RequirementReadResult.Unreadable(reason));
    }
}
