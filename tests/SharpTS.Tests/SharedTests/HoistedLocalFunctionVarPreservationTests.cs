using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

// Reference TypeScript reports TS2300 for the annotated function/var merges;
// SharpTS strict checking also reports TS2454 for the initializer-free forms.
// These Node-accepted runtime regressions use SharpTS's ordinary checked profile.
[Collection("CompilationService")]
public sealed class HoistedLocalFunctionVarPreservationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["initializer-free-var-preserves-hoisted-function", """
            function run() {
                const before = helper;
                console.log(helper(), typeof helper);
                var helper: () => string;
                console.log(helper(), helper === before);
                function helper(): string { return "function"; }
            }
            run();
            """, "function function\nfunction true\n"];
        yield return ["named-function-without-var-control", """
            function run() {
                const before = helper;
                console.log(helper(), typeof helper);
                console.log(helper(), helper === before);
                function helper(): string { return "function"; }
            }
            run();
            """, "function function\nfunction true\n"];
        yield return ["explicit-initializer-still-replaces-hoisted-function", """
            function run() {
                const before = helper;
                console.log(helper(), typeof helper);
                var helper: () => string = () => "replacement";
                console.log(helper(), helper === before);
                function helper(): string { return "function"; }
            }
            run();
            """, "function function\nreplacement false\n"];
        yield return ["repeated-initializer-free-var-preserves-hoisted-function", """
            function run() {
                const before = helper;
                console.log(helper(), typeof helper);
                var helper: () => string;
                var helper: () => string;
                console.log(helper(), helper === before);
                function helper(): string { return "function"; }
            }
            run();
            """, "function function\nfunction true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void InitializerFreeVarsPreserveHoistedLocalFunctions(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        new TypeChecker().Check(TestHarness.ParseOrThrow(source));
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
