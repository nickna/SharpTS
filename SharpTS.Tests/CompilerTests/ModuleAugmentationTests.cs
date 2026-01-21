using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for module augmentation and ambient declarations.
/// </summary>
public class ModuleAugmentationTests
{
    #region Parser Tests

    /// <summary>
    /// Tests that declare module 'path' { } is parsed correctly.
    /// </summary>
    [Fact]
    public void DeclareModule_ParsesCorrectly()
    {
        var source = """
            declare module 'lodash' {
                export function chunk<T>(arr: T[], size: number): T[][];
            }
            console.log("parsed");
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("parsed\n", output);
    }

    /// <summary>
    /// Tests that declare global { } is parsed correctly.
    /// </summary>
    [Fact]
    public void DeclareGlobal_ParsesCorrectly()
    {
        var source = """
            declare global {
                interface Array<T> {
                    customMethod(): T;
                }
            }
            console.log("parsed global");
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("parsed global\n", output);
    }

    #endregion

    #region Global Augmentation Tests

    /// <summary>
    /// Tests that declare global can define a new interface.
    /// </summary>
    [Fact]
    public void DeclareGlobal_DefinesNewInterface()
    {
        var source = """
            declare global {
                interface MyGlobalType {
                    value: number;
                }
            }

            function useGlobal(obj: MyGlobalType): number {
                return obj.value;
            }

            console.log(useGlobal({ value: 42 }));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    /// <summary>
    /// Tests that declare global with export works correctly.
    /// </summary>
    [Fact]
    public void DeclareGlobal_WithExport_DefinesInterface()
    {
        var source = """
            declare global {
                export interface CustomGlobal {
                    name: string;
                }
            }

            function getName(obj: CustomGlobal): string {
                return obj.name;
            }

            console.log(getName({ name: "test" }));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("test\n", output);
    }

    #endregion

    #region Ambient Module Declaration Tests

    /// <summary>
    /// Tests that ambient module declarations are type-only (no runtime effect).
    /// </summary>
    [Fact]
    public void AmbientModule_IsTypeOnly()
    {
        var source = """
            declare module 'some-package' {
                export interface Config {
                    debug: boolean;
                }
                export function configure(config: Config): void;
            }

            // Just verify parsing and type-checking works
            // The actual module doesn't exist, but the declaration provides types
            console.log("ambient declaration works");
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("ambient declaration works\n", output);
    }

    /// <summary>
    /// Tests ambient module with multiple declarations.
    /// </summary>
    [Fact]
    public void AmbientModule_MultipleDeclarations()
    {
        var source = """
            declare module 'my-lib' {
                export interface Options {
                    timeout: number;
                }
                export function init(opts: Options): void;
                export const VERSION: string;
            }

            console.log("multiple declarations work");
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("multiple declarations work\n", output);
    }

    #endregion

    #region Module Augmentation Tests

    /// <summary>
    /// Tests augmenting an existing module with a new interface.
    /// </summary>
    [Fact]
    public void ModuleAugmentation_AddsNewInterface()
    {
        var files = new Dictionary<string, string>
        {
            ["./config.ts"] = """
                export interface Config {
                    debug: boolean;
                }
                export const defaultConfig: Config = { debug: false };
                """,
            ["./main.ts"] = """
                import { Config, defaultConfig } from './config';

                declare module './config' {
                    interface ExtendedConfig {
                        verbose: boolean;
                    }
                }

                console.log(defaultConfig.debug);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("false\n", output);
    }

    #endregion

    #region Type Alias in Declare Block Tests

    /// <summary>
    /// Tests that type aliases can be declared in declare module blocks.
    /// </summary>
    [Fact]
    public void DeclareModule_WithTypeAlias()
    {
        var source = """
            declare module 'types' {
                export type ID = string | number;
                export type Callback<T> = (value: T) => void;
            }

            console.log("type alias works");
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("type alias works\n", output);
    }

    #endregion

    #region Interpreter Tests

    /// <summary>
    /// Tests that declare module is handled in the interpreter (type-only).
    /// </summary>
    [Fact]
    public void DeclareModule_Interpreter_IsNoOp()
    {
        var source = """
            declare module 'test' {
                export interface Test {
                    value: number;
                }
            }
            console.log("interpreter works");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("interpreter works\n", output);
    }

    /// <summary>
    /// Tests that declare global is handled in the interpreter (type-only).
    /// </summary>
    [Fact]
    public void DeclareGlobal_Interpreter_IsNoOp()
    {
        var source = """
            declare global {
                interface MyType {
                    name: string;
                }
            }
            console.log("global works");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("global works\n", output);
    }

    #endregion
}
