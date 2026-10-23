using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class NamedExpressionDefaultSelfTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-direct", """
            const target: any = function privateSelf(value: any = privateSelf) { return value; };
            console.log(target() === target);
            """ + "\n", "true\n"];
        yield return ["ordinary-closure", """
            const target: any = function privateSelf(read: any = () => privateSelf) { return read(); };
            console.log(target() === target);
            """ + "\n", "true\n"];
        yield return ["async-direct", """
            const target: any = async function privateSelf(value: any = privateSelf) { return value; };
            target().then((value: any) => console.log(value === target));
            """ + "\n", "true\n"];
        yield return ["async-closure", """
            const target: any = async function privateSelf(read: any = () => privateSelf) { return read(); };
            target().then((value: any) => console.log(value === target));
            """ + "\n", "true\n"];
        yield return ["anonymous-earlier-outer", """
            const outer: any = { tag: 7 };
            const ordinary: any = function(value: any = outer) { return value; };
            const asynchronous: any = async function(value: any = outer) { return value; };
            console.log(ordinary() === outer);
            asynchronous().then((value: any) => console.log(value === outer));
            """ + "\n", "true\ntrue\n"];
        yield return ["named-earlier-outer", """
            const outer: any = { tag: 7 };
            const ordinary: any = function ordinaryName(value: any = outer) { return value; };
            const asynchronous: any = async function asyncName(value: any = outer) { return value; };
            console.log(ordinary() === outer);
            asynchronous().then((value: any) => console.log(value === outer));
            """ + "\n", "true\ntrue\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void PrivateNamesRemainVisibleDuringDefaultInitialization(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        var checkedSource = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source));
        Assert.Empty(checkedSource.Diagnostics);
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
