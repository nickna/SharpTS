using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.Modules;

public class TypeScriptProgramTests
{
    private static string VirtualPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine([Path.GetTempPath(), "virtual-sharpts", .. parts]));

    [Fact]
    public void LoadProgram_LoadsPinnedDefaultLibraryGraph()
    {
        string entryPath = VirtualPath("main.ts");
        var resolver = new ModuleResolver(
            entryPath,
            new Dictionary<string, string> { [entryPath] = "const x = 1;" },
            TypeScriptProgramOptions.Default);

        var entry = resolver.LoadProgram(entryPath);
        var modules = resolver.GetModulesInOrder(entry);

        Assert.Contains(modules, module => module.Path == "typescript-lib:lib.es5.d.ts");
        Assert.Contains(modules, module => module.Path == "typescript-lib:lib.dom.d.ts");
        Assert.All(modules.Where(module => module.IsDefaultLibrary),
            module => Assert.True(module.IsDeclarationFile));
        Assert.Same(entry, modules[^1]);
    }

    [Theory]
    [InlineData("lib.es2016.full.d.ts")]
    [InlineData("lib.es2018.full.d.ts")]
    [InlineData("lib.es2020.full.d.ts")]
    [InlineData("lib.es2022.full.d.ts")]
    [InlineData("lib.esnext.full.d.ts")]
    public void LoadProgram_ParsesTargetDefaultLibraryGraphs(string library)
    {
        string entryPath = VirtualPath(library, "main.ts");
        var resolver = new ModuleResolver(
            entryPath,
            new Dictionary<string, string> { [entryPath] = "const x = 1;" },
            TypeScriptProgramOptions.Default with { Lib = [library] });

        var entry = resolver.LoadProgram(entryPath);
        var modules = resolver.GetModulesInOrder(entry);
        var checker = new TypeChecker(maxErrors: 50);
        checker.CheckModules(modules, resolver);

        Assert.Contains(
            modules,
            module => module.Path == $"typescript-lib:{library}");
        Assert.Empty(checker.GetDiagnostics());
    }

    [Fact]
    public void LoadedDomDeclaration_DrivesUserTypeChecking()
    {
        string entryPath = VirtualPath("main.ts");
        var resolver = new ModuleResolver(
            entryPath,
            new Dictionary<string, string>
            {
                [entryPath] = "export const title: number = document.title;",
            },
            TypeScriptProgramOptions.Default);

        var entry = resolver.LoadProgram(entryPath);
        var checker = new TypeChecker(maxErrors: 50);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);

        Assert.Contains(checker.GetDiagnostics(), diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void LoadedWebWorkerDeclaration_ProvidesWorkerGlobals()
    {
        string entryPath = VirtualPath("worker", "main.ts");
        var resolver = new ModuleResolver(
            entryPath,
            new Dictionary<string, string>
            {
                [entryPath] = "export const href: string = self.location.href;",
            },
            TypeScriptProgramOptions.Default with
            {
                Lib = ["lib.es2022.d.ts", "lib.webworker.d.ts"],
            });

        var entry = resolver.LoadProgram(entryPath);
        var checker = new TypeChecker(maxErrors: 50);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);

        Assert.Empty(checker.GetDiagnostics());
    }

    [Fact]
    public void LoadedEs2018Intl_ProvidesMergedNamespaceValues()
    {
        string entryPath = VirtualPath("es2018-intl", "main.ts");
        var resolver = new ModuleResolver(
            entryPath,
            new Dictionary<string, string>
            {
                [entryPath] = """
                    export const ctor = Intl.PluralRules;
                    export const method = Intl.PluralRules.supportedLocalesOf;
                    """,
            },
            TypeScriptProgramOptions.Default with { Lib = ["lib.es2018.full.d.ts"] });

        var entry = resolver.LoadProgram(entryPath);
        var checker = new TypeChecker(maxErrors: 50);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);

        Assert.Empty(checker.GetDiagnostics());
    }

    [Fact]
    public void EmptyLibSelection_LoadsNoDefaultLibraries()
    {
        string entryPath = VirtualPath("main.ts");
        var resolver = new ModuleResolver(
            entryPath,
            new Dictionary<string, string> { [entryPath] = "const x = 1;" },
            TypeScriptProgramOptions.Default with { Lib = [] });

        var entry = resolver.LoadProgram(entryPath);

        Assert.DoesNotContain(
            resolver.GetModulesInOrder(entry), module => module.IsDefaultLibrary);
    }

    [Fact]
    public void MissingLibrary_ReportsAvailableLibraries()
    {
        string entryPath = VirtualPath("missing-lib", "main.ts");
        var resolver = new ModuleResolver(
            entryPath,
            new Dictionary<string, string> { [entryPath] = "const x = 1;" },
            TypeScriptProgramOptions.Default with { Lib = ["not-a-real-lib"] });

        var exception = Assert.Throws<Exception>(() => resolver.LoadProgram(entryPath));

        Assert.Contains("Cannot resolve library 'lib.not-a-real-lib.d.ts'", exception.Message);
        Assert.Contains("Available libraries: default, decorators", exception.Message);
    }

    [Fact]
    public void AutomaticTypes_LoadsVisibleDeclarationPackage()
    {
        string entryPath = VirtualPath("src", "main.ts");
        string declarationPath = VirtualPath("node_modules", "@types", "example", "index.d.ts");
        var files = new Dictionary<string, string>
        {
            [entryPath] = "export const item: PackageGlobal = { value: 1 };",
            [declarationPath] = "interface PackageGlobal { value: string; }",
        };
        var resolver = new ModuleResolver(entryPath, files, TypeScriptProgramOptions.Default);

        var entry = resolver.LoadProgram(entryPath);
        var modules = resolver.GetModulesInOrder(entry);
        var checker = new TypeChecker(maxErrors: 50);
        checker.CheckModules(modules, resolver);

        Assert.Contains(modules, module => module.Path == declarationPath);
        Assert.Contains(checker.GetDiagnostics(), diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void AutomaticTypes_AllowsPathReferencesFromDeclarationModules()
    {
        string entryPath = VirtualPath("declaration-path-reference", "src", "main.ts");
        string packageRoot = VirtualPath(
            "declaration-path-reference", "node_modules", "@types", "example");
        string declarationPath = Path.Combine(packageRoot, "index.d.ts");
        string globalsPath = Path.Combine(packageRoot, "globals.d.ts");
        var files = new Dictionary<string, string>
        {
            [entryPath] = "export const item: ReferencedGlobal = { value: 1 };",
            [declarationPath] = """
                /// <reference path="./globals.d.ts" />
                export {};
                """,
            [globalsPath] = "interface ReferencedGlobal { value: string; }",
        };
        var resolver = new ModuleResolver(entryPath, files, TypeScriptProgramOptions.Default);

        var entry = resolver.LoadProgram(entryPath);
        var modules = resolver.GetModulesInOrder(entry);
        var checker = new TypeChecker(maxErrors: 50);
        checker.CheckModules(modules, resolver);

        Assert.Contains(modules, module => module.Path == declarationPath);
        Assert.Contains(modules, module => module.Path == globalsPath);
        Assert.Contains(checker.GetDiagnostics(), diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void AutomaticTypes_DeclarationPackageNodeImportsDoNotResolveAsFiles()
    {
        string entryPath = VirtualPath("node-facade-imports", "src", "main.ts");
        string packageRoot = VirtualPath(
            "node-facade-imports", "node_modules", "@types", "node");
        var files = new Dictionary<string, string>
        {
            [entryPath] = "export const answer: number = 42;",
            [Path.Combine(packageRoot, "index.d.ts")] = """
                /// <reference path="./console.d.ts" />
                /// <reference path="./web-globals/console.d.ts" />
                """,
            [Path.Combine(packageRoot, "console.d.ts")] = """
                declare module "node:console" {
                    interface ConsoleShape {
                        log(message: string): void;
                    }
                    const console: ConsoleShape;
                    export = console;
                }
                """,
            // Mirrors @types/node's web-globals/console.d.ts: an ESM namespace import of
            // the package's own ambient "node:console" declaration — a module SharpTS's
            // stdlib does not provide, so it must not fall through to bare-specifier
            // file resolution.
            [Path.Combine(packageRoot, "web-globals", "console.d.ts")] = """
                export {};

                import * as console from "node:console";

                declare global {
                    var webConsole: typeof console;
                }
                """,
        };
        // The legacy/embedding options path (Disabled) resolves imports eagerly rather
        // than deferring failures to the checker, so it is the path that crashes when
        // the facade guard misses.
        var resolver = new ModuleResolver(entryPath, files);

        var entry = resolver.LoadProgram(entryPath);
        var modules = resolver.GetModulesInOrder(entry);

        Assert.Contains(modules, module =>
            module.Path.EndsWith("console.d.ts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AutomaticTypes_AppliesPackageTypesVersions()
    {
        string entryPath = VirtualPath("types-versions", "src", "main.ts");
        string packageRoot = VirtualPath(
            "types-versions", "node_modules", "@types", "example");
        string rootDeclaration = Path.Combine(packageRoot, "index.d.ts");
        string versionedDeclaration = Path.Combine(packageRoot, "ts5.6", "index.d.ts");
        var resolver = new ModuleResolver(entryPath, new Dictionary<string, string>
        {
            [entryPath] = "export const value: VersionedGlobal = { version: 5 };",
            [Path.Combine(packageRoot, "package.json")] = """
                {
                    "types": "index.d.ts",
                    "typesVersions": {
                        "<=5.6": { "*": ["ts5.6/*"] },
                        "<=5.7": { "*": ["ts5.7/*"] }
                    }
                }
                """,
            [rootDeclaration] = "interface WrongRoot { value: string; }",
            [versionedDeclaration] = "interface VersionedGlobal { version: number; }",
        }, TypeScriptProgramOptions.Default with { Lib = [] });

        var entry = resolver.LoadProgram(entryPath);
        var modules = resolver.GetModulesInOrder(entry);

        Assert.Contains(modules, module => module.Path == versionedDeclaration);
        Assert.DoesNotContain(modules, module => module.Path == rootDeclaration);
    }

    [Fact]
    public void DeclarationExtension_IsResolvedBeforeJavaScriptFallback()
    {
        string entryPath = VirtualPath("main.ts");
        string declarationPath = VirtualPath("dep.d.ts");
        var resolver = new ModuleResolver(entryPath, new Dictionary<string, string>
        {
            [entryPath] = """import { value } from "./dep"; value;""",
            [declarationPath] = "export declare const value: string;",
            [VirtualPath("dep.js")] = "export const value = 1;",
        });

        Assert.Equal(declarationPath, resolver.ResolveModulePath("./dep", entryPath));
    }

    [Fact]
    public void DeclarationModules_AllowCircularImportGraphs()
    {
        string entryPath = VirtualPath("declaration-cycle", "main.ts");
        string firstPath = VirtualPath("declaration-cycle", "first.d.ts");
        string secondPath = VirtualPath("declaration-cycle", "second.d.ts");
        var resolver = new ModuleResolver(entryPath, new Dictionary<string, string>
        {
            [entryPath] = """import type { First } from "./first"; export let value: First;""",
            [firstPath] = """
                import type { Second } from "./second";
                export interface First { next?: Second; }
                """,
            [secondPath] = """
                import type { First } from "./first";
                export interface Second { next?: First; }
                """,
        }, TypeScriptProgramOptions.Default with { Lib = [] });

        var entry = resolver.LoadProgram(entryPath);
        var modules = resolver.GetModulesInOrder(entry);

        Assert.Contains(modules, module => module.Path == firstPath);
        Assert.Contains(modules, module => module.Path == secondPath);
    }

    [Fact]
    public void DeclarationNodeImports_DoNotLoadExecutableStdlibFacades()
    {
        string entryPath = VirtualPath("declaration-node-import", "main.ts");
        string declarationPath = VirtualPath(
            "declaration-node-import", "package.d.ts");
        var resolver = new ModuleResolver(entryPath, new Dictionary<string, string>
        {
            [entryPath] = """import type { Resource } from "./package"; let value: Resource;""",
            [declarationPath] = """
                import type { URL } from "node:url";
                export interface Resource { url: URL; }
                """,
        }, TypeScriptProgramOptions.Default with { Lib = [] });

        var entry = resolver.LoadProgram(entryPath);
        var modules = resolver.GetModulesInOrder(entry);

        Assert.Contains(modules, module => module.Path == declarationPath);
        Assert.DoesNotContain(
            modules, module => module.Path == "stdlib:node/url.ts");
    }

    [Fact]
    public void ProgramResolution_UsesPackageExportsTypesCondition()
    {
        string entryPath = VirtualPath("exports-types", "main.ts");
        string packageRoot = VirtualPath("exports-types", "node_modules", "example");
        string declarationPath = Path.Combine(packageRoot, "types", "index.d.ts");
        string runtimePath = Path.Combine(packageRoot, "dist", "index.js");
        var files = new Dictionary<string, string>
        {
            [entryPath] = """import { value } from "example";""",
            [Path.Combine(packageRoot, "package.json")] = """
                {
                    "exports": {
                        ".": {
                            "types": "./types/index.d.ts",
                            "import": "./dist/index.js"
                        }
                    }
                }
                """,
            [declarationPath] = "export declare const value: string;",
            [runtimePath] = "export const value = 1;",
        };

        var programResolver = new ModuleResolver(
            entryPath, files, TypeScriptProgramOptions.Default);
        var runtimeResolver = new ModuleResolver(entryPath, files);

        Assert.Equal(declarationPath, programResolver.ResolveModulePath("example", entryPath));
        Assert.Equal(runtimePath, programResolver.ResolveRuntimeModulePath("example", entryPath));
        Assert.Equal(runtimePath, runtimeResolver.ResolveModulePath("example", entryPath));

        var entry = programResolver.LoadProgram(entryPath);
        Assert.Contains(
            programResolver.GetModulesInOrder(entry),
            module => module.Path == declarationPath);
        Assert.Contains(
            programResolver.GetRuntimeModulesInOrder(entry),
            module => module.Path == runtimePath);
    }

    [Fact]
    public void NamespaceDefaultReExport_SupportsTypeAndValueMembers()
    {
        string entryPath = VirtualPath("namespace-default", "main.ts");
        string barrelPath = VirtualPath("namespace-default", "barrel.d.ts");
        string typesPath = VirtualPath("namespace-default", "types.d.ts");
        var resolver = new ModuleResolver(entryPath, new Dictionary<string, string>
        {
            [entryPath] = """
                import declarations from "./barrel";
                type Imported = declarations.Named;
                export const value: Imported = declarations.Named;
                """,
            [barrelPath] = """export * as default from "./types";""",
            [typesPath] = """
                export type Named = 0;
                export declare const Named: 0;
                """,
        }, TypeScriptProgramOptions.Default with { Lib = [] });

        var entry = resolver.LoadProgram(entryPath);
        var checker = new TypeChecker(maxErrors: 50);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);

        Assert.Empty(checker.GetDiagnostics());
    }
}
