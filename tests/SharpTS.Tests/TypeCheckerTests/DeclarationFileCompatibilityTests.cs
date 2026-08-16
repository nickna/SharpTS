using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class DeclarationFileCompatibilityTests
{
    [Fact]
    public void ParsesDestructuredParametersInFunctionTypes()
    {
        const string source = """
            interface Options { enabled?: boolean; }
            type Select<T, K> = T;
            export declare const configure:
                (name: string, { enabled, }?: Select<Options, "enabled">) => string;
            """;

        var parsed = new Parser(new Lexer(source).ScanTokens())
            .AsDeclarationFile()
            .Parse();

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
    }

    [Fact]
    public void DuplicateAmbientProperties_DoNotCrashInterfacePreregistration()
    {
        const string source = """
            interface PropertyDescriptor {
                configurable?: boolean;
                configurable?: boolean;
            }
            """;

        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();

        Assert.True(parsed.IsSuccess);
        var exception = Record.Exception(
            () => new TypeChecker(maxErrors: 50).CheckWithRecovery(parsed.Statements));
        Assert.Null(exception);
    }

    [Fact]
    public void TypeAndValueFacets_WithSameName_DoNotOverwriteEachOther()
    {
        const string source = """
            interface Error { message: string; }
            interface ErrorConstructor { new(message?: string): Error; }
            declare var Error: ErrorConstructor;
            interface RangeError extends Error { range: number; }
            const value: RangeError = { message: "bad", range: 1 };
            """;

        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();
        var result = new TypeChecker(maxErrors: 50).CheckWithRecovery(parsed.Statements);

        Assert.True(parsed.IsSuccess);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Parser_AcceptsDeclarationLibrarySyntax()
    {
        const string source = """
            interface Context<
                T extends abstract new (...args: any) => any = abstract new (...args: any) => any,
            > {
                get value(): T;
                set value(next: T);
                readonly sentinel: -1;
                get?(): unknown;
            }
            """;

        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
    }

    [Fact]
    public void Parser_AcceptsAmbientNamespaceConstants()
    {
        const string source = """
            declare namespace Intl {
                const PluralRules: PluralRulesConstructor;
                function getCanonicalLocales(locales: string): string[];
            }
            """;

        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
    }

    [Fact]
    public void Parser_AcceptsGlobalAugmentationInsideAmbientModule()
    {
        const string source = """
            declare module "node:buffer" {
                global {
                    interface BufferConstructor {
                        new(value: string): Uint8Array;
                    }
                }
            }
            """;

        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
    }

    [Fact]
    public void Parser_AcceptsAmbientFunctionAsiAndImportTypeQuery()
    {
        const string source = """
            declare function install(): void
            export { install }
            declare namespace Library {
                const Dispatcher: typeof import("./dispatcher").default
            }
            declare class Dispatcher {
                connect<T>(value: T): Promise<T>
                entries: () => Iterator<[string, string]>
                [Symbol.iterator]: () => Iterator<[string, string]>
                readonly [Symbol.toStringTag]: string
                readonly delete: (name: string) => void
                _construct?(callback: (error?: Error | null) => void): void
            }
            type Headers = Record<
                | "accept"
                | "content-type",
                string
            >
            type PullArgs = [...transforms: Transform[], options: PullOptions]
            type FrameError = [type: number, code: number, id: number]
            interface ServerOptions<
                Request extends typeof Incoming = typeof Incoming,
                Response extends typeof Server<InstanceType<Request>> = typeof Server,
            > {}
            interface RequireExtensions
                extends Dict<(module: Module, filename: string) => any> {
                ".js": (module: Module, filename: string) => any
            }
            interface Disposable {
                register<T extends object>(
                    ref: T,
                    callback: (ref: T, event: "exit") => void
                ): void;
            }
            interface ProcessLike {
                finalization: {
                    register<T extends object>(
                        ref: T,
                        callback: (ref: T, event: "exit") => void
                    ): void;
                }
            }
            declare namespace Timers {
                const promisify: () => void
                export { promisify }
            }
            declare module "node:assert" {
                import strict = require("node:assert/strict")
                import inspectAlias = assert
                import { AssertionError } from "node:assert"
                function assert(value: unknown): asserts value
                export { type AssertionError }
                export import globalAssert = globalThis.assert
                export = assert
            }
            declare module "assert" {
                import module = require("node:module")
                export * from "node:assert"
            }
            """;

        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
    }

    [Fact]
    public void Parser_DeclarationFileModeMakesExportedClassesAmbient()
    {
        const string source = """
            export class Client {
                constructor(url: string)
                readonly closed: boolean
            }
            export const version: string
            """;

        var parsed = new Parser(new Lexer(source).ScanTokens())
            .AsDeclarationFile()
            .Parse();

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
    }

    [Theory]
    [InlineData("export as namespace Library;")]
    [InlineData("export * as ns from './types';")]
    [InlineData("export * as default from './types';")]
    public void Parser_AcceptsDeclarationModuleExportForms(string source)
    {
        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
    }

    [Theory]
    [InlineData("""const response = await fetch(new URL("../x", import.meta.url).toString());""")]
    [InlineData("""(async () => {})();""")]
    [InlineData("""(async () => { const response = await fetch(new URL("../x", import.meta.url).toString()); })();""")]
    [InlineData("""const { async: async64, value: value64 } = Atomics.waitAsync(int64, 0, BigInt(0));""")]
    [InlineData("""
        const { async, value } = Atomics.waitAsync(int32, 0, 0);
        const { async: async64, value: value64 } = Atomics.waitAsync(int64, 0, BigInt(0));
        """)]
    [InlineData("""
        const sab = new SharedArrayBuffer(Int32Array.BYTES_PER_ELEMENT * 1024);
        const int32 = new Int32Array(sab);
        const sab64 = new SharedArrayBuffer(BigInt64Array.BYTES_PER_ELEMENT * 1024);
        const int64 = new BigInt64Array(sab64);
        const waitValue = Atomics.wait(int32, 0, 0);
        const { async, value } = Atomics.waitAsync(int32, 0, 0);
        const { async: async64, value: value64 } = Atomics.waitAsync(int64, 0, BigInt(0));

        const main = async () => {
            if (async) await value;
            if (async64) await value64;
        }
        main();
        """)]
    public void Parser_AcceptsModernDeclarationConsumerExpressions(string source)
    {
        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
    }
}
