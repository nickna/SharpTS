using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public class CliDeclarationEmitTests
{
    [Fact]
    public void CompileEmitDeclarationOnlyWritesDtsWithoutAssembly()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("src/library.ts", """
            export interface Box<T> {
              value: T;
              map<U>(fn: (value: T) => U): Box<U>;
            }

            export class Service {
              private secret: number = 1;
              run(value: string) {
                return value.length;
              }
            }

            export const answer = 42;
            export function identity<T>(value: T) {
              return value;
            }
            """);

        var result = CliTestHelper.RunCli(
            "--no-tsconfig --compile src/library.ts --emitDeclarationOnly " +
            "--declarationDir types -o library.dll",
            dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(dir.GetPath("library.dll")));
        string declaration = File.ReadAllText(dir.GetPath("types/library.d.ts"));
        Assert.Contains("export interface Box<T>", declaration);
        Assert.Contains("map<U>(arg0: (arg0: T) => U): Box<U>;", declaration);
        Assert.Contains("export declare class Service", declaration);
        Assert.Contains("public run(value: string): number;", declaration);
        Assert.Contains("private secret: number;", declaration);
        Assert.Contains("export declare const answer: 42;", declaration);
        Assert.Contains("export declare function identity<T>(value: T): T;", declaration);
    }

    [Fact]
    public void ProjectDeclarationEmitUsesRootDirAndDeclarationDir()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("src/models/user.ts", "export interface User { name: string; }");
        dir.CreateFile("src/index.ts", """
            export { User } from "./models/user";
            export const version: string = "1.0";
            """);
        dir.CreateFile("tsconfig.json", """
            {
              "include": ["src"],
              "compilerOptions": {
                "declaration": true,
                "emitDeclarationOnly": true,
                "rootDir": "src",
                "declarationDir": "types"
              }
            }
            """);

        var result = CliTestHelper.RunCli("-p .", dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("emitted 2 declaration file(s)", result.StandardOutput);
        Assert.True(File.Exists(dir.GetPath("types/index.d.ts")));
        Assert.True(File.Exists(dir.GetPath("types/models/user.d.ts")));
    }

    [Fact]
    public void DeclarationEmitPreservesTypeOnlyImportsAndExports()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("src/types.ts", """
            export interface Box<T> { value: T; }
            export const runtime = 1;
            """);
        dir.CreateFile("src/index.ts", """
            import type { Box } from "./types";
            export type { Box } from "./types";
            export { type Box as RenamedBox } from "./types";
            export function wrap<T>(value: T): Box<T> {
              return { value };
            }
            """);
        dir.CreateFile("tsconfig.json", """
            {
              "files": ["src/index.ts", "src/types.ts"],
              "compilerOptions": {
                "declaration": true,
                "rootDir": "src",
                "declarationDir": "types"
              }
            }
            """);

        var result = CliTestHelper.RunCli("-p .", dir.Path);

        Assert.Equal(0, result.ExitCode);
        string declaration = File.ReadAllText(dir.GetPath("types/index.d.ts"));
        Assert.Contains("""import type { Box } from "./types";""", declaration);
        Assert.Contains("""export type { Box } from "./types";""", declaration);
        Assert.Contains("""export { type Box as RenamedBox } from "./types";""", declaration);
    }

    [Fact]
    public void TypeOnlyExportsHaveNoRuntimeBinding()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("types.ts", """
            export interface Box<T> { value: T; }
            export const value = 42;
            """);
        dir.CreateFile("main.ts", """
            import { value } from "./types";
            interface LocalOnly { name: string; }
            export { type LocalOnly };
            export type { Box } from "./types";
            console.log(value);
            """);

        var result = CliTestHelper.RunCli("main.ts", dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("42\n", result.StandardOutput);
    }

    [Fact]
    public void ProjectEmitUsesModuleSpecificDeclarationExtensions()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("src/esm.mts", "export const esm = true;");
        dir.CreateFile("src/common.cts", "export const common = true;");
        dir.CreateFile("tsconfig.json", """
            {
              "include": ["src"],
              "compilerOptions": {
                "declaration": true,
                "rootDir": "src",
                "declarationDir": "types"
              }
            }
            """);

        var result = CliTestHelper.RunCli("-p .", dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(dir.GetPath("types/esm.d.mts")));
        Assert.True(File.Exists(dir.GetPath("types/common.d.cts")));
    }

    [Fact]
    public void IncrementalProjectInvalidatesWhenDeclarationOutputIsDeleted()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("main.ts", "export const value = 1;");
        dir.CreateFile("tsconfig.json", """
            {
              "files": ["main.ts"],
              "compilerOptions": {
                "declaration": true,
                "incremental": true
              }
            }
            """);

        var first = CliTestHelper.RunCli("-p .", dir.Path);
        var second = CliTestHelper.RunCli("-p .", dir.Path);
        File.Delete(dir.GetPath("main.d.ts"));
        var third = CliTestHelper.RunCli("-p .", dir.Path);

        Assert.Equal(0, first.ExitCode);
        Assert.Contains("up to date", second.StandardOutput);
        Assert.Equal(0, third.ExitCode);
        Assert.DoesNotContain("up to date", third.StandardOutput);
        Assert.True(File.Exists(dir.GetPath("main.d.ts")));
    }

    [Fact]
    public void DeclarationEmitDoesNotWritePartialOutputWhenCheckingFails()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("main.ts", """export const value: number = "wrong";""");
        dir.CreateFile("tsconfig.json", """
            {
              "files": ["main.ts"],
              "compilerOptions": { "declaration": true }
            }
            """);

        var result = CliTestHelper.RunCli("-p .", dir.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.False(File.Exists(dir.GetPath("main.d.ts")));
    }

    [Fact]
    public void DeclarationEmitRejectsClrTypesInPublicSurface()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("main.ts", """
            import { StringBuilder } from "dotnet:System.Text";
            export function makeBuilder(): StringBuilder {
              return new StringBuilder();
            }
            """);

        var result = CliTestHelper.RunCli(
            "--no-tsconfig --compile main.ts --emitDeclarationOnly",
            dir.Path);

        Assert.Equal(1, result.ExitCode);
        // Compile errors print to stderr (release smokes and MSBuild both parse it there).
        Assert.Contains("is not portable to TypeScript consumers", result.StandardError);
        Assert.False(File.Exists(dir.GetPath("main.d.ts")));
    }
}
