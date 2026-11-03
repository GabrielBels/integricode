using Integricode.Agent;

namespace Integricode.Tests.Agent;

public class LensCatalogTests
{
    public static TheoryData<string[], string[]> Selections() => new()
    {
        { ["src/Cart.cs"], [LensCatalog.CSharp, LensCatalog.Security] },
        { ["web/app.tsx", "web/site.css"], [LensCatalog.React, LensCatalog.Security] },
        { ["tools/sync.py"], [LensCatalog.Python, LensCatalog.Security] },
        { [".github/workflows/ci.yml"], [LensCatalog.GitHubActions, LensCatalog.Security] },
        { ["deploy/Dockerfile.api", "docker-compose.prod.yml"], [LensCatalog.Containers, LensCatalog.Security] },
        { ["Directory.Packages.props", "api/openapi.yaml"], [LensCatalog.Contracts, LensCatalog.Security] },
        { ["src/Shop.csproj"], [LensCatalog.CSharp, LensCatalog.Contracts, LensCatalog.Security] },
        { ["packages.config"], [LensCatalog.Contracts, LensCatalog.Security] },
        { ["web/package.json"], [LensCatalog.React, LensCatalog.Contracts, LensCatalog.Security] },
        { ["Pipfile", "tools/types.pyi"], [LensCatalog.Python, LensCatalog.Contracts, LensCatalog.Security] },
        { ["requirements-dev.txt"], [LensCatalog.Python, LensCatalog.Contracts, LensCatalog.Security] },
        { [".dockerignore", "api.dockerfile"], [LensCatalog.Containers, LensCatalog.Security] },
        { ["events/asyncapi.yaml"], [LensCatalog.Contracts, LensCatalog.Security] },
        { ["schemas/order.schema.json"], [LensCatalog.Contracts, LensCatalog.Security] },
        { ["src/Api/OrdersController.cs"], [LensCatalog.CSharp, LensCatalog.Contracts, LensCatalog.Security] },
        { ["Controllers/Home.cs"], [LensCatalog.CSharp, LensCatalog.Contracts, LensCatalog.Security] },
        { ["src/Api/Controllers/Routes.cs"], [LensCatalog.CSharp, LensCatalog.Contracts, LensCatalog.Security] },
        { ["src/AsyncApiClient.cs"], [LensCatalog.CSharp, LensCatalog.Security] },
        { ["tests/Controllers/OrdersControllerTests.cs"], [LensCatalog.CSharp, LensCatalog.Security] },
        { ["test/Controllers/OrdersControllerTests.cs"], [LensCatalog.CSharp, LensCatalog.Security] },
        { ["src/Shop.Tests/Controllers/OrdersControllerTests.cs"], [LensCatalog.CSharp, LensCatalog.Security] },
        { ["src/Shop.UnitTests/Controllers/OrdersControllerTests.cs"], [LensCatalog.CSharp, LensCatalog.Security] },
        { ["src/Shop.Test/Controllers/OrdersControllerTests.cs"], [LensCatalog.CSharp, LensCatalog.Security] },
        { ["src/Contests/Controllers/EntriesController.cs"], [LensCatalog.CSharp, LensCatalog.Contracts, LensCatalog.Security] },
        { ["src/Shop.Latest/Controllers/OrdersController.cs"], [LensCatalog.CSharp, LensCatalog.Contracts, LensCatalog.Security] },
        { ["src/Acme.Contests/Controllers/EntriesController.cs"], [LensCatalog.CSharp, LensCatalog.Contracts, LensCatalog.Security] },
        { ["src/OpenApiClient.cs", "src/SwaggerSetup.cs"], [LensCatalog.CSharp, LensCatalog.Security] },
        { ["api/swagger.json"], [LensCatalog.Contracts, LensCatalog.Security] },
        { ["docs/guide.md", "README.md"], [LensCatalog.Docs] },
        { ["docs/guide.md", "src/Cart.cs"], [LensCatalog.CSharp, LensCatalog.Docs, LensCatalog.Security] },
        { ["config/settings.json"], [LensCatalog.Security] },
        { [], [] },
    };

    [Theory]
    [MemberData(nameof(Selections))]
    public void The_files_choose_the_lenses_and_security_covers_every_change_beyond_docs(string[] paths, string[] lenses)
    {
        Assert.Equal(lenses, LensCatalog.Select(paths));
    }

    [Theory]
    [InlineData(LensCatalog.CSharp)]
    [InlineData(LensCatalog.Python)]
    [InlineData(LensCatalog.React)]
    [InlineData(LensCatalog.GitHubActions)]
    [InlineData(LensCatalog.Containers)]
    [InlineData(LensCatalog.Contracts)]
    [InlineData(LensCatalog.Docs)]
    [InlineData(LensCatalog.Security)]
    public void Every_lens_is_embedded_with_a_hunting_list_and_no_step_the_review_cannot_run(string lens)
    {
        var text = LensCatalog.Load(lens);

        Assert.StartsWith("# ", text);
        Assert.Contains("## Typical light", text);
        Assert.DoesNotContain("<skill>", text);
        Assert.DoesNotContain("post_review", text);
        Assert.DoesNotContain("fetch_owasp", text);
    }
}
