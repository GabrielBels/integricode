namespace Integricode.Requirements;

public enum RequirementKind
{
    ReqEntry,
    JiraIssue,
}

public enum RequirementLinkSource
{
    Title,
    Branch,
    Description,
}

/// <summary>A pull request's link to its requirement: a REQ ID or a Jira key, and where it was found.</summary>
public sealed record RequirementLink(RequirementKind Kind, string Id, RequirementLinkSource Source);
