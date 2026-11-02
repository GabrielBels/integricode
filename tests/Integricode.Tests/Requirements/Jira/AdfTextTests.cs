using System.Text.Json;
using Integricode.Requirements.Jira;

namespace Integricode.Tests.Requirements.Jira;

public sealed class AdfTextTests
{
    private static string Convert(string json)
    {
        using var document = JsonDocument.Parse(json);
        return AdfText.ToText(document.RootElement);
    }

    [Fact]
    public void Paragraphs_are_separated_by_a_blank_line_and_marks_are_dropped()
    {
        var text = Convert("""
            {"type": "doc", "version": 1, "content": [
              {"type": "paragraph", "content": [
                {"type": "text", "text": "The total applies "},
                {"type": "text", "text": "every", "marks": [{"type": "strong"}]},
                {"type": "text", "text": " discount."}]},
              {"type": "paragraph", "content": [{"type": "text", "text": "Prices are in cents."}]}]}
            """);

        Assert.Equal("The total applies every discount.\n\nPrices are in cents.", text);
    }

    [Fact]
    public void Headings_keep_their_level()
    {
        var text = Convert("""
            {"type": "doc", "version": 1, "content": [
              {"type": "heading", "attrs": {"level": 2}, "content": [{"type": "text", "text": "Acceptance criteria"}]},
              {"type": "heading", "attrs": {"level": 3}, "content": [{"type": "text", "text": "Totals"}]},
              {"type": "paragraph", "content": [{"type": "text", "text": "Rounded to cents."}]}]}
            """);

        Assert.Equal("## Acceptance criteria\n\n### Totals\n\nRounded to cents.", text);
    }

    [Fact]
    public void Bullet_lists_become_dash_items_and_nested_lists_are_indented()
    {
        var text = Convert("""
            {"type": "doc", "version": 1, "content": [
              {"type": "bulletList", "content": [
                {"type": "listItem", "content": [{"type": "paragraph", "content": [{"type": "text", "text": "Discounts apply."}]}]},
                {"type": "listItem", "content": [
                  {"type": "paragraph", "content": [{"type": "text", "text": "Empty carts:"}]},
                  {"type": "bulletList", "content": [
                    {"type": "listItem", "content": [{"type": "paragraph", "content": [{"type": "text", "text": "total zero"}]}]}]}]}]}]}
            """);

        Assert.Equal("- Discounts apply.\n- Empty carts:\n  - total zero", text);
    }

    [Fact]
    public void Ordered_lists_are_numbered_from_their_start()
    {
        var text = Convert("""
            {"type": "doc", "version": 1, "content": [
              {"type": "orderedList", "attrs": {"order": 3}, "content": [
                {"type": "listItem", "content": [{"type": "paragraph", "content": [{"type": "text", "text": "Add an item."}]}]},
                {"type": "listItem", "content": [{"type": "paragraph", "content": [{"type": "text", "text": "Open the cart."}]}]}]},
              {"type": "orderedList", "content": [
                {"type": "listItem", "content": [{"type": "paragraph", "content": [{"type": "text", "text": "Pay."}]}]}]}]}
            """);

        Assert.Equal("3. Add an item.\n4. Open the cart.\n\n1. Pay.", text);
    }

    [Fact]
    public void Code_blocks_are_fenced_with_their_language()
    {
        var text = Convert("""
            {"type": "doc", "version": 1, "content": [
              {"type": "codeBlock", "attrs": {"language": "csharp"}, "content": [{"type": "text", "text": "var total = cart.Total();\nreturn total;"}]},
              {"type": "codeBlock", "content": [{"type": "text", "text": "GET /cart"}]}]}
            """);

        Assert.Equal("```csharp\nvar total = cart.Total();\nreturn total;\n```\n\n```\nGET /cart\n```", text);
    }

    [Fact]
    public void Hard_breaks_become_new_lines()
    {
        var text = Convert("""
            {"type": "doc", "version": 1, "content": [
              {"type": "paragraph", "content": [
                {"type": "text", "text": "First line"}, {"type": "hardBreak"}, {"type": "text", "text": "second line"}]}]}
            """);

        Assert.Equal("First line\nsecond line", text);
    }

    [Fact]
    public void An_unknown_node_keeps_its_text_content()
    {
        var text = Convert("""
            {"type": "doc", "version": 1, "content": [
              {"type": "panel", "attrs": {"panelType": "info"}, "content": [
                {"type": "paragraph", "content": [{"type": "text", "text": "Totals are final."}]},
                {"type": "paragraph", "content": [
                  {"type": "text", "text": "See "}, {"type": "futureInline", "content": [{"type": "text", "text": "the pricing page"}]}, {"type": "text", "text": "."}]}]},
              {"type": "rule"}]}
            """);

        Assert.Equal("Totals are final.\n\nSee the pricing page.", text);
    }

    [Fact]
    public void Inline_nodes_that_keep_their_text_in_attributes_give_it()
    {
        var text = Convert("""
            {"type": "doc", "version": 1, "content": [
              {"type": "paragraph", "content": [
                {"type": "text", "text": "Ship before "}, {"type": "date", "attrs": {"timestamp": "1798761600000"}},
                {"type": "text", "text": " once "}, {"type": "status", "attrs": {"text": "APPROVED", "color": "green"}},
                {"type": "text", "text": ", per "}, {"type": "inlineCard", "attrs": {"url": "https://wiki.example.test/cart-rules"}},
                {"type": "text", "text": " "}, {"type": "emoji", "attrs": {"shortName": ":tada:"}},
                {"type": "date", "attrs": {"timestamp": "not a number"}}]}]}
            """);

        Assert.Equal("Ship before 2027-01-01 once APPROVED, per https://wiki.example.test/cart-rules :tada:", text);
    }

    [Fact]
    public void A_mention_does_not_carry_the_persons_name()
    {
        var text = Convert("""
            {"type": "doc", "version": 1, "content": [
              {"type": "paragraph", "content": [
                {"type": "mention", "attrs": {"id": "synthetic-account-0002", "text": "@Synthetic Person"}},
                {"type": "text", "text": " confirmed the rule."}]}]}
            """);

        Assert.Equal("@user confirmed the rule.", text);
        Assert.DoesNotContain("Synthetic Person", text);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"plain\"")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"type": "doc", "content": "not a list"}""")]
    [InlineData("""{"type": "doc", "content": [1, null, {"type": 7}, {"type": "text", "text": 5}]}""")]
    public void Malformed_input_gives_empty_text(string json)
    {
        Assert.Equal(string.Empty, Convert(json));
    }

    [Fact]
    public void The_same_document_always_gives_the_same_text()
    {
        const string Json = """
            {"type": "doc", "version": 1, "content": [
              {"type": "heading", "attrs": {"level": 1}, "content": [{"type": "text", "text": "Cart"}]},
              {"type": "bulletList", "content": [
                {"type": "listItem", "content": [{"type": "paragraph", "content": [{"type": "text", "text": "One"}, {"type": "hardBreak"}, {"type": "text", "text": "Two"}]}]}]}]}
            """;

        Assert.Equal(Convert(Json), Convert(Json));
        Assert.Equal("# Cart\n\n- One\n  Two", Convert(Json));
    }
}
