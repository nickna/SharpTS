using SharpTS.LanguageServer.Services;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

public class MemberHoverServiceTests
{
    private readonly MemberHoverService _svc = new(); // in-process resolver (BCL)

    // 0-based (line, character) at the start of the n-th occurrence of `needle`.
    private static (int line, int ch) PosOf(string text, string needle, int occurrence = 0)
    {
        int idx = -1;
        for (int i = 0; i <= occurrence; i++) idx = text.IndexOf(needle, idx + 1, System.StringComparison.Ordinal);
        int line = 0, lineStart = 0;
        for (int i = 0; i < idx; i++) if (text[i] == '\n') { line++; lineStart = i + 1; }
        return (line, idx - lineStart);
    }

    [Fact]
    public void DeclarationHover_ShowsRealClrMember()
    {
        const string src =
            "@DotNetType(\"System.Text.StringBuilder\")\n" +
            "declare class StringBuilder { append(value: string): StringBuilder; }";
        var (line, ch) = PosOf(src, "append");

        var hover = _svc.Hover(src, line, ch);

        Assert.NotNull(hover);
        Assert.Contains("Append", hover!.Contents.MarkupContent!.Value); // the real CLR method name
    }

    [Fact]
    public void UsageHover_ResolvesReceiverViaTypeMap()
    {
        const string src =
            "@DotNetType(\"System.Text.StringBuilder\")\n" +
            "declare class StringBuilder { constructor(); append(value: string): StringBuilder; }\n" +
            "const sb = new StringBuilder();\n" +
            "sb.append(\"x\");";
        var (line, ch) = PosOf(src, "append", occurrence: 1); // the `sb.append` usage

        var hover = _svc.Hover(src, line, ch);

        Assert.NotNull(hover);
        Assert.Contains("Append", hover!.Contents.MarkupContent!.Value);
    }

    [Fact]
    public void Hover_OnNonMember_ReturnsNull()
        => Assert.Null(_svc.Hover("const x: number = 1;", line: 0, character: 6));

    [Fact]
    public void UsageHover_ResolvesDotNetImportBinding()
    {
        // dotnet: imports carry no declare-class surface — the binding comes from the
        // import statement, and the receiver resolves through the synthesized class type.
        const string src =
            "import { StringBuilder } from \"dotnet:System.Text.StringBuilder\";\n" +
            "const sb = new StringBuilder();\n" +
            "sb.append(\"x\");";
        var (line, ch) = PosOf(src, "append");

        var hover = _svc.Hover(src, line, ch);

        Assert.NotNull(hover);
        Assert.Contains("Append", hover!.Contents.MarkupContent!.Value);
    }

    [Fact]
    public void UsageHover_ResolvesAliasedDotNetImport()
    {
        const string src =
            "import { StringBuilder as SB } from \"dotnet:System.Text.StringBuilder\";\n" +
            "const sb = new SB();\n" +
            "sb.append(\"x\");";
        var (line, ch) = PosOf(src, "append");

        var hover = _svc.Hover(src, line, ch);

        Assert.NotNull(hover);
        Assert.Contains("Append", hover!.Contents.MarkupContent!.Value);
    }
}
