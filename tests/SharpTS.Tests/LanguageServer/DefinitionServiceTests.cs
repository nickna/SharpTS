using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Handlers;
using SharpTS.LanguageServer.Services;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.Tests.LanguageServer;

/// <summary>
/// Covers local value navigation through the checker's semantic binding index. These cases are
/// deliberately scope-sensitive: a text search could pass the simple name checks but not the
/// shadowing, hoisting, or parameter-default cases.
/// </summary>
public class DefinitionServiceTests
{
    private readonly DefinitionService _definitions = new();

    [Fact]
    public void ForwardFunctionReferenceResolvesThroughHoisting()
    {
        const string source = """
            greet();
            function greet(): void {}
            """;

        var definition = Assert.Single(Definitions(source, "greet", occurrence: 0));

        Assert.Equal(new Range(1, 9, 1, 14), definition.Range);
    }

    [Fact]
    public void ShadowedLocalResolvesToTheNarrowestCheckerScope()
    {
        const string source = """
            const value = 1;
            function read(): number {
              const value = 2;
              return value;
            }
            """;

        var definition = Assert.Single(Definitions(source, "value", occurrence: 2));

        Assert.Equal(new Range(2, 8, 2, 13), definition.Range);
    }

    [Fact]
    public void ParameterDefaultBindsToThePrecedingParameter()
    {
        const string source =
            "function scale(input: number, factor: number = input): number { return factor; }\n";

        var definition = Assert.Single(Definitions(source, "input", occurrence: 1));

        Assert.Equal(new Range(0, 15, 0, 20), definition.Range);
    }

    [Fact]
    public void AssignmentTargetResolvesToItsDeclaration()
    {
        const string source = """
            let count = 0;
            count = 1;
            """;

        var definition = Assert.Single(Definitions(source, "count", occurrence: 1));

        Assert.Equal(new Range(0, 4, 0, 9), definition.Range);
    }

    [Fact]
    public void ArrowBodyResolvesToItsParameter()
    {
        const string source = "const select = (item: number): number => item;\n";

        var definition = Assert.Single(Definitions(source, "item", occurrence: 1));

        Assert.Equal(new Range(0, 16, 0, 20), definition.Range);
    }

    [Fact]
    public void CatchAndLoopBindingsUseTheirLexicalDeclarations()
    {
        const string source = """
            try {} catch (error) { console.log(error); }
            for (const item of [1]) { console.log(item); }
            """;

        var catchDefinition = Assert.Single(Definitions(source, "error", occurrence: 1));
        var loopDefinition = Assert.Single(Definitions(source, "item", occurrence: 1));

        Assert.Equal(new Range(0, 14, 0, 19), catchDefinition.Range);
        Assert.Equal(new Range(1, 11, 1, 15), loopDefinition.Range);
    }

    [Fact]
    public void ClassValueReferenceResolvesToTheClassDeclaration()
    {
        const string source = "class Box { create() { return new Box(); } }\n";

        var definition = Assert.Single(Definitions(source, "Box", occurrence: 1));

        Assert.Equal(new Range(0, 6, 0, 9), definition.Range);
    }

    [Fact]
    public void TypeErrorsElsewhereDoNotEraseValidBindings()
    {
        const string source = """
            const target = 1;
            const bad: number = "wrong";
            console.log(target);
            """;

        var definition = Assert.Single(Definitions(source, "target", occurrence: 1));

        Assert.Equal(new Range(0, 6, 0, 12), definition.Range);
    }

    [Fact]
    public void UnresolvedImportDoesNotEraseValidLocalBindings()
    {
        const string source = """
            import { absent } from "./not-present";
            const target = 1;
            console.log(target);
            """;

        var definition = Assert.Single(Definitions(source, "target", occurrence: 1));

        Assert.Equal(new Range(1, 6, 1, 12), definition.Range);
    }

    [Fact]
    public void PropertyNamesAreNotGuessedFromMatchingText()
    {
        const string source = """
            const value = 1;
            const object = { value: 2 };
            console.log(object.value);
            """;

        Assert.Empty(Definitions(source, "value", occurrence: 2));
    }

    [Fact]
    public void FunctionOverloadsShareOneIdentityWithAllDeclarations()
    {
        const string source = """
            function convert(value: string): string;
            function convert(value: number): number;
            function convert(value: string | number): string | number { return value; }
            convert("x");
            """;

        var definitions = Definitions(source, "convert", occurrence: 3);

        Assert.Equal(3, definitions.Count);
        Assert.Equal(
            [0, 1, 2],
            definitions.Select(location => (int)location.Range.Start.Line).ToArray());
    }

    [Fact]
    public void ReusedCheckerDoesNotReturnStaleDeclarationsFromAPriorCheck()
    {
        var checker = new TypeChecker();
        var first = Parse("first.ts", "const current = 1; console.log(current);\n");
        checker.CheckWithRecovery(first.Statements, first.Document);

        var second = Parse("second.ts", "const current = 2; console.log(current);\n");
        checker.CheckWithRecovery(second.Statements, second.Document);

        var definition = Assert.Single(checker.Bindings.FindDefinitions(
            second.Document,
            second.Document.Text.LastIndexOf("current", StringComparison.Ordinal)));
        Assert.Same(second.Document, definition.Document);
        Assert.Equal(6, definition.Name.Start);
    }

    [Fact]
    public async Task HandlerReturnsTheDefinitionFromTheOpenDocumentSnapshot()
    {
        string path = Path.GetFullPath("handler-definition-test.ts");
        DocumentUri uri = DocumentUri.FromFileSystemPath(path);
        const string source = "const answer = 42;\nconsole.log(answer);\n";
        var store = new DocumentStore();
        store.Set(uri.ToString(), source);
        var handler = new DefinitionHandler(store, new DefinitionService());

        var result = await handler.Handle(
            new DefinitionParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(1, 12),
            },
            CancellationToken.None);

        var target = Assert.Single(Assert.IsAssignableFrom<LocationOrLocationLinks>(result));
        Assert.True(target.IsLocation);
        var location = Assert.IsType<Location>(target.Location);
        Assert.Equal(new Range(0, 6, 0, 12), location.Range);
    }

    [Fact]
    public async Task HandlerResolvesAnAliasedImportToADirtyOpenDependency()
    {
        string root = Path.GetFullPath("open-module-definition-test");
        string dependencyPath = Path.Combine(root, "dependency.ts");
        string entryPath = Path.Combine(root, "entry.ts");
        DocumentUri dependencyUri = DocumentUri.FromFileSystemPath(dependencyPath);
        DocumentUri entryUri = DocumentUri.FromFileSystemPath(entryPath);
        const string dependency = "export const original = 1;\n";
        const string entry = """
            import { original as local } from "./dependency";
            console.log(local);
            """;
        var store = new DocumentStore();
        store.Set(dependencyUri.ToString(), dependency);
        store.Set(entryUri.ToString(), entry);
        var handler = new DefinitionHandler(store, new DefinitionService());

        var result = await handler.Handle(
            new DefinitionParams
            {
                TextDocument = new TextDocumentIdentifier(entryUri),
                Position = new Position(1, 12),
            },
            CancellationToken.None);

        var target = Assert.Single(Assert.IsAssignableFrom<LocationOrLocationLinks>(result));
        var location = Assert.IsType<Location>(target.Location);
        Assert.Equal(dependencyUri, location.Uri);
        Assert.Equal(new Range(0, 13, 0, 21), location.Range);
    }

    [Fact]
    public void DefaultImportResolvesToTheNamedDefaultDeclaration()
    {
        string root = Path.GetFullPath("default-module-definition-test");
        string dependencyPath = Path.Combine(root, "dependency.ts");
        string entryPath = Path.Combine(root, "entry.ts");
        const string dependency = "export default function launch(): void {}\n";
        const string entry = """
            import start from "./dependency";
            start();
            """;
        var overlay = new Dictionary<string, string>
        {
            [dependencyPath] = dependency,
            [entryPath] = entry,
        };

        var definition = Assert.Single(
            Definitions(entryPath, entry, "start", occurrence: 1, overlay));

        Assert.Equal(DocumentUri.FromFileSystemPath(dependencyPath), definition.Uri);
        Assert.Equal(new Range(0, 24, 0, 30), definition.Range);
    }

    [Fact]
    public void ReExportChainPreservesTheOriginalValueDeclaration()
    {
        string root = Path.GetFullPath("reexport-module-definition-test");
        string dependencyPath = Path.Combine(root, "dependency.ts");
        string barrelPath = Path.Combine(root, "barrel.ts");
        string entryPath = Path.Combine(root, "entry.ts");
        const string dependency = "export function run(): void {}\n";
        const string barrel = "export { run as execute } from \"./dependency\";\n";
        const string entry = """
            import { execute as start } from "./barrel";
            start();
            """;
        var overlay = new Dictionary<string, string>
        {
            [dependencyPath] = dependency,
            [barrelPath] = barrel,
            [entryPath] = entry,
        };

        var definition = Assert.Single(
            Definitions(entryPath, entry, "start", occurrence: 1, overlay));

        Assert.Equal(DocumentUri.FromFileSystemPath(dependencyPath), definition.Uri);
        Assert.Equal(new Range(0, 16, 0, 19), definition.Range);
    }

    [Fact]
    public void TypeOnlyImportInAnAnnotationResolvesToItsInterface()
    {
        string root = Path.GetFullPath("type-module-definition-test");
        string dependencyPath = Path.Combine(root, "dependency.ts");
        string entryPath = Path.Combine(root, "entry.ts");
        const string dependency = "export interface Shape { size: number; }\n";
        const string entry = """
            import type { Shape as LocalShape } from "./dependency";
            const value: LocalShape = { size: 1 };
            """;
        var overlay = new Dictionary<string, string>
        {
            [dependencyPath] = dependency,
            [entryPath] = entry,
        };

        var definition = Assert.Single(
            Definitions(entryPath, entry, "LocalShape", occurrence: 1, overlay));

        Assert.Equal(DocumentUri.FromFileSystemPath(dependencyPath), definition.Uri);
        Assert.Equal(new Range(0, 17, 0, 22), definition.Range);
    }

    private IReadOnlyList<Location> Definitions(
        string source,
        string name,
        int occurrence)
    {
        int offset = NthIndexOf(source, name, occurrence);
        var lines = new SharpTS.Parsing.LineIndex(source);
        var (line, column) = lines.ToPosition(offset);
        return _definitions.FindDefinitions(
            Path.GetFullPath("definition-test.ts"),
            source,
            new Position(line - 1, column - 1));
    }

    private IReadOnlyList<Location> Definitions(
        string path,
        string source,
        string name,
        int occurrence,
        IReadOnlyDictionary<string, string> openDocuments)
    {
        int offset = NthIndexOf(source, name, occurrence);
        var lines = new SharpTS.Parsing.LineIndex(source);
        var (line, column) = lines.ToPosition(offset);
        return _definitions.FindDefinitions(
            path,
            source,
            new Position(line - 1, column - 1),
            openDocuments);
    }

    private static int NthIndexOf(string source, string value, int occurrence)
    {
        int offset = -1;
        for (int i = 0; i <= occurrence; i++)
        {
            offset = source.IndexOf(value, offset + 1, StringComparison.Ordinal);
            Assert.True(offset >= 0, $"Occurrence {occurrence} of '{value}' was not found.");
        }
        return offset;
    }

    private static (List<Stmt> Statements, SourceDocument Document) Parse(
        string path,
        string source)
    {
        var document = new SourceDocument(path, source);
        var parsed = new Parser(new Lexer(source).ScanTokens())
            .WithSourceDocument(document)
            .Parse();
        Assert.True(parsed.IsSuccess);
        return (parsed.Statements, document);
    }
}
