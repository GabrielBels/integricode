using System.Text.Json;
using System.Text.Json.Serialization;
using Integricode.Requirements;

namespace Integricode.Domain;

/// <summary>The review result handed from the review job to the comment job as <c>result.json</c>.</summary>
public sealed record ReviewResult
{
    public const int CurrentSchemaVersion = 1;

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false) },
    };

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The repository as <c>owner/name</c>.</summary>
    public required string Repository { get; init; }

    public required int PullRequestNumber { get; init; }

    public required string HeadSha { get; init; }

    /// <summary>SHA-256 of the context snapshot (FR-8); absent when the review did not reach the model.</summary>
    public string? ContextHash { get; init; }

    /// <summary>The requirement link the review used, if any (FR-4).</summary>
    public RequirementLink? Requirement { get; init; }

    public required Coverage Coverage { get; init; }

    public IReadOnlyList<Finding> Findings { get; init; } = [];

    /// <summary>Dropped findings by drop reason (FR-11).</summary>
    public IReadOnlyDictionary<string, int> Dropped { get; init; } = new Dictionary<string, int>();

    public Usage Usage { get; init; } = new();

    public int DroppedCount => Dropped.Values.Sum();

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public static ReviewResult Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number
            || version.GetInt32() != CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported result schema version; expected {CurrentSchemaVersion}.");
        }

        return JsonSerializer.Deserialize<ReviewResult>(json, JsonOptions)
            ?? throw new InvalidDataException("The result file is empty.");
    }

    public async Task WriteAsync(string path, CancellationToken cancellationToken)
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, Serialize(), cancellationToken);
    }

    public static async Task<ReviewResult> ReadAsync(string path, CancellationToken cancellationToken) =>
        Deserialize(await File.ReadAllTextAsync(path, cancellationToken));
}
