using Integricode.Agent;
using Integricode.Tests.Fakes;

namespace Integricode.Tests.Agent;

public sealed class PathGuardTests : IDisposable
{
    private readonly TempRepository repository = new();
    private readonly TempRepository outside = new();

    public PathGuardTests()
    {
        repository.Write("src/Cart.cs", "class Cart {}");
        outside.Write("secret.txt", "outside secret");
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("src/../../secret.txt")]
    [InlineData("src\\..\\..\\secret.txt")]
    public void Parent_directory_segments_are_refused(string path)
    {
        var check = new PathGuard(repository.Root).Check(path);

        Assert.False(check.Allowed);
        Assert.Equal("parent directory segments are not allowed", check.Refusal);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("\\Windows\\win.ini")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("C:relative.txt")]
    public void Absolute_paths_are_refused(string path)
    {
        var check = new PathGuard(repository.Root).Check(path);

        Assert.False(check.Allowed);
        Assert.Equal("absolute paths are not allowed", check.Refusal);
    }

    [Fact]
    public void An_absolute_path_into_the_checkout_is_still_refused()
    {
        var check = new PathGuard(repository.Root).Check(repository.FullPath("src/Cart.cs"));

        Assert.False(check.Allowed);
    }

    [Fact]
    public void A_directory_symlink_leaving_the_checkout_is_refused()
    {
        Directory.CreateSymbolicLink(repository.FullPath("escape"), outside.Root);

        var check = new PathGuard(repository.Root).Check("escape/secret.txt");

        Assert.False(check.Allowed);
        Assert.Equal("a symbolic link leaves the checkout", check.Refusal);
    }

    [Fact]
    public void A_file_symlink_leaving_the_checkout_is_refused()
    {
        File.CreateSymbolicLink(repository.FullPath("src/link.txt"), outside.FullPath("secret.txt"));

        var check = new PathGuard(repository.Root).Check("src/link.txt");

        Assert.False(check.Allowed);
        Assert.Equal("a symbolic link leaves the checkout", check.Refusal);
    }

    [Fact]
    public void A_symlink_inside_the_checkout_is_allowed()
    {
        File.CreateSymbolicLink(repository.FullPath("alias.cs"), repository.FullPath("src/Cart.cs"));

        Assert.True(new PathGuard(repository.Root).Check("alias.cs").Allowed);
    }

    [Theory]
    [InlineData(".env")]
    [InlineData("config/.env.production")]
    [InlineData("certs/server.pem")]
    [InlineData("certs/server.KEY")]
    [InlineData("deploy/cert.pfx")]
    [InlineData("keys/id_rsa.pub")]
    [InlineData("package-lock.json")]
    [InlineData("web/yarn.lock")]
    [InlineData("web/app.min.js")]
    [InlineData(".git/config")]
    [InlineData("lib/native.dll")]
    [InlineData("assets/logo.png")]
    public void Default_exclusions_are_refused(string path)
    {
        var check = new PathGuard(repository.Root).Check(path);

        Assert.False(check.Allowed);
        Assert.Equal("the path is excluded from the review context", check.Refusal);
    }

    [Theory]
    [InlineData("secrets/**", "secrets/prod/db.txt", true)]
    [InlineData("**/*.generated.cs", "src/deep/Model.generated.cs", true)]
    [InlineData("docs/private/", "docs/private/plan.md", true)]
    [InlineData("internal.md", "notes/internal.md", true)]
    [InlineData("secrets/**", "src/secrets.cs", false)]
    public void Configured_exclusions_are_applied(string glob, string path, bool excluded)
    {
        var guard = new PathGuard(repository.Root, [glob]);

        Assert.Equal(excluded, guard.IsExcluded(path));
    }

    [Theory]
    [InlineData("src/Cart.cs", "src/Cart.cs")]
    [InlineData("./src//Cart.cs", "src/Cart.cs")]
    [InlineData("src\\Cart.cs", "src/Cart.cs")]
    [InlineData(".", "")]
    public void Paths_inside_the_checkout_are_normalized(string path, string normalized)
    {
        var check = new PathGuard(repository.Root).Check(path);

        Assert.True(check.Allowed);
        Assert.Equal(normalized, check.RelativePath);
    }

    [Fact]
    public void Exclusions_can_be_skipped_for_fixed_inputs()
    {
        Assert.True(new PathGuard(repository.Root).Check(".env", applyExclusions: false).Allowed);
    }

    public void Dispose()
    {
        repository.Dispose();
        outside.Dispose();
    }
}
