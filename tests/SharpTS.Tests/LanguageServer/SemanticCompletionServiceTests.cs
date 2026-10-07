using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Services;
using SharpTS.Parsing;
using SharpTS.Tests.IntegrationTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

public sealed class SemanticCompletionServiceTests
{
    [Theory]
    [InlineData("vi/*cursor*/", "vi")]
    [InlineData("vis/*cursor*/ible", "visible")]
    public async Task LexicalCompletionReplacesTheWholeIdentifierIncludingItsSuffix(string written, string replaced)
    {
        using var project = new CompletionProject("const visible: number = 1; " + written + ";");
        CompletionItem item = Assert.Single((await project.CompleteAsync()).Items);
        Assert.Equal("visible", item.Label);
        Assert.Equal(CompletionItemKind.Constant, item.Kind);
        Assert.Contains("number", item.Detail);
        project.AssertEdit(item, replaced, "visible");
        project.AssertAppliedSyntax(item);
        await project.CompleteAsync();
        Assert.Equal(1, project.Analysis.Statistics.Checks);
    }

    [Theory]
    [InlineData("let item: Sha/*cursor*/;", "Shared")]
    [InlineData("Sha/*cursor*/;", "number")]
    public async Task WrittenTypeAndValueContextsSelectTheCorrectFacet(string query, string detail)
    {
        using var project = new CompletionProject("interface Shared { field: string; } const Shared: number = 1; " + query);
        var item = Assert.Single((await project.CompleteAsync()).Items);
        Assert.Equal("Shared", item.Label);
        Assert.Contains(detail, item.Detail);
    }

    [Fact]
    public async Task UnresolvedLazyTypeAliasesAreNotEvaluatedToInventCompletionTypes()
    {
        using var project = new CompletionProject("type Hidden = string; let item: Hid/*cursor*/;");
        Assert.Empty((await project.CompleteAsync()).Items);
    }

    [Fact]
    public async Task ParametersAndBlockShadowingUseTheActualVisibleDeclaration()
    {
        using var project = new CompletionProject("const value: string = 'outer'; function read(input: boolean) { { const value: number = 1; va/*cursor*/; } }");
        var item = Assert.Single((await project.CompleteAsync()).Items);
        Assert.Equal("value", item.Label);
        Assert.Contains("number", item.Detail);
        Assert.DoesNotContain("string", item.Detail);
    }

    [Fact]
    public async Task TdzLocalShadowsAnOuterNameWithoutOfferingEitherBinding()
    {
        using var project = new CompletionProject("let value: string = 'outer'; { va/*cursor*/; let value: number = 1; }");
        Assert.Empty((await project.CompleteAsync()).Items);
    }

    [Fact]
    public async Task ParserDiscardedLocalCannotExposeAnOuterSameSpelledBinding()
    {
        using var project = new CompletionProject("const value: string = 'outer'; function read() { const value = ; va/*cursor*/; }");
        Assert.Empty((await project.CompleteAsync()).Items);
    }

    [Fact]
    public async Task ImportsKeepTheLocalAliasAndExcludeTheSourceSpelling()
    {
        using var project = new CompletionProject("import { value as renamed } from './dependency'; re/*cursor*/;");
        project.AddFile("dependency.ts", "export const value: number = 1;");
        var items = (await project.CompleteAsync()).Items.ToArray();
        var item = Assert.Single(items, item => item.Label == "renamed");
        Assert.Equal("renamed", item.Label);
        Assert.DoesNotContain(items, item => item.Label == "value");
        Assert.Contains("number", item.Detail);
        project.AssertAppliedSyntax(item);
    }

    [Fact]
    public async Task ExistingMemberSyntaxUsesItsCheckedReceiverWithoutRecovery()
    {
        using var project = new CompletionProject("class Box { value: number = 1; read(): string { return 'result'; } } const box = new Box(); box.va/*cursor*/;");
        var item = Assert.Single((await project.CompleteAsync()).Items);
        Assert.Equal("value", item.Label);
        Assert.Equal(CompletionItemKind.Field, item.Kind);
        project.AssertEdit(item, "va", "value");
        await project.CompleteAsync();
        Assert.Equal(1, project.Analysis.Statistics.Checks);
    }

    [Theory]
    [InlineData("box./*cursor*/")]
    [InlineData("box?. /*cursor*/")]
    [InlineData("create()./*cursor*/")]
    public async Task MissingMemberUsesAFreshCheckedCursorGraphAndReusesIt(string query)
    {
        using var project = new CompletionProject("class Box { value: number = 1; } const box = new Box(); function create() { return box; } " + query);
        var first = await project.CompleteAsync();
        var item = Assert.Single(first.Items);
        Assert.Equal("value", item.Label);
        project.AssertAppliedSyntax(item);
        long checks = project.Analysis.Statistics.Checks;
        Assert.Equal(2, checks);
        Assert.Equal("value", Assert.Single((await project.CompleteAsync()).Items).Label);
        Assert.Equal(checks, project.Analysis.Statistics.Checks);
    }

    [Fact]
    public async Task GenericInheritedAndStaticMembersKeepTheirActualTypesAndFacets()
    {
        using var instance = new CompletionProject("class Base<T> { value: T; } class Derived extends Base<number> { static title: string = 'text'; } new Derived().va/*cursor*/;");
        var field = Assert.Single((await instance.CompleteAsync()).Items);
        Assert.Equal("value", field.Label);
        Assert.Contains("number", field.Detail);
        using var statics = new CompletionProject("class Box { value: number = 1; static title: string = 'text'; } Box./*cursor*/");
        Assert.Equal("title", Assert.Single((await statics.CompleteAsync()).Items).Label);
    }

    [Fact]
    public async Task OwnPrivateAndProtectedNamesRemainBoundToTheLexicalClass()
    {
        using var own = new CompletionProject("class Base { #secret: number = 1; private hidden: string = 'text'; protected inherited: number = 2; read() { this./*cursor*/ } }");
        var names = (await own.CompleteAsync()).Items.Select(item => item.Label).ToArray();
        Assert.Contains("#secret", names);
        Assert.Contains("hidden", names);
        Assert.Contains("inherited", names);
        using var derived = new CompletionProject("class Base { #secret: number = 1; private hidden: string = 'text'; protected inherited: number = 2; } class Derived extends Base { read() { this./*cursor*/ } }");
        names = (await derived.CompleteAsync()).Items.Select(item => item.Label).ToArray();
        Assert.DoesNotContain("#secret", names);
        Assert.DoesNotContain("hidden", names);
        Assert.Contains("inherited", names);
    }

    [Fact]
    public async Task OutsideClassCompletionOmitsPrivateAndProtectedNames()
    {
        using var project = new CompletionProject("class Box { #secret: number = 1; private hidden: string = 'text'; protected inherited: number = 2; value: number = 3; } new Box()./*cursor*/");
        Assert.Equal("value", Assert.Single((await project.CompleteAsync()).Items).Label);
    }

    [Fact]
    public async Task SuperCompletionUsesItsProvenSuperclassMethodProjection()
    {
        using var project = new CompletionProject("class Base { read(): number { return 1; } } class Derived extends Base { inspect() { super./*cursor*/ } }");
        var item = Assert.Single((await project.CompleteAsync()).Items);
        Assert.Equal("read", item.Label);
        Assert.Equal(CompletionItemKind.Method, item.Kind);
        Assert.Contains("number", item.Detail);
    }

    [Fact]
    public async Task OptionalNullableReceiverKeepsKnownMembers()
    {
        using var project = new CompletionProject("class Box { value: number = 1; } function read(box: Box | null) { box?.va/*cursor*/; }");
        Assert.Equal("value", Assert.Single((await project.CompleteAsync()).Items).Label);
    }

    [Fact]
    public async Task ImportedNamespaceExportsAndClassInstancesUseTheirCheckedProjections()
    {
        using var project = new CompletionProject("import * as lib from './dependency'; lib./*cursor*/");
        project.AddFile("dependency.ts", "export const value: number = 1; export function read(): string { return 'result'; }");
        Assert.Equal(new[] { "read", "value" }, (await project.CompleteAsync()).Items.Select(item => item.Label));
        using var imported = new CompletionProject("import { Box } from './dependency'; const box = new Box(); box./*cursor*/");
        imported.AddFile("dependency.ts", "export class Box { value: number = 1; }");
        Assert.Equal("value", Assert.Single((await imported.CompleteAsync()).Items).Label);
    }

    [Fact]
    public async Task NamespaceTypeOnlyExportsAreNotOfferedAsValueMembers()
    {
        using var project = new CompletionProject("import * as lib from './dependency'; lib./*cursor*/");
        project.AddFile("dependency.ts", "export type TypeOnly = number; export const value: number = 1;");
        Assert.Equal("value", Assert.Single((await project.CompleteAsync()).Items).Label);
    }

    [Fact]
    public async Task ContextualKeywordReceiverCanUseFreshMemberRecovery()
    {
        using var project = new CompletionProject("import { Box as from } from './dependency'; const type = new from(); type./*cursor*/");
        project.AddFile("dependency.ts", "export class Box { value: number = 1; }");
        Assert.Equal("value", Assert.Single((await project.CompleteAsync()).Items).Label);
    }

    [Fact]
    public async Task JsxRawTextAfterPunctuationIsNotAnEmptyLexicalExpressionContext()
    {
        using var project = new CompletionProject("const value: number = 1; const view = <div>hello; /*cursor*/</div>;", "main.tsx");
        Assert.Empty((await project.CompleteAsync()).Items);
    }

    [Theory]
    [InlineData("interface Shape { value: number; read(): string; } function inspect(shape: Shape) { shape./*cursor*/ }")]
    [InlineData("function inspect(shape: { value: number; read: () => string }) { shape./*cursor*/ }")]
    public async Task StructuralAndInterfaceMembersDoNotRequireInventedSourceOrigins(string source)
    {
        using var project = new CompletionProject(source);
        var items = (await project.CompleteAsync()).Items.ToArray();
        Assert.Equal(new[] { "read", "value" }, items.Select(item => item.Label));
        Assert.All(items, item => Assert.False(string.IsNullOrEmpty(item.Detail)));
    }

    [Theory]
    [InlineData("function inspect(value: any) { value./*cursor*/ }")]
    [InlineData("missing./*cursor*/")]
    [InlineData("function inspect(value: Missing) { value./*cursor*/ }")]
    [InlineData("class Box { value: number = 1; } const box = new Box(); try {} catch { box.va/*cursor*/; }")]
    public async Task DynamicUnresolvedAndUnvisitedReceiversHaveNoGuessedMembers(string source)
    {
        using var project = new CompletionProject(source);
        Assert.Empty((await project.CompleteAsync()).Items);
    }

    [Fact]
    public async Task AnnotatedClassReturnBaselineAnyCannotBeReplacedByItsDisplayedSignature()
    {
        const string prefix = "class Box { value: number = 1; } const box = new Box(); function create(): Box { return box; } ";
        string source = prefix + "create().value;";
        var document = new SourceDocument("ordinary-call.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker();
        var baseline = checker.CheckWithRecovery(statements, document);
        var receiver = Assert.Single(document.EditorSyntax!.Members).Receiver;
        Assert.IsType<TypeInfo.Any>(baseline.TypeMap.Get(receiver));
        using var project = new CompletionProject(prefix + "create()./*cursor*/");
        Assert.Empty((await project.CompleteAsync()).Items);
    }

    [Theory]
    [InlineData("const value = 1; // va/*cursor*/")]
    [InlineData("const value = 1; /* va/*cursor*/ */")]
    [InlineData("const value = 1; const text = 'va/*cursor*/';")]
    [InlineData("const value = 1; const text = `va/*cursor*/`;")]
    [InlineData("const value = 1; const text = /*cursor*/'literal';")]
    [InlineData("const value = 1; const text = /*cursor*/`literal`;")]
    [InlineData("const value = 1; 1./*cursor*/")]
    [InlineData("const value = 1; import./*cursor*/")]
    [InlineData("const value = 1; const spread = [.../*cursor*/[]];")]
    public async Task CommentsLiteralsAndNonmemberDotsAreNotLexicalCompletionContexts(string source)
    {
        using var project = new CompletionProject(source);
        Assert.Empty((await project.CompleteAsync()).Items);
    }

    [Fact]
    public async Task QuotedComputedAndNumericKeysAreOmittedWithoutRewriteEdits()
    {
        using var project = new CompletionProject("function inspect(value: { 'not-valid': number; '123': string; valid: boolean }) { value./*cursor*/ }");
        Assert.Equal("valid", Assert.Single((await project.CompleteAsync()).Items).Label);
    }

    [Fact]
    public async Task CandidateEnumerationIsBoundedSortedDeduplicatedAndMarkedIncomplete()
    {
        string fields = string.Join(" ", Enumerable.Range(0, 300).Select(index => "value" + index.ToString("D3") + ": number = 1;"));
        using var project = new CompletionProject("class Box { " + fields + " } new Box()./*cursor*/");
        var result = await project.CompleteAsync();
        var items = result.Items.ToArray();
        Assert.True(result.IsIncomplete);
        Assert.Equal(256, items.Length);
        Assert.Equal(items.Select(item => item.Label).Order(StringComparer.Ordinal), items.Select(item => item.Label));
        Assert.Equal(items.Length, items.Select(item => item.Label).Distinct(StringComparer.Ordinal).Count());
        Assert.All(items, item =>
        {
            Assert.NotNull(item.Detail);
            Assert.True(item.Detail.Length <= 1024);
        });
    }

    [Theory]
    [InlineData("\n", "main.ts")]
    [InlineData("\r\n", "main.ts")]
    [InlineData("\n", "main.tsx")]
    [InlineData("\r\n", "main.tsx")]
    public async Task Utf16UnicodeAndLineEndingsKeepExactWholeNameEdits(string newline, string fileName)
    {
        using var project = new CompletionProject(string.Join(newline, "// 😀", "const résumé: number = 1;", "const text = '😀'; ré/*cursor*/sumé;"), fileName);
        var item = Assert.Single((await project.CompleteAsync()).Items);
        Assert.Equal("résumé", item.Label);
        project.AssertEdit(item, "résumé", "résumé");
        project.AssertAppliedSyntax(item);
    }

    private sealed class CompletionProject : IDisposable
    {
        private readonly TempTestDirectory _directory = CliTestHelper.CreateTempDirectory();
        private readonly DocumentStore _store = new();
        private readonly SemanticCompletionService _completion;
        private readonly DocumentUri _uri;
        private readonly string _source;
        private readonly int _offset;
        public SemanticAnalysisService Analysis { get; } = new();

        public CompletionProject(string source, string fileName = "main.ts")
        {
            const string marker = "/*cursor*/";
            _offset = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(_offset >= 0);
            _source = source.Remove(_offset, marker.Length);
            _directory.CreateFile("tsconfig.json", """{"compilerOptions":{"noLib":true,"types":[]},"include":["*.ts","*.tsx"]}""");
            _uri = DocumentUri.FromFileSystemPath(_directory.CreateFile(fileName, _source));
            _store.Set(_uri.ToString(), _source);
            _completion = new(Analysis);
        }

        public void AddFile(string name, string source)
        {
            var uri = DocumentUri.FromFileSystemPath(_directory.CreateFile(name, source));
            _store.Set(uri.ToString(), source);
        }

        public async Task<CompletionList> CompleteAsync()
        {
            Assert.True(_store.TryCapture(_uri.ToString(), out var capture));
            var (line, column) = new LineIndex(_source).ToPosition(_offset);
            var result = await _completion.CompletionAsync(capture!, new Position(line - 1, column - 1), CancellationToken.None);
            Assert.True(result.IsCurrent());
            return result.List;
        }

        public void AssertEdit(CompletionItem item, string replaced, string inserted)
        {
            Assert.Equal(InsertTextFormat.PlainText, item.InsertTextFormat);
            Assert.Equal(inserted, item.InsertText);
            Assert.NotNull(item.TextEdit);
            TextEdit edit = Assert.IsType<TextEdit>(item.TextEdit.TextEdit);
            var lines = new LineIndex(_source);
            int start = lines.ToOffset(edit.Range.Start.Line + 1, edit.Range.Start.Character + 1);
            int end = lines.ToOffset(edit.Range.End.Line + 1, edit.Range.End.Character + 1);
            Assert.Equal(replaced, _source[start..end]);
            Assert.Equal(inserted, edit.NewText);
            Assert.InRange(_offset, start, end);
        }

        public void AssertAppliedSyntax(CompletionItem item)
        {
            Assert.NotNull(item.TextEdit);
            TextEdit edit = Assert.IsType<TextEdit>(item.TextEdit.TextEdit);
            var lines = new LineIndex(_source);
            int start = lines.ToOffset(edit.Range.Start.Line + 1, edit.Range.Start.Character + 1);
            int end = lines.ToOffset(edit.Range.End.Line + 1, edit.Range.End.Character + 1);
            string applied = _source[..start] + edit.NewText + _source[end..];
            var parsed = new Parser(new Lexer(applied).ScanTokens()).Parse();
            Assert.Empty(parsed.Diagnostics);
        }

        public void Dispose()
        {
            Analysis.Dispose();
            _directory.Dispose();
        }
    }
}
