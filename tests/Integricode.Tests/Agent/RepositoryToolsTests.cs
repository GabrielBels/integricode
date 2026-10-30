using Integricode.Agent;
using Integricode.Domain;
using Integricode.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace Integricode.Tests.Agent;

public sealed class RepositoryToolsTests : IDisposable
{
    private readonly TempRepository repository = new();

    public RepositoryToolsTests()
    {
        repository
            .Write("src/Cart.cs", "namespace Shop;\npublic class Cart\n{\n    public decimal Total;\n}\n")
            .Write("src/Checkout.cs", "// uses Cart.Total\nclass Checkout {}\n")
            .Write(".env", "API_KEY=Cart.Total-secret\n")
            .Write("docs/readme.md", "Cart docs\n");
    }

    private RepositoryTools Tools(ContextBudget? budget = null, IEnumerable<string>? exclusions = null) =>
        new(new PathGuard(repository.Root, exclusions), budget ?? new ContextBudget());

    [Fact]
    public void Read_file_returns_numbered_lines_and_logs_the_chunk()
    {
        var tools = Tools();

        var output = tools.ReadFile("src/Cart.cs", 2, 4);

        Assert.Equal("2: public class Cart\n3: {\n4:     public decimal Total;", output);
        var chunk = Assert.Single(tools.Chunks);
        Assert.Equal(new ContextChunk("read_file", "src/Cart.cs", 2, 4, ContextChunk.HashText(output)), chunk);
        Assert.Equal(1, tools.Budget.ToolCalls);
        Assert.Equal(output.Length, tools.Budget.CharactersRead);
    }

    [Fact]
    public void Read_file_without_a_range_reads_the_whole_file()
    {
        var output = Tools().ReadFile("src/Checkout.cs");

        Assert.Equal("1: // uses Cart.Total\n2: class Checkout {}", output);
    }

    [Theory]
    [InlineData(".env")]
    [InlineData("../outside.txt")]
    [InlineData("/etc/passwd")]
    public void Refused_reads_return_a_refusal_and_log_no_chunk(string path)
    {
        var tools = Tools();

        var output = tools.ReadFile(path);

        Assert.StartsWith("refused: ", output);
        Assert.Empty(tools.Chunks);
        Assert.Equal(0, tools.Budget.CharactersRead);
    }

    [Fact]
    public void The_call_after_forty_is_refused_and_marks_the_budget_reached()
    {
        var tools = Tools();
        for (var i = 0; i < 40; i++)
        {
            Assert.DoesNotContain("refused", tools.ReadFile("src/Cart.cs", 1, 1));
        }

        var output = tools.ListDir(".");

        Assert.StartsWith("refused: context budget reached (40 tool calls)", output);
        Assert.True(tools.Budget.Reached);
        Assert.Equal(40, tools.Budget.ToolCalls);
        Assert.Equal(40, tools.Chunks.Count);
    }

    [Fact]
    public void A_read_past_four_hundred_thousand_characters_is_refused_and_marks_the_budget_reached()
    {
        repository.Write("src/Big.cs", string.Join('\n', Enumerable.Repeat(new string('x', 1000), 400)));
        var tools = Tools();

        var output = tools.ReadFile("src/Big.cs");

        Assert.StartsWith("refused: context budget reached (400000 characters)", output);
        Assert.True(tools.Budget.Reached);
        Assert.Empty(tools.Chunks);
        Assert.Equal(0, tools.Budget.CharactersRead);
    }

    [Fact]
    public void Reads_within_the_character_budget_are_allowed()
    {
        var tools = Tools(new ContextBudget(maxCharacters: 1000));

        Assert.DoesNotContain("refused", tools.ReadFile("src/Cart.cs"));
        Assert.False(tools.Budget.Reached);
    }

    [Fact]
    public void Search_returns_matches_outside_excluded_files_and_logs_each_line()
    {
        var tools = Tools();

        var output = tools.Search("cart.total");

        Assert.Equal("src/Checkout.cs:1: // uses Cart.Total", output);
        Assert.Equal([ContextChunk.Create("search", "src/Checkout.cs", 1, 1, "// uses Cart.Total")], tools.Chunks);
    }

    [Fact]
    public void Search_skips_symlinks_leaving_the_checkout()
    {
        using var outside = new TempRepository().Write("leak.txt", "Cart.Total leaked");
        Directory.CreateSymbolicLink(repository.FullPath("linked"), outside.Root);

        var output = Tools().Search("leaked");

        Assert.Equal("no matches", output);
    }

    [Fact]
    public void List_dir_hides_excluded_entries()
    {
        var output = Tools().ListDir(".");

        Assert.Equal("docs/\nsrc/", output);
    }

    [Fact]
    public void List_dir_applies_configured_exclusions()
    {
        var output = Tools(exclusions: ["docs/"]).ListDir(".");

        Assert.Equal("src/", output);
    }

    [Fact]
    public void Changed_files_use_the_character_budget_but_not_the_call_budget()
    {
        var tools = Tools();

        var output = tools.ReadChangedFile("src/Cart.cs");

        Assert.StartsWith("1: namespace Shop;", output);
        Assert.Equal(0, tools.Budget.ToolCalls);
        Assert.Equal(output!.Length, tools.Budget.CharactersRead);
        Assert.Equal("changed_file", Assert.Single(tools.Chunks).Source);
    }

    [Fact]
    public void An_excluded_changed_file_is_not_read()
    {
        Assert.Null(Tools().ReadChangedFile(".env"));
    }

    [Fact]
    public async Task The_tools_are_exposed_as_ai_functions()
    {
        var tools = Tools();
        var functions = tools.AsAITools().Cast<AIFunction>().ToDictionary(f => f.Name);

        var output = await functions["read_file"].InvokeAsync(new AIFunctionArguments { ["path"] = "src/Cart.cs", ["from"] = 1, ["to"] = 1 });

        Assert.Equal(["list_dir", "read_file", "search"], functions.Keys.Order());
        Assert.Equal("1: namespace Shop;", output?.ToString());
    }

    public void Dispose() => repository.Dispose();
}
