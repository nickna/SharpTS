using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Handlers;
using SharpTS.LanguageServer.Services;
using SharpTS.Tests.IntegrationTests;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.Tests.LanguageServer;

/// <summary>
/// Covers inverse semantic binding lookup. These cases intentionally require checker identity:
/// text matching cannot distinguish shadowing, properties, overload groups, or type/value facets.
/// </summary>
public class ReferenceServiceTests
{
    private readonly ReferenceService _references = new();

    [Fact]
    public void DeclarationInclusionFollowsTheRequestContext()
    {
        const string source = """
            const value = 1;
            console.log(value);
            value;
            """;

        var withoutDeclaration = References(
            source, "value", occurrence: 1, includeDeclaration: false);
        var withDeclaration = References(
            source, "value", occurrence: 1, includeDeclaration: true);

        Assert.Equal(
            [
                new Range(1, 12, 1, 17),
                new Range(2, 0, 2, 5),
            ],
            withoutDeclaration.Select(location => location.Range).ToArray());
        Assert.Equal(
            [
                new Range(0, 6, 0, 11),
                new Range(1, 12, 1, 17),
                new Range(2, 0, 2, 5),
            ],
            withDeclaration.Select(location => location.Range).ToArray());
    }

    [Fact]
    public void ShadowedBindingDoesNotIncludeSameNamedOuterOccurrences()
    {
        const string source = """
            const value = 1;
            function read(): number {
              const value = 2;
              return value;
            }
            console.log(value);
            """;

        var references = References(
            source, "value", occurrence: 2, includeDeclaration: true);

        Assert.Equal(
            [
                new Range(2, 8, 2, 13),
                new Range(3, 9, 3, 14),
            ],
            references.Select(location => location.Range).ToArray());
    }

    [Fact]
    public void OverloadDeclarationsAndCallShareOneReferenceIdentity()
    {
        const string source = """
            function convert(value: string): string;
            function convert(value: number): number;
            function convert(value: string | number): string | number { return value; }
            convert("x");
            """;

        var withoutDeclarations = References(
            source, "convert", occurrence: 3, includeDeclaration: false);
        var withDeclarations = References(
            source, "convert", occurrence: 3, includeDeclaration: true);

        Assert.Single(withoutDeclarations);
        Assert.Equal(new Range(3, 0, 3, 7), withoutDeclarations[0].Range);
        Assert.Equal(
            [0, 1, 2, 3],
            withDeclarations.Select(location => (int)location.Range.Start.Line).ToArray());
    }

    [Fact]
    public void ClassDeclarationUnionsItsTypeAndValueReferences()
    {
        const string source = """
            class Box {}
            const constructor = Box;
            const item: Box = new Box();
            """;

        var references = References(
            source, "Box", occurrence: 0, includeDeclaration: true);

        Assert.Equal(
            [
                new Range(0, 6, 0, 9),
                new Range(1, 20, 1, 23),
                new Range(2, 12, 2, 15),
                new Range(2, 22, 2, 25),
            ],
            references.Select(location => location.Range).ToArray());
    }

    [Fact]
    public void PropertyNamesAreNotGuessedFromMatchingText()
    {
        const string source = """
            const value = 1;
            const object = { value: 2 };
            console.log(value, object.value);
            """;

        var references = References(
            source, "value", occurrence: 0, includeDeclaration: true);

        Assert.Equal(
            [
                new Range(0, 6, 0, 11),
                new Range(2, 12, 2, 17),
            ],
            references.Select(location => location.Range).ToArray());
    }

    [Fact]
    public void OpenImporterReferencesResolveBackToTheSourceDeclaration()
    {
        string root = Path.GetFullPath("reference-module-test");
        string dependencyPath = Path.Combine(root, "dependency.ts");
        string entryPath = Path.Combine(root, "entry.ts");
        const string dependency = "export const original = 1;\n";
        const string entry = """
            import { original as local } from "./dependency";
            console.log(local);
            local;
            """;
        var openDocuments = new Dictionary<string, string>
        {
            [dependencyPath] = dependency,
            [entryPath] = entry,
        };

        var references = References(
            dependencyPath,
            dependency,
            "original",
            occurrence: 0,
            includeDeclaration: false,
            openDocuments);

        Assert.Equal(4, references.Count);
        Assert.All(
            references,
            location => Assert.Equal(
                DocumentUri.FromFileSystemPath(entryPath),
                location.Uri));
        Assert.Equal(
            [
                new Range(0, 9, 0, 17),
                new Range(0, 21, 0, 26),
                new Range(1, 12, 1, 17),
                new Range(2, 0, 2, 5),
            ],
            references.Select(location => location.Range).ToArray());
    }

    [Fact]
    public void ClosedImportersAreDiscoveredFromTheConfiguredProject()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile(
            "tsconfig.json",
            """{ "include": ["src/**/*.ts"], "exclude": ["excluded"] }""");
        string dependencyPath = directory.CreateFile(
            "src/dependency.ts",
            "export const original = 1;\n");
        string importerPath = directory.CreateFile(
            "src/importer.ts",
            """
            import { original as local } from "./dependency";
            console.log(local);
            local;
            """);
        string excludedPath = directory.CreateFile(
            "excluded/importer.ts",
            """
            import { original } from "../src/dependency";
            original;
            """);
        string dependency = File.ReadAllText(dependencyPath);

        var references = References(
            dependencyPath,
            dependency,
            "original",
            occurrence: 0,
            includeDeclaration: false,
            new Dictionary<string, string> { [dependencyPath] = dependency });

        Assert.Equal(4, references.Count);
        Assert.All(
            references,
            location => Assert.Equal(
                DocumentUri.FromFileSystemPath(importerPath),
                location.Uri));
        Assert.DoesNotContain(
            references,
            location => location.Uri == DocumentUri.FromFileSystemPath(excludedPath));
    }

    [Fact]
    public void ConfiguredModuleResolutionIsUsedForClosedReverseImporters()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile(
            "tsconfig.json",
            """
            {
              "compilerOptions": {
                "baseUrl": ".",
                "paths": { "@lib/*": ["src/lib/*"] }
              },
              "include": ["src/**/*.ts"]
            }
            """);
        string dependencyPath = directory.CreateFile(
            "src/lib/dependency.ts",
            "export interface Shape { size: number; }\n");
        string importerPath = directory.CreateFile(
            "src/importer.ts",
            """
            import type { Shape as LocalShape } from "@lib/dependency";
            const value: LocalShape = { size: 1 };
            """);
        string dependency = File.ReadAllText(dependencyPath);

        var references = References(
            dependencyPath,
            dependency,
            "Shape",
            occurrence: 0,
            includeDeclaration: false,
            new Dictionary<string, string> { [dependencyPath] = dependency });

        Assert.Equal(3, references.Count);
        Assert.All(
            references,
            location => Assert.Equal(
                DocumentUri.FromFileSystemPath(importerPath),
                location.Uri));
    }

    [Fact]
    public void ConfiguredRootSetReportsWhetherReverseDiscoveryIsComplete()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string configPath = directory.CreateFile(
            "tsconfig.json",
            """{ "include": ["src/**/*.ts"] }""");
        string dependencyPath = directory.CreateFile(
            "src/dependency.ts",
            "export const value = 1;\n");
        directory.CreateFile(
            "src/importer.ts",
            "import { value } from \"./dependency\";\nvalue;\n");
        string dependency = File.ReadAllText(dependencyPath);

        var model = Assert.IsType<CheckedNavigationModel>(
            NavigationModelBuilder.TryBuild(
                dependencyPath,
                dependency,
                new Dictionary<string, string> { [dependencyPath] = dependency }));

        Assert.True(model.Scope.IsComplete);
        Assert.Equal(Path.GetFullPath(configPath), model.Scope.ConfigPath);
        Assert.Equal(2, model.Scope.RootFiles.Count);
    }

    [Fact]
    public void FailedConfiguredRootMakesReverseDiscoveryExplicitlyIncomplete()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile(
            "tsconfig.json",
            """{ "include": ["src/**/*.ts"] }""");
        string dependencyPath = directory.CreateFile(
            "src/dependency.ts",
            "export const value = 1;\n");
        directory.CreateFile("src/broken.ts", "const = ;\n");
        string dependency = File.ReadAllText(dependencyPath);

        var model = Assert.IsType<CheckedNavigationModel>(
            NavigationModelBuilder.TryBuild(
                dependencyPath,
                dependency,
                new Dictionary<string, string> { [dependencyPath] = dependency }));

        Assert.False(model.Scope.IsComplete);
        Assert.NotNull(model.Scope.ConfigPath);
    }

    [Fact]
    public void OpenDocumentFallbackIsExplicitlyIncompleteWithoutAConfig()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.GetPath("standalone.ts");
        const string source = "const value = 1;\nvalue;\n";

        var model = Assert.IsType<CheckedNavigationModel>(
            NavigationModelBuilder.TryBuild(
                path,
                source,
                new Dictionary<string, string> { [path] = source }));

        Assert.False(model.Scope.IsComplete);
        Assert.Null(model.Scope.ConfigPath);
        Assert.Empty(model.Scope.RootFiles);
    }

    [Fact]
    public void TypeOnlyImportReferencesIncludeAliasesAndAnnotations()
    {
        string root = Path.GetFullPath("type-reference-module-test");
        string dependencyPath = Path.Combine(root, "dependency.ts");
        string entryPath = Path.Combine(root, "entry.ts");
        const string dependency = "export interface Shape { size: number; }\n";
        const string entry = """
            import type { Shape as LocalShape } from "./dependency";
            const value: LocalShape = { size: 1 };
            """;
        var openDocuments = new Dictionary<string, string>
        {
            [dependencyPath] = dependency,
            [entryPath] = entry,
        };

        var references = References(
            entryPath,
            entry,
            "LocalShape",
            occurrence: 1,
            includeDeclaration: true,
            openDocuments);

        Assert.Equal(4, references.Count);
        Assert.Equal(
            DocumentUri.FromFileSystemPath(dependencyPath),
            references[0].Uri);
        Assert.Equal(new Range(0, 17, 0, 22), references[0].Range);
        Assert.Equal(
            [
                new Range(0, 14, 0, 19),
                new Range(0, 23, 0, 33),
                new Range(1, 13, 1, 23),
            ],
            references.Skip(1).Select(location => location.Range).ToArray());
    }

    [Fact]
    public void ReExportChainPreservesAllAliasOccurrences()
    {
        string root = Path.GetFullPath("reexport-reference-module-test");
        string dependencyPath = Path.Combine(root, "dependency.ts");
        string barrelPath = Path.Combine(root, "barrel.ts");
        string entryPath = Path.Combine(root, "entry.ts");
        const string dependency = "export function run(): void {}\n";
        const string barrel = "export { run as execute } from \"./dependency\";\n";
        const string entry = """
            import { execute as start } from "./barrel";
            start();
            """;
        var openDocuments = new Dictionary<string, string>
        {
            [dependencyPath] = dependency,
            [barrelPath] = barrel,
            [entryPath] = entry,
        };

        var references = References(
            dependencyPath,
            dependency,
            "run",
            occurrence: 0,
            includeDeclaration: true,
            openDocuments);

        Assert.Equal(6, references.Count);
        Assert.Equal(
            new Range(0, 16, 0, 19),
            Assert.Single(
                references,
                location =>
                    location.Uri == DocumentUri.FromFileSystemPath(dependencyPath)).Range);
        Assert.Equal(
            [
                new Range(0, 9, 0, 12),
                new Range(0, 16, 0, 23),
            ],
            references
                .Where(location =>
                    location.Uri == DocumentUri.FromFileSystemPath(barrelPath))
                .Select(location => location.Range)
                .ToArray());
        Assert.Equal(
            [
                new Range(0, 9, 0, 16),
                new Range(0, 20, 0, 25),
                new Range(1, 0, 1, 5),
            ],
            references
                .Where(location =>
                    location.Uri == DocumentUri.FromFileSystemPath(entryPath))
                .Select(location => location.Range)
                .ToArray());
    }

    [Fact]
    public void MalformedUnrelatedOpenBufferDoesNotEraseReferences()
    {
        string root = Path.GetFullPath("isolated-reference-model-test");
        string currentPath = Path.Combine(root, "current.ts");
        string malformedPath = Path.Combine(root, "malformed.ts");
        const string source = "const target = 1;\ntarget;\n";
        var openDocuments = new Dictionary<string, string>
        {
            [currentPath] = source,
            [malformedPath] = "const = ;",
        };

        var references = References(
            currentPath,
            source,
            "target",
            occurrence: 1,
            includeDeclaration: true,
            openDocuments);

        Assert.Equal(2, references.Count);
        Assert.All(
            references,
            location => Assert.Equal(
                DocumentUri.FromFileSystemPath(currentPath),
                location.Uri));
    }

    [Fact]
    public void UnrelatedOpenScriptDoesNotMergeSameNamedGlobals()
    {
        string root = Path.GetFullPath("unrelated-reference-model-test");
        string currentPath = Path.Combine(root, "current.ts");
        string unrelatedPath = Path.Combine(root, "unrelated.ts");
        const string source = "const target = 1;\ntarget;\n";
        var openDocuments = new Dictionary<string, string>
        {
            [currentPath] = source,
            [unrelatedPath] = "const target = 2;\ntarget;\n",
        };

        var references = References(
            currentPath,
            source,
            "target",
            occurrence: 1,
            includeDeclaration: true,
            openDocuments);

        Assert.Equal(2, references.Count);
        Assert.All(
            references,
            location => Assert.Equal(
                DocumentUri.FromFileSystemPath(currentPath),
                location.Uri));
    }

    [Fact]
    public void ReusedCheckerDoesNotReturnStaleOccurrences()
    {
        var checker = new TypeChecker();
        var first = Parse("first-reference.ts", "const current = 1; current;\n");
        checker.CheckWithRecovery(first.Statements, first.Document);

        var second = Parse("second-reference.ts", "const current = 2; current; current;\n");
        checker.CheckWithRecovery(second.Statements, second.Document);

        var references = checker.Bindings.FindReferences(
            second.Document,
            second.Document.Text.LastIndexOf("current", StringComparison.Ordinal),
            includeDeclarations: true);

        Assert.Equal(3, references.Count);
        Assert.All(
            references,
            reference => Assert.Same(second.Document, reference.Document));
    }

    [Fact]
    public async Task HandlerHonorsIncludeDeclaration()
    {
        string path = Path.GetFullPath("handler-reference-test.ts");
        DocumentUri uri = DocumentUri.FromFileSystemPath(path);
        const string source = "const answer = 42;\nconsole.log(answer);\n";
        var store = new DocumentStore();
        store.Set(uri.ToString(), source);
        var handler = new ReferencesHandler(store, new ReferenceService());

        var result = await handler.Handle(
            new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(1, 12),
                Context = new ReferenceContext { IncludeDeclaration = false },
            },
            CancellationToken.None);

        var location = Assert.Single(
            Assert.IsAssignableFrom<LocationContainer>(result));
        Assert.Equal(new Range(1, 12, 1, 18), location.Range);
    }

    private IReadOnlyList<Location> References(
        string source,
        string name,
        int occurrence,
        bool includeDeclaration)
    {
        string path = Path.GetFullPath("reference-test.ts");
        return References(
            path,
            source,
            name,
            occurrence,
            includeDeclaration,
            new Dictionary<string, string> { [path] = source });
    }

    private IReadOnlyList<Location> References(
        string path,
        string source,
        string name,
        int occurrence,
        bool includeDeclaration,
        IReadOnlyDictionary<string, string> openDocuments)
    {
        int offset = NthIndexOf(source, name, occurrence);
        var lines = new LineIndex(source);
        var (line, column) = lines.ToPosition(offset);
        return _references.FindReferences(
            path,
            source,
            new Position(line - 1, column - 1),
            includeDeclaration,
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
