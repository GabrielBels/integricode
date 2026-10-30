namespace Integricode.GitHub;

/// <summary>The pull-request fields the review reads.</summary>
public sealed record PullRequest(
    int Number,
    string Title,
    string? Body,
    string HeadRef,
    string HeadSha,
    string BaseRepository,
    string? HeadRepository,
    bool Draft);

/// <summary>A file of the pull request; <see cref="Patch"/> is null for binary or too-large diffs.</summary>
public sealed record PullRequestFile(string Path, string Status, string? Patch);
