using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class SymbolUnionEqualityTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["string-symbol-equality", """
            function isName(value: string | symbol) { return value === "name"; }
            console.log(isName("name"), isName("other"), isName(Symbol("name")));
            """ + "\n", "true false false\n"];
        yield return ["number-symbol-equality", """
            function isOne(value: number | symbol) { return value === 1; }
            console.log(isOne(1), isOne(2), isOne(Symbol("one")));
            """ + "\n", "true false false\n"];
        yield return ["contextual-ownkeys-filter", """
            console.log(Reflect.ownKeys({ name: 1 }).filter(key => key === "name").length);
            """ + "\n", "1\n"];
        yield return ["any-ownkeys-filter-control", """
            console.log(Reflect.ownKeys({ name: 1 }).filter((key: any) => key === "name").length);
            """ + "\n", "1\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void OverlappingSymbolUnionMembersRemainComparable(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        Assert.Empty(diagnostics);
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
}
