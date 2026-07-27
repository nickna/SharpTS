using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public class CliProjectTests
{
    [Fact]
    public void ProjectCommandChecksEveryIncludedRoot()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("src/ok.ts", "export const ok: number = 1;");
        dir.CreateFile("src/bad.ts", """export const bad: number = "wrong";""");
        dir.CreateFile("ignored/bad.ts", """export const ignored: number = "wrong";""");
        dir.CreateFile("tsconfig.json", """
            { "include": ["src"], "exclude": ["ignored"] }
            """);

        var result = CliTestHelper.RunCli("-p .", dir.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("src", result.StandardOutput);
        Assert.DoesNotContain("ignored", result.StandardOutput);
    }

    [Fact]
    public void ExcludeDoesNotBlockImportedDependencies()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("src/main.ts", """import { bad } from "../generated/bad"; console.log(bad);""");
        dir.CreateFile("generated/bad.ts", """export const bad: number = "wrong";""");
        dir.CreateFile("tsconfig.json", """
            { "include": ["src"], "exclude": ["generated"] }
            """);

        var result = CliTestHelper.RunCli("-p .", dir.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("bad.ts", result.StandardOutput);
    }

    [Fact]
    public void ProjectCommandUsesBaseUrlPaths()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("src/main.ts", """import { value } from "@app/value"; const n: number = value;""");
        dir.CreateFile("src/lib/value.ts", "export const value: number = 42;");
        dir.CreateFile("tsconfig.json", """
            {
              "files": ["src/main.ts"],
              "compilerOptions": {
                "baseUrl": ".",
                "paths": { "@app/*": ["src/lib/*"] },
                "moduleResolution": "bundler"
              }
            }
            """);

        var result = CliTestHelper.RunCli("-p .", dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("checked 1 root file(s)", result.StandardOutput);
    }

    [Fact]
    public void ScriptExecutionUsesProjectPaths()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile(
            "main.ts",
            """import { value } from "@app/value"; console.log(value);""");
        dir.CreateFile("src/value.ts", "export const value: number = 42;");
        dir.CreateFile("tsconfig.json", """
            {
              "files": ["main.ts"],
              "compilerOptions": {
                "baseUrl": ".",
                "paths": { "@app/*": ["src/*"] },
                "moduleResolution": "bundler"
              }
            }
            """);

        var result = CliTestHelper.RunCli("main.ts", dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("42\n", result.StandardOutput);
    }

    [Fact]
    public void TypesAddsGlobalDeclarationPackages()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("types/custom/index.d.ts", "declare const projectName: string;");
        dir.CreateFile("main.ts", "const name: string = projectName;");
        dir.CreateFile("tsconfig.json", """
            {
              "files": ["main.ts"],
              "compilerOptions": {
                "typeRoots": ["types"],
                "types": ["custom"]
              }
            }
            """);

        var result = CliTestHelper.RunCli("-p .", dir.Path);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void TypesCanDeclareAnAmbientModule()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("types/virtual-package/index.d.ts", """
            declare module "virtual-package" {
              export function greet(name: string): string;
            }
            """);
        dir.CreateFile("main.ts", """
            import { greet } from "virtual-package";
            const message: string = greet("SharpTS");
            """);
        dir.CreateFile("tsconfig.json", """
            {
              "files": ["main.ts"],
              "compilerOptions": {
                "typeRoots": ["types"],
                "types": ["virtual-package"]
              }
            }
            """);

        var result = CliTestHelper.RunCli("-p .", dir.Path);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void LibLoadsDeclarationsFromInstalledTypeScript()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile(
            "node_modules/typescript/lib/lib.custom.d.ts",
            "declare const libGlobal: number;");
        dir.CreateFile("main.ts", "const value: number = libGlobal;");
        dir.CreateFile("tsconfig.json", """
            {
              "files": ["main.ts"],
              "compilerOptions": { "lib": ["custom"] }
            }
            """);

        var result = CliTestHelper.RunCli("-p .", dir.Path);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void BuildChecksReferencesInDependencyOrderAndReusesState()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("packages/core/core.ts", "export const core: number = 1;");
        string coreConfig = dir.CreateFile(
            "packages/core/tsconfig.json",
            """{ "files": ["core.ts"], "compilerOptions": { "composite": true } }""");
        dir.CreateFile("app.ts", "export const app: number = 1;");
        string appConfig = dir.CreateFile("tsconfig.json", """
            {
              "files": ["app.ts"],
              "references": [{ "path": "packages/core" }]
            }
            """);

        var first = CliTestHelper.RunCli("--build .", dir.Path);
        var second = CliTestHelper.RunCli("--build .", dir.Path);

        Assert.Equal(0, first.ExitCode);
        Assert.True(
            first.StandardOutput.IndexOf(coreConfig, StringComparison.OrdinalIgnoreCase) <
            first.StandardOutput.IndexOf(appConfig, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(2, second.StandardOutput.Split("up to date").Length - 1);
        Assert.True(File.Exists(dir.GetPath("packages/core/tsconfig.sharptsbuildinfo")));
        Assert.True(File.Exists(dir.GetPath("tsconfig.sharptsbuildinfo")));
    }

    [Fact]
    public void IncrementalProjectInvalidatesWhenAnInputChanges()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("main.ts", "export const value: number = 1;");
        dir.CreateFile("tsconfig.json", """
            { "files": ["main.ts"], "compilerOptions": { "incremental": true } }
            """);

        var first = CliTestHelper.RunCli("-p .", dir.Path);
        var second = CliTestHelper.RunCli("-p .", dir.Path);
        dir.CreateFile("main.ts", "export const value: number = 2;");
        var third = CliTestHelper.RunCli("-p .", dir.Path);

        Assert.Equal(0, first.ExitCode);
        Assert.Contains("up to date", second.StandardOutput);
        Assert.DoesNotContain("up to date", third.StandardOutput);
        Assert.Contains("checked", third.StandardOutput);
    }

    [Fact]
    public void IncrementalProjectInvalidatesWhenIncludeFindsANewRoot()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("src/main.ts", "export const value: number = 1;");
        dir.CreateFile("tsconfig.json", """
            { "include": ["src"], "compilerOptions": { "incremental": true } }
            """);

        Assert.Equal(0, CliTestHelper.RunCli("-p .", dir.Path).ExitCode);
        Assert.Contains("up to date", CliTestHelper.RunCli("-p .", dir.Path).StandardOutput);
        dir.CreateFile("src/new.ts", "export const added: number = 2;");
        var afterNewRoot = CliTestHelper.RunCli("-p .", dir.Path);

        Assert.Equal(0, afterNewRoot.ExitCode);
        Assert.DoesNotContain("up to date", afterNewRoot.StandardOutput);
        Assert.Contains("checked 2 root file(s)", afterNewRoot.StandardOutput);
    }

    [Fact]
    public void IncrementalProjectInvalidatesWhenCliCheckingOptionsChange()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("main.ts", "const value: number = null;");
        dir.CreateFile("tsconfig.json", """
            { "files": ["main.ts"], "compilerOptions": { "incremental": true } }
            """);

        var loose = CliTestHelper.RunCli("--strictNullChecks=false -p .", dir.Path);
        var strict = CliTestHelper.RunCli("--strictNullChecks=true -p .", dir.Path);

        Assert.Equal(0, loose.ExitCode);
        Assert.Equal(1, strict.ExitCode);
        Assert.DoesNotContain("up to date", strict.StandardOutput);
        Assert.Contains("not assignable", strict.StandardOutput);
    }
}
