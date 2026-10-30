using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Integricode.Domain;

/// <summary>One piece of repository content the review consumed: where it came from and the SHA-256 of its text.</summary>
public sealed record ContextChunk(string Source, string Path, int FromLine, int ToLine, string ContentHash)
{
    public static ContextChunk Create(string source, string path, int fromLine, int toLine, string content) =>
        new(source, path, fromLine, toLine, HashText(content));

    public static string HashText(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}

/// <summary>A pull-request file in the diff, with its patch when GitHub provides one.</summary>
public sealed record DiffFile(string Path, string? Patch);

/// <summary>The exact inputs a review consumed (FR-8): head SHA, diff, requirement text, and every chunk read, in order.</summary>
public sealed record ContextSnapshot(
    string HeadSha,
    IReadOnlyList<DiffFile> Diff,
    string? RequirementText,
    IReadOnlyList<ContextChunk> Chunks)
{
    /// <summary>SHA-256 over a length-prefixed canonical serialization of the snapshot, as lowercase hex.</summary>
    public string ComputeHash()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Field(string name, string? value)
        {
            var text = value is null
                ? name + ":-1:\n"
                : name + ":" + value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value + "\n";
            hash.AppendData(Encoding.UTF8.GetBytes(text));
        }

        Field("version", "integricode-context-v1");
        Field("head", HeadSha);
        Field("diff", Diff.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var file in Diff)
        {
            Field("path", file.Path);
            Field("patch", file.Patch);
        }

        Field("requirement", RequirementText);
        Field("chunks", Chunks.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var chunk in Chunks)
        {
            Field("source", chunk.Source);
            Field("path", chunk.Path);
            Field("from", chunk.FromLine.ToString(CultureInfo.InvariantCulture));
            Field("to", chunk.ToLine.ToString(CultureInfo.InvariantCulture));
            Field("hash", chunk.ContentHash);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
