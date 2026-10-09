using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Services;
using SharpTS.Parsing;
using SharpTS.Tests.IntegrationTests;
using Xunit;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.Tests.LanguageServer;

public sealed class MemberDefinitionSnapshotTests
{
    [Fact]
    public async Task ImportedReexportedGenericBaseTargetsItsOriginalSourceMember()
    {
        using var directory = Project();
        const string original = "export class Base<T> { value: T; }\n";
        string originalPath = directory.CreateFile("original.ts", original);
        directory.CreateFile("barrel.ts", "export { Base as Model } from './original';\n");
        const string source = "import { Model as Alias } from './barrel'; class Derived extends Alias<number> {} const model = new Derived(); model.value;";
        string path = directory.CreateFile("entry.ts", source);
        var capture = Capture(Open(path, source), path);
        using var analysis = new SemanticAnalysisService();
        using var definitions = new DefinitionService(analysis);

        NavigationDefinitionResult result = await definitions.FindDefinitionsAsync(capture,
            PositionAt(source, source.LastIndexOf("value", StringComparison.Ordinal)), CancellationToken.None);

        Assert.True(result.IsCurrent());
        AssertTarget(Assert.Single(result.Locations), originalPath, RangeAt(original,
            original.IndexOf("value", StringComparison.Ordinal), "value".Length));
    }

    [Theory]
    [InlineData("model.ts", "export class Model { value: number = 1; }\n")]
    [InlineData("model.d.ts", "export declare class Model { value: number; }\n")]
    public async Task DirtyDependencyAndCloseUseTheCapturedSourceAndReuseUnchangedQueries(
        string dependencyName, string original)
    {
        using var directory = Project();
        string dependency = directory.CreateFile(dependencyName, original);
        const string source = "import { Model } from './model'; const model = new Model(); model.value;";
        string path = directory.CreateFile("entry.ts", source);
        var store = Open(path, source);
        Position position = PositionAt(source, source.LastIndexOf("value", StringComparison.Ordinal));
        using var analysis = new SemanticAnalysisService();
        using var definitions = new DefinitionService(analysis);
        NavigationDefinitionResult disk = await definitions.FindDefinitionsAsync(Capture(store, path), position, CancellationToken.None);
        AssertTarget(Assert.Single(disk.Locations), dependency, RangeAt(original,
            original.IndexOf("value", StringComparison.Ordinal), "value".Length));

        const string prefix = "// unsaved dependency\r\n// 😀\r\n";
        string dirty = prefix + original;
        Assert.True(store.Open(UriOf(dependency).ToString(), dirty, version: 1));
        DocumentRequestSnapshot dirtyCapture = Capture(store, path);
        NavigationDefinitionResult moved = await definitions.FindDefinitionsAsync(dirtyCapture, position, CancellationToken.None);
        NavigationDefinitionResult repeated = await definitions.FindDefinitionsAsync(dirtyCapture, position, CancellationToken.None);
        Range expected = RangeAt(dirty, dirty.IndexOf("value", StringComparison.Ordinal), "value".Length);
        AssertTarget(Assert.Single(moved.Locations), dependency, expected);
        AssertTarget(Assert.Single(repeated.Locations), dependency, expected);
        Assert.Equal(2, analysis.Statistics.Checks);
        Assert.Equal(original, File.ReadAllText(dependency));

        Assert.NotNull(store.Remove(UriOf(dependency).ToString()));
        NavigationDefinitionResult closed = await definitions.FindDefinitionsAsync(Capture(store, path), position, CancellationToken.None);
        AssertTarget(Assert.Single(closed.Locations), dependency, Assert.Single(disk.Locations).Range);
        Assert.Equal(3, analysis.Statistics.Checks);
    }

    [Fact]
    public async Task RetainedDefinitionValidationRejectsSameLengthSameTimestampClosedDependencyEdit()
    {
        using var directory = Project();
        const string original = "// first line\nexport class Model { value: number = 1; }\n";
        const string changed = "//first\n//lin\nexport class Model { value: number = 1; }\n";
        Assert.Equal(original.Length, changed.Length);
        string dependency = directory.CreateFile("model.ts", original);
        const string source = "import { Model } from './model'; const model = new Model(); model.value;";
        string path = directory.CreateFile("entry.ts", source);
        DocumentRequestSnapshot capture = Capture(Open(path, source), path);
        Position position = PositionAt(source, source.LastIndexOf("value", StringComparison.Ordinal));
        using var analysis = new SemanticAnalysisService();
        using var definitions = new DefinitionService(analysis);
        NavigationDefinitionResult first = await definitions.FindDefinitionsAsync(capture, position, CancellationToken.None);
        Assert.True(first.IsCurrent());
        Assert.Equal(1, Assert.Single(first.Locations).Range.Start.Line);

        DateTime timestamp = File.GetLastWriteTimeUtc(dependency);
        File.WriteAllText(dependency, changed);
        File.SetLastWriteTimeUtc(dependency, timestamp);

        Assert.False(first.IsCurrent());
        NavigationDefinitionResult fresh = await definitions.FindDefinitionsAsync(capture, position, CancellationToken.None);
        Assert.True(fresh.IsCurrent());
        AssertTarget(Assert.Single(fresh.Locations), dependency,
            RangeAt(changed, changed.IndexOf("value", StringComparison.Ordinal), "value".Length));
        Assert.Equal(2, analysis.Statistics.Checks);
        Assert.Equal(1, Assert.Single(first.Locations).Range.Start.Line);
    }

    [Fact]
    public async Task TsxTargetUsesItsOwnUtf16CrLfCoordinatesAndEscapedFileUri()
    {
        using var directory = Project();
        const string prefix = "export class Model { /* 😀 */ ";
        const string dependencySource = "// target π\r\n" + prefix + "value: number = 1; }\r\n";
        string dependency = directory.CreateFile("model space.tsx", dependencySource);
        const string source = "// request 😀\r\nimport { Model } from './model space';\r\nconst model = new Model();\r\nmodel.value;\r\n";
        string path = directory.CreateFile("entry space.tsx", source);
        using var analysis = new SemanticAnalysisService();
        using var definitions = new DefinitionService(analysis);

        NavigationDefinitionResult result = await definitions.FindDefinitionsAsync(Capture(Open(path, source), path),
            new Position(3, "model.".Length), CancellationToken.None);

        Location target = Assert.Single(result.Locations);
        AssertTarget(target, dependency, new Range(1, prefix.Length, 1, prefix.Length + "value".Length));
        Assert.Contains("%20", target.Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnrelatedBrokenAndMissingConfiguredRootsDoNotEraseAProvenMemberTarget()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("tsconfig.json",
            """{"compilerOptions":{"noLib":true,"types":[]},"files":["entry.ts","model.ts","broken.ts","missing.ts"]}""");
        const string dependencySource = "export class Model { value: number = 1; }";
        string dependency = directory.CreateFile("model.ts", dependencySource);
        directory.CreateFile("broken.ts", "function { definitely broken");
        const string source = "import { Model } from './model'; const model = new Model(); model.value;";
        string path = directory.CreateFile("entry.ts", source);
        DocumentRequestSnapshot capture = Capture(Open(path, source), path);
        using var analysis = new SemanticAnalysisService();
        using var definitions = new DefinitionService(analysis);

        NavigationDefinitionResult result = await definitions.FindDefinitionsAsync(capture,
            PositionAt(source, source.LastIndexOf("value", StringComparison.Ordinal)), CancellationToken.None);

        AssertTarget(Assert.Single(result.Locations), dependency, RangeAt(dependencySource,
            dependencySource.IndexOf("value", StringComparison.Ordinal), "value".Length));
        using var lease = await analysis.GetDocumentAsync(capture);
        Assert.NotNull(lease);
        Assert.False(lease.Model.Scope.IsComplete);
        Assert.Equal(1, analysis.Statistics.Checks);
    }

    [Fact]
    public async Task NamespaceLexicalMemberKeepsItsOriginBesideASameSpelledClassMember()
    {
        using var directory = Project();
        const string source = "namespace Space { export const value = 1; } class Model { value: number = 2; } const model = new Model(); Space.value; model.value;";
        string path = directory.CreateFile("entry.ts", source);
        DocumentRequestSnapshot capture = Capture(Open(path, source), path);
        using var analysis = new SemanticAnalysisService();
        using var definitions = new DefinitionService(analysis);

        NavigationDefinitionResult lexical = await definitions.FindDefinitionsAsync(capture,
            PositionAt(source, source.IndexOf("Space.value", StringComparison.Ordinal) + "Space.".Length), CancellationToken.None);
        NavigationDefinitionResult member = await definitions.FindDefinitionsAsync(capture,
            PositionAt(source, source.LastIndexOf("value", StringComparison.Ordinal)), CancellationToken.None);

        AssertTarget(Assert.Single(lexical.Locations), path, RangeAt(source,
            source.IndexOf("value", StringComparison.Ordinal), "value".Length));
        AssertTarget(Assert.Single(member.Locations), path, RangeAt(source,
            source.IndexOf("value: number", StringComparison.Ordinal), "value".Length));
        Assert.Equal(1, analysis.Statistics.Checks);
    }

    [Fact]
    public async Task ParameterPropertyDefinitionDoesNotEnableItsLexicalOrMemberRename()
    {
        using var directory = Project();
        const string source = "class Model { constructor(public value: number) { value; } read(): number { return this.value; } } const model = new Model(1); const local = 1; local;";
        string path = directory.CreateFile("entry.ts", source);
        DocumentRequestSnapshot capture = Capture(Open(path, source), path);
        using var analysis = new SemanticAnalysisService();
        using var definitions = new DefinitionService(analysis);
        using var references = new ReferenceService(analysis);
        var rename = new RenameService(references);
        Range parameterRange = RangeAt(source, source.IndexOf("value", StringComparison.Ordinal), "value".Length);
        foreach (int offset in new[]
        {
            source.IndexOf("value", StringComparison.Ordinal),
            source.IndexOf("value;", StringComparison.Ordinal),
            source.IndexOf("this.value", StringComparison.Ordinal) + "this.".Length,
        })
        {
            Position position = PositionAt(source, offset);
            NavigationDefinitionResult result = await definitions.FindDefinitionsAsync(capture, position, CancellationToken.None);
            AssertTarget(Assert.Single(result.Locations), path, parameterRange);
            Assert.Null((await rename.PrepareAsync(capture, position, [directory.Path], CancellationToken.None)).Value);
            Assert.Null((await rename.RenameAsync(capture, position, "renamed", [directory.Path], CancellationToken.None)).Value);
        }
        Position local = PositionAt(source, source.LastIndexOf("local", StringComparison.Ordinal));
        Assert.NotNull((await rename.PrepareAsync(capture, local, [directory.Path], CancellationToken.None)).Value);
        Assert.NotNull((await rename.RenameAsync(capture, local, "renamedLocal", [directory.Path], CancellationToken.None)).Value);
    }

    private static TempTestDirectory Project()
    {
        var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("tsconfig.json",
            """{"compilerOptions":{"noLib":true,"types":[]},"include":["**/*.ts","**/*.tsx"]}""");
        return directory;
    }

    private static DocumentUri UriOf(string path) => DocumentUri.FromFileSystemPath(path);

    private static DocumentStore Open(string path, string source)
    {
        var store = new DocumentStore();
        Assert.True(store.Open(UriOf(path).ToString(), source, version: 1));
        return store;
    }

    private static DocumentRequestSnapshot Capture(DocumentStore store, string path)
    {
        Assert.True(store.TryCapture(UriOf(path).ToString(), out var capture));
        return capture;
    }

    private static Position PositionAt(string source, int offset)
    {
        var (line, column) = new LineIndex(source).ToPosition(offset);
        return new Position(line - 1, column - 1);
    }

    private static Range RangeAt(string source, int start, int length) =>
        new(PositionAt(source, start), PositionAt(source, start + length));

    private static void AssertTarget(Location actual, string path, Range range)
    {
        Assert.Equal(UriOf(path), actual.Uri);
        Assert.Equal(range, actual.Range);
    }
}
