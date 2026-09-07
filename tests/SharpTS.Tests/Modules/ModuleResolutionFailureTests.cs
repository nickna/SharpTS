using SharpTS.Configuration;
using SharpTS.Modules;
using SharpTS.Modules.Stdlib;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.Modules;

public class ModuleResolutionFailureTests
{
    private static readonly string Entry = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(), "sharpts-resolution-failures", "main.ts"));

    private static ModuleResolver CreateResolver(string source, bool declarations,
        string? dependency = null, bool runtime = false)
    {
        var files = new Dictionary<string, string> { [Entry] = source };
        if (dependency is not null)
            files[Path.Combine(Path.GetDirectoryName(Entry)!, runtime ? "dep.ts" : "dep.d.ts")] = dependency;
        return new ModuleResolver(Entry, files,
            new TypeScriptProgramOptions { PreferDeclarationFiles = declarations });
    }

    [Theory]
    [InlineData("import { value } from './missing';")]
    [InlineData("export { value } from './missing';")]
    [InlineData("export * from './missing';")]
    public void MissingDependency_RecoversForCheckingButFailsForRuntime(string source)
    {
        var resolver = CreateResolver(source, declarations: true);
        var entry = resolver.LoadModule(Entry);
        Assert.Empty(entry.Dependencies);
        var checker = new TypeChecker();
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Contains(checker.GetDiagnostics(), diagnostic =>
            diagnostic.TsCode == "TS2307" && diagnostic.Line == 1);

        var runtimeResolver = CreateResolver(source, declarations: false);
        var exception = Assert.Throws<ModuleResolutionException>(() => runtimeResolver.LoadModule(Entry));
        Assert.Equal(ModuleResolutionFailure.NotFound, exception.Reason);
    }

    [Theory]
    [InlineData("import { value } from './dep';")]
    [InlineData("export { value } from './dep';")]
    [InlineData("export * from './dep';")]
    [InlineData("import dep = require('./dep');")]
    public void DeclarationOnlyDependency_PreservesDeclarationGraph(string source)
    {
        string packagePath = Path.Combine(Path.GetDirectoryName(Entry)!, "node_modules", "dep");
        var resolver = new ModuleResolver(Entry, new Dictionary<string, string>
        {
            [Entry] = source.Replace("./dep", "dep"),
            [Path.Combine(packagePath, "package.json")] =
                """{ "types": "./types.d.ts", "main": "./missing.js" }""",
            [Path.Combine(packagePath, "types.d.ts")] = "export declare const value: number;",
        }, new TypeScriptProgramOptions { PreferDeclarationFiles = true });
        var entry = resolver.LoadModule(Entry);
        Assert.True(Assert.Single(entry.Dependencies).IsDeclarationFile);
        Assert.Empty(entry.RuntimeDependencies);
        Assert.Throws<ModuleResolutionException>(() => resolver.ResolveRuntimeModulePath("dep", Entry));
    }

    [Theory]
    [InlineData("Module Error: Cannot resolve anything")]
    [InlineData("No matching module exists")]
    [InlineData("")]
    public void RecoveryPolicy_IsIndependentOfDiagnosticWording(string message)
    {
        var exception = new ModuleResolutionException(ModuleResolutionFailure.NotFound, message);
        Assert.True(CreateResolver("", declarations: true).CanRecoverResolutionFailure(exception));
        Assert.False(CreateResolver("", declarations: false).CanRecoverResolutionFailure(exception));
        Assert.Equal(message, exception.Message);
    }

    [Theory]
    [InlineData("import { value } from './dep';")]
    [InlineData("export { value } from './dep';")]
    public void DependencyParserFailure_IsNotRecovered(string source)
    {
        var resolver = CreateResolver(source, declarations: true, "export const = ;", runtime: true);
        var exception = Record.Exception(() => resolver.LoadModule(Entry));
        Assert.NotNull(exception);
        Assert.IsNotType<ModuleResolutionException>(exception);
    }

    [Theory]
    [InlineData("import { value } from 'dep';", false)]
    [InlineData("export { value } from 'dep';", false)]
    [InlineData("import { value } from 'dep';", true)]
    [InlineData("export { value } from 'dep';", true)]
    public void ProviderFailure_UsesTypeInsteadOfMessage(string source, bool runtimeDependency)
    {
        foreach (bool unresolved in new[] { false, true })
        {
            var resolver = CreateResolver(source, declarations: true, "export declare const value: number;");
            Exception failure = unresolved
                ? new ModuleResolutionException(ModuleResolutionFailure.NotFound, "Diagnostic wording changed")
                : new IOException("Module Error: Cannot resolve host resource");
            // Inject a provider through the internal chain to exercise the real catch boundaries.
            var providers = Assert.IsType<List<IModuleProvider>>(resolver.StdlibChain.Providers);
            providers.Insert(0, new FailingProvider(failure, runtimeDependency));

            if (unresolved)
            {
                var entry = resolver.LoadModule(Entry);
                Assert.Empty(entry.RuntimeDependencies);
                Assert.Equal(runtimeDependency ? 1 : 0, entry.Dependencies.Count);
            }
            else
            {
                Assert.Same(failure, Record.Exception(() => resolver.LoadModule(Entry)));
            }
        }
    }

    private sealed class FailingProvider(Exception failure, bool runtimeDependency) : IModuleProvider
    {
        private int _calls;
        public string Name => "test-failure";
        public IReadOnlyCollection<string> ProvidedModules => ["dep"];

        public bool TryResolve(string specifier, out StdlibModule? module)
        {
            module = null;
            if (specifier != "dep")
                return false;
            if (runtimeDependency && _calls++ == 0)
            {
                module = new StdlibModule("dep", new TypeScriptSource(""), Name,
                    Path.Combine(Path.GetDirectoryName(Entry)!, "dep.d.ts"));
                return true;
            }
            throw failure;
        }
    }

    [Theory]
    [InlineData("missing", ModuleResolutionMode.Node16,
        "Module Error: Cannot resolve bare specifier 'missing'. Bare imports require a node_modules directory with the package installed.")]
    [InlineData("missing", ModuleResolutionMode.Classic,
        "Module Error: Cannot resolve bare specifier 'missing' with classic module resolution.")]
    [InlineData("#missing", ModuleResolutionMode.Node16,
        "Module Error: Cannot resolve subpath import '#missing'. No matching entry found in the nearest package.json \"imports\" field.")]
    public void UnresolvedSpecifier_PreservesDiagnostic(string specifier, ModuleResolutionMode mode, string message)
    {
        var resolver = new ModuleResolver(Entry,
            new ModuleResolutionOptions(mode, null, new Dictionary<string, IReadOnlyList<string>>()),
            new Dictionary<string, string>());
        var exception = Assert.Throws<ModuleResolutionException>(() => resolver.ResolveModulePath(specifier, Entry));
        Assert.Equal(ModuleResolutionFailure.NotFound, exception.Reason);
        Assert.Equal(message, exception.Message);
    }
}
