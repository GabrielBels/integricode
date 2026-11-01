using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Integricode.Requirements.Jira;

/// <summary>
/// Converts Atlassian Document Format (ADF), the rich text of Jira Cloud's REST API v3, to plain text: blocks separated
/// by a blank line, headings as <c>#</c> lines, bullet and ordered lists as <c>-</c> and <c>1.</c> items, code blocks
/// fenced, hard breaks as new lines, and marks dropped. A node it does not know keeps its text content, and a mention
/// becomes <c>@user</c>, so no person's name reaches the model. The same document always gives the same text, so quotes
/// taken from it pass the provenance check.
/// </summary>
public static class AdfText
{
    private const string Mention = "@user";

    /// <summary>The text of an ADF node, usually a <c>doc</c>; anything that is not a JSON object gives an empty string.</summary>
    public static string ToText(JsonElement node) => node.ValueKind != JsonValueKind.Object ? string.Empty
        : IsInline(node) ? InlineNode(node)
        : Block(node);

    private static string Block(JsonElement node) => TypeOf(node) switch
    {
        "paragraph" => InlineContent(node),
        "heading" => new string('#', Math.Clamp(IntAttribute(node, "level") ?? 1, 1, 6)) + " " + InlineContent(node),
        "bulletList" => List(node, start: null),
        "orderedList" => List(node, start: IntAttribute(node, "order") ?? 1),
        "codeBlock" => "```" + (StringAttribute(node, "language") ?? string.Empty) + "\n" + InlineContent(node) + "\n```",
        _ => Blocks(node, "\n\n"),
    };

    /// <summary>The node's children as blocks joined by the separator; a run of inline children forms one block.</summary>
    private static string Blocks(JsonElement node, string separator)
    {
        var blocks = new List<string>();
        var run = new StringBuilder();
        foreach (var child in Content(node))
        {
            if (IsInline(child))
            {
                run.Append(InlineNode(child));
                continue;
            }

            blocks.Add(run.ToString());
            run.Clear();
            blocks.Add(Block(child));
        }

        blocks.Add(run.ToString());
        return string.Join(separator, blocks.Where(block => !string.IsNullOrWhiteSpace(block)));
    }

    /// <summary>One item per line; an item's further lines are indented under its first.</summary>
    private static string List(JsonElement node, int? start)
    {
        var items = new List<string>();
        var number = start ?? 0;
        foreach (var item in Content(node))
        {
            var marker = start is null ? "- " : (number++).ToString(CultureInfo.InvariantCulture) + ". ";
            items.Add(marker + Blocks(item, "\n").Replace("\n", "\n" + new string(' ', marker.Length), StringComparison.Ordinal));
        }

        return string.Join('\n', items);
    }

    private static string InlineContent(JsonElement node) => string.Concat(Content(node).Select(InlineNode));

    private static string InlineNode(JsonElement node) => TypeOf(node) switch
    {
        "text" => node.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString()! : string.Empty,
        "hardBreak" => "\n",
        "mention" => Mention,
        _ => InlineContent(node),
    };

    private static bool IsInline(JsonElement node) => TypeOf(node) is "text" or "hardBreak" or "mention";

    private static string? TypeOf(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;

    private static IEnumerable<JsonElement> Content(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            ? content.EnumerateArray()
            : [];

    private static JsonElement? Attribute(JsonElement node, string name) =>
        node.TryGetProperty("attrs", out var attributes) && attributes.ValueKind == JsonValueKind.Object && attributes.TryGetProperty(name, out var value)
            ? value
            : null;

    private static int? IntAttribute(JsonElement node, string name) =>
        Attribute(node, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : null;

    private static string? StringAttribute(JsonElement node, string name) =>
        Attribute(node, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}
