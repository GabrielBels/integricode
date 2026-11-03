using Integricode.Domain;
using Integricode.Requirements;

namespace Integricode.Tests.Domain;

public class ReviewResultTests
{
    private static ReviewResult Sample() => new()
    {
        Repository = "octo/sample",
        PullRequestNumber = 7,
        HeadSha = "0123456789abcdef0123456789abcdef01234567",
        ContextHash = new string('a', 64),
        Requirement = new RequirementLink(RequirementKind.ReqEntry, "REQ-001", RequirementLinkSource.Branch),
        Coverage = Coverage.Incomplete(["context budget reached (40 tool calls)"]),
        Findings =
        [
            new Finding(FindingKind.RequirementMismatch, "Total ignores discounts", "src/Cart.cs", 12, "A cart with a 10% discount totals 100 instead of 90.", "The total applies every discount."),
            new Finding(FindingKind.Bug, "Null customer", "src/Cart.cs", 20, "A guest checkout throws NullReferenceException."),
        ],
        Dropped = new Dictionary<string, int> { ["quote-not-found"] = 2, ["line-not-found"] = 1 },
        Usage = new Usage(InputTokens: 1200, OutputTokens: 300, ToolCalls: 4, CharactersRead: 5000),
    };

    [Fact]
    public void Round_trip_keeps_every_field_and_the_schema_version()
    {
        var original = Sample();

        var json = original.Serialize();
        var restored = ReviewResult.Deserialize(json);

        Assert.Contains("\"schemaVersion\": 2", json);
        Assert.Equal(ReviewResult.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Equal(original.Repository, restored.Repository);
        Assert.Equal(original.PullRequestNumber, restored.PullRequestNumber);
        Assert.Equal(original.HeadSha, restored.HeadSha);
        Assert.Equal(original.ContextHash, restored.ContextHash);
        Assert.Equal(original.Requirement, restored.Requirement);
        Assert.Equal(original.Coverage.State, restored.Coverage.State);
        Assert.Equal(original.Coverage.Reasons, restored.Coverage.Reasons);
        Assert.Equal(original.Findings, restored.Findings);
        Assert.Equal(original.Dropped, restored.Dropped);
        Assert.Equal(3, restored.DroppedCount);
        Assert.Equal(original.Usage, restored.Usage);
        Assert.Equal(json, restored.Serialize());
    }

    [Fact]
    public void Enums_are_written_as_kebab_case_strings()
    {
        var json = Sample().Serialize();

        Assert.Contains("\"requirement-mismatch\"", json);
        Assert.Contains("\"bug\"", json);
        Assert.Contains("\"incomplete\"", json);
        Assert.Contains("\"req-entry\"", json);
    }

    [Fact]
    public void Findings_carry_their_fingerprint_in_the_file()
    {
        var result = Sample();

        var json = result.Serialize();

        Assert.Contains(result.Findings[0].Fingerprint, json);
    }

    [Theory]
    [InlineData("{\"schemaVersion\": 1, \"repository\": \"o/r\", \"pullRequestNumber\": 1, \"headSha\": \"x\", \"coverage\": {\"state\": \"complete\", \"reasons\": []}}")]
    [InlineData("{\"repository\": \"o/r\", \"pullRequestNumber\": 1, \"headSha\": \"x\", \"coverage\": {\"state\": \"complete\", \"reasons\": []}}")]
    public void Unsupported_or_missing_schema_version_is_rejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => ReviewResult.Deserialize(json));
    }

    [Fact]
    public async Task Write_and_read_a_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"integricode-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "result.json");
        try
        {
            await Sample().WriteAsync(path, CancellationToken.None);

            var restored = await ReviewResult.ReadAsync(path, CancellationToken.None);

            Assert.Equal(Sample().Findings, restored.Findings);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
