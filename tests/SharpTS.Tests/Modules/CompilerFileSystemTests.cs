using SharpTS.Configuration;
using SharpTS.IO;
using SharpTS.Modules;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.Modules;

public sealed class CompilerFileSystemTests
{
    [Fact]
    public void ScopedSourceReadUsesTheCapturedTextReader()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("main.ts", "const value = 1;");
        File.WriteAllText(path, "const value = 1;", new System.Text.UTF8Encoding(true));
        var inputs = new RecordingFileSystem();
        using var scope = CompilerFileSystem.Use(inputs);

        var document = CompilerFileSystem.ReadSourceDocument(path);

        Assert.Equal("const value = 1;", document.Text);
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(document.Text)), document.Checksum);
        Assert.Equal([path], inputs.Reads);
    }

    [Fact]
    public void OverlayProgramLoadsDiskAutomaticTypesAndCustomLibrary()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string entryPath = directory.CreateFile("main.ts", "const disk = 0;");
        string typesPath = Path.GetFullPath(directory.CreateFile("node_modules/@types/widget/index.d.ts",
            "declare const widget: string;"));
        string libraryPath = Path.GetFullPath(directory.CreateFile("node_modules/typescript/lib/lib.custom.d.ts",
            "declare const custom: number;"));
        var inputs = new RecordingFileSystem();
        using var scope = CompilerFileSystem.Use(inputs);
        var resolver = new ModuleResolver(entryPath, ModuleResolutionOptions.Default,
            new Dictionary<string, string> { [entryPath] = "const dirty = 1;" },
            new TypeScriptProgramOptions
            {
                Lib = ["custom"],
                TypeRoots = [directory.GetPath("node_modules/@types")],
            }, virtualFilesFallBackToDisk: true);

        ParsedModule entry = resolver.LoadProgram(entryPath);
        var modules = resolver.GetModulesInOrder(entry);

        Assert.Equal("const dirty = 1;", entry.Document!.Text);
        Assert.Contains(modules, module => module.Path == typesPath);
        Assert.Contains(modules, module => module.Path == libraryPath);
        Assert.Contains(typesPath, inputs.Reads);
        Assert.Contains(libraryPath, inputs.Reads);
        Assert.DoesNotContain(entryPath, inputs.Reads);
        Assert.Contains(Path.GetFullPath(directory.GetPath("node_modules/@types")), inputs.DirectoryEnumerations);
        Assert.NotEmpty(entry.Tokens);
    }

    [Fact]
    public void PureVirtualProgramUsesVirtualCommonJsAndAutomaticTypesWithoutDisk()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sharpts-virtual-inputs"));
        string entryPath = Path.Combine(root, "main.js");
        string typeRoot = Path.Combine(root, "node_modules", "@types");
        string declarationPath = Path.Combine(typeRoot, "widget", "index.d.ts");
        var inputs = new RecordingFileSystem(rejectReads: true);
        using var scope = CompilerFileSystem.Use(inputs);
        var resolver = new ModuleResolver(entryPath, ModuleResolutionOptions.Default,
            new Dictionary<string, string>
            {
                [entryPath] = "module.exports = 1;",
                [declarationPath] = "declare const widget: string;",
            }, new TypeScriptProgramOptions { NoLib = true, TypeRoots = [typeRoot] });

        ParsedModule entry = resolver.LoadProgram(entryPath);

        Assert.True(entry.IsCommonJs);
        Assert.Contains(resolver.GetModulesInOrder(entry), module => module.Path == declarationPath);
        Assert.Equal(0, inputs.Calls);
    }

    [Fact]
    public void PureVirtualCustomLibraryFailureDoesNotProbeDisk()
    {
        string entryPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sharpts-virtual-inputs", "main.ts"));
        var inputs = new RecordingFileSystem(rejectReads: true);
        using var scope = CompilerFileSystem.Use(inputs);
        var resolver = new ModuleResolver(entryPath,
            new Dictionary<string, string> { [entryPath] = "const value = 1;" },
            new TypeScriptProgramOptions { Lib = ["missing-custom"], Types = [] });

        Exception exception = Assert.Throws<Exception>(() => resolver.LoadProgram(entryPath));

        Assert.Contains("Cannot resolve library", exception.Message);
        Assert.Equal(0, inputs.Calls);
    }

    [Fact]
    public void ConfigDiscoveryAndExtendsObserveNegativeCandidatesAndExactReads()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string configPath = directory.CreateFile("tsconfig.json",
            """{ "extends": "./base", "files": ["src/main.ts"] }""");
        string basePath = directory.CreateFile("base.json", """{ "compilerOptions": { "types": [] } }""");
        directory.CreateFile("src/main.ts", "const value = 1;");
        var inputs = new RecordingFileSystem();
        using var scope = CompilerFileSystem.Use(inputs);

        TsConfigResult? project = TsConfigLoader.FindAndLoad(directory.GetPath("src"));

        Assert.NotNull(project);
        Assert.Equal(configPath, project.ConfigPath);
        Assert.Contains(Path.GetFullPath(directory.GetPath("src/tsconfig.json")), inputs.FileProbes);
        Assert.Contains(directory.GetPath("base"), inputs.FileProbes);
        Assert.Contains(configPath, inputs.Reads);
        Assert.Contains(basePath, inputs.Reads);
    }

    [Fact]
    public async Task NestedAsyncScopesDoNotReplaceOtherAnalysisReaders()
    {
        var outer = new RecordingFileSystem();
        using (CompilerFileSystem.Use(outer))
        {
            await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                Assert.Same(outer, CompilerFileSystem.Current);
                var nested = new RecordingFileSystem();
                using (CompilerFileSystem.Use(nested))
                {
                    await Task.Yield();
                    Assert.Same(nested, CompilerFileSystem.Current);
                }
                Assert.Same(outer, CompilerFileSystem.Current);
            }));
            Assert.Same(outer, CompilerFileSystem.Current);
        }
        Assert.Same(CompilerFileSystem.Physical, CompilerFileSystem.Current);
    }

    [Fact]
    public void ScopedCancellationEscapesConfigurationAndModuleFallbacks()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var inputs = new RecordingFileSystem(rejectReads: true);
        using var scope = CompilerFileSystem.Use(inputs, cancellation.Token);

        Assert.Throws<OperationCanceledException>(() => TsConfigLoader.Discover(Path.GetTempPath()));
        Assert.Throws<OperationCanceledException>(() => new ModuleResolver(Path.GetTempPath()).LoadModule("dotnet:System"));
        Assert.Equal(0, inputs.Calls);
    }

    [Fact]
    public void CancellationReadingAnExtendedConfigEscapesLoading()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string config = directory.CreateFile("tsconfig.json", """{ "extends": "./base", "files": [] }""");
        string extended = directory.CreateFile("base.json", "{}");
        var inputs = new RecordingFileSystem { CancelReadPath = extended };
        using var scope = CompilerFileSystem.Use(inputs);

        Assert.Throws<OperationCanceledException>(() => TsConfigLoader.Load(config));
        Assert.Contains(extended, inputs.Reads);
    }

    [Fact]
    public void CancellationReadingASelectedTypePackageEscapesConfigRecovery()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string config = directory.CreateFile("tsconfig.json", """
            { "compilerOptions": { "types": ["widget"], "typeRoots": ["./types"] }, "files": [] }
            """);
        string package = Path.GetFullPath(directory.CreateFile("types/widget/package.json",
            """{ "types": "index.d.ts" }"""));
        directory.CreateFile("types/widget/index.d.ts", "declare const widget: string;");
        var inputs = new RecordingFileSystem { CancelReadPath = package };
        using var scope = CompilerFileSystem.Use(inputs);

        Assert.Throws<OperationCanceledException>(() => TsConfigLoader.Load(config));
        Assert.Contains(package, inputs.Reads);
    }

    [Fact]
    public void CancellationReadingPackageMetadataEscapesModuleResolutionRecovery()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string entry = directory.CreateFile("main.ts", "export {};");
        string package = Path.GetFullPath(directory.CreateFile("node_modules/widget/package.json",
            """{ "main": "index.js" }"""));
        directory.CreateFile("node_modules/widget/index.js", "module.exports = 1;");
        var inputs = new RecordingFileSystem { CancelReadPath = package };
        using var scope = CompilerFileSystem.Use(inputs);

        Assert.Throws<OperationCanceledException>(() => new ModuleResolver(entry).ResolveModulePath("widget", entry));
        Assert.Contains(package, inputs.Reads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellationReadingCommonJsDetectionInputsEscapesHeuristicFallback(bool hasPackage)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string entry = directory.CreateFile("main.js", "module.exports = 1;");
        string cancelledPath = hasPackage
            ? directory.CreateFile("package.json", """{ "type": "commonjs" }""")
            : entry;
        var inputs = new RecordingFileSystem { CancelReadPath = cancelledPath };
        using var scope = CompilerFileSystem.Use(inputs);

        Assert.Throws<OperationCanceledException>(() => CommonJsDetector.Detect(entry));
        Assert.Contains(cancelledPath, inputs.Reads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellationReadingCommonJsDependenciesEscapesOptionalDependencyRecovery(bool cancelMetadata)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string entry = directory.CreateFile("main.cjs", "const value = require('widget'); module.exports = value;");
        string package = Path.GetFullPath(directory.CreateFile("node_modules/widget/package.json",
            """{ "main": "index.js" }"""));
        string dependency = Path.GetFullPath(directory.CreateFile("node_modules/widget/index.js", "module.exports = 1;"));
        var inputs = new RecordingFileSystem { CancelReadPath = cancelMetadata ? package : dependency };
        using var scope = CompilerFileSystem.Use(inputs);

        Assert.Throws<OperationCanceledException>(() => new ModuleResolver(entry).LoadModule(entry));
        Assert.Contains(inputs.CancelReadPath!, inputs.Reads);
    }

    private sealed class RecordingFileSystem(bool rejectReads = false) : ICompilerFileSystem
    {
        public List<string> Reads { get; } = [];
        public List<string> FileProbes { get; } = [];
        public List<string> DirectoryEnumerations { get; } = [];
        public int Calls { get; private set; }
        public string? CancelReadPath { get; init; }

        private void Observe()
        {
            Calls++;
            if (rejectReads) throw new InvalidOperationException("Pure virtual programs must not access disk.");
        }

        public bool FileExists(string path)
        {
            Observe();
            FileProbes.Add(path);
            return CompilerFileSystem.Physical.FileExists(path);
        }

        public bool DirectoryExists(string path)
        {
            Observe();
            return CompilerFileSystem.Physical.DirectoryExists(path);
        }

        public string ReadAllText(string path)
        {
            Observe();
            Reads.Add(path);
            if (CancelReadPath is not null &&
                Path.GetFullPath(path).Equals(CancelReadPath, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new OperationCanceledException("The test cancelled a filesystem read during analysis.");
            return CompilerFileSystem.Physical.ReadAllText(path);
        }

        public FileAttributes GetAttributes(string path)
        {
            Observe();
            return CompilerFileSystem.Physical.GetAttributes(path);
        }

        public IReadOnlyList<string> EnumerateFiles(string path, string pattern, EnumerationOptions options)
        {
            Observe();
            return CompilerFileSystem.Physical.EnumerateFiles(path, pattern, options);
        }

        public IReadOnlyList<string> EnumerateDirectories(string path, string pattern, EnumerationOptions options)
        {
            Observe();
            DirectoryEnumerations.Add(path);
            return CompilerFileSystem.Physical.EnumerateDirectories(path, pattern, options);
        }
    }
}
