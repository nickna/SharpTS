using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class MathDescriptorRecreationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public-descriptor-recreation", """
            for (const pair of [[Math, "max"], [JSON, "parse"]] as const) {
                const target: any = pair[0], key = pair[1];
                const original = Object.getOwnPropertyDescriptor(target, key)!;
                try {
                    delete target[key];
                    Object.defineProperty(target, key, {
                        value: 41, writable: true, enumerable: false, configurable: true
                    });
                    console.log(key, target[key], Object.getOwnPropertyDescriptor(target, key) !== undefined);
                } finally {
                    Object.defineProperty(target, key, original);
                }
            }
            """ + "\n", "max 41 true\nparse 41 true\n"];
        yield return ["descriptor-recreation-lifecycle", """
            const target: any = Math, key = "max";
            const original = Object.getOwnPropertyDescriptor(target, key)!;
            let reads = 0;
            const getter = () => { reads++; return 7; };
            try {
                delete target[key];
                Object.defineProperty(target, key, { get: getter, enumerable: false, configurable: true });
                const accessor = Object.getOwnPropertyDescriptor(target, key);
                console.log("accessor", accessor !== undefined, accessor?.get === getter, reads);
                console.log("read", target[key], reads);
                console.log("delete", delete target[key], Object.getOwnPropertyDescriptor(target, key) === undefined);
                Object.defineProperty(target, key, {
                    value: undefined, writable: true, enumerable: false, configurable: true
                });
                const data = Object.getOwnPropertyDescriptor(target, key);
                console.log("undefined", data !== undefined, data?.value === undefined,
                    data?.writable === true, data?.enumerable === false, data?.configurable === true);
                console.log("delete", delete target[key], Object.getOwnPropertyDescriptor(target, key) === undefined);
            } finally {
                Object.defineProperty(target, key, original);
            }
            const restored = Object.getOwnPropertyDescriptor(target, key);
            console.log("restored", restored !== undefined, restored?.value === original.value, target[key] === original.value);
            """ + "\n", "accessor true true 0\nread 7 1\ndelete true true\nundefined true true true true true\ndelete true true\nrestored true true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void RecreatedMathPropertiesExposeTheirStoredDescriptors(
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
