using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for module augmentation and ambient declarations.
/// Runs against both interpreter and compiler.
/// </summary>
public class ModuleAugmentationTests
{
    #region Parser Tests

    [Theory, ModeData]
    public void DeclareModule_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            declare module 'lodash' {
                export function chunk<T>(arr: T[], size: number): T[][];
            }
            console.log("parsed");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("parsed\n", output);
    }

    [Theory, ModeData]
    public void DeclareGlobal_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            declare global {
                interface Array<T> {
                    customMethod(): T;
                }
            }
            console.log("parsed global");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("parsed global\n", output);
    }

    #endregion

    #region Global Augmentation Tests

    [Theory, ModeData]
    public void DeclareGlobal_DefinesNewInterface(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void DeclareGlobal_WithExport_DefinesInterface(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    #endregion

    #region Ambient Module Declaration Tests

    [Theory, ModeData]
    public void BodylessAmbientModule_ParsesAsUntypedPackage(ExecutionMode mode)
    {
        var source = "declare module 'untyped-package';\nconsole.log('parsed');";

        var output = TestHarness.Run(source, mode);

        Assert.Equal("parsed\n", output);
    }

    [Theory, ModeData]
    public void AmbientModule_IsTypeOnly(ExecutionMode mode)
    {
        var source = """
            declare module 'some-package' {
                export interface Config {
                    debug: boolean;
                }
                export function configure(config: Config): void;
            }

            console.log("ambient declaration works");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("ambient declaration works\n", output);
    }

    [Theory, ModeData]
    public void AmbientModule_MultipleDeclarations(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("multiple declarations work\n", output);
    }

    #endregion

    #region Module Augmentation Tests

    [Theory, ModeData]
    public void ModuleAugmentation_AddsNewInterface(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("false\n", output);
    }

    #endregion

    #region Type Alias in Declare Block Tests

    [Theory, ModeData]
    public void DeclareModule_WithTypeAlias(ExecutionMode mode)
    {
        var source = """
            declare module 'types' {
                export type ID = string | number;
                export type Callback<T> = (value: T) => void;
            }

            console.log("type alias works");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("type alias works\n", output);
    }

    #endregion
}
