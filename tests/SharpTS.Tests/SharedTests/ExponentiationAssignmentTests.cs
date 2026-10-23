using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ExponentiationAssignmentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public", """
            let value = 2;
            value **= 3;
            console.log(value);
            """.ReplaceLineEndings("\n") + "\n", "8\n"];
        yield return ["reference-order-conversion-and-result", """
            const trace: string[] = [];
            let stored: any = { valueOf() { trace.push("left"); return 2; } };
            const holder: any = {
                get value() { trace.push("get"); return stored; },
                set value(next: any) { trace.push("set"); stored = next; }
            };
            const elements: any[] = [];
            Object.defineProperty(elements, "0", {
                get() { return holder.value; },
                set(next: any) { holder.value = next; },
                configurable: true
            });
            function receiver(): any[] { trace.push("receiver"); return elements; }
            function key(): number { trace.push("key"); return 0; }
            function dottedReceiver(): any { trace.push("receiver"); return holder; }
            function rhs(): any {
                trace.push("rhs");
                return { valueOf() { trace.push("right"); return 3; } };
            }
            const indexed = (receiver()[key()] **= rhs());
            console.log(indexed, stored, trace.join(","));
            trace.length = 0;
            stored = 2;
            const dotted = (dottedReceiver().value **= 3);
            console.log(dotted, stored, trace.join(","));
            """.ReplaceLineEndings("\n") + "\n", "8 8 receiver,key,get,rhs,left,right,set\n8 8 receiver,get,set\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void NumericExponentiationAssignmentsPreserveReferenceSemantics(
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
        var path = Path.Combine(Path.GetTempPath(), "exponentiation-assignment-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }
}
