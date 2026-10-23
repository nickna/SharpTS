using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class StateMachineConstructorValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public-async-number-constructor", """
            console.log("entry", typeof Number);
            async function run() {
                const constructor: any = Number;
                console.log("async", typeof constructor);
            }
            run().catch((error: any) => console.log(error.name, error.message));
            """ + "\n", "entry function\nasync function\n"];
        yield return ["constructor-values-and-shadows", """
            console.log("entry", typeof Number);
            let entered = false;
            async function declaration(wait: Promise<number>) {
                const before: any = Number;
                entered = true;
                await wait;
                const after: any = Number;
                return typeof before + ":" + typeof after + ":" + (before === after);
            }
            const arrow = async () => {
                const before: any = Number;
                await Promise.resolve(0);
                const after: any = Number;
                return typeof before + ":" + typeof after + ":" + (before === after);
            };
            function* generated() {
                const before: any = Number;
                yield before;
                yield Number;
            }
            async function* asynchronous() {
                const before: any = Number;
                await Promise.resolve(0);
                yield before;
                yield Number;
            }
            const sentinel: any = { tag: "shadow" };
            async function parameterShadow(Number: any) {
                const before = Number;
                await Promise.resolve(0);
                return before === Number && Number === sentinel;
            }
            const localShadow = async () => {
                const Number: any = sentinel;
                await Promise.resolve(0);
                return Number === sentinel;
            };
            async function classShadow() {
                class Number {}
                const before: any = Number;
                await Promise.resolve(0);
                return before === Number && typeof Number === "function";
            }
            async function main() {
                let release: (value: number) => void = () => {};
                const pending = new Promise<number>(resolve => { release = resolve; });
                const operation = declaration(pending);
                console.log("pending", entered);
                release(7);
                console.log("declaration", await operation);
                console.log("arrow", await arrow());
                const iterator = generated();
                const first: any = iterator.next().value, second: any = iterator.next().value;
                console.log("generator", typeof first, first === Number, second === Number);
                const asyncIterator = asynchronous();
                const asyncFirst: any = (await asyncIterator.next()).value;
                const asyncSecond: any = (await asyncIterator.next()).value;
                console.log("async-generator", typeof asyncFirst, asyncFirst === Number, asyncSecond === Number);
                const objectValue: any = Object, functionValue: any = Function;
                const stringValue: any = String, booleanValue: any = Boolean;
                console.log("siblings", typeof objectValue, typeof functionValue, typeof stringValue, typeof booleanValue);
                console.log("shadows", await parameterShadow(sentinel), await localShadow(), await classShadow());
            }
            main().catch((error: any) => console.log("unexpected", error.name, error.message));
            """ + "\n", "entry function\npending true\ndeclaration function:function:true\narrow function:function:true\ngenerator function true true\nasync-generator function true true\nsiblings function function function function\nshadows true true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ConstructorValuesRespectLexicalBindingsAcrossSuspension(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
