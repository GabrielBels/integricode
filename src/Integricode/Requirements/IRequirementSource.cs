namespace Integricode.Requirements;

/// <summary>Reads the requirement a link points to.</summary>
public interface IRequirementSource
{
    RequirementKind Kind { get; }

    Task<RequirementReadResult> ReadAsync(RequirementLink link, CancellationToken cancellationToken);
}

/// <summary>The requirement text, or why it could not be read (reported as incomplete coverage, FR-7).</summary>
public sealed record RequirementReadResult(string? Text, string? UnreadableReason)
{
    public bool IsRead => Text is not null;

    public static RequirementReadResult Read(string text) => new(text, null);

    public static RequirementReadResult Unreadable(string reason) => new(null, reason);
}
