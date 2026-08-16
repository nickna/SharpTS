using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer.Services;
using Xunit;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.Tests.LanguageServer;

public class NavigationDomainTests
{
    private readonly DefinitionService _definitions = new();
    private readonly ReferenceService _references = new();

    [Fact]
    public void GenericTypeParameterUsesResolveToTheirDeclaration()
    {
        const string source =
            "function identity<T>(value: T): T { return value; }\n";

        Location parameterType = Assert.Single(
            Definitions(source, "T", occurrence: 1));
        Location returnType = Assert.Single(
            Definitions(source, "T", occurrence: 2));
        IReadOnlyList<Location> references = References(
            source,
            "T",
            occurrence: 1);

        Assert.Equal(new Range(0, 18, 0, 19), parameterType.Range);
        Assert.Equal(parameterType, returnType);
        Assert.Equal(
            [
                new Range(0, 18, 0, 19),
                new Range(0, 28, 0, 29),
                new Range(0, 32, 0, 33),
            ],
            references.Select(location => location.Range).ToArray());
    }

    [Fact]
    public void MappedAndInferParametersHaveIndependentTypeIdentities()
    {
        const string source = """
            type Keys<T> = { [K in keyof T]: K };
            type Item<T> = T extends Array<infer U> ? U : never;
            """;

        Location mapped = Assert.Single(
            Definitions(source, "K", occurrence: 1));
        Location inferred = Assert.Single(
            Definitions(source, "U", occurrence: 1));

        Assert.Equal(new Range(0, 18, 0, 19), mapped.Range);
        Assert.Equal(new Range(1, 37, 1, 38), inferred.Range);
    }

    [Fact]
    public void BreakAndContinueLabelsResolveToTheLabeledStatement()
    {
        const string source = """
            outer: for (let i = 0; i < 2; i++) {
              if (i === 0) continue outer;
              break outer;
            }
            """;

        Location definition = Assert.Single(
            Definitions(source, "outer", occurrence: 1));
        IReadOnlyList<Location> references = References(
            source,
            "outer",
            occurrence: 2);

        Assert.Equal(new Range(0, 0, 0, 5), definition.Range);
        Assert.Equal(
            [
                new Range(0, 0, 0, 5),
                new Range(1, 24, 1, 29),
                new Range(2, 8, 2, 13),
            ],
            references.Select(location => location.Range).ToArray());
    }

    [Fact]
    public void QualifiedNamespaceTypeAndValueMembersResolvePrecisely()
    {
        const string source = """
            namespace Tools {
              export interface Shape { size: number; }
              export const value = 1;
              export namespace Nested {
                export function run(): void {}
              }
            }
            const shape: Tools.Shape = { size: 1 };
            console.log(Tools.value);
            Tools.Nested.run();
            """;

        Location shape = Assert.Single(
            Definitions(source, "Shape", occurrence: 1));
        Location value = Assert.Single(
            Definitions(source, "value", occurrence: 1));
        Location nested = Assert.Single(
            Definitions(source, "Nested", occurrence: 1));
        Location run = Assert.Single(
            Definitions(source, "run", occurrence: 1));
        IReadOnlyList<Location> runReferences = References(
            source,
            "run",
            occurrence: 1);

        Assert.Equal(new Range(1, 19, 1, 24), shape.Range);
        Assert.Equal(new Range(2, 15, 2, 20), value.Range);
        Assert.Equal(new Range(3, 19, 3, 25), nested.Range);
        Assert.Equal(new Range(4, 20, 4, 23), run.Range);
        Assert.Equal(
            [
                new Range(4, 20, 4, 23),
                new Range(9, 13, 9, 16),
            ],
            runReferences.Select(location => location.Range).ToArray());
    }

    [Fact]
    public void NamespaceImportMembersResolveAcrossModules()
    {
        string root = Path.GetFullPath("namespace-import-navigation");
        string dependencyPath = Path.Combine(root, "dependency.ts");
        string entryPath = Path.Combine(root, "entry.ts");
        const string dependency = """
            export interface Shape { size: number; }
            export const value = 1;
            """;
        const string entry = """
            import * as lib from "./dependency";
            const shape: lib.Shape = { size: 1 };
            console.log(lib.value);
            """;
        var openDocuments = new Dictionary<string, string>
        {
            [dependencyPath] = dependency,
            [entryPath] = entry,
        };

        Location shape = Assert.Single(
            Definitions(
                entryPath,
                entry,
                "Shape",
                occurrence: 0,
                openDocuments));
        Location value = Assert.Single(
            Definitions(
                entryPath,
                entry,
                "value",
                occurrence: 0,
                openDocuments));

        Assert.Equal(DocumentUri.FromFileSystemPath(dependencyPath), shape.Uri);
        Assert.Equal(new Range(0, 17, 0, 22), shape.Range);
        Assert.Equal(DocumentUri.FromFileSystemPath(dependencyPath), value.Uri);
        Assert.Equal(new Range(1, 13, 1, 18), value.Range);

        IReadOnlyList<Location> valueReferences = _references.FindReferences(
            entryPath,
            entry,
            PositionOf(entry, "value", occurrence: 0),
            includeDeclaration: true,
            openDocuments);
        Assert.Equal(2, valueReferences.Count);
        Assert.Contains(
            valueReferences,
            location =>
                location.Uri == DocumentUri.FromFileSystemPath(dependencyPath) &&
                location.Range == new Range(1, 13, 1, 18));
    }

    private IReadOnlyList<Location> Definitions(
        string source,
        string name,
        int occurrence) =>
        Definitions(
            Path.GetFullPath("navigation-domain.ts"),
            source,
            name,
            occurrence,
            new Dictionary<string, string>());

    private IReadOnlyList<Location> Definitions(
        string path,
        string source,
        string name,
        int occurrence,
        IReadOnlyDictionary<string, string> openDocuments)
    {
        Position position = PositionOf(source, name, occurrence);
        return _definitions.FindDefinitions(
            path,
            source,
            position,
            openDocuments);
    }

    private IReadOnlyList<Location> References(
        string source,
        string name,
        int occurrence)
    {
        string path = Path.GetFullPath("navigation-domain.ts");
        return _references.FindReferences(
            path,
            source,
            PositionOf(source, name, occurrence),
            includeDeclaration: true,
            new Dictionary<string, string> { [path] = source });
    }

    private static Position PositionOf(
        string source,
        string name,
        int occurrence)
    {
        int offset = -1;
        for (int i = 0; i <= occurrence; i++)
        {
            offset = source.IndexOf(
                name,
                offset + 1,
                StringComparison.Ordinal);
            Assert.True(offset >= 0);
        }

        var lines = new SharpTS.Parsing.LineIndex(source);
        var (line, column) = lines.ToPosition(offset);
        return new Position(line - 1, column - 1);
    }
}
