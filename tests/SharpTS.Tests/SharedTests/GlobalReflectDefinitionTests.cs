using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class GlobalReflectDefinitionTests
{
    public static IEnumerable<object[]> Cases()
    {
        var original = GlobalParseIntBindingTests.Cases().Single(row =>
            (string)row[0] == "reflect-and-map-descriptor-installs-share-the-global-binding");
        // Historical public source is the entire unchanged old program plus its final CRLF.
        yield return ["public-reflect-global-definition", (string)original[1] + "\r\n", original[2]];
        yield return ["global-reflect-descriptor-identity-lifecycle", """
            const target: any = globalThis, key = "parseInt";
            const original = Object.getOwnPropertyDescriptor(target, key)!;
            const replacement = (_text: string, _radix?: number) => 41;
            let reads = 0, receiver = true;
            try {
                const data = Reflect.defineProperty(target, key, {
                    value: replacement, writable: true, enumerable: false, configurable: true
                });
                console.log("data", data, parseInt === replacement, parseInt("7", 10),
                    Object.getOwnPropertyDescriptor(target, key)!.value === replacement);
                const attributes = Reflect.defineProperty(target, key, { enumerable: true });
                const updated = Object.getOwnPropertyDescriptor(target, key)!;
                console.log("attributes", attributes, updated.writable, updated.enumerable,
                    updated.configurable, updated.value === replacement);
                const getter = function(this: any) { reads++; receiver = receiver && this === target; return replacement; };
                const accessor = Reflect.defineProperty(target, key, {
                    get: getter, set: undefined, enumerable: false, configurable: true
                });
                const descriptor = Object.getOwnPropertyDescriptor(target, key)!;
                console.log("accessor", accessor, descriptor.get === getter, descriptor.set === undefined, reads);
                const held = parseInt;
                console.log("read", held === replacement, held("8", 10), reads, receiver);
                const cleared = Reflect.defineProperty(target, key, {
                    value: undefined, writable: true, enumerable: false, configurable: true
                });
                console.log("undefined", cleared, parseInt === undefined,
                    Object.getOwnPropertyDescriptor(target, key)!.value === undefined);
                let abrupt = false;
                const token = new Error("descriptor");
                const throwing: any = { get value() { throw token; } };
                try { Reflect.defineProperty(target, key, throwing); }
                catch (error) { abrupt = error === token; }
                console.log("abrupt", abrupt, parseInt === undefined);
            } finally {
                Object.defineProperty(target, key, original);
            }
            console.log("restored", parseInt("12", 10), Object.getOwnPropertyDescriptor(target, key)!.value === original.value);
            const ordinary: any = {};
            Object.defineProperty(ordinary, "locked", { value: 3, writable: false, configurable: false });
            console.log("refused", Reflect.defineProperty(ordinary, "locked", { value: 4 }), ordinary.locked);
            """ + "\n", "data true true 41 true\nattributes true true true true true\naccessor true true true 0\nread true 41 1 true\nundefined true true true\nabrupt true true\nrestored 12 true\nrefused false 3\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ReflectDefinitionsShareTheGlobalBinding(
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
