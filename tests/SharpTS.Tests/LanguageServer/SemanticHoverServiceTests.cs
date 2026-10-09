using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Services;
using SharpTS.Parsing;
using SharpTS.Tests.IntegrationTests;
using Xunit;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.Tests.LanguageServer;

public sealed class SemanticHoverServiceTests
{
    [Fact]
    public async Task InferredDeclarationWideningAndFlowOccurrenceTypesRemainSeparate()
    {
        using var project = new HoverProject("let /*inferred*/widened = 1; const /*declaration*/value: string | number = 2; /*use*/value;");

        await project.AssertTypeAsync("inferred", "widened", "number");
        var declaration = await project.AssertTypeAsync("declaration", "value", "string");
        Assert.Contains("number", Text(declaration));
        var occurrence = await project.AssertTypeAsync("use", "value", "number");
        Assert.DoesNotContain("string", Text(occurrence));
    }

    [Fact]
    public async Task ShadowedAndContextualParametersUseTheirActualSourceOwners()
    {
        using var project = new HoverProject("""
            function read(/*parameter*/item: string): string {
                { const item: number = 1; /*inner*/item; }
                return /*outer*/item;
            }
            const callback: (item: number) => number = /*arrow*/item => /*arrowUse*/item;
            """);

        await project.AssertTypeAsync("parameter", "item", "string");
        await project.AssertTypeAsync("inner", "item", "number");
        await project.AssertTypeAsync("outer", "item", "string");
        await project.AssertTypeAsync("arrow", "item", "number");
        await project.AssertTypeAsync("arrowUse", "item", "number");
    }

    [Fact]
    public async Task FunctionsExposeGenericAndPublicOverloadSignaturesWithoutImplementationAny()
    {
        using var project = new HoverProject("""
            function /*generic*/identity<T>(value: T): T { return value; }
            function convert(value: string): string;
            function convert(value: number): number;
            function /*implementation*/convert(value: any): any { return value; }
            /*use*/convert(1);
            """);

        var generic = await project.AssertTypeAsync("generic", "identity", "<T>");
        Assert.Contains("value: T", Text(generic));
        foreach (string marker in new[] { "implementation", "use" })
        {
            var hover = await project.AssertTypeAsync(marker, "convert", "string");
            Assert.Contains("number", Text(hover));
            Assert.DoesNotContain("any", Text(hover));
        }
    }

    [Fact]
    public async Task NamedTypesUseResolvedTypeContextAndNeverConfuseAQualifiedNamespaceWithItsTerminalType()
    {
        using var project = new HoverProject("""
            class Box<T> { value: T; }
            const box: /*generic*/Box</*argument*/number> = new Box<number>();
            namespace N { export type T = number; }
            const value: /*namespace*/N./*terminal*/T = 1;
            """);

        var generic = await project.AssertTypeAsync("generic", "Box", "Box<number>");
        Assert.DoesNotContain("typeof", Text(generic));
        await project.AssertTypeAsync("argument", "number", "number");
        await project.AssertTypeAsync("terminal", "T", "number");
        var prefix = await project.QueryAsync("namespace");
        if (prefix is not null)
        {
            project.AssertRange(prefix, "namespace", "N");
            Assert.DoesNotContain("number", Text(prefix));
        }
    }

    [Fact]
    public async Task NamespaceValueMembersKeepTheirExactLexicalBindingProof()
    {
        using var project = new HoverProject("namespace N { export const value: number = 1; } N./*member*/value;");

        await project.AssertTypeAsync("member", "value", "number");
    }

    [Fact]
    public async Task ContextualKeywordImportsKeepOriginalSourceAliasAndUseRanges()
    {
        using var project = new HoverProject("import { /*source*/type as /*alias*/from } from './original'; /*use*/from;");
        project.AddFile("original.ts", "export let item: number = 1; export { item as type };");

        await project.AssertTypeAsync("source", "type", "number");
        await project.AssertTypeAsync("alias", "from", "number");
        await project.AssertTypeAsync("use", "from", "number");
    }

    [Fact]
    public async Task UnknownAnnotationsCannotMasqueradeAsCheckedAny()
    {
        using var project = new HoverProject("let /*declaration*/value: /*annotation*/Missing; /*use*/value; let explicit: any; /*any*/explicit;");

        foreach (string marker in new[] { "declaration", "annotation", "use" })
            Assert.Null(await project.QueryAsync(marker));
        await project.AssertTypeAsync("any", "explicit", "any");
    }

    [Fact]
    public async Task ActualReadAnyCannotBypassAnUnprovenSourceMemberAnnotation()
    {
        using var project = new HoverProject("class Box { /*declaration*/value: Missing; } const box = new Box(); box./*use*/value;");

        Assert.Null(await project.QueryAsync("declaration"));
        Assert.Null(await project.QueryAsync("use"));
    }

    [Fact]
    public async Task FieldWritesPrivateCallsAndBrandChecksDoNotDisplayTheirExpressionResultType()
    {
        using var project = new HoverProject("""
            class Box {
                value: string | number = 1;
                #secret: number = 1;
                #read(input: number): string { return 'result'; }
                inspect(other: Box, candidate: object): boolean {
                    other./*privateCall*/#read(1);
                    return /*brand*/#secret in candidate;
                }
            }
            const box = new Box();
            box./*write*/value = 2;
            """);

        var write = await project.AssertTypeAsync("write", "value", "string");
        Assert.Contains("number", Text(write));
        var call = await project.AssertTypeAsync("privateCall", "#read", "input: number");
        Assert.Contains("string", Text(call));
        await project.AssertTypeAsync("brand", "#secret", "number");
    }

    [Fact]
    public async Task InheritedGenericStaticThisAndSuperUseActualReceiverFacets()
    {
        using var project = new HoverProject("""
            class Base<T> { value: T; read(): number { return 1; } }
            class Derived extends Base<number> {
                static title: string = 'derived';
                inspect(): number {
                    /*this*/this.value;
                    return /*super*/super./*method*/read();
                }
            }
            const derived = new Derived();
            derived./*field*/value;
            Derived./*static*/title;
            """);

        await project.AssertTypeAsync("field", "value", "number");
        await project.AssertTypeAsync("static", "title", "string");
        await project.AssertTypeAsync("this", "this", "Derived");
        var super = await project.AssertTypeAsync("super", "super", "Base");
        Assert.DoesNotContain("typeof", Text(super));
        await project.AssertTypeAsync("method", "read", "number");
    }

    [Fact]
    public async Task AccessorsAutoAccessorsAndParameterPropertiesHaveKnownDeclarationTypes()
    {
        using var project = new HoverProject("""
            class Box {
                accessor count: number = 0;
                get value(): string { return 'value'; }
                set value(next: string) {}
                constructor(public token: number) {}
            }
            const box = new Box(1);
            box./*auto*/count;
            box./*get*/value;
            box./*set*/value = 'next';
            box./*parameter*/token;
            """);

        await project.AssertTypeAsync("auto", "count", "number");
        await project.AssertTypeAsync("get", "value", "string");
        await project.AssertTypeAsync("set", "value", "string");
        await project.AssertTypeAsync("parameter", "token", "number");
    }

    [Fact]
    public async Task AssignmentNamesKeepTheBoundDeclarationTypeInsteadOfTheRhsResult()
    {
        using var project = new HoverProject("let value: string | number = 'start'; /*assignment*/value = 1;");

        var hover = await project.AssertTypeAsync("assignment", "value", "string");
        Assert.Contains("number", Text(hover));
    }

    [Fact]
    public async Task GenericUnionReadsCanUseExactFinalOccurrenceAfterUnanimousOriginProof()
    {
        using var project = new HoverProject("class Box<T> { value: T; } function read(box: Box<number> | Box<string>) { return box./*use*/value; }");

        var hover = await project.AssertTypeAsync("use", "value", "number");
        Assert.Contains("string", Text(hover));
    }

    [Fact]
    public async Task UnsupportedAmbiguousAndInaccessibleMembersHaveNoHover()
    {
        using var project = new HoverProject("""
            class A { value: number = 1; private hidden: number = 2; }
            class B { value: number = 2; }
            function distinct(box: A | B) { return box./*ambiguous*/value; }
            function dynamic(box: any) { return box./*any*/value; }
            function structural(box: { value: number }) { return box./*structural*/value; }
            const box = new A();
            box./*private*/hidden;
            box./*missing*/absent;
            """);

        foreach (string marker in new[] { "ambiguous", "any", "structural", "private", "missing" })
            Assert.Null(await project.QueryAsync(marker));
    }

    [Fact]
    public async Task ValidMemberReadSurvivesWrongCallArgumentsAndIndependentErrors()
    {
        using var project = new HoverProject("""
            class Box { read(input: number): string { return 'result'; } }
            const wrong: number = 'wrong';
            new Box()./*call*/read('wrong');
            """);

        var hover = await project.AssertTypeAsync("call", "read", "input: number");
        Assert.Contains("string", Text(hover));
    }

    [Fact]
    public async Task UnvisitedBodyDoesNotReusePreparatoryTypes()
    {
        using var project = new HoverProject("class Box { value: number = 1; } const box = new Box(); try {} catch { box./*skipped*/value; } box./*known*/value;");

        Assert.Null(await project.QueryAsync("skipped"));
        await project.AssertTypeAsync("known", "value", "number");
    }

    [Fact]
    public async Task RecoveredFileCanKeepIndependentTrustworthyNamesWithoutGuessingInGaps()
    {
        using var project = new HoverProject("const stable: number = 1; /*known*/stable; class Box { value: number = 1; } const box = new Box(); box./*gap*/ value/*end*/; const broken = ;");

        await project.AssertTypeAsync("known", "stable", "number");
        Assert.Null(await project.QueryAsync("gap"));
        Assert.Null(await project.QueryAsync("end"));
    }

    [Fact]
    public async Task LargePresentationStaysBoundedAndEscapesItsMarkdownFence()
    {
        string union = string.Join(" | ", Enumerable.Range(0, 600).Select(index => "'value" + index + "'"));
        using var large = new HoverProject("let value: " + union + "; /*use*/value;");
        var hover = await large.AssertTypeAsync("use", "value", "…");
        Assert.True(Text(hover).Length <= 4096);
        Assert.DoesNotContain("unavailable", Text(hover));

        using var quoted = new HoverProject("const value: '```' = '```'; /*use*/value;");
        var markdown = Assert.IsType<Hover>(await quoted.QueryAsync("use", MarkupKind.Markdown));
        Assert.StartsWith("````typescript\n", Text(markdown));
        Assert.EndsWith("\n````", Text(markdown));
    }

    [Fact]
    public void PlaintextLegacyAdapterPreservesSignaturesDocumentationAndLinkText()
    {
        var original = new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = "```csharp\nStringBuilder Append(String value)\n```\n\n**Summary**: [guide](https://example.test/guide).",
            }),
        };

        var plain = Assert.IsType<Hover>(SemanticHoverService.AdaptMarkup(original, MarkupKind.PlainText));
        Assert.Equal(MarkupKind.PlainText, plain.Contents.MarkupContent!.Kind);
        Assert.Contains("StringBuilder Append(String value)", Text(plain));
        Assert.Contains("Summary: guide (https://example.test/guide).", Text(plain));
        Assert.DoesNotContain("```", Text(plain));
        Assert.Same(original, SemanticHoverService.AdaptMarkup(original, MarkupKind.Markdown));
    }

    [Theory]
    [InlineData("\n", "main.ts")]
    [InlineData("\r\n", "main.ts")]
    [InlineData("\n", "main.tsx")]
    [InlineData("\r\n", "main.tsx")]
    public async Task ExactUtf16NamesSurviveLfCrlfAndTsx(string newline, string fileName)
    {
        string source = string.Join(newline, "// 😀", "const value: number = 1;", "const text = '😀'; /*use*/value;");
        using var project = new HoverProject(source, fileName);

        await project.AssertTypeAsync("use", "value", "number");
    }

    private static string Text(Hover hover) => hover.Contents.MarkupContent!.Value;

    private sealed class HoverProject : IDisposable
    {
        private readonly TempTestDirectory _directory = CliTestHelper.CreateTempDirectory();
        private readonly SemanticAnalysisService _analysis = new();
        private readonly SemanticHoverService _hover;
        private readonly DocumentStore _store = new();
        private readonly string _source;
        private readonly DocumentUri _uri;

        public HoverProject(string source, string fileName = "main.ts")
        {
            _source = source;
            _directory.CreateFile("tsconfig.json", """{"compilerOptions":{"noLib":true,"types":[]},"include":["*.ts","*.tsx"]}""");
            _uri = DocumentUri.FromFileSystemPath(_directory.CreateFile(fileName, source));
            _store.Set(_uri.ToString(), source);
            _hover = new(_analysis);
        }

        public void AddFile(string fileName, string source)
        {
            var uri = DocumentUri.FromFileSystemPath(_directory.CreateFile(fileName, source));
            _store.Set(uri.ToString(), source);
        }

        public async Task<Hover?> QueryAsync(string marker, MarkupKind? format = null)
        {
            Assert.True(_store.TryCapture(_uri.ToString(), out var capture));
            var (line, column) = new LineIndex(_source).ToPosition(Offset(marker));
            var result = await _hover.HoverAsync(capture!, new Position(line - 1, column - 1),
                format ?? MarkupKind.PlainText, CancellationToken.None);
            Assert.True(result.IsCurrent());
            return result.Hover;
        }

        public async Task<Hover> AssertTypeAsync(string marker, string name, string expectedType)
        {
            var hover = Assert.IsType<Hover>(await QueryAsync(marker));
            Assert.Equal(MarkupKind.PlainText, hover.Contents.MarkupContent!.Kind);
            Assert.Contains(name + ": ", Text(hover));
            Assert.Contains(expectedType, Text(hover));
            AssertRange(hover, marker, name);
            return hover;
        }

        public void AssertRange(Hover hover, string marker, string name)
        {
            int offset = Offset(marker);
            Assert.Equal(name, _source.Substring(offset, name.Length));
            var lines = new LineIndex(_source);
            var (startLine, startColumn) = lines.ToPosition(offset);
            var (endLine, endColumn) = lines.ToPosition(offset + name.Length);
            Assert.Equal(new Range(startLine - 1, startColumn - 1, endLine - 1, endColumn - 1), hover.Range);
        }

        private int Offset(string marker)
        {
            string comment = "/*" + marker + "*/";
            int offset = _source.IndexOf(comment, StringComparison.Ordinal);
            Assert.True(offset >= 0, "Missing marker " + marker);
            return offset + comment.Length;
        }

        public void Dispose()
        {
            _analysis.Dispose();
            _directory.Dispose();
        }
    }
}
