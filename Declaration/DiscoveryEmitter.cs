using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpTS.Declaration;

/// <summary>
/// Renders a <see cref="DiscoveryReport"/> for human reading (text) or tooling (<c>--json</c>).
/// Being discovery-oriented, it has no obligation to produce syntactically valid TypeScript, so it
/// can show unsupported members transparently instead of mangling them into invalid syntax.
/// </summary>
public static class DiscoveryEmitter
{
    private const string UsableMarker = "[usable]     ";
    private const string UnsupportedMarker = "[unsupported]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Keep generic-argument angle brackets and quotes readable in signatures rather than
        // <-escaping them — this output is a dev tool, not HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Serializes the report as indented JSON for editor/tooling consumption.</summary>
    public static string EmitJson(DiscoveryReport report) => JsonSerializer.Serialize(report, JsonOptions);

    /// <summary>Renders the report as human-readable text.</summary>
    public static string EmitText(DiscoveryReport report) => report.Kind switch
    {
        DiscoveryKind.TypeDetail => EmitTypeDetail(report.Type!),
        DiscoveryKind.TableOfContents => EmitTableOfContents(report),
        _ => throw new ArgumentOutOfRangeException(nameof(report))
    };

    private static string EmitTypeDetail(TypeReport type)
    {
        var sb = new StringBuilder();
        sb.Append(type.FullName).Append(" — ").Append(type.Kind).AppendLine();

        if (type.ImportLine != null)
        {
            sb.AppendLine();
            sb.Append("  ").AppendLine(type.ImportLine);
        }
        else if (type.UnsupportedTypeReason != null)
        {
            sb.AppendLine();
            sb.Append("  This type cannot be imported: ").AppendLine(type.UnsupportedTypeReason);
        }

        // Group members by category, preserving first-seen order.
        var categories = new List<string>();
        var byCategory = new Dictionary<string, List<MemberReport>>();
        foreach (var member in type.Members)
        {
            if (!byCategory.TryGetValue(member.Category, out var list))
            {
                list = [];
                byCategory[member.Category] = list;
                categories.Add(member.Category);
            }
            list.Add(member);
        }

        foreach (var category in categories)
        {
            sb.AppendLine();
            sb.Append("  ").Append(category).AppendLine(":");
            foreach (var member in byCategory[category])
            {
                EmitMemberLine(sb, member);
            }
        }

        if (type.Members.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("  (no public members)");
        }

        return sb.ToString();
    }

    private static void EmitMemberLine(StringBuilder sb, MemberReport member)
    {
        // Enum values carry no usability marker — they are always plain numbers.
        if (member.Category == "Values")
        {
            sb.Append("    ").AppendLine(member.Signature);
            return;
        }

        string marker = member.Usable ? UsableMarker : UnsupportedMarker;
        sb.Append("    ").Append(marker).Append(' ').Append(member.Signature);
        if (!member.Usable && member.UnsupportedReason != null)
        {
            sb.Append("   — ").Append(member.UnsupportedReason);
        }
        sb.AppendLine();
    }

    private static string EmitTableOfContents(DiscoveryReport report)
    {
        var types = report.Types!;
        var sb = new StringBuilder();

        int usable = types.Count(t => t.Usable);
        int unsupported = types.Count - usable;

        sb.Append(report.Scope).Append(" — ")
          .Append(types.Count).Append(" public type(s), ")
          .Append(usable).Append(" usable")
          .Append(unsupported > 0 ? $", {unsupported} unsupported" : "")
          .AppendLine();
        sb.AppendLine();

        foreach (var entry in types)
        {
            string marker = entry.Usable ? UsableMarker : UnsupportedMarker;
            sb.Append("  ").Append(marker).Append(' ')
              .Append(entry.Kind.PadRight(14)).Append(' ')
              .Append(entry.FullName);
            if (!entry.Usable && entry.UnsupportedReason != null)
            {
                sb.Append("   — ").Append(entry.UnsupportedReason);
            }
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("For a full member breakdown and the dotnet: import line, run:");
        sb.Append("    sharpts --gen-decl <TypeName>").AppendLine();

        return sb.ToString();
    }
}
