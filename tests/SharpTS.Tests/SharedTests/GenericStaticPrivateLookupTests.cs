using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericStaticPrivateLookupTests
{
    // New controls for #1904. The historical note omitted its original source.
    [Theory, ModeData]
    public void GenericConstructorResolvesItsStaticPrivateFieldsAndMethods(ExecutionMode mode)
    {
        const string source = """
            class Box<T> {
                static #value: number = 1;
                static #read() { return Box.#value; }
                static read() { return Box.#value; }
                static call() { return Box.#read(); }
                static write(value: number) { Box.#value = value; }
                readFromInstance() { return Box.#read(); }
            }
            console.log(Box.read());
            console.log(Box.call());
            Box.write(7);
            console.log(Box.read());
            console.log(Box.call());
            console.log(new Box<number>().readFromInstance());
            console.log(new Box<string>().readFromInstance());
            """;
        Assert.Equal("1\n1\n7\n7\n7\n7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NonGenericStaticPrivateLookupKeepsExistingBehavior(ExecutionMode mode)
    {
        const string source = """
            class Box {
                static #value: number = 1;
                static #read() { return Box.#value; }
                static read() { return Box.#read(); }
                static write(value: number) { Box.#value = value; }
            }
            console.log(Box.read());
            Box.write(7);
            console.log(Box.read());
            """;
        Assert.Equal("1\n7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GenericStaticPrivateNamesSurviveClosuresAndSuspension(ExecutionMode mode)
    {
        const string source = """
            class Box<T> {
                static #value: number = 7;
                static #read() { return Box.#value; }
                static closure() { const read = () => Box.#read(); return read(); }
                static async read() { await Promise.resolve(0); return Box.#read(); }
                static *values() { yield Box.#read(); }
                static async *asyncValues() { await Promise.resolve(0); yield Box.#read(); }
                static async write() { Box.#value = await Promise.resolve(9); return Box.#value; }
            }
            console.log(Box.closure());
            console.log(Box.values().next().value);
            async function main() {
                console.log(await Box.read());
                console.log((await Box.asyncValues().next()).value);
                console.log(await Box.write());
            }
            main();
            """;
        Assert.Equal("7\n7\n7\n7\n9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ModuleLocalGenericOwnersKeepIndependentStaticPrivateState(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["a.ts"] = "export class Box<T> { static #value: number = 1; static #read() { return Box.#value; } static read() { return Box.#read(); } static write() { Box.#value = 7; } }",
            ["b.ts"] = "export class Box<T> { static #value: number = 2; static #read() { return Box.#value; } static read() { return Box.#read(); } }",
            ["main.ts"] = "import { Box as A } from './a'; import { Box as B } from './b'; console.log(A.read()); console.log(B.read()); A.write(); console.log(A.read()); console.log(B.read());"
        };
        if (mode == ExecutionMode.Compiled)
            Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("1\n2\n7\n2\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void GenericConstructorAliasesRetainStaticPrivateDeclaration(ExecutionMode mode)
    {
        const string source = """
            class Box<T> {
                #instance = 0;
                static #value: number = 1;
                static #read() { return Box.#value; }
                static read() {
                    const alias = Box;
                    alias.#value = 7;
                    return alias.#read();
                }
            }
            console.log(Box.read());
            """;
        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("class Box<T> { static #value: number = 1; static write() { Box.#value = 'bad'; } }", "TS2322")]
    [InlineData("class Box<T> { static #read(value: number) { return value; } static read() { return Box.#read('bad'); } }", "TS2345")]
    [InlineData("class Box<T> { static #read(value: number) { return value; } static read() { return Box.#read(); } }", "TS2554")]
    [InlineData("class Box<T> { static #value: number = 1; } console.log(Box.#value);", "TS18013")]
    [InlineData("class Box<T> { static #value: number = 1; } class Derived<T> extends Box<T> { static read() { return Derived.#value; } }", "TS2339")]
    [InlineData("class Other<T> { static #value: number = 2; } class Box<T> { static #value: number = 1; static read() { return Other.#value; } }", "TS2339")]
    public void GenericLookupPreservesTypeArityAndLexicalAccessChecks(string source, string code)
    {
        var error = Assert.Throws<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }
}
