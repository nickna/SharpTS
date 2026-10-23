using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class NamespaceMethodDescriptorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public-namespace-reflection", """
            const math: any = Math;
            const max = Object.getOwnPropertyDescriptor(math, "max")!;
            try {
                console.log("Math", delete math.max, math.max === undefined,
                    Object.hasOwn(math, "max"), Object.getOwnPropertyDescriptor(math, "max") === undefined);
            } finally {
                Object.defineProperty(math, "max", max);
            }
            const json: any = JSON;
            const parse = Object.getOwnPropertyDescriptor(json, "parse")!;
            try {
                console.log("JSON", delete json.parse, json.parse === undefined,
                    Object.hasOwn(json, "parse"), Object.getOwnPropertyDescriptor(json, "parse") === undefined);
            } finally {
                Object.defineProperty(json, "parse", parse);
            }
            """ + "\n", "Math true true false true\nJSON true true false true\n"];
        yield return ["json-own-storage-lifecycle", """
            const target: any = JSON, key = "parse";
            const original = Object.getOwnPropertyDescriptor(target, key)!;
            try {
                console.log("deleted", delete target.parse, target.parse === undefined, Object.hasOwn(target, key));
                Object.defineProperty(target, key, {
                    value: undefined, writable: true, enumerable: false, configurable: true
                });
                console.log("undefined", Object.hasOwn(target, key), target.parse === undefined);
                console.log("indexed", delete target[key], target[key] === undefined, Object.hasOwn(target, key));
                Object.defineProperty(target, key, {
                    value: 41, writable: true, enumerable: false, configurable: true
                });
                console.log("data", Object.hasOwn(target, key), target[key],
                    Object.getOwnPropertyDescriptor(target, key)!.value === 41);
            } finally {
                Object.defineProperty(target, key, original);
            }
            console.log("restored", Object.hasOwn(target, key), target.parse === original.value);
            """ + "\n", "deleted true true false\nundefined true true\nindexed true true false\ndata true 41 true\nrestored true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void NativeNamespaceDescriptorsFollowCurrentMethodStorage(
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
