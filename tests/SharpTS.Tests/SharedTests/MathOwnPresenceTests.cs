using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class MathOwnPresenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public-math-own-presence", """
            const target: any = Math;
            const original = Object.getOwnPropertyDescriptor(target, "max")!;
            try {
                console.log(delete target.max, target.max === undefined, Object.hasOwn(target, "max"));
            } finally { Object.defineProperty(target, "max", original); }
            """ + "\n", "true true false\n"];
        yield return ["math-own-presence-after-deletion-and-recreation", """
            const target: any = Math;
            const original = Object.getOwnPropertyDescriptor(target, "max")!;
            try {
                console.log("initial", Object.hasOwn(target, "max"), typeof target.max,
                    Object.hasOwn(target, "PI"), Object.hasOwn(target, "abs"));
                console.log("delete", delete target.max, target.max === undefined,
                    Object.hasOwn(target, "max"), Object.hasOwn(target, "abs"));
                Object.defineProperty(target, "max", {
                    value: 41, writable: true, enumerable: false, configurable: true
                });
                console.log("data", Object.hasOwn(target, "max"), target.max,
                    Object.getOwnPropertyDescriptor(target, "max")!.value === 41);
                console.log("redelete", delete target.max, target.max === undefined,
                    Object.hasOwn(target, "max"));

                let calls = 0;
                Object.defineProperty(target, "max", {
                    get() { calls++; return 9; }, configurable: true
                });
                console.log("accessor-own", Object.hasOwn(target, "max"), calls);
                console.log("accessor-read", target.max, calls);
                Object.defineProperty(target, "max", {
                    get: undefined, set: undefined, configurable: true
                });
                console.log("getterless", Object.hasOwn(target, "max"), target.max === undefined, calls);
                const key = "max";
                console.log("indexed-delete", delete target[key], target[key] === undefined,
                    Object.hasOwn(target, key));

                Object.defineProperty(target, "max", {
                    value: undefined, writable: true, enumerable: false, configurable: true
                });
                console.log("undefined-own", Object.hasOwn(target, "max"), target.max === undefined);
                console.log("undefined-delete", delete target.max, target.max === undefined,
                    Object.hasOwn(target, "max"));
            } finally {
                Object.defineProperty(target, "max", original);
            }
            console.log("restored", Object.hasOwn(target, "max"), target.max === original.value,
                Object.hasOwn(target, "PI"), Object.hasOwn(target, "abs"));
            const record: any = { max: 3 };
            console.log("ordinary", delete record.max, record.max === undefined, Object.hasOwn(record, "max"));
            """ + "\n", "initial true function true true\ndelete true true false true\ndata true 41 true\nredelete true true false\naccessor-own true 0\naccessor-read 9 1\ngetterless true true 1\nindexed-delete true true false\nundefined-own true true\nundefined-delete true true false\nrestored true true true true\nordinary true true false\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void OwnPresenceTracksActualMathProperties(
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
