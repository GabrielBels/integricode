using Integricode.Domain;

namespace Integricode.Tests.Validation;

public class ContextSnapshotTests
{
    private static ContextSnapshot Snapshot(
        string headSha = "abc123",
        string? patch = "@@ -1 +1 @@\n-a\n+b",
        string? requirement = "## [REQ-001] Cart\n- [ ] Totals.",
        string chunkContent = "1: namespace Shop;") =>
        new(
            headSha,
            [new DiffFile("src/Cart.cs", patch), new DiffFile("img/logo.png", null)],
            requirement,
            [
                ContextChunk.Create("changed_file", "src/Cart.cs", 1, 1, "1: b"),
                ContextChunk.Create("read_file", "src/Shop.cs", 1, 1, chunkContent),
            ]);

    [Fact]
    public void Equal_inputs_give_an_equal_hash()
    {
        Assert.Equal(Snapshot().ComputeHash(), Snapshot().ComputeHash());
        Assert.Matches("^[0-9a-f]{64}$", Snapshot().ComputeHash());
    }

    [Fact]
    public void One_changed_chunk_changes_the_hash()
    {
        Assert.NotEqual(Snapshot().ComputeHash(), Snapshot(chunkContent: "1: namespace Shop2;").ComputeHash());
    }

    [Fact]
    public void The_head_sha_diff_and_requirement_are_part_of_the_hash()
    {
        var original = Snapshot().ComputeHash();

        Assert.NotEqual(original, Snapshot(headSha: "def456").ComputeHash());
        Assert.NotEqual(original, Snapshot(patch: "@@ -1 +1 @@\n-a\n+c").ComputeHash());
        Assert.NotEqual(original, Snapshot(patch: null).ComputeHash());
        Assert.NotEqual(original, Snapshot(requirement: null).ComputeHash());
    }

    [Fact]
    public void Chunk_order_is_part_of_the_hash()
    {
        var a = ContextChunk.Create("read_file", "a.cs", 1, 1, "x");
        var b = ContextChunk.Create("read_file", "b.cs", 1, 1, "y");

        Assert.NotEqual(
            new ContextSnapshot("sha", [], null, [a, b]).ComputeHash(),
            new ContextSnapshot("sha", [], null, [b, a]).ComputeHash());
    }
}
