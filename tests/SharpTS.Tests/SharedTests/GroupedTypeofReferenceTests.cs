using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class GroupedTypeofReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        var original0 = GlobalParseIntBindingTests.Cases().Single(row =>
            (string)row[0] == "deleted-binding-throws-before-call-arguments-with-lexical-shadows");
        yield return ["public-grouped-typeof-deleted-global", (string)original0[1] + "\r\n", original0[2]];
        var original1 = GlobalParseIntBindingTests.Cases().Single(row =>
            (string)row[0] == "deleted-binding-typeof-and-read-after-await-in-both-async-forms");
        yield return ["grouped-typeof-after-await-in-both-async-forms", (string)original1[1] + "\r\n", original1[2]];
        yield return ["grouped-typeof-keeps-getter-and-call-effects", """
            const target: any = globalThis;
            const original = Object.getOwnPropertyDescriptor(target, "parseInt")!;
            let reads = 0;
            function check(label: string, operation: () => any) {
                let caught = false;
                try { operation(); }
                catch { caught = true; }
                console.log(label, caught, reads);
            }
            try {
                Object.defineProperty(target, "parseInt", {
                    configurable: true, get() { reads++; return (null as any).value; }
                });
                check("bare", () => typeof parseInt);
                check("grouped", () => typeof (((parseInt))));
            } finally {
                Object.defineProperty(target, "parseInt", original);
            }
            let getterReads = 0, calls = 0;
            const object: any = { get value() { getterReads++; return 3; } };
            function value() { calls++; return "x"; }
            console.log("ordinary", typeof ((object.value)), getterReads, typeof (((value()))), calls);
            console.log("restored", parseInt("12", 10));
            """ + "\n", "bare true 1\ngrouped true 2\nordinary number 1 string 1\nrestored 12\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void TypeofPreservesGroupedIdentifierReferences(
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
