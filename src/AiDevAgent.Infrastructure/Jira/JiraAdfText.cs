using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiDevAgent.Infrastructure.Jira;

/// <summary>Converts Jira Cloud ADF (or a plain string) into agent-readable text.</summary>
public static class JiraAdfText
{
    public static string From(JsonElement node)
    {
        if (node.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return string.Empty;
        if (node.ValueKind == JsonValueKind.String)
            return node.GetString() ?? string.Empty;
        if (node.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
            return string.Empty;

        var builder = new StringBuilder();
        Walk(node, builder);
        var text = Regex.Replace(builder.ToString(), @"[ \t]+\n", "\n");
        return Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
    }

    private static void Walk(JsonElement node, StringBuilder builder)
    {
        if (node.ValueKind == JsonValueKind.String)
        {
            builder.Append(node.GetString());
            return;
        }

        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray())
                Walk(child, builder);
            return;
        }

        if (node.ValueKind != JsonValueKind.Object)
            return;

        var type = node.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : null;
        switch (type)
        {
            case "hardBreak":
            case "rule":
                builder.AppendLine();
                break;
            case "mention":
                AppendAttr(node, "text", builder);
                break;
            case "emoji":
                AppendAttr(node, "shortName", builder);
                break;
            case "inlineCard":
            case "blockCard":
            case "embedCard":
                AppendAttr(node, "url", builder);
                break;
            case "text":
                if (node.TryGetProperty("text", out var text))
                    builder.Append(text.GetString());
                break;
            case "listItem":
                builder.Append("- ");
                break;
        }

        if (type is "codeBlock")
            builder.AppendLine();

        if (node.TryGetProperty("content", out var content))
            Walk(content, builder);
        else if (type is null && node.TryGetProperty("text", out var fallback))
            builder.Append(fallback.GetString());

        if (type is "paragraph" or "heading" or "listItem" or "codeBlock" or "blockquote")
            builder.AppendLine();
    }

    private static void AppendAttr(JsonElement node, string name, StringBuilder builder)
    {
        if (node.TryGetProperty("attrs", out var attrs) && attrs.TryGetProperty(name, out var value))
            builder.Append(value.GetString());
    }
}
