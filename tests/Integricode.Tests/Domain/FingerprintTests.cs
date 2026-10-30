using Integricode.Domain;

namespace Integricode.Tests.Domain;

public class FingerprintTests
{
    [Fact]
    public void Equal_findings_have_equal_fingerprints()
    {
        var a = new Finding(FindingKind.Bug, "Null customer", "src/Cart.cs", 20, "A guest checkout throws.");
        var b = new Finding(FindingKind.Bug, "Null customer", "src/Cart.cs", 20, "A guest checkout throws.");

        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Matches("^[0-9a-f]{64}$", a.Fingerprint);
    }

    [Fact]
    public void Whitespace_differences_do_not_change_the_fingerprint()
    {
        var a = Fingerprint.Compute("src/Cart.cs", 20, "Null   customer", "A guest\n checkout throws.");
        var b = Fingerprint.Compute("src/Cart.cs", 20, " Null customer ", "A guest checkout\tthrows.");

        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("src/Other.cs", 20, "Null customer", "A guest checkout throws.")]
    [InlineData("src/Cart.cs", 21, "Null customer", "A guest checkout throws.")]
    [InlineData("src/Cart.cs", 20, "Null customers", "A guest checkout throws.")]
    [InlineData("src/Cart.cs", 20, "Null customer", "A member checkout throws.")]
    public void A_changed_path_line_or_text_changes_the_fingerprint(string path, int line, string title, string scenario)
    {
        var original = Fingerprint.Compute("src/Cart.cs", 20, "Null customer", "A guest checkout throws.");

        Assert.NotEqual(original, Fingerprint.Compute(path, line, title, scenario));
    }

    [Fact]
    public void Field_boundaries_are_unambiguous()
    {
        Assert.NotEqual(
            Fingerprint.Compute("a", 1, "b c", "d"),
            Fingerprint.Compute("a", 1, "b", "c d"));
    }
}
