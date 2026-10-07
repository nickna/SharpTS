using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Project;
using SharpTS.LanguageServer.Services;
using SharpTS.Parsing;
using SharpTS.Tests.IntegrationTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

public sealed class CursorSemanticAnalysisTests
{
    [Theory]
    [InlineData("{ invalid configuration")]
    [InlineData("{\"files\":[\"other.ts\"],\"compilerOptions\":{\"noLib\":true,\"strict\":true}}")]
    public async Task DiscoveredConfigurationCannotBecomeDefaultOptionsAfterFailure(string configuration)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("tsconfig.json", configuration);
        directory.CreateFile("other.ts", "export const other = 1;");
        const string text = "class Box { value = 1; } const box = new Box(); box.";
        string path = directory.CreateFile("entry.ts", text);
        using var service = new SemanticAnalysisService();

        using var result = await service.GetCursorDocumentAsync(Capture(Open(path), path), text.Length,
            EditorQueryKind.Completion, null, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, service.Statistics.Checks);
        Assert.Equal(0, service.Statistics.RetainedSnapshots);
    }

    [Fact]
    public async Task ColdRecoveryWithoutConfigurationRemainsSupported()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        const string text = "class Box { value = 1; } const box = new Box(); box.";
        string path = directory.CreateFile("entry.ts", text);
        using var service = new SemanticAnalysisService();

        using var result = await service.GetCursorDocumentAsync(Capture(Open(path), path), text.Length,
            EditorQueryKind.Completion, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result.Model.Scope.ConfigPath);
        var receiver = Assert.Single(result.Model.Document.EditorSyntax!.Members, member => member.IsRecovered).Receiver;
        Assert.Contains(result.Model.EditorFacts.GetReceiverMembers(result.Model.Document, receiver).Members,
            member => member.Name == "value");
    }

    [Fact]
    public async Task RecoveredCheckOwnsAFreshGraphAndRepeatedQueriesReuseIt()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        const string text = "class Box { value: number = 1; } const box = new Box(); box.";
        string path = directory.CreateFile("entry.ts", text);
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var seed = await service.GetDocumentAsync(capture);
        Assert.NotNull(seed);
        Assert.True(seed.Model.Snapshot.TryGetDocument(path, out var original));
        var statements = original!.Statements.ToArray();
        int spans = seed.Model.Document.Spans.Count;
        var syntax = original.Syntax;
        var facts = seed.Model.EditorFacts;

        using var recovered = await service.GetCursorDocumentAsync(capture, text.Length,
            EditorQueryKind.Completion, seed, CancellationToken.None);
        using var repeated = await service.GetCursorDocumentAsync(capture, text.Length,
            EditorQueryKind.Completion, seed, CancellationToken.None);

        Assert.NotNull(recovered); Assert.NotNull(repeated);
        Assert.Same(recovered.Model.Snapshot, repeated.Model.Snapshot);
        Assert.NotSame(seed.Model.Snapshot, recovered.Model.Snapshot);
        Assert.NotSame(seed.Model.Document, recovered.Model.Document);
        Assert.NotSame(seed.Model.Bindings, recovered.Model.Bindings);
        Assert.NotSame(facts, recovered.Model.EditorFacts);
        Assert.True(recovered.Model.Snapshot.HasRecoveredSyntax);
        Assert.Equal(text, recovered.Model.Document.Text);
        Assert.Equal(seed.Model.Snapshot.Options, recovered.Model.Snapshot.Options);
        Assert.True(recovered.Model.Snapshot.TryGetDocument(path, out var fresh));
        Assert.NotSame(original.Tokens[0], fresh!.Tokens[0]);
        Assert.NotSame(original.Statements[0], fresh.Statements[0]);
        var member = Assert.Single(fresh.Syntax!.Members, member => member.IsRecovered);
        Assert.True(member.Name.Start < 0);
        Assert.Equal(EditorFactAvailability.Available,
            recovered.Model.EditorFacts.GetOccurrence(recovered.Model.Document, member.Receiver)!.Availability);
        Assert.Equal("number", Assert.Single(recovered.Model.EditorFacts
            .GetReceiverMembers(recovered.Model.Document, member.Receiver).Members,
                candidate => candidate.Name == "value").Type.Text);
        Assert.Empty(facts.GetReceiverMembers(seed.Model.Document, member.Receiver).Members);
        Assert.Equal(statements, original.Statements);
        Assert.Equal(spans, seed.Model.Document.Spans.Count);
        Assert.Same(syntax, original.Syntax);
        Assert.Same(facts, seed.Model.EditorFacts);
        Assert.Equal(2, service.Statistics.Checks);
    }

    [Fact]
    public async Task FreshFailedReceiverCannotBorrowAnEarlierSuccessfulReceiver()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        const string text = "class Box { value = 1; } const box = new Box(); box.value; missing.";
        string path = directory.CreateFile("entry.ts", text);
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var seed = await service.GetDocumentAsync(capture);
        Assert.NotNull(seed);
        var oldReceiver = seed.Model.Document.EditorSyntax!.Members[0].Receiver;
        Assert.NotEmpty(seed.Model.EditorFacts.GetReceiverMembers(seed.Model.Document, oldReceiver).Members);

        using var fresh = await service.GetCursorDocumentAsync(capture, text.Length,
            EditorQueryKind.Completion, seed, CancellationToken.None);

        Assert.NotNull(fresh);
        var missing = Assert.Single(fresh.Model.Document.EditorSyntax!.Members, member => member.IsRecovered);
        Assert.Empty(fresh.Model.EditorFacts.GetReceiverMembers(fresh.Model.Document, missing.Receiver).Members);
        Assert.NotEqual(EditorFactAvailability.Available,
            fresh.Model.EditorFacts.GetOccurrence(fresh.Model.Document, missing.Receiver)?.Availability);
        Assert.Empty(fresh.Model.EditorFacts.GetReceiverMembers(fresh.Model.Document, oldReceiver).Members);
    }

    [Fact]
    public async Task ColdRecoveryHonorsConfiguredPathsLibrariesStrictnessDirectivesAndDirtyDeclarations()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("tsconfig.json", """
            {"files":["src/entry.ts"],"compilerOptions":{"strict":true,"lib":["es5"],"types":[],
            "baseUrl":".","paths":{"@model":["types/model.d.ts"]},"experimentalDecorators":true}}
            """);
        string declaration = directory.CreateFile("types/model.d.ts", "export declare class Box { value: number; }");
        string ambient = directory.CreateFile("src/ambient.d.ts", "declare const ambient: string;");
        const string text = "/// <reference path=\"./ambient.d.ts\" />\n" +
            "import { Box } from '@model'; const box = new Box(); ambient; box.";
        string path = directory.CreateFile("src/entry.ts", text);
        var store = Open(path);
        const string dirty = "export declare class Box { value: string; }";
        store.Open(UriOf(declaration), dirty, version: 1);
        using var service = new SemanticAnalysisService();

        using var fresh = await service.GetCursorDocumentAsync(Capture(store, path), text.Length,
            EditorQueryKind.Completion, null, CancellationToken.None);

        Assert.NotNull(fresh);
        Assert.True(fresh.Model.Snapshot.Options!.CheckerOptions.StrictNullChecks);
        Assert.Equal(DecoratorMode.Legacy, fresh.Model.Snapshot.Options.DecoratorMode);
        Assert.Equal(dirty, Document(fresh, declaration).Document.Text);
        Assert.Equal(File.ReadAllText(ambient), Document(fresh, ambient).Document.Text);
        // Embedded trusted libraries have no source document; their exact selected graph
        // dependency still proves that the configured program library was loaded.
        Assert.Contains(fresh.Model.Snapshot.Dependencies,
            edge => edge.TargetPath == "typescript-lib:lib.es5.d.ts");
        Assert.Contains(fresh.Model.Snapshot.Dependencies, edge =>
            edge.SourcePath == Path.GetFullPath(path) && edge.TargetPath == Path.GetFullPath(ambient));
        var receiver = Assert.Single(fresh.Model.Document.EditorSyntax!.Members, member => member.IsRecovered).Receiver;
        Assert.Equal("string", Assert.Single(fresh.Model.EditorFacts.GetReceiverMembers(fresh.Model.Document, receiver).Members,
            candidate => candidate.Name == "value").Type.Text);
        Assert.Equal(1, service.Statistics.Checks);
    }

    [Fact]
    public async Task RecoveredTsxKeepsProjectOptionsAndPerFileFactoryPragmas()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("tsconfig.json", """
            {"files":["entry.tsx"],"compilerOptions":{"noLib":true,"types":[],"jsx":"react-jsx",
            "jsxFactory":"configuredFactory","jsxImportSource":"configured-runtime"}}
            """);
        const string text = "/** @jsxRuntime classic */\n/** @jsx writtenFactory */\n" +
            "declare function writtenFactory(tag: any, props: any): any; " +
            "class Box { value = 1; } const view = <div/>; const box = new Box(); box.";
        string path = directory.CreateFile("entry.tsx", text);
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var seed = await service.GetDocumentAsync(capture);
        Assert.NotNull(seed);

        using var fresh = await service.GetCursorDocumentAsync(capture, text.Length,
            EditorQueryKind.Completion, seed, CancellationToken.None);

        Assert.NotNull(fresh);
        Assert.Equal(seed.Model.Snapshot.Options, fresh.Model.Snapshot.Options);
        Assert.Equal("configured-runtime", fresh.Model.Snapshot.Options!.JsxOptions.ImportSource);
        var view = Assert.Single(Document(fresh, path).Statements.OfType<Stmt.Const>(), statement => statement.Name.Lexeme == "view");
        var call = Assert.IsType<Expr.Call>(view.Initializer);
        Assert.Equal("writtenFactory", Assert.IsType<Expr.Variable>(call.Callee).Name.Lexeme);
        Assert.DoesNotContain(Document(fresh, path).Statements, statement => statement is Stmt.Import);
        Assert.Single(fresh.Model.Document.EditorSyntax!.Members, member => member.IsRecovered);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongCaptureSeedCannotReplayOldOverlayOrVersion(bool sameText)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        const string original = "class Box { value: number = 1; } const box = new Box(); box.";
        string path = directory.CreateFile("entry.ts", original);
        var store = Open(path);
        using var service = new SemanticAnalysisService();
        using var seed = await service.GetDocumentAsync(Capture(store, path));
        Assert.NotNull(seed);
        string changed = sameText ? original : original.Replace("value: number = 1", "value: string = 'new'", StringComparison.Ordinal);
        store.Open(UriOf(path), changed, version: 2);
        var capture = Capture(store, path);

        using var fresh = await service.GetCursorDocumentAsync(capture, changed.Length,
            EditorQueryKind.Completion, seed, CancellationToken.None);
        using var cold = await service.GetCursorDocumentAsync(capture, changed.Length,
            EditorQueryKind.Completion, null, CancellationToken.None);

        Assert.NotNull(fresh); Assert.NotNull(cold);
        Assert.Same(fresh.Model.Snapshot, cold.Model.Snapshot);
        Assert.Equal(changed, fresh.Model.Document.Text);
        var receiver = Assert.Single(fresh.Model.Document.EditorSyntax!.Members, member => member.IsRecovered).Receiver;
        Assert.Equal(sameText ? "number" : "string", Assert.Single(fresh.Model.EditorFacts
            .GetReceiverMembers(fresh.Model.Document, receiver).Members, candidate => candidate.Name == "value").Type.Text);
        Assert.Equal(2, service.Statistics.Checks);
    }

    [Fact]
    public async Task ForeignServiceSeedUsesTheIndependentColdBuildKey()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        const string text = "class Box { value = 1; } const box = new Box(); box.";
        string path = directory.CreateFile("entry.ts", text);
        var capture = Capture(Open(path), path);
        using var foreignService = new SemanticAnalysisService();
        using var foreignSeed = await foreignService.GetDocumentAsync(capture);
        Assert.NotNull(foreignSeed);
        using var service = new SemanticAnalysisService();

        using var fresh = await service.GetCursorDocumentAsync(capture, text.Length,
            EditorQueryKind.Completion, foreignSeed, CancellationToken.None);
        using var cold = await service.GetCursorDocumentAsync(capture, text.Length,
            EditorQueryKind.Completion, null, CancellationToken.None);

        Assert.NotNull(fresh); Assert.NotNull(cold);
        Assert.Same(fresh.Model.Snapshot, cold.Model.Snapshot);
        Assert.NotSame(foreignSeed.Model.Snapshot, fresh.Model.Snapshot);
        Assert.Equal(1, service.Statistics.Checks);
    }

    [Fact]
    public async Task ClosedInputsAndNegativeResolutionProbesInvalidateRecoveredGraphs()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string config = Configure(directory);
        string dependency = directory.CreateFile("model.js", "export class Box { value: number = 1; }");
        const string text = "import { Box } from './model'; const box = new Box(); box.";
        string path = directory.CreateFile("entry.ts", text);
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var seed = await service.GetDocumentAsync(capture);
        Assert.NotNull(seed);
        using var first = await service.GetCursorDocumentAsync(capture, text.Length,
            EditorQueryKind.Completion, seed, CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotNull(Document(first, dependency));

        File.WriteAllText(config, """{"compilerOptions":{"noLib":true,"types":[],"strict":true},"include":["*.ts","*.js"]}""");
        string preferred = directory.CreateFile("model.ts", "export class Box { value: string = 'new'; }");
        Assert.False(first.IsCurrent());
        using var changed = await service.GetCursorDocumentAsync(capture, text.Length,
            EditorQueryKind.Completion, seed, CancellationToken.None);

        Assert.NotNull(changed);
        Assert.NotSame(first.Model.Snapshot, changed.Model.Snapshot);
        Assert.True(changed.Model.Snapshot.Options!.CheckerOptions.StrictNullChecks);
        Assert.NotNull(Document(changed, preferred));
        Assert.False(changed.Model.Snapshot.TryGetDocument(dependency, out _));
        var receiver = Assert.Single(changed.Model.Document.EditorSyntax!.Members, member => member.IsRecovered).Receiver;
        Assert.Equal("string", Assert.Single(changed.Model.EditorFacts.GetReceiverMembers(changed.Model.Document, receiver).Members,
            candidate => candidate.Name == "value").Type.Text);
    }

    [Fact]
    public async Task CursorGraphDoesNotCreateOrdinaryDocumentAliases()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        string dependency = directory.CreateFile("model.ts", "export class Box { value = 1; }");
        const string text = "import { Box } from './model'; const box = new Box(); box.";
        string path = directory.CreateFile("entry.ts", text);
        var store = Open(path);
        store.Open(UriOf(dependency), File.ReadAllText(dependency), version: 1);
        using var service = new SemanticAnalysisService();
        using var cursor = await service.GetCursorDocumentAsync(Capture(store, path), text.Length,
            EditorQueryKind.Completion, null, CancellationToken.None);
        using var ordinary = await service.GetDocumentAsync(Capture(store, dependency));

        Assert.NotNull(cursor); Assert.NotNull(ordinary);
        Assert.NotSame(cursor.Model.Snapshot, ordinary.Model.Snapshot);
        Assert.Equal(dependency, ordinary.Model.Document.Path);
        Assert.Equal(2, service.Statistics.Checks);
    }

    [Fact]
    public async Task RecoveredImportNamespacePreservesExactExportFacetsInFrozenMetadata()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        directory.CreateFile("dependency.ts", "export type TypeOnly = number; export const value: number = 1;");
        const string text = "import * as Api from './dependency'; Api.";
        string path = directory.CreateFile("entry.ts", text);
        using var service = new SemanticAnalysisService();

        using var result = await service.GetCursorDocumentAsync(Capture(Open(path), path), text.Length,
            EditorQueryKind.Completion, null, CancellationToken.None);

        Assert.NotNull(result);
        var receiver = Assert.Single(result.Model.Document.EditorSyntax!.Members, member => member.IsRecovered).Receiver;
        var set = result.Model.EditorFacts.GetReceiverMembers(result.Model.Document, receiver);
        Assert.Equal(BindingNamespace.Type, Assert.Single(set.Members, member => member.Name == "TypeOnly").NamespaceFacet);
        Assert.Equal(BindingNamespace.Value, Assert.Single(set.Members, member => member.Name == "value").NamespaceFacet);
    }

    [Fact]
    public async Task SeedAndCombinedInputsAreValidatedAfterTheFreshCheck()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        string dependency = directory.CreateFile("model.ts", "export class Box { value: number = 1; }");
        const string text = "import { Box } from './model'; const box = new Box(); box.";
        string path = directory.CreateFile("entry.ts", text);
        var capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var seed = await service.GetDocumentAsync(capture);
        Assert.NotNull(seed);
        DateTime timestamp = File.GetLastWriteTimeUtc(dependency);
        service.BeforeCheck = () =>
        {
            File.WriteAllText(dependency, "export class Box { value: number = 2; }");
            File.SetLastWriteTimeUtc(dependency, timestamp);
        };

        using var result = await service.GetCursorDocumentAsync(capture, text.Length,
            EditorQueryKind.Completion, seed, CancellationToken.None);

        Assert.Null(result);
        Assert.False(seed.IsCurrent());
        Assert.Equal(1, service.Statistics.RetainedSnapshots); // only the old, invalid base entry
        service.BeforeCheck = null;
        using var fresh = await service.GetCursorDocumentAsync(capture, text.Length,
            EditorQueryKind.Completion, seed, CancellationToken.None);
        Assert.NotNull(fresh);
        Assert.Equal("export class Box { value: number = 2; }", Document(fresh, dependency).Document.Text);
    }

    [Fact]
    public async Task ConcurrentCursorWaitersCoalesceAndOneCancellationLeavesItsPeerAlive()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        const string text = "class Box { value = 1; } const box = new Box(); box.";
        string path = directory.CreateFile("entry.ts", text);
        var capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var cancellation = new CancellationTokenSource();
        var gate = new CheckGate();
        service.BeforeCheck = gate.Hold;
        try
        {
            var cancelled = service.GetCursorDocumentAsync(capture, text.Length,
                EditorQueryKind.Completion, null, cancellation.Token);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            var peer = service.GetCursorDocumentAsync(capture, text.Length,
                EditorQueryKind.Completion, null, CancellationToken.None);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled.WaitAsync(TimeSpan.FromSeconds(20)));
            gate.Release();
            using var completed = await peer.WaitAsync(TimeSpan.FromSeconds(20));
            using var reused = await service.GetCursorDocumentAsync(capture, text.Length,
                EditorQueryKind.Completion, null, CancellationToken.None);
            Assert.NotNull(completed); Assert.NotNull(reused);
            Assert.Same(completed.Model.Snapshot, reused.Model.Snapshot);
            Assert.Equal(1, service.Statistics.Builds);
            Assert.Equal(1, service.Statistics.Checks);
        }
        finally { gate.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidationOrDisposalRefusesAnInFlightRecoveredCapture(bool dispose)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        const string text = "const value = 1; value.";
        string path = directory.CreateFile("entry.ts", text);
        var capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        var gate = new CheckGate();
        service.BeforeCheck = gate.Hold;
        try
        {
            var pending = service.GetCursorDocumentAsync(capture, text.Length,
                EditorQueryKind.Completion, null, CancellationToken.None);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(20));
            if (dispose) service.Dispose(); else service.InvalidateAll();
            gate.Release();
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.Equal(0, service.Statistics.RetainedSnapshots);
            Assert.Equal(0, service.Statistics.EstimatedRetainedBytes);
        }
        finally { gate.Release(); }
    }

    [Fact]
    public async Task CursorCaretQueryAndPolicyKeysHaveAFourEntrySharedLruBound()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        const string text = "const value = 1; value;";
        string path = directory.CreateFile("entry.ts", text);
        var capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService(maxSnapshots: 8);
        using var seed = await service.GetDocumentAsync(capture);
        Assert.NotNull(seed);
        using var first = await service.GetCursorDocumentAsync(capture, 0, EditorQueryKind.Syntax, seed, CancellationToken.None);
        Assert.NotNull(first);
        for (int cursor = 1; cursor <= 5; cursor++)
        {
            using var next = await service.GetCursorDocumentAsync(capture, cursor, EditorQueryKind.Syntax, seed, CancellationToken.None);
            Assert.NotNull(next);
        }
        Assert.Equal(5, service.Statistics.RetainedSnapshots); // ordinary seed + four cursor entries
        using var rebuilt = await service.GetCursorDocumentAsync(capture, 0, EditorQueryKind.Syntax, seed, CancellationToken.None);
        using var otherQuery = await service.GetCursorDocumentAsync(capture, 0, EditorQueryKind.Hover, seed, CancellationToken.None);
        using var otherPolicy = await service.GetCursorDocumentAsync(capture, 0, EditorQueryKind.Hover, seed, CancellationToken.None,
            EditorRecoveryPolicy.Default with { Version = 2 });
        Assert.NotNull(rebuilt); Assert.NotNull(otherQuery); Assert.NotNull(otherPolicy);
        Assert.NotSame(first.Model.Snapshot, rebuilt.Model.Snapshot);
        Assert.NotSame(rebuilt.Model.Snapshot, otherQuery.Model.Snapshot);
        Assert.NotSame(otherQuery.Model.Snapshot, otherPolicy.Model.Snapshot);
        Assert.True(first.IsCurrent());
        Assert.Equal(5, service.Statistics.RetainedSnapshots);
        Assert.Equal(10, service.Statistics.Checks);
    }

    [Fact]
    public async Task OversizedAndPrecancelledCursorRequestsDoNotRetainOrStartUnnecessaryWork()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        const string text = "const value = 1; value.";
        string path = directory.CreateFile("entry.ts", text);
        var capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService(maxRetainedBytes: 1);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetCursorDocumentAsync(capture,
            text.Length, EditorQueryKind.Completion, null, cancellation.Token));
        Assert.Equal(0, service.Statistics.Builds);
        using var first = await service.GetCursorDocumentAsync(capture, text.Length, EditorQueryKind.Completion, null, CancellationToken.None);
        using var second = await service.GetCursorDocumentAsync(capture, text.Length, EditorQueryKind.Completion, null, CancellationToken.None);
        Assert.NotNull(first); Assert.NotNull(second);
        Assert.NotSame(first.Model.Snapshot, second.Model.Snapshot);
        Assert.Equal(0, service.Statistics.RetainedSnapshots);
        Assert.Equal(0, service.Statistics.EstimatedRetainedBytes);
        Assert.Equal(2, service.Statistics.Checks);
    }

    [Fact]
    public async Task CursorOwnsItsMetadataViewAndRefreshesAfterAReferenceReplacement()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        const string text = "const value = 1; value.";
        string path = directory.CreateFile("entry.ts", text);
        string reference = Path.Combine(directory.Path, "reference.dll");
        File.Copy(typeof(BindingIndex).Assembly.Location, reference);
        using var provider = new AnalysisMetadataProvider(references: [reference], startDirectory: directory.Path);
        using var service = new SemanticAnalysisService(metadata: provider, maxSnapshots: 1);
        var capture = Capture(Open(path), path);
        using var seed = await service.GetDocumentAsync(capture);
        Assert.NotNull(seed);
        using var cursor = await service.GetCursorDocumentAsync(capture, text.Length,
            EditorQueryKind.Completion, seed, CancellationToken.None);
        Assert.NotNull(cursor);
        seed.Dispose(); // the cursor remains independently usable after base cache eviction
        using (cursor.EnterMetadataScope()) Assert.NotNull(provider.Resolve("SharpTS.TypeSystem.BindingIndex"));

        File.Copy(typeof(AnalysisMetadataProvider).Assembly.Location, reference, overwrite: true);
        Assert.False(cursor.IsCurrent());
        using var fresh = await service.GetCursorDocumentAsync(capture, text.Length,
            EditorQueryKind.Completion, null, CancellationToken.None);
        Assert.NotNull(fresh);
        using (cursor.EnterMetadataScope()) Assert.NotNull(provider.Resolve("SharpTS.TypeSystem.BindingIndex"));
        using (fresh.EnterMetadataScope())
        {
            Assert.Null(provider.Resolve("SharpTS.TypeSystem.BindingIndex"));
            Assert.NotNull(provider.Resolve("SharpTS.LanguageServer.Project.AnalysisMetadataProvider"));
        }
    }

    private static string Configure(TempTestDirectory directory) => directory.CreateFile("tsconfig.json",
        """{"compilerOptions":{"noLib":true,"types":[]},"include":["*.ts","*.js"]}""");
    private static string UriOf(string path) => new Uri(path).AbsoluteUri;
    private static DocumentStore Open(string path)
    {
        var store = new DocumentStore();
        Assert.True(store.Open(UriOf(path), File.ReadAllText(path), version: 1));
        return store;
    }
    private static DocumentRequestSnapshot Capture(DocumentStore store, string path)
    {
        Assert.True(store.TryCapture(UriOf(path), out var capture));
        return capture;
    }
    private static AnalysisDocument Document(AnalysisLease lease, string path)
    {
        Assert.True(lease.Model.Snapshot.TryGetDocument(path, out var document), $"Missing checked document {path}");
        return document!;
    }
    private sealed class CheckGate
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public void Hold()
        {
            _entered.TrySetResult();
            _release.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        }
        public void Release() => _release.TrySetResult();
    }
}
