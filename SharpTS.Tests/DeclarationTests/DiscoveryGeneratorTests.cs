using System.Text.Json;
using SharpTS.Declaration;
using Xunit;

namespace SharpTS.Tests.DeclarationTests;

/// <summary>
/// Tests for the <c>--gen-decl</c> discovery/inspection tool (issue #1194): faithful .NET
/// signatures, per-member usability classification, the <c>dotnet:</c> import line, namespace vs
/// type granularity, and <c>--json</c> output.
/// </summary>
public class DiscoveryGeneratorTests
{
    private readonly DiscoveryGenerator _generator = new();

    // ── Faithful type rendering (DotNetTypeMapper.Describe) ──────────

    [Theory]
    [InlineData(typeof(int), "int")]
    [InlineData(typeof(string), "string")]
    [InlineData(typeof(void), "void")]
    [InlineData(typeof(int[]), "int[]")]
    [InlineData(typeof(int?), "int?")]
    [InlineData(typeof(List<int>), "List<int>")]
    [InlineData(typeof(Dictionary<string, int>), "Dictionary<string, int>")]
    [InlineData(typeof(ReadOnlySpan<char>), "ReadOnlySpan<char>")]
    public void Describe_RendersFaithfulDotNetShape(Type type, string expected)
    {
        Assert.Equal(expected, DotNetTypeMapper.Describe(type));
    }

    // ── Interop classification ───────────────────────────────────────

    [Fact]
    public void Classifier_MarksPlainTypesUsable()
    {
        Assert.Null(DotNetInteropClassifier.UnsupportedSlotReason(typeof(int)));
        Assert.Null(DotNetInteropClassifier.UnsupportedSlotReason(typeof(string)));
        Assert.Null(DotNetInteropClassifier.UnsupportedSlotReason(typeof(List<int>)));
    }

    [Fact]
    public void Classifier_MarksRefStructUnsupported()
    {
        Assert.Equal(
            DotNetInteropClassifier.ReasonRefStruct,
            DotNetInteropClassifier.UnsupportedSlotReason(typeof(ReadOnlySpan<char>)));
    }

    [Fact]
    public void Classifier_MarksByRefUnsupported()
    {
        Assert.Equal(
            DotNetInteropClassifier.ReasonByRef,
            DotNetInteropClassifier.UnsupportedSlotReason(typeof(int).MakeByRefType()));
    }

    [Fact]
    public void Classifier_MarksPointerUnsupported()
    {
        Assert.Equal(
            DotNetInteropClassifier.ReasonPointer,
            DotNetInteropClassifier.UnsupportedSlotReason(typeof(int).MakePointerType()));
    }

    [Fact]
    public void Classifier_MarksOpenGenericUnsupported()
    {
        Type openArg = typeof(List<>).GetGenericArguments()[0]; // the unbound T
        Assert.Equal(
            DotNetInteropClassifier.ReasonOpenGeneric,
            DotNetInteropClassifier.UnsupportedSlotReason(openArg));
    }

    // ── Type-detail report ───────────────────────────────────────────

    [Fact]
    public void GenerateForType_StringBuilder_HasImportLineAndUsableAppend()
    {
        var report = _generator.Generate("System.Text.StringBuilder");

        Assert.Equal(DiscoveryKind.TypeDetail, report.Kind);
        Assert.NotNull(report.Type);
        Assert.Equal(
            "import { StringBuilder } from \"dotnet:System.Text.StringBuilder\";",
            report.Type!.ImportLine);

        // append(value: string): StringBuilder is marshalable → usable.
        var stringAppend = report.Type.Members.FirstOrDefault(m =>
            m.Signature == "append(value: string): StringBuilder");
        Assert.NotNull(stringAppend);
        Assert.True(stringAppend!.Usable);
        Assert.Null(stringAppend.UnsupportedReason);
    }

    [Fact]
    public void GenerateForType_StringBuilder_FlagsSpanOverloadUnsupported()
    {
        var report = _generator.Generate("System.Text.StringBuilder");

        // Every ReadOnlySpan<char> Append overload is unsupported with the ref-struct reason.
        var spanAppends = report.Type!.Members
            .Where(m => m.Signature.Contains("ReadOnlySpan<char>") && m.Signature.StartsWith("append("))
            .ToList();

        Assert.NotEmpty(spanAppends);
        Assert.All(spanAppends, m =>
        {
            Assert.False(m.Usable);
            Assert.Equal(DotNetInteropClassifier.ReasonRefStruct, m.UnsupportedReason);
        });
    }

    [Fact]
    public void GenerateForType_Guid_RendersOutParamFaithfullyAndFlagsIt()
    {
        var report = _generator.Generate("System.Guid");

        // tryParse(input: string, result: out Guid): bool — faithful `out`, by-ref unsupported.
        var tryParse = report.Type!.Members.FirstOrDefault(m =>
            m.Signature == "static tryParse(input: string, result: out Guid): bool");
        Assert.NotNull(tryParse);
        Assert.False(tryParse!.Usable);
        Assert.Equal(DotNetInteropClassifier.ReasonByRef, tryParse.UnsupportedReason);
    }

    [Fact]
    public void GenerateForType_Guid_ReportsStructKind()
    {
        var report = _generator.Generate("System.Guid");
        Assert.Equal("struct", report.Type!.Kind);
    }

    // ── Namespace table of contents ──────────────────────────────────

    [Fact]
    public void Generate_Namespace_ReturnsTableOfContents()
    {
        var report = _generator.Generate("System.Text");

        Assert.Equal(DiscoveryKind.TableOfContents, report.Kind);
        Assert.NotNull(report.Types);
        Assert.Contains(report.Types!, t => t.FullName == "System.Text.StringBuilder" && t.Usable);
        // Ref-struct enumerators in the namespace are listed but marked unsupported.
        Assert.Contains(report.Types!, t => !t.Usable);
    }

    [Fact]
    public void Generate_UnresolvableInput_Throws()
    {
        Assert.Throws<ArgumentException>(() => _generator.Generate("Nope.Not.A.Real.Type"));
    }

    // ── Emitter output ───────────────────────────────────────────────

    [Fact]
    public void EmitText_TypeDetail_IncludesMarkersAndImport()
    {
        var report = _generator.Generate("System.Text.StringBuilder");
        string text = DiscoveryEmitter.EmitText(report);

        Assert.Contains("import { StringBuilder } from \"dotnet:System.Text.StringBuilder\";", text);
        Assert.Contains("[usable]", text);
        Assert.Contains("[unsupported]", text);
        Assert.Contains("ReadOnlySpan<char>", text);
    }

    [Fact]
    public void EmitJson_ProducesValidParseableJson()
    {
        var report = _generator.Generate("System.Guid");
        string json = DiscoveryEmitter.EmitJson(report);

        using var doc = JsonDocument.Parse(json); // throws if invalid
        var root = doc.RootElement;
        Assert.Equal("typeDetail", root.GetProperty("kind").GetString());
        Assert.Equal("System.Guid", root.GetProperty("query").GetString());
        Assert.True(root.GetProperty("type").GetProperty("members").GetArrayLength() > 0);
        // Angle brackets stay readable (relaxed escaping), not <-escaped.
        Assert.DoesNotContain("\\u003C", json);
    }
}
