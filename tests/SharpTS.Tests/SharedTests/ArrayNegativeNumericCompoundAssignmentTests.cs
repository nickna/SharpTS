using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArrayNegativeNumericCompoundAssignmentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public", """
            const values: any[] = [1];
            Object.defineProperty(values, "length", { writable: false });
            const result = values[-1] += 2;
            console.log("sloppy", Number.isNaN(result), values.length, Object.hasOwn(values, "-1"));
            function strictCase() {
                "use strict";
                const values: any[] = [1];
                Object.defineProperty(values, "length", { writable: false });
                try {
                    const result = values[-1] += 2;
                    console.log("strict", Number.isNaN(result), values.length, Object.hasOwn(values, "-1"));
                } catch (error) {
                    console.log("strict", error instanceof TypeError, values.length, Object.hasOwn(values, "-1"));
                }
            }
            strictCase();
            """.ReplaceLineEndings("\n") + "\n", "sloppy true 1 true\nstrict true 1 true\n"];
        yield return ["negative-property-reference-order-and-descriptor", """
            function run() {
                "use strict";
                const trace: string[] = [];
                const values: any[] = [1], other: any[] = [20];
                let stored: any = { valueOf() { trace.push("left"); return 4; } };
                let same = true;
                Object.defineProperty(values, "-1", {
                    get() { trace.push("get"); same = same && this === values; return stored; },
                    set(next: any) { trace.push("set"); same = same && this === values; stored = next; },
                    enumerable: true, configurable: true
                });
                Object.defineProperty(values, "length", { writable: false });
                let selected = values;
                function receiver(): any[] { trace.push("receiver"); return selected; }
                function key(): number { trace.push("key"); return -1; }
                function rhs(): any {
                    trace.push("rhs"); selected = other;
                    return { valueOf() { trace.push("right"); return 3; } };
                }
                const result = (receiver()[key()] += rhs());
                const descriptor = Object.getOwnPropertyDescriptor(values, "-1")!;
                console.log(result, stored, same, values.length, other.length,
                    Object.hasOwn(values, "-1"), Object.hasOwn(other, "-1"), trace.join(","));
                console.log(typeof descriptor.get, typeof descriptor.set, descriptor.enumerable, descriptor.configurable);
            }
            run();
            """.ReplaceLineEndings("\n") + "\n", "7 7 true 1 1 true false receiver,key,get,rhs,left,right,set\nfunction function true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void NegativeNumericKeysUseOrdinaryArrayProperties(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        AssertStrictSource(source);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        }
    }

    internal static void AssertStrictSource(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), "array-negative-numeric-compound-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }
}
