using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Services;
using SharpTS.Parsing;
using SharpTS.Tests.IntegrationTests;
using Xunit;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.Tests.LanguageServer;

public sealed class MemberReferenceServiceTests
{
    [Theory]
    [InlineData("model.ts", "export class Model<T> { /*declaration*/value: T; }")]
    [InlineData("model.d.ts", "export declare class Model<T> { /*declaration*/value: T; }")]
    public async Task CanonicalMemberAnchorUnitesClosedReverseImportersAcrossIndependentProjects(
        string fileName, string model)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("tsconfig.json", """{"files":[],"references":[{"path":"lib"},{"path":"apps/one"},{"path":"apps/two/tsconfig.app.json"}]}""");
        directory.CreateFile("lib/tsconfig.json", """{"compilerOptions":{"noLib":true,"types":[],"composite":true},"include":["*.ts"]}""");
        string modelPath = directory.CreateFile("lib/" + fileName, model);
        directory.CreateFile("lib/barrel.ts", "export { Model as Original } from './model';");
        const string first = "import { Original as Alias } from '@one/barrel'; const first = new Alias<number>(); first./*first*/value;";
        string firstPath = directory.CreateFile("apps/one/entry.ts", first);
        directory.CreateFile("apps/one/tsconfig.json", """{"compilerOptions":{"noLib":true,"types":[],"strictNullChecks":true,"baseUrl":".","paths":{"@one/*":["../../lib/*"]}},"include":["*.ts"],"references":[{"path":"../../lib"}]}""");
        const string second = "import { Model as DifferentAlias } from '@two/model'; const second = new DifferentAlias<string>(); second./*second*/value;";
        string secondPath = directory.CreateFile("apps/two/entry.ts", second);
        directory.CreateFile("apps/two/tsconfig.app.json", """{"compilerOptions":{"noLib":true,"types":[],"strictNullChecks":false,"baseUrl":".","paths":{"@two/*":["../../lib/*"]}},"include":["*.ts"],"references":[{"path":"../../lib"}]}""");
        var store = Open(modelPath, model);
        using var analysis = new SemanticAnalysisService();
        using var references = new ReferenceService(analysis);

        var fromDeclaration = await QueryAsync(references, Capture(store, modelPath), model, "declaration", [directory.Path]);
        Assert.True(fromDeclaration.IsComplete);
        Assert.True(fromDeclaration.IsCurrent());
        Assert.False(fromDeclaration.IsRenameEligible);
        Assert.Equal(4, fromDeclaration.ConfigPaths.Count);
        AssertLocations(fromDeclaration, Target(modelPath, model, "declaration", "value"),
            Target(firstPath, first, "first", "value"), Target(secondPath, second, "second", "value"));

        Assert.True(store.Open(UriOf(firstPath).ToString(), first, 1));
        var fromUse = await QueryAsync(references, Capture(store, firstPath), first, "first", [directory.Path]);
        Assert.True(fromUse.IsComplete);
        Assert.Equal(Keys(fromDeclaration.Locations), Keys(fromUse.Locations));
        var withoutDeclarations = await QueryAsync(references, Capture(store, firstPath), first, "first", [directory.Path], includeDeclaration: false);
        AssertLocations(withoutDeclarations, Target(firstPath, first, "first", "value"), Target(secondPath, second, "second", "value"));
    }

    [Fact]
    public async Task DirtyDeclarationOverlayAndCloseMoveEveryProjectAnchorWithoutWarmRechecks()
    {
        using var directory = Project();
        const string model = "export class Model { /*declaration*/value: number = 1; }";
        string modelPath = directory.CreateFile("model.ts", model);
        const string first = "import { Model } from './model'; const model = new Model(); model./*first*/value;";
        string firstPath = directory.CreateFile("entry.ts", first);
        const string closed = "import { Model } from './model'; const model = new Model(); model./*closed*/value = 2;";
        string closedPath = directory.CreateFile("closed.ts", closed);
        var store = Open(firstPath, first);
        using var analysis = new SemanticAnalysisService();
        using var references = new ReferenceService(analysis);
        var diskCapture = Capture(store, firstPath);
        var disk = await QueryAsync(references, diskCapture, first, "first", [directory.Path]);
        Assert.True(disk.IsComplete);

        string dirty = "// unsaved 😀\r\n// second line\r\n" + model;
        Assert.True(store.Open(UriOf(modelPath).ToString(), dirty, 1));
        var capture = Capture(store, firstPath);
        var moved = await QueryAsync(references, capture, first, "first", [directory.Path]);
        Assert.True(moved.IsComplete);
        AssertLocations(moved, Target(modelPath, dirty, "declaration", "value"),
            Target(firstPath, first, "first", "value"), Target(closedPath, closed, "closed", "value"));
        Assert.False(store.IsCurrent(diskCapture.Document.Uri, diskCapture.Document.Version, diskCapture.WorkspaceVersion));
        long checkedBeforeRepeat = analysis.Statistics.Checks;
        var repeated = await QueryAsync(references, capture, first, "first", [directory.Path]);
        Assert.Equal(Keys(moved.Locations), Keys(repeated.Locations));
        Assert.Equal(checkedBeforeRepeat, analysis.Statistics.Checks);
        Assert.Equal(model, File.ReadAllText(modelPath));

        Assert.NotNull(store.Remove(UriOf(modelPath).ToString()));
        Assert.False(store.IsCurrent(capture.Document.Uri, capture.Document.Version, capture.WorkspaceVersion));
        var restored = await QueryAsync(references, Capture(store, firstPath), first, "first", [directory.Path]);
        Assert.True(restored.IsComplete);
        Assert.Equal(Keys(disk.Locations), Keys(restored.Locations));
    }

    [Fact]
    public async Task OverloadAndAccessorDeclarationsDeduplicateWithExactUtf16CrLfTargetRanges()
    {
        using var directory = Project();
        const string model = "// target 😀\r\nexport class Model { /* 😀 */\r\n" +
            " /*overload1*/select(input: number): number;\r\n /*overload2*/select(input: string): number;\r\n" +
            " /*implementation*/select(input: any): number { return 1; }\r\n" +
            " get /*getter*/value(): number { return 1; }\r\n set /*setter*/value(input: number) {}\r\n}\r\n";
        string modelPath = directory.CreateFile("model space.tsx", model);
        const string source = "// request 😀\r\nimport { Model } from './model space';\r\nconst model = new Model();\r\n" +
            "model./*call*/select(1);\r\nmodel./*read*/value;\r\nmodel./*write*/value = 2;\r\n";
        string path = directory.CreateFile("entry space.tsx", source);
        var capture = Capture(Open(path, source), path);
        using var analysis = new SemanticAnalysisService();
        using var references = new ReferenceService(analysis);

        var methods = await QueryAsync(references, capture, source, "call", [directory.Path]);
        Assert.True(methods.IsComplete);
        AssertLocations(methods, Target(modelPath, model, "overload1", "select"), Target(modelPath, model, "overload2", "select"),
            Target(modelPath, model, "implementation", "select"), Target(path, source, "call", "select"));
        Assert.All(methods.Locations, location => Assert.Contains("%20", location.Uri.ToString(), StringComparison.Ordinal));
        var methodUses = await QueryAsync(references, capture, source, "call", [directory.Path], includeDeclaration: false);
        AssertLocations(methodUses, Target(path, source, "call", "select"));
        var accessor = await QueryAsync(references, capture, source, "write", [directory.Path]);
        AssertLocations(accessor, Target(modelPath, model, "getter", "value"), Target(modelPath, model, "setter", "value"),
            Target(path, source, "read", "value"), Target(path, source, "write", "value"));
        var accessorUses = await QueryAsync(references, capture, source, "read", [directory.Path], includeDeclaration: false);
        AssertLocations(accessorUses, Target(path, source, "read", "value"), Target(path, source, "write", "value"));
    }

    [Fact]
    public async Task SourceIdentitySeparatesOverridesStaticFacetsUnrelatedClassesAndClassExpressions()
    {
        using var directory = Project();
        const string source = """
            class Base<T> { /*field*/value: T; /*baseMethod*/method(): number { return 1; } }
            class Derived extends Base<number> { inspect(): number { return super./*super*/method(); } }
            class Override extends Base<number> { /*overrideDeclaration*/method(): number { return 2; } }
            class Other { /*otherDeclaration*/value: number = 2; }
            class Facets { /*instanceDeclaration*/value: number = 1; static /*staticDeclaration*/value: number = 2; }
            const first = new Base<number>(); const second = new Base<string>(); const derived = new Derived();
            first./*first*/value; second./*second*/value; derived?./*inherited*/value;
            first./*baseCall*/method(); new Override()./*overrideUse*/method(); new Other()./*otherUse*/value;
            new Facets()./*instanceUse*/value; Facets./*staticUse*/value;
            const Factory = class { /*expressionDeclaration*/value: number = 3; }; new Factory()./*expressionUse*/value;
            """;
        string path = directory.CreateFile("entry.ts", source);
        var capture = Capture(Open(path, source), path);
        using var analysis = new SemanticAnalysisService();
        using var references = new ReferenceService(analysis);
        var field = await QueryAsync(references, capture, source, "first", [directory.Path]);
        Assert.True(field.IsComplete);
        AssertLocations(field, Target(path, source, "field", "value"), Target(path, source, "first", "value"),
            Target(path, source, "second", "value"), Target(path, source, "inherited", "value"));
        AssertLocations(await QueryAsync(references, capture, source, "super", [directory.Path]),
            Target(path, source, "baseMethod", "method"), Target(path, source, "super", "method"), Target(path, source, "baseCall", "method"));
        foreach (var pair in new[] { ("overrideDeclaration", "overrideUse", "method"), ("otherDeclaration", "otherUse", "value"),
            ("instanceDeclaration", "instanceUse", "value"), ("staticDeclaration", "staticUse", "value"),
            ("expressionDeclaration", "expressionUse", "value") })
            AssertLocations(await QueryAsync(references, capture, source, pair.Item2, [directory.Path]),
                Target(path, source, pair.Item1, pair.Item3), Target(path, source, pair.Item2, pair.Item3));
    }

    [Fact]
    public async Task PrivateBrandReadWriteAndCallReferencesKeepTheirFullPrivateNameSpans()
    {
        using var directory = Project();
        const string source = """
            class Model {
                /*field*/#value: number = 1;
                /*method*/#method(input: number): number { return input; }
                inspect(candidate: object): boolean {
                    this./*read*/#value; this./*write*/#value = 2; this./*call*/#method(1);
                    return /*brand*/#value in candidate;
                }
            }
            """;
        string path = directory.CreateFile("entry.ts", source);
        var capture = Capture(Open(path, source), path);
        using var references = new ReferenceService();
        var field = await QueryAsync(references, capture, source, "brand", [directory.Path]);
        Assert.True(field.IsComplete);
        AssertLocations(field, Target(path, source, "field", "#value"), Target(path, source, "read", "#value"),
            Target(path, source, "write", "#value"), Target(path, source, "brand", "#value"));
        AssertLocations(await QueryAsync(references, capture, source, "call", [directory.Path]),
            Target(path, source, "method", "#method"), Target(path, source, "call", "#method"));
        AssertLocations(await QueryAsync(references, capture, source, "brand", [directory.Path], includeDeclaration: false),
            Target(path, source, "read", "#value"), Target(path, source, "write", "#value"), Target(path, source, "brand", "#value"));
    }

    [Fact]
    public async Task LexicalParameterPropertyFacetWinsAndCompleteMemberReferencesStillCannotRename()
    {
        using var directory = Project();
        const string source = "class Model { constructor(public /*parameter*/value: number) { /*local*/value; } " +
            "read(): number { return this./*member*/value; } /*field*/other: number = 1; } " +
            "const model = new Model(1); model./*external*/value; model./*other*/other;";
        string path = directory.CreateFile("entry.ts", source);
        var capture = Capture(Open(path, source), path);
        using var analysis = new SemanticAnalysisService();
        using var references = new ReferenceService(analysis);
        var rename = new RenameService(references);
        var lexical = await QueryAsync(references, capture, source, "parameter", [directory.Path]);
        Assert.True(lexical.IsComplete);
        AssertLocations(lexical, Target(path, source, "parameter", "value"), Target(path, source, "local", "value"));
        var member = await QueryAsync(references, capture, source, "member", [directory.Path]);
        Assert.True(member.IsComplete);
        AssertLocations(member, Target(path, source, "parameter", "value"), Target(path, source, "member", "value"),
            Target(path, source, "external", "value"));
        foreach (string marker in new[] { "parameter", "local", "member", "external", "other" })
        {
            var domain = await QueryAsync(references, capture, source, marker, [directory.Path]);
            Assert.True(domain.IsComplete);
            Assert.False(domain.IsRenameEligible);
            Assert.Null((await rename.PrepareAsync(capture, At(source, marker), [directory.Path], CancellationToken.None)).Value);
            Assert.Null((await rename.RenameAsync(capture, At(source, marker), "renamed", [directory.Path], CancellationToken.None)).Value);
        }
    }

    [Theory]
    [InlineData("function read(object: any) { return object./*query*/value; }")]
    [InlineData("function read(object: unknown) { return object./*query*/value; }")]
    [InlineData("function read(object: { value: number }) { return object./*query*/value; }")]
    [InlineData("interface Shape { value: number; } function read(object: Shape) { return object./*query*/value; }")]
    [InlineData("class A { value: number = 1; } class B { value: number = 2; } function read(object: A | B) { return object./*query*/value; }")]
    [InlineData("class A { value: number = 1; } class B { value: number = 2; } function read(object: A & B) { return object./*query*/value; }")]
    [InlineData("class A { value: number = 1; } const object = new A(); object[/*query*/'val' + 'ue'];")]
    [InlineData("class A { value: number = 1; } const object = new A(); object[/*query*/'value'];")]
    [InlineData("class A { ['value']: number = 1; } new A()./*query*/value;")]
    [InlineData("'text'./*query*/length;")]
    public async Task UnsupportedOrAmbiguousOccurrencesNeverGuessMemberReferences(string source)
    {
        using var directory = Project();
        string path = directory.CreateFile("entry.ts", source);
        using var references = new ReferenceService();
        var result = await QueryAsync(references, Capture(Open(path, source), path), source, "query", [directory.Path]);
        Assert.Empty(result.Locations);
        Assert.False(result.IsRenameEligible);
    }

    [Theory]
    [InlineData("no-roots")]
    [InlineData("outside-roots")]
    [InlineData("no-config")]
    [InlineData("missing-file")]
    [InlineData("invalid-config")]
    [InlineData("missing-reference")]
    public async Task IncompleteDiscoveryCannotLeakEvenAProvedSeedMember(string failure)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        const string source = "class Model { /*declaration*/value: number = 1; } new Model()./*query*/value;";
        string path = directory.CreateFile("entry.ts", source);
        if (failure != "no-config")
            directory.CreateFile("tsconfig.json", failure switch
            {
                "missing-file" => """{"compilerOptions":{"noLib":true,"types":[]},"files":["entry.ts","missing.ts"]}""",
                "missing-reference" => """{"compilerOptions":{"noLib":true,"types":[]},"files":["entry.ts"],"references":[{"path":"missing-project"}]}""",
                _ => """{"compilerOptions":{"noLib":true,"types":[]},"files":["entry.ts"]}""",
            });
        if (failure == "invalid-config") directory.CreateFile("broken/tsconfig.json", "{ not json");
        IReadOnlyList<string>? roots = failure switch
        {
            "no-roots" => null,
            "outside-roots" => [directory.GetPath("outside")],
            _ => [directory.Path],
        };
        using var references = new ReferenceService();
        var result = await QueryAsync(references, Capture(Open(path, source), path), source, "query", roots);
        Assert.Empty(result.Locations);
        Assert.False(result.IsComplete);
        Assert.False(result.IsRenameEligible);
    }

    [Fact]
    public async Task NewClosedReverseImporterInvalidatesTheRetainedWorkspaceResult()
    {
        using var directory = Project();
        const string model = "export class Model { /*query*/value: number = 1; }";
        string modelPath = directory.CreateFile("model.ts", model);
        var capture = Capture(Open(modelPath, model), modelPath);
        using var analysis = new SemanticAnalysisService();
        using var references = new ReferenceService(analysis);
        var first = await QueryAsync(references, capture, model, "query", [directory.Path]);
        Assert.True(first.IsComplete);
        Assert.Single(first.Locations);
        const string importer = "import { Model } from './model'; new Model()./*use*/value;";
        string importerPath = directory.CreateFile("closed.ts", importer);

        Assert.False(first.IsCurrent());
        var next = await QueryAsync(references, capture, model, "query", [directory.Path]);
        Assert.True(next.IsCurrent());
        Assert.True(next.IsComplete);
        AssertLocations(next, Target(modelPath, model, "query", "value"), Target(importerPath, importer, "use", "value"));
        Assert.Single(first.Locations);
    }

    [Fact]
    public async Task UnrelatedTypeErrorDoesNotEraseAProvedUseInACompleteDiscoveryGraph()
    {
        using var directory = Project();
        const string source = "class Model { /*declaration*/value: number = 1; } const broken: number = 'wrong'; new Model()./*query*/value;";
        string path = directory.CreateFile("entry.ts", source);
        var capture = Capture(Open(path, source), path);
        using var analysis = new SemanticAnalysisService();
        using var references = new ReferenceService(analysis);
        var result = await QueryAsync(references, capture, source, "query", [directory.Path]);
        Assert.True(result.IsComplete);
        AssertLocations(result, Target(path, source, "declaration", "value"), Target(path, source, "query", "value"));
        using var lease = await analysis.GetDocumentAsync(capture);
        Assert.NotNull(lease);
        Assert.True(lease.Model.Snapshot.HasPartialSemantics);
        Assert.NotEmpty(lease.Model.Snapshot.Diagnostics);
    }

    [Fact]
    public async Task RetainedValidationRejectsSameSizeAndTimestampDependencyChanges()
    {
        using var directory = Project();
        const string model = "// first line\nexport class Model { /*declaration*/value: number = 1; }";
        const string changed = "//first\n//lin\nexport class Model { /*declaration*/value: number = 1; }";
        Assert.Equal(model.Length, changed.Length);
        string modelPath = directory.CreateFile("model.ts", model);
        const string source = "import { Model } from './model'; new Model()./*query*/value;";
        string path = directory.CreateFile("entry.ts", source);
        var capture = Capture(Open(path, source), path);
        using var references = new ReferenceService();
        var first = await QueryAsync(references, capture, source, "query", [directory.Path]);
        Assert.True(first.IsCurrent());
        DateTime timestamp = File.GetLastWriteTimeUtc(modelPath);
        File.WriteAllText(modelPath, changed);
        File.SetLastWriteTimeUtc(modelPath, timestamp);
        Assert.False(first.IsCurrent());
        var next = await QueryAsync(references, capture, source, "query", [directory.Path]);
        Assert.True(next.IsCurrent());
        AssertLocations(next, Target(modelPath, changed, "declaration", "value"), Target(path, source, "query", "value"));
        Assert.NotEqual(Keys(first.Locations), Keys(next.Locations));
    }

    [Fact]
    public async Task CancellingOneReferenceWaiterLeavesItsPeerAndWarmWorkspaceReusable()
    {
        using var directory = Project();
        const string source = "class Model { /*declaration*/value: number = 1; } new Model()./*query*/value;";
        string path = directory.CreateFile("entry.ts", source);
        var capture = Capture(Open(path, source), path);
        using var analysis = new SemanticAnalysisService();
        using var references = new ReferenceService(analysis);
        using var gate = new CheckGate();
        analysis.BeforeCheck = gate.HoldFirst;
        using var cancellation = new CancellationTokenSource();
        Task<NavigationReferenceResult> cancelled = QueryAsync(references, capture, source, "query", [directory.Path], cancellationToken: cancellation.Token);
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        Task<NavigationReferenceResult> peer = QueryAsync(references, capture, source, "query", [directory.Path]);
        await cancellation.CancelAsync();
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await cancelled.WaitAsync(TimeSpan.FromSeconds(10))); }
        finally { gate.Release(); }
        var result = await peer.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.IsComplete);
        AssertLocations(result, Target(path, source, "declaration", "value"), Target(path, source, "query", "value"));
        long checkedBeforeRepeat = analysis.Statistics.Checks;
        var warm = await QueryAsync(references, capture, source, "query", [directory.Path]);
        Assert.Equal(Keys(result.Locations), Keys(warm.Locations));
        Assert.Equal(checkedBeforeRepeat, analysis.Statistics.Checks);
    }

    private static TempTestDirectory Project()
    {
        var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("tsconfig.json", """{"compilerOptions":{"noLib":true,"types":[]},"include":["**/*.ts","**/*.tsx"]}""");
        return directory;
    }

    private static DocumentUri UriOf(string path) => DocumentUri.FromFileSystemPath(Path.GetFullPath(path));
    private static DocumentStore Open(string path, string source)
    {
        var store = new DocumentStore();
        Assert.True(store.Open(UriOf(path).ToString(), source, 1));
        return store;
    }
    private static DocumentRequestSnapshot Capture(DocumentStore store, string path)
    {
        Assert.True(store.TryCapture(UriOf(path).ToString(), out var capture));
        return capture;
    }
    private static int Offset(string source, string marker)
    {
        string comment = "/*" + marker + "*/";
        int offset = source.IndexOf(comment, StringComparison.Ordinal);
        Assert.True(offset >= 0, "Missing marker " + marker);
        return offset + comment.Length;
    }
    private static Position PositionAt(string source, int offset)
    {
        var (line, column) = new LineIndex(source).ToPosition(offset);
        return new(line - 1, column - 1);
    }
    private static Position At(string source, string marker) => PositionAt(source, Offset(source, marker));
    private static Location Target(string path, string source, string marker, string token)
    {
        int offset = Offset(source, marker);
        Assert.Equal(token, source.Substring(offset, token.Length));
        return new() { Uri = UriOf(path), Range = new Range(PositionAt(source, offset), PositionAt(source, offset + token.Length)) };
    }
    private static string[] Keys(IEnumerable<Location> locations) => locations.Select(location =>
        $"{location.Uri}|{location.Range.Start.Line}:{location.Range.Start.Character}-{location.Range.End.Line}:{location.Range.End.Character}").ToArray();
    private static void AssertLocations(NavigationReferenceResult result, params Location[] expected)
    {
        Location[] ordered = expected.OrderBy(location => location.Uri.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(location => location.Range.Start.Line).ThenBy(location => location.Range.Start.Character)
            .ThenBy(location => location.Range.End.Line).ThenBy(location => location.Range.End.Character).ToArray();
        Assert.Equal(Keys(ordered), Keys(result.Locations));
        Assert.Equal(result.Locations.Count, Keys(result.Locations).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
    private static Task<NavigationReferenceResult> QueryAsync(ReferenceService service, DocumentRequestSnapshot capture,
        string source, string marker, IReadOnlyList<string>? roots, bool includeDeclaration = true,
        CancellationToken cancellationToken = default) => service.FindReferenceResultAsync(capture, At(source, marker),
            includeDeclaration, roots, cancellationToken: cancellationToken);

    private sealed class CheckGate : IDisposable
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _released = new(false);
        private int _checks;
        public Task Entered => _entered.Task;
        public void HoldFirst()
        {
            if (Interlocked.Increment(ref _checks) != 1) return;
            _entered.TrySetResult();
            if (!_released.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Reference test check gate timed out.");
        }
        public void Release() => _released.Set();
        public void Dispose() { Release(); _released.Dispose(); }
    }
}
