namespace Integricode.Requirements;

/// <summary>Reads the requirement a link points to.</summary>
public interface IRequirementSource
{
    RequirementKind Kind { get; }

    Task<RequirementReadResult> ReadAsync(RequirementLink link, CancellationToken cancellationToken);
}

/// <summary>
/// The requirement text, or why it could not be read, or both when part of it is missing; a reason is reported as
/// incomplete coverage (FR-7).
/// </summary>
public sealed record RequirementReadResult(string? Text, string? UnreadableReason)
{
    public bool IsRead => Text is not null;

    public static RequirementReadResult Read(string text) => new(text, null);

    /// <summary>Text that was read with a part missing, such as a Jira issue without acceptance criteria.</summary>
    public static RequirementReadResult ReadIncomplete(string text, string reason) => new(text, reason);

    public static RequirementReadResult Unreadable(string reason) => new(null, reason);
}
