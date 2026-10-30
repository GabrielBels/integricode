using Integricode.GitHub;

namespace Integricode.Tests.GitHub;

public class DiffMapTests
{
    private const string TwoHunks =
        "@@ -1,4 +1,5 @@\n" +
        " namespace Shop;\n" +
        "-public class Cart\n" +
        "+public sealed class Cart\n" +
        " {\n" +
        "+    // added\n" +
        "     int total;\n" +
        "@@ -20,3 +21,4 @@ public void Add()\n" +
        "     a();\n" +
        "-    b();\n" +
        "+    c();\n" +
        "+    d();\n" +
        "\\ No newline at end of file";

    [Fact]
    public void Changed_lines_are_exactly_the_added_and_modified_lines_of_each_hunk()
    {
        var diff = DiffMap.Parse(TwoHunks);

        Assert.True(diff.HasPatch);
        Assert.Equal(new[] { 2, 4, 22, 23 }, diff.ChangedLines);
    }

    [Fact]
    public void Diff_lines_include_context_lines_shown_in_the_hunks()
    {
        var diff = DiffMap.Parse(TwoHunks);

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 21, 22, 23 }, diff.DiffLines);
    }

    [Fact]
    public void A_deletion_only_hunk_changes_no_right_side_line()
    {
        var diff = DiffMap.Parse("@@ -3,2 +3,0 @@\n-gone();\n-gone2();");

        Assert.Empty(diff.ChangedLines);
    }

    [Fact]
    public void A_new_file_has_every_line_changed()
    {
        var diff = DiffMap.Parse("@@ -0,0 +1,3 @@\n+a\n+b\n+c\n");

        Assert.Equal(new[] { 1, 2, 3 }, diff.ChangedLines);
    }

    [Fact]
    public void A_file_without_a_patch_is_listed_but_has_no_changed_lines()
    {
        var map = DiffMap.Build([new PullRequestFile("img/logo.png", "added", null), new PullRequestFile("src/A.cs", "modified", "@@ -1 +1 @@\n-a\n+b")]);

        Assert.True(map.Contains("img/logo.png"));
        Assert.False(map.Get("img/logo.png")!.HasPatch);
        Assert.False(map.IsChangedLine("img/logo.png", 1));
        Assert.True(map.IsChangedLine("src/A.cs", 1));
        Assert.False(map.Contains("src/B.cs"));
    }

    [Fact]
    public void A_blank_context_line_without_its_leading_space_still_counts()
    {
        var diff = DiffMap.Parse("@@ -1,3 +1,3 @@\n a\n\n-b\n+c\n");

        Assert.Equal(new[] { 3 }, diff.ChangedLines);
        Assert.Equal(new[] { 1, 2, 3 }, diff.DiffLines);
    }

    [Fact]
    public void Windows_line_endings_in_a_patch_are_handled()
    {
        var diff = DiffMap.Parse("@@ -1,2 +1,2 @@\r\n a\r\n-b\r\n+c\r\n");

        Assert.Equal(new[] { 2 }, diff.ChangedLines);
    }
}
