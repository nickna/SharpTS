using SharpTS.Compilation;
using SharpTS.IO;
using SharpTS.LanguageServer.Documentation;
using SharpTS.LanguageServer.Project;
using SharpTS.LanguageServer.Services;
using SharpTS.References;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

public sealed class MetadataSnapshotTests
{
    [Fact]
    public void RetainedRealAssemblyDoesNotLockTheBuildOutputOrChangeAfterOverwrite()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string assemblyPath = Path.Combine(directory.Path, "SharpTS.dll");
        File.Copy(typeof(SharpTS.TypeSystem.BindingIndex).Assembly.Location, assemblyPath);
        using var loader = new ReloadingAssemblyReferenceLoader([assemblyPath]);
        using var original = loader.AcquireSnapshot();
        Type bindingType = Assert.IsAssignableFrom<Type>(original.TryResolve("SharpTS.TypeSystem.BindingIndex"));
        Assert.Equal(assemblyPath, AssemblyReferenceLoader.GetSourcePath(bindingType.Assembly));

        File.Copy(typeof(AnalysisMetadataProvider).Assembly.Location, assemblyPath, overwrite: true);

        Assert.Equal(1, loader.RefreshGeneration());
        using var updated = loader.AcquireSnapshot();
        Assert.NotEmpty(bindingType.GetMethods());
        Assert.Same(bindingType, original.TryResolve("SharpTS.TypeSystem.BindingIndex"));
        Assert.Null(updated.TryResolve("SharpTS.TypeSystem.BindingIndex"));
        Assert.NotNull(updated.TryResolve("SharpTS.LanguageServer.Project.AnalysisMetadataProvider"));
    }

    [Fact]
    public void RefreshDetectsSameLengthSameTimestampAssemblyChangesWithoutResolvingTypes()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("changing.dll", "first metadata placeholder");
        using var loader = new ReloadingAssemblyReferenceLoader([path]);
        using var original = loader.AcquireSnapshot();
        Type originalString = Assert.IsAssignableFrom<Type>(original.TryResolve("System.String"));
        DateTime timestamp = File.GetLastWriteTimeUtc(path);

        File.WriteAllText(path, "other metadata placeholder");
        File.SetLastWriteTimeUtc(path, timestamp);

        Assert.Equal(1, loader.RefreshGeneration());
        using var updated = loader.AcquireSnapshot();
        Assert.Equal(0, original.Generation);
        Assert.Equal(1, updated.Generation);
        Assert.NotEmpty(originalString.GetMethods());
        Assert.Same(originalString, original.TryResolve("System.String"));
        Assert.NotSame(originalString, updated.TryResolve("System.String"));
    }

    [Fact]
    public void PublishedGenerationFingerprintsTheCapturedBytesWhenAnAssemblyChangesDuringReload()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = Path.Combine(directory.Path, "SharpTS.dll");
        string original = typeof(SharpTS.TypeSystem.BindingIndex).Assembly.Location;
        File.Copy(original, path);
        using var loader = new ReloadingAssemblyReferenceLoader([path]);
        File.Copy(typeof(AnalysisMetadataProvider).Assembly.Location, path, overwrite: true);
        int probes = 0;
        var reader = new ProbeFileSystem(CompilerFileSystem.Physical, candidate =>
        {
            // First probe precedes the warm streaming hash; the second precedes capture.
            if (candidate == path && ++probes == 2)
                File.Copy(original, path, overwrite: true);
        });
        using var fileSystemScope = CompilerFileSystem.Use(reader);

        Assert.Equal(1, loader.RefreshGeneration());
        Assert.Equal(1, loader.RefreshGeneration());
        using var captured = loader.AcquireSnapshot();
        Assert.NotNull(captured.TryResolve("SharpTS.TypeSystem.BindingIndex"));
        Assert.Null(captured.TryResolve("SharpTS.LanguageServer.Project.AnalysisMetadataProvider"));
    }

    [Fact]
    public void RetainedViewOutlivesManagerAndDisposedSibling()
    {
        var loader = new ReloadingAssemblyReferenceLoader([]);
        var first = loader.AcquireSnapshot();
        using var retained = first.Retain();

        first.Dispose();
        loader.Dispose();

        Assert.NotEmpty(Assert.IsAssignableFrom<Type>(retained.TryResolve("System.String")).GetMethods());
        Assert.Throws<ObjectDisposedException>(() => first.TryResolve("System.String"));
        Assert.Throws<ObjectDisposedException>(() => loader.AcquireSnapshot());
    }

    [Fact]
    public void CompatibilityTypeOutlivesItsDisposedManager()
    {
        var loader = new ReloadingAssemblyReferenceLoader([]);
        Type type = Assert.IsAssignableFrom<Type>(loader.TryResolve("System.String"));

        loader.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.NotEmpty(type.GetMethods());
        Assert.Throws<ObjectDisposedException>(() => loader.TryResolve("System.String"));
    }

    [Fact]
    public void ExplicitSdkInventoryAndContentChangesRefreshGeneration()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string sdk = Path.Combine(directory.Path, "sdk");
        Directory.CreateDirectory(sdk);
        string runtimeReference = Path.Combine(SdkResolver.FindReferenceAssembliesPath()!, "System.Runtime.dll");
        File.Copy(runtimeReference, Path.Combine(sdk, "System.Runtime.dll"));
        using var loader = new ReloadingAssemblyReferenceLoader([], sdk);
        Assert.Equal(0, loader.RefreshGeneration());

        string extra = Path.Combine(sdk, "extra.dll");
        File.WriteAllText(extra, "first");
        Assert.Equal(1, loader.RefreshGeneration());
        DateTime timestamp = File.GetLastWriteTimeUtc(extra);
        File.WriteAllText(extra, "other");
        File.SetLastWriteTimeUtc(extra, timestamp);
        Assert.Equal(2, loader.RefreshGeneration());

        File.Delete(extra);
        Assert.Equal(3, loader.RefreshGeneration());
    }

    [Fact]
    public void ProviderRefreshesProjectAndNewNearestManifestWithoutChangingAnActiveScope()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string firstReference = directory.CreateFile("first.dll", "not an assembly");
        string otherReference = directory.CreateFile("other.dll", "not an assembly");
        File.Copy(typeof(SharpTS.TypeSystem.BindingIndex).Assembly.Location, firstReference, overwrite: true);
        File.Copy(typeof(SharpTS.TypeSystem.BindingIndex).Assembly.Location, otherReference, overwrite: true);
        string project = directory.CreateFile("test.csproj", Project("first.dll"));
        using var provider = new AnalysisMetadataProvider(projectFile: project, startDirectory: directory.Path);
        using AnalysisMetadataView original = provider.Capture();
        Assert.Equal(original.Generation, provider.RefreshGeneration());
        Assert.True(original.IsComplete);
        Assert.Equal([firstReference], original.ReferencePaths);
        using var scope = original.EnterScope();
        Type originalString = Assert.IsAssignableFrom<Type>(provider.Resolve("System.String"));
        DateTime timestamp = File.GetLastWriteTimeUtc(project);

        File.WriteAllText(project, Project("other.dll"));
        File.SetLastWriteTimeUtc(project, timestamp);

        Assert.NotEqual(original.Generation, provider.RefreshGeneration());
        Assert.Equal(original.Generation, provider.CurrentGeneration);
        Assert.Same(originalString, provider.Resolve("System.String"));
        using AnalysisMetadataView updated = provider.Capture();
        Assert.Equal([otherReference], updated.ReferencePaths);
        Assert.NotSame(originalString, updated.Resolve("System.String"));

        directory.CreateFile("sharpts.json", """{"references":["first.dll"]}""");
        using AnalysisMetadataView manifestUpdated = provider.Capture();
        Assert.NotEqual(updated.Generation, manifestUpdated.Generation);
        Assert.Contains(firstReference, manifestUpdated.ReferencePaths);
        Assert.Contains(otherReference, manifestUpdated.ReferencePaths);
    }

    [Fact]
    public void MissingProjectReferenceCreationInvalidatesTheMetadataGeneration()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string project = directory.CreateFile("test.csproj", Project("later.dll"));
        using var provider = new AnalysisMetadataProvider(projectFile: project, startDirectory: directory.Path);
        using AnalysisMetadataView missing = provider.Capture();
        Assert.Empty(missing.ReferencePaths);

        string reference = directory.CreateFile("later.dll", "not an assembly");
        using AnalysisMetadataView created = provider.Capture();

        Assert.NotEqual(missing.Generation, created.Generation);
        Assert.Equal([reference], created.ReferencePaths);
    }

    [Fact]
    public void ReadOnlyPackageRefreshDoesNotCreateRestoreArtifacts()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("sharpts.json", """{"packages":{"Nonexistent.Test.Package":"1.0.0"}}""");

        Exception failure = Assert.Throws<Exception>(() => DotNetReferences.ResolveReadOnly(directory.Path, []));
        Assert.Contains("packages need restore", failure.Message);
        using var provider = new AnalysisMetadataProvider(startDirectory: directory.Path);
        using AnalysisMetadataView view = provider.Capture();

        Assert.False(view.IsComplete);
        Assert.False(Directory.Exists(Path.Combine(directory.Path, ".sharpts")));
        Assert.NotNull(view.Resolve("System.String"));
    }

    [Fact]
    public void UnreadableAndInvalidReferencesPreserveSafeMetadataAndRemainExplicitlyPartial()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = Path.Combine(directory.Path, "SharpTS.dll");
        File.Copy(typeof(SharpTS.TypeSystem.BindingIndex).Assembly.Location, path);
        var log = new List<string>();
        using var provider = new AnalysisMetadataProvider(references: [path], startDirectory: directory.Path,
            log: log.Add);
        var unreadable = new ProbeFileSystem(CompilerFileSystem.Physical, candidate =>
        {
            if (candidate == path) throw new UnauthorizedAccessException("Test reference is unreadable.");
        });
        int partialGeneration;
        using (CompilerFileSystem.Use(unreadable))
        using (AnalysisMetadataView partial = provider.Capture())
        {
            partialGeneration = partial.Generation;
            Assert.False(partial.IsComplete);
            Assert.NotNull(partial.Resolve("System.String"));
            Assert.Null(partial.Resolve("SharpTS.TypeSystem.BindingIndex"));
        }
        Assert.Contains(log, message => message.Contains("unreadable", StringComparison.Ordinal));

        using AnalysisMetadataView readable = provider.Capture();
        Assert.NotEqual(partialGeneration, readable.Generation);
        Assert.True(readable.IsComplete);
        Assert.NotNull(readable.Resolve("SharpTS.TypeSystem.BindingIndex"));
        File.WriteAllText(path, "invalid assembly image");
        using AnalysisMetadataView invalid = provider.Capture();
        Assert.False(invalid.IsComplete);
        Assert.NotNull(invalid.Resolve("System.String"));
        Assert.Contains(log, message => message.Contains("metadata unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidExplicitSdkRetainsSafeBclMetadataAndMarksItsViewPartial()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string sdk = Path.Combine(directory.Path, "sdk");
        Directory.CreateDirectory(sdk);
        File.WriteAllText(Path.Combine(sdk, "System.Runtime.dll"), "invalid reference assembly");
        using var provider = new AnalysisMetadataProvider(sdkPath: sdk, startDirectory: directory.Path);

        using AnalysisMetadataView metadata = provider.Capture();

        Assert.False(metadata.IsComplete);
        Assert.NotNull(metadata.Resolve("System.String"));
    }

    [Fact]
    public async Task MetadataReplacementInvalidatesWarmSemanticAnalysisWithoutAFileWatcher()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("entry.ts", "const value = 1; value;\n");
        directory.CreateFile("tsconfig.json", """{"files":["entry.ts"],"compilerOptions":{"noLib":true}}""");
        string reference = Path.Combine(directory.Path, "SharpTS.dll");
        File.Copy(typeof(SharpTS.TypeSystem.BindingIndex).Assembly.Location, reference);
        using var provider = new AnalysisMetadataProvider(references: [reference], startDirectory: directory.Path);
        using var service = new SemanticAnalysisService(metadata: provider);
        string text = File.ReadAllText(path);
        using var original = await service.GetDocumentAsync(path, text);
        using var reused = await service.GetDocumentAsync(path, text);
        Assert.NotNull(original);
        Assert.NotNull(reused);
        Assert.Same(original.Model.Snapshot, reused.Model.Snapshot);

        File.Copy(typeof(AnalysisMetadataProvider).Assembly.Location, reference, overwrite: true);

        Assert.False(original.IsCurrent());
        using var updated = await service.GetDocumentAsync(path, text);
        Assert.NotNull(updated);
        Assert.NotSame(original.Model.Snapshot, updated.Model.Snapshot);
        Assert.Equal(2, service.Statistics.Checks);
        using (original.EnterMetadataScope())
            Assert.NotNull(provider.Resolve("SharpTS.TypeSystem.BindingIndex"));
        using (updated.EnterMetadataScope())
        {
            Assert.Null(provider.Resolve("SharpTS.TypeSystem.BindingIndex"));
            Assert.NotNull(provider.Resolve("SharpTS.LanguageServer.Project.AnalysisMetadataProvider"));
        }
    }

    [Fact]
    public void DecoratorTypeNamesFollowTheirCapturedGeneration()
    {
        int generation = 1;
        string[] names = ["Before.Type"];
        int enumerations = 0;
        var service = new DecoratorService(typeNames: () => { enumerations++; return names; },
            metadataGeneration: () => generation);
        const string text = "@DotNetType(\"";

        Assert.Equal("Before.Type", Assert.Single(service.Completion(text, 0, text.Length)!.Items).Label);
        Assert.Equal("Before.Type", Assert.Single(service.Completion(text, 0, text.Length)!.Items).Label);
        Assert.Equal(1, enumerations);
        names = ["After.Type"];
        generation++;

        Assert.Equal("After.Type", Assert.Single(service.Completion(text, 0, text.Length)!.Items).Label);
        Assert.Equal(2, enumerations);
    }

    [Fact]
    public void XmlDocumentationRevalidatesMissingChangedAndDeletedContent()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string assemblyPath = Path.Combine(directory.Path, "SharpTS.dll");
        File.Copy(typeof(SharpTS.TypeSystem.BindingIndex).Assembly.Location, assemblyPath);
        using var loader = new ReloadingAssemblyReferenceLoader([assemblyPath]);
        using var metadata = loader.AcquireSnapshot();
        Type type = Assert.IsAssignableFrom<Type>(metadata.TryResolve("SharpTS.TypeSystem.BindingIndex"));
        var docs = new XmlDocLoader();
        string xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        Assert.Null(docs.GetTypeSummary(type));

        File.WriteAllText(xmlPath, Xml("first"));
        Assert.Equal("first", docs.GetTypeSummary(type));
        DateTime timestamp = File.GetLastWriteTimeUtc(xmlPath);
        File.WriteAllText(xmlPath, Xml("other"));
        File.SetLastWriteTimeUtc(xmlPath, timestamp);
        Assert.Equal("other", docs.GetTypeSummary(type));

        File.Delete(xmlPath);
        Assert.Null(docs.GetTypeSummary(type));
    }

    private static string Project(string reference) =>
        $"<Project><ItemGroup><Reference Include=\"Ref\"><HintPath>{reference}</HintPath></Reference></ItemGroup></Project>";

    private static string Xml(string summary) =>
        $"<doc><members><member name=\"T:SharpTS.TypeSystem.BindingIndex\"><summary>{summary}</summary></member></members></doc>";

    private sealed class ProbeFileSystem(ICompilerFileSystem inner, Action<string> probe) : ICompilerFileSystem
    {
        public bool FileExists(string path) { probe(path); return inner.FileExists(path); }
        public bool DirectoryExists(string path) => inner.DirectoryExists(path);
        public string ReadAllText(string path) => inner.ReadAllText(path);
        public FileAttributes GetAttributes(string path) => inner.GetAttributes(path);
        public IReadOnlyList<string> EnumerateFiles(string path, string pattern, EnumerationOptions options) =>
            inner.EnumerateFiles(path, pattern, options);
        public IReadOnlyList<string> EnumerateDirectories(string path, string pattern, EnumerationOptions options) =>
            inner.EnumerateDirectories(path, pattern, options);
    }
}
