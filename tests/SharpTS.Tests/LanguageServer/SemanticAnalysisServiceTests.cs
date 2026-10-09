using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Services;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

public sealed class SemanticAnalysisServiceTests
{
    [Fact]
    public async Task UnchangedCaptureSharesOneCheckedSemanticGraph()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("entry.ts", "const value = 1; value;\n");
        var store = Open(path);
        DocumentRequestSnapshot capture = Capture(store, path);
        using var service = new SemanticAnalysisService();

        using var first = await service.GetDocumentAsync(capture, CancellationToken.None);
        using var second = await service.GetDocumentAsync(capture, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Same(first.Model.Snapshot, second.Model.Snapshot);
        Assert.Same(first.Model.Document, second.Model.Document);
        Assert.Same(first.Model.Bindings, second.Model.Bindings);
        Assert.True(first.IsCurrent(CancellationToken.None));
        Assert.True(first.Validation.IsCurrent(CancellationToken.None));
        Assert.Equal(1, service.Statistics.Builds);
        Assert.Equal(1, service.Statistics.Checks);
        Assert.True(service.Statistics.CacheHits >= 1);
        Assert.Equal(1, service.Statistics.RetainedSnapshots);

        first.Dispose();
        Assert.True(second.IsCurrent(CancellationToken.None));
        Assert.Equal(2, second.Model.Bindings.FindReferences(
            second.Model.Document,
            second.Model.Document.Text.LastIndexOf("value", StringComparison.Ordinal),
            includeDeclarations: true).Count);
    }

    [Fact]
    public async Task LegacyOverloadAlsoReusesCompletedAnalysis()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("entry.ts", "const value = 1; value;\n");
        string text = File.ReadAllText(path);
        var overlays = new Dictionary<string, string> { [path] = text };
        using var service = new SemanticAnalysisService();

        using var first = await service.GetDocumentAsync(path, text, overlays, CancellationToken.None);
        using var second = await service.GetDocumentAsync(path, text, overlays, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Same(first.Model.Snapshot, second.Model.Snapshot);
        Assert.Equal(1, service.Statistics.Builds);
        Assert.Equal(1, service.Statistics.Checks);
    }

    [Fact]
    public async Task TwoDocumentsInTheSameCapturedComponentShareTheirSemanticGraph()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        string dependency = directory.CreateFile("dependency.ts", "export const value = 1;\n");
        string path = directory.CreateFile("entry.ts", "import { value } from './dependency'; value;\n");
        var store = Open(path);
        store.Open(UriOf(dependency), File.ReadAllText(dependency), version: 1);
        using var service = new SemanticAnalysisService();

        using var importer = await service.GetDocumentAsync(Capture(store, path), CancellationToken.None);
        using var imported = await service.GetDocumentAsync(Capture(store, dependency), CancellationToken.None);

        Assert.NotNull(importer);
        Assert.NotNull(imported);
        Assert.Same(importer.Model.Snapshot, imported.Model.Snapshot);
        Assert.Equal(path, importer.Model.Document.Path);
        Assert.Equal(dependency, imported.Model.Document.Path);
        Assert.NotSame(importer.Model.Document, imported.Model.Document);
        Assert.Equal(1, service.Statistics.Checks);
        Assert.Equal(1, service.Statistics.RetainedSnapshots);
    }

    [Fact]
    public async Task ConcurrentIdenticalRequestsShareTheInFlightCheck()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("entry.ts", "const value = 1; value;\n");
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        var gate = new CheckGate();
        service.BeforeCheck = gate.HoldFirstCheck;

        try
        {
            var first = service.GetDocumentAsync(capture, CancellationToken.None);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            var second = service.GetDocumentAsync(capture, CancellationToken.None);
            gate.Release();
            using var firstLease = await first.WaitAsync(TimeSpan.FromSeconds(10));
            using var secondLease = await second.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.NotNull(firstLease);
            Assert.NotNull(secondLease);
            Assert.Same(firstLease.Model.Snapshot, secondLease.Model.Snapshot);
            Assert.Equal(1, service.Statistics.Builds);
            Assert.Equal(1, service.Statistics.Checks);
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task CancelledWaiterDoesNotCancelItsPeerBuild()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("entry.ts", "const value = 1; value;\n");
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var cancellation = new CancellationTokenSource();
        var gate = new CheckGate();
        service.BeforeCheck = gate.HoldFirstCheck;

        try
        {
            var cancelled = service.GetDocumentAsync(capture, cancellation.Token);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            var peer = service.GetDocumentAsync(capture, CancellationToken.None);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await cancelled.WaitAsync(TimeSpan.FromSeconds(10));
            });
            gate.Release();
            using var peerLease = await peer.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.NotNull(peerLease);
            Assert.True(peerLease.IsCurrent(CancellationToken.None));
            Assert.Equal(1, service.Statistics.Builds);
            Assert.Equal(1, service.Statistics.Checks);
            using var reused = await service.GetDocumentAsync(capture, CancellationToken.None);
            Assert.NotNull(reused);
            Assert.Same(peerLease.Model.Snapshot, reused.Model.Snapshot);
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task PreCancelledRequestDoesNotStartAnalysis()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("entry.ts", "const value = 1;\n");
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await service.GetDocumentAsync(capture, cancellation.Token);
        });

        Assert.Equal(0, service.Statistics.Builds);
        Assert.Equal(0, service.Statistics.Checks);
        Assert.Equal(0, service.Statistics.RetainedSnapshots);
    }

    [Fact]
    public async Task DirtyDependencyOverlayReplacesDiskTextAndVersionedCapture()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory, """{ "files": ["entry.ts", "dependency.ts"], "compilerOptions": { "noLib": true } }""");
        string dependency = directory.CreateFile("dependency.ts", "export const value = 1;\n");
        string path = directory.CreateFile("entry.ts",
            "import { value } from './dependency'; const result: string = value;\n");
        var store = Open(path);
        const string dirty = "export const value = 'dirty';\n";
        store.Open(UriOf(dependency), dirty, version: 1);
        using var service = new SemanticAnalysisService();
        using var first = await service.GetDocumentAsync(Capture(store, path), CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(dirty, DocumentText(first.Model.Snapshot, dependency));

        const string replacement = "export const value = 'newer';\n";
        store.Open(UriOf(dependency), replacement, version: 2);
        // Open/change/close handlers invalidate immediately; the store owns version currentness.
        service.InvalidateAll();
        using var second = await service.GetDocumentAsync(Capture(store, path), CancellationToken.None);

        Assert.NotNull(second);
        Assert.NotSame(first.Model.Snapshot, second.Model.Snapshot);
        Assert.Equal(replacement, DocumentText(second.Model.Snapshot, dependency));
        Assert.Equal("export const value = 1;\n", File.ReadAllText(dependency));
        Assert.False(first.IsCurrent(CancellationToken.None));
        Assert.True(second.IsCurrent(CancellationToken.None));
        Assert.Equal(2, service.Statistics.Checks);
    }

    [Fact]
    public async Task SameTextWithANewerDocumentVersionUsesANewCapture()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("entry.ts", "const value = 1;\n");
        var store = Open(path);
        DocumentRequestSnapshot original = Capture(store, path);
        using var service = new SemanticAnalysisService();
        using var first = await service.GetDocumentAsync(original, CancellationToken.None);
        Assert.NotNull(first);

        store.Open(UriOf(path), original.Document.Text, version: 2);
        DocumentRequestSnapshot newer = Capture(store, path);
        using var second = await service.GetDocumentAsync(newer, CancellationToken.None);

        Assert.NotNull(second);
        Assert.False(store.IsCurrent(original.Document.Uri, original.Document.Version, original.WorkspaceVersion));
        Assert.NotSame(first.Model.Snapshot, second.Model.Snapshot);
        Assert.Equal(2, service.Statistics.Checks);
        Assert.Equal(original.Document.Text, second.Model.Document.Text);
    }

    [Fact]
    public async Task ClosingADirtyDependencyReturnsToDiskState()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        const string disk = "export const value = 1;\n";
        string dependency = directory.CreateFile("dependency.ts", disk);
        string path = directory.CreateFile("entry.ts", "import { value } from './dependency'; value;\n");
        var store = Open(path);
        store.Open(UriOf(dependency), "export const value = 'dirty';\n", version: 1);
        using var service = new SemanticAnalysisService();
        using var dirty = await service.GetDocumentAsync(Capture(store, path), CancellationToken.None);
        Assert.NotNull(dirty);

        Assert.NotNull(store.Remove(UriOf(dependency)));
        service.InvalidateAll();
        using var closed = await service.GetDocumentAsync(Capture(store, path), CancellationToken.None);

        Assert.NotNull(closed);
        Assert.Equal(disk, DocumentText(closed.Model.Snapshot, dependency));
        Assert.NotSame(dirty.Model.Snapshot, closed.Model.Snapshot);
        Assert.False(dirty.Validation.IsCurrent(CancellationToken.None));
    }

    [Fact]
    public async Task ClosedDependencyEditInvalidatesEvenWithIdenticalLengthAndTimestamp()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        string dependency = directory.CreateFile("dependency.ts", "export const value = 1;\n");
        string path = directory.CreateFile("entry.ts", "import { value } from './dependency'; value;\n");
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var original = await service.GetDocumentAsync(capture, CancellationToken.None);
        Assert.NotNull(original);
        DateTime timestamp = File.GetLastWriteTimeUtc(dependency);
        long length = new FileInfo(dependency).Length;

        const string replacement = "export const value = 2;\n";
        File.WriteAllText(dependency, replacement);
        File.SetLastWriteTimeUtc(dependency, timestamp);
        Assert.Equal(length, new FileInfo(dependency).Length);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(dependency));
        Assert.False(original.IsCurrent(CancellationToken.None));
        Assert.False(original.Validation.IsCurrent(CancellationToken.None));
        using var updated = await service.GetDocumentAsync(capture, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.NotSame(original.Model.Snapshot, updated.Model.Snapshot);
        Assert.Equal(replacement, DocumentText(updated.Model.Snapshot, dependency));
        Assert.Equal(2, service.Statistics.Checks);
    }

    [Fact]
    public async Task CreatingAMissingImportAllowsTheSameCaptureToResolve()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        string path = directory.CreateFile("entry.ts", "import { value } from './missing'; value;\n");
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var missing = await service.GetDocumentAsync(capture, CancellationToken.None);

        string dependency = directory.CreateFile("missing.ts", "export const value = 1;\n");
        if (missing is not null)
            Assert.False(missing.IsCurrent(CancellationToken.None));
        using var resolved = await service.GetDocumentAsync(capture, CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal("export const value = 1;\n", DocumentText(resolved.Model.Snapshot, dependency));
        Assert.True(resolved.IsCurrent(CancellationToken.None));
    }

    [Fact]
    public async Task CreatingAHigherPriorityResolutionCandidateInvalidatesACachedGraph()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        string fallback = directory.CreateFile("dependency.js", "export const value = 1;\n");
        string path = directory.CreateFile("entry.ts", "import { value } from './dependency'; value;\n");
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var first = await service.GetDocumentAsync(capture, CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(File.ReadAllText(fallback), DocumentText(first.Model.Snapshot, fallback));

        string preferred = directory.CreateFile("dependency.ts", "export const value = 'preferred';\n");
        Assert.False(first.Validation.IsCurrent(CancellationToken.None));
        using var second = await service.GetDocumentAsync(capture, CancellationToken.None);

        Assert.NotNull(second);
        Assert.NotSame(first.Model.Snapshot, second.Model.Snapshot);
        Assert.Equal(File.ReadAllText(preferred), DocumentText(second.Model.Snapshot, preferred));
        Assert.False(second.Model.Snapshot.TryGetDocument(fallback, out _));
    }

    [Fact]
    public async Task ConfigurationChangesRebuildResolvedCheckerOptions()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string config = Configure(directory,
            """{ "files": ["entry.ts"], "compilerOptions": { "noLib": true, "strictNullChecks": false } }""");
        string path = directory.CreateFile("entry.ts", "const value: string = null;\n");
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var first = await service.GetDocumentAsync(capture, CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotNull(first.Model.Snapshot.Options);
        Assert.False(first.Model.Snapshot.Options.CheckerOptions.StrictNullChecks);

        File.WriteAllText(config,
            """{ "files": ["entry.ts"], "compilerOptions": { "noLib": true, "strictNullChecks": true } }""");
        Assert.False(first.IsCurrent(CancellationToken.None));
        using var second = await service.GetDocumentAsync(capture, CancellationToken.None);

        Assert.NotNull(second);
        Assert.NotNull(second.Model.Snapshot.Options);
        Assert.True(second.Model.Snapshot.Options.CheckerOptions.StrictNullChecks);
        Assert.NotSame(first.Model.Snapshot, second.Model.Snapshot);
        Assert.Equal(2, service.Statistics.Checks);
    }

    [Fact]
    public async Task ExtendedConfigurationChangesInvalidateTheDependentProject()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string baseConfig = directory.CreateFile("base.json",
            """{ "compilerOptions": { "noLib": true, "strictNullChecks": false } }""");
        Configure(directory, """{ "extends": "./base.json", "files": ["entry.ts"] }""");
        string path = directory.CreateFile("entry.ts", "const value: string = null;\n");
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var first = await service.GetDocumentAsync(capture, CancellationToken.None);
        Assert.NotNull(first);

        File.WriteAllText(baseConfig,
            """{ "compilerOptions": { "noLib": true, "strictNullChecks": true } }""");
        Assert.False(first.Validation.IsCurrent(CancellationToken.None));
        using var second = await service.GetDocumentAsync(capture, CancellationToken.None);

        Assert.NotNull(second);
        Assert.NotNull(second.Model.Snapshot.Options);
        Assert.True(second.Model.Snapshot.Options.CheckerOptions.StrictNullChecks);
        Assert.NotSame(first.Model.Snapshot, second.Model.Snapshot);
    }

    [Fact]
    public async Task WorkspaceExpansionReusesItsSeedAndTracksReferencedProjectConfiguration()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string parentConfig = Configure(directory,
            """{ "files": ["entry.ts"], "references": [{ "path": "./lib" }], "compilerOptions": { "noLib": true } }""");
        string childConfig = directory.CreateFile("lib/tsconfig.json",
            """{ "files": ["dependency.ts"], "compilerOptions": { "noLib": true, "strictNullChecks": false } }""");
        string dependency = directory.CreateFile("lib/dependency.ts", "export const value = 1;\n");
        string path = directory.CreateFile("entry.ts", "import { value } from './lib/dependency'; value;\n");
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var seed = await service.GetDocumentAsync(capture, CancellationToken.None);
        Assert.NotNull(seed);
        using var expanded = await service.GetWorkspaceAsync(
            capture, dependency, seed, [directory.Path], CancellationToken.None);
        Assert.NotNull(expanded);
        Assert.NotNull(expanded.Workspace);
        Assert.Equal(2, expanded.Workspace.ConfigPaths.Count);
        Assert.Contains(expanded.Workspace.Models, model =>
            model.Scope.ConfigPath == parentConfig && ReferenceEquals(model.Snapshot, seed.Model.Snapshot));
        Assert.Equal(2, service.Statistics.Checks);

        File.WriteAllText(childConfig,
            """{ "files": ["dependency.ts"], "compilerOptions": { "noLib": true, "strictNullChecks": true } }""");
        Assert.False(expanded.Validation.IsCurrent(CancellationToken.None));
        using var updated = await service.GetWorkspaceAsync(
            capture, dependency, seed, [directory.Path], CancellationToken.None);

        Assert.NotNull(updated);
        Assert.NotNull(updated.Workspace);
        var child = Assert.Single(updated.Workspace.Models,
            model => model.Scope.ConfigPath == Path.GetFullPath(childConfig));
        Assert.NotNull(child.Snapshot.Options);
        Assert.True(child.Snapshot.Options.CheckerOptions.StrictNullChecks);
        Assert.True(updated.IsCurrent(CancellationToken.None));
        Assert.Equal(3, service.Statistics.Checks);
    }

    [Fact]
    public async Task ClosedDeclarationChangesReplaceTheCapturedDeclarationGraph()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        string declaration = directory.CreateFile("ambient.d.ts", "declare const ambient: number;\n");
        string path = directory.CreateFile("entry.ts",
            "/// <reference path=\"./ambient.d.ts\" />\nconst value = ambient;\n");
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var first = await service.GetDocumentAsync(capture, CancellationToken.None);
        Assert.NotNull(first);

        const string replacement = "declare const ambient: string;\n";
        File.WriteAllText(declaration, replacement);
        Assert.False(first.Validation.IsCurrent(CancellationToken.None));
        using var second = await service.GetDocumentAsync(capture, CancellationToken.None);

        Assert.NotNull(second);
        Assert.NotSame(first.Model.Snapshot, second.Model.Snapshot);
        Assert.Equal(replacement, DocumentText(second.Model.Snapshot, declaration));
    }

    [Fact]
    public async Task IncludeGlobMembershipChangesInvalidateTheRootSet()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory,
            """{ "include": ["src/**/*.ts"], "compilerOptions": { "noLib": true } }""");
        string path = directory.CreateFile("src/entry.ts", "export const value = 1;\n");
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var first = await service.GetDocumentAsync(capture, CancellationToken.None);
        Assert.NotNull(first);
        Assert.Single(first.Model.Scope.RootFiles);

        string added = directory.CreateFile("src/new.ts", "export const extra = 2;\n");
        Assert.False(first.IsCurrent(CancellationToken.None));
        using var second = await service.GetDocumentAsync(capture, CancellationToken.None);

        Assert.NotNull(second);
        Assert.Equal(2, second.Model.Scope.RootFiles.Count);
        Assert.Contains(Path.GetFullPath(added), second.Model.Scope.RootFiles, StringComparer.OrdinalIgnoreCase);
        Assert.True(second.Model.Scope.IsComplete);
        Assert.NotSame(first.Model.Snapshot, second.Model.Snapshot);
    }

    [Fact]
    public async Task ClosedRootBecomingAReverseImporterChangesTheConnectedComponent()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        string path = directory.CreateFile("entry.ts", "export const target = 1;\n");
        string importer = directory.CreateFile("importer.ts", "export const unrelated = 0;\n");
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var first = await service.GetDocumentAsync(capture, CancellationToken.None);
        Assert.NotNull(first);
        Assert.False(first.Model.Snapshot.TryGetDocument(importer, out _));

        const string replacement = "import { target } from './entry'; target;\n";
        File.WriteAllText(importer, replacement);
        Assert.False(first.IsCurrent(CancellationToken.None));
        using var second = await service.GetDocumentAsync(capture, CancellationToken.None);

        Assert.NotNull(second);
        Assert.Equal(replacement, DocumentText(second.Model.Snapshot, importer));
        Assert.Equal(3, second.Model.Bindings.FindReferences(
            second.Model.Document,
            second.Model.Document.Text.IndexOf("target", StringComparison.Ordinal),
            includeDeclarations: true).Count);
        Assert.Equal(2, service.Statistics.Checks);
    }

    [Fact]
    public async Task DirtyNewReverseImporterParticipatesWithoutChangingConfiguredRoots()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory,
            """{ "files": ["entry.ts"], "compilerOptions": { "noLib": true } }""");
        string path = directory.CreateFile("entry.ts", "export const target = 1;\n");
        string importer = directory.GetPath("scratch.ts");
        var store = Open(path);
        const string dirty = "import { target } from './entry'; target;\n";
        store.Open(UriOf(importer), dirty, version: 1);
        using var service = new SemanticAnalysisService();

        using var lease = await service.GetDocumentAsync(Capture(store, path), CancellationToken.None);

        Assert.NotNull(lease);
        Assert.Equal(dirty, DocumentText(lease.Model.Snapshot, importer));
        Assert.Single(lease.Model.Scope.RootFiles);
        Assert.True(lease.Model.Scope.IsComplete);
        Assert.Equal(3, lease.Model.Bindings.FindReferences(
            lease.Model.Document,
            lease.Model.Document.Text.IndexOf("target", StringComparison.Ordinal),
            includeDeclarations: true).Count);
        Assert.False(File.Exists(importer));
    }

    [Fact]
    public async Task UnrelatedOpenScriptsKeepIndependentGlobals()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("entry.ts", "const target = 1; target;\n");
        string unrelated = directory.GetPath("unrelated.ts");
        var store = Open(path);
        store.Open(UriOf(unrelated), "const target = 2; target;\n", version: 1);
        using var service = new SemanticAnalysisService();

        using var lease = await service.GetDocumentAsync(Capture(store, path), CancellationToken.None);

        Assert.NotNull(lease);
        Assert.False(lease.Model.Snapshot.TryGetDocument(unrelated, out _));
        var references = lease.Model.Bindings.FindReferences(
            lease.Model.Document,
            lease.Model.Document.Text.LastIndexOf("target", StringComparison.Ordinal),
            includeDeclarations: true);
        Assert.Equal(2, references.Count);
        Assert.All(references, reference => Assert.Same(lease.Model.Document, reference.Document));
    }

    [Fact]
    public async Task InputMutationDuringCheckingCannotPublishTheOldGraph()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Configure(directory);
        string dependency = directory.CreateFile("dependency.ts", "export const value = 1;\n");
        string path = directory.CreateFile("entry.ts", "import { value } from './dependency'; value;\n");
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        var gate = new CheckGate();
        service.BeforeCheck = gate.HoldFirstCheck;

        try
        {
            var pending = service.GetDocumentAsync(capture, CancellationToken.None);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            const string replacement = "export const value = 2;\n";
            File.WriteAllText(dependency, replacement);
            gate.Release();
            using var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            // A service may discard the stale build or retry it, but cannot return old input.
            if (result is not null)
            {
                Assert.True(result.IsCurrent(CancellationToken.None));
                Assert.Equal(replacement, DocumentText(result.Model.Snapshot, dependency));
            }
            using var fresh = await service.GetDocumentAsync(capture, CancellationToken.None);
            Assert.NotNull(fresh);
            Assert.Equal(replacement, DocumentText(fresh.Model.Snapshot, dependency));
            Assert.True(fresh.Validation.IsCurrent(CancellationToken.None));
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task ExplicitInvalidationExpiresHeldLeasesAndDropsRetainedSnapshots()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("entry.ts", "const value = 1;\n");
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService();
        using var first = await service.GetDocumentAsync(capture, CancellationToken.None);
        Assert.NotNull(first);

        service.InvalidateAll();

        Assert.False(first.IsCurrent(CancellationToken.None));
        Assert.False(first.Validation.IsCurrent(CancellationToken.None));
        Assert.Equal(0, service.Statistics.RetainedSnapshots);
        Assert.Equal(0, service.Statistics.EstimatedRetainedBytes);
        using var second = await service.GetDocumentAsync(capture, CancellationToken.None);
        Assert.NotNull(second);
        Assert.NotSame(first.Model.Snapshot, second.Model.Snapshot);
        Assert.Equal(2, service.Statistics.Checks);
    }

    [Fact]
    public async Task DocumentVersionChangeDuringBuildCannotRetainThePreviousCapture()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("entry.ts", "const value = 1;\n");
        var store = Open(path);
        DocumentRequestSnapshot capture = Capture(store, path);
        using var service = new SemanticAnalysisService();
        var gate = new CheckGate();
        service.BeforeCheck = gate.HoldFirstCheck;

        try
        {
            var pending = service.GetDocumentAsync(capture, CancellationToken.None);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            const string replacement = "const value = 2;\n";
            store.Open(UriOf(path), replacement, version: 2);
            service.InvalidateAll();
            gate.Release();
            using var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(result);
            Assert.False(store.IsCurrent(capture.Document.Uri,
                capture.Document.Version, capture.WorkspaceVersion));
            using var fresh = await service.GetDocumentAsync(Capture(store, path), CancellationToken.None);
            Assert.NotNull(fresh);
            Assert.Equal(replacement, fresh.Model.Document.Text);
            Assert.True(fresh.IsCurrent(CancellationToken.None));
            Assert.Equal(1, service.Statistics.RetainedSnapshots);
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task SnapshotCountEvictionDoesNotBreakAnActiveLease()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string firstPath = directory.CreateFile("first.ts", "const first = 1; first;\n");
        string secondPath = directory.CreateFile("second.ts", "const second = 2;\n");
        string thirdPath = directory.CreateFile("third.ts", "const third = 3;\n");
        var store = Open(firstPath);
        store.Open(UriOf(secondPath), File.ReadAllText(secondPath), version: 1);
        store.Open(UriOf(thirdPath), File.ReadAllText(thirdPath), version: 1);
        using var service = new SemanticAnalysisService(maxSnapshots: 2);
        DocumentRequestSnapshot firstCapture = Capture(store, firstPath);
        using var first = await service.GetDocumentAsync(firstCapture, CancellationToken.None);
        using var second = await service.GetDocumentAsync(Capture(store, secondPath), CancellationToken.None);
        using var third = await service.GetDocumentAsync(Capture(store, thirdPath), CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);
        Assert.Equal(2, service.Statistics.RetainedSnapshots);
        Assert.Equal(3, service.Statistics.Checks);
        Assert.Equal("const first = 1; first;\n", first.Model.Document.Text);
        Assert.Equal(2, first.Model.Bindings.FindReferences(
            first.Model.Document,
            first.Model.Document.Text.LastIndexOf("first", StringComparison.Ordinal),
            includeDeclarations: true).Count);
        using var rebuilt = await service.GetDocumentAsync(firstCapture, CancellationToken.None);
        Assert.NotNull(rebuilt);
        Assert.NotSame(first.Model.Snapshot, rebuilt.Model.Snapshot);
        Assert.Equal(4, service.Statistics.Checks);
        Assert.Equal(2, service.Statistics.RetainedSnapshots);
    }

    [Fact]
    public async Task RetainedByteBudgetEvictsWithoutExcludingEitherIndividualSnapshot()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string firstPath = directory.CreateFile("first.ts", "const first = 1; first;\n");
        string otherPath = directory.CreateFile("other.ts", "const other = 2; other;\n");
        var store = Open(firstPath);
        store.Open(UriOf(otherPath), File.ReadAllText(otherPath), version: 1);
        DocumentRequestSnapshot firstCapture = Capture(store, firstPath);
        DocumentRequestSnapshot otherCapture = Capture(store, otherPath);
        long firstSize;
        long otherSize;
        using (var measured = new SemanticAnalysisService())
        {
            using var first = await measured.GetDocumentAsync(firstCapture, CancellationToken.None);
            Assert.NotNull(first);
            firstSize = measured.Statistics.EstimatedRetainedBytes;
            measured.InvalidateAll();
            using var other = await measured.GetDocumentAsync(otherCapture, CancellationToken.None);
            Assert.NotNull(other);
            otherSize = measured.Statistics.EstimatedRetainedBytes;
        }
        Assert.True(firstSize > 1 && otherSize > 1);
        long budget = checked(firstSize + otherSize - 1);
        using var service = new SemanticAnalysisService(maxRetainedBytes: budget);
        using var firstLease = await service.GetDocumentAsync(firstCapture, CancellationToken.None);
        using var otherLease = await service.GetDocumentAsync(otherCapture, CancellationToken.None);

        Assert.NotNull(firstLease);
        Assert.NotNull(otherLease);
        Assert.Equal(1, service.Statistics.RetainedSnapshots);
        Assert.InRange(service.Statistics.EstimatedRetainedBytes, 1, budget);
        using var rebuilt = await service.GetDocumentAsync(firstCapture, CancellationToken.None);
        Assert.NotNull(rebuilt);
        Assert.NotSame(firstLease.Model.Snapshot, rebuilt.Model.Snapshot);
        Assert.Equal(3, service.Statistics.Checks);
        Assert.Equal(1, service.Statistics.RetainedSnapshots);
        Assert.InRange(service.Statistics.EstimatedRetainedBytes, 1, budget);
    }

    [Fact]
    public async Task OversizedSnapshotCanBeUsedButIsExcludedFromTheCache()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("entry.ts", "const value = 1; value;\n");
        DocumentRequestSnapshot capture = Capture(Open(path), path);
        using var service = new SemanticAnalysisService(maxRetainedBytes: 1);

        using var first = await service.GetDocumentAsync(capture, CancellationToken.None);
        Assert.NotNull(first);
        Assert.True(first.IsCurrent(CancellationToken.None));
        Assert.Equal(0, service.Statistics.RetainedSnapshots);
        Assert.Equal(0, service.Statistics.EstimatedRetainedBytes);
        using var second = await service.GetDocumentAsync(capture, CancellationToken.None);

        Assert.NotNull(second);
        Assert.NotSame(first.Model.Snapshot, second.Model.Snapshot);
        Assert.Equal(2, service.Statistics.Builds);
        Assert.Equal(2, service.Statistics.Checks);
        Assert.Equal(0, service.Statistics.RetainedSnapshots);
    }

    private static string Configure(TempTestDirectory directory, string? json = null) =>
        directory.CreateFile("tsconfig.json", json ??
            """{ "include": ["**/*.ts"], "compilerOptions": { "noLib": true } }""");

    private static DocumentStore Open(string path)
    {
        var store = new DocumentStore();
        Assert.True(store.Open(UriOf(path), File.ReadAllText(path), version: 1));
        return store;
    }

    private static string UriOf(string path) => new Uri(path).AbsoluteUri;

    private static DocumentRequestSnapshot Capture(DocumentStore store, string path)
    {
        Assert.True(store.TryCapture(UriOf(path), out DocumentRequestSnapshot? capture));
        return capture;
    }

    private static string DocumentText(AnalysisSnapshot snapshot, string path)
    {
        Assert.True(snapshot.TryGetDocument(path, out AnalysisDocument? document),
            $"The checked snapshot does not contain {path}.");
        Assert.NotNull(document);
        return document.Document.Text;
    }

    private sealed class CheckGate
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _checks;

        public Task Entered => _entered.Task;

        public void HoldFirstCheck()
        {
            if (Interlocked.Increment(ref _checks) != 1)
                return;
            _entered.TrySetResult();
            _released.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }

        public void Release() => _released.TrySetResult();
    }
}
