using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ReflectOrdinaryObjectPrototypeTests
{
    public const string IssueSource = """
        const value = {};
        console.log(Object.getPrototypeOf(value) === Object.prototype,
            Reflect.getPrototypeOf(value) === Object.prototype, Reflect.getPrototypeOf(value) === null);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-literal-implicit-default", IssueSource, "true true false\n"];
        yield return ["distinct-literals-share-the-default-prototype", """
            const first = {}, second = { slot: 1 }, third = { slot: 2 };
            console.log(first !== second, second !== third);
            const a = Reflect.getPrototypeOf(first), b = Reflect.getPrototypeOf(second), c = Reflect.getPrototypeOf(third);
            console.log(a === Object.prototype, b === Object.prototype, c === Object.prototype,
                a === b, b === c, Object.getPrototypeOf(second) === b);
            console.log(Reflect.getPrototypeOf(Object.prototype) === null, Object.getPrototypeOf(Object.prototype) === null);
            """, "true true\ntrue true true true true true\ntrue true\n"];
        yield return ["explicit-null-roots-remain-null", """
            const created = Object.create(null), changed = { slot: 1 };
            Object.setPrototypeOf(changed, null);
            console.log(Reflect.getPrototypeOf(created) === null, Object.getPrototypeOf(created) === null);
            console.log(Reflect.getPrototypeOf(changed) === null, Object.getPrototypeOf(changed) === null);
            console.log(Reflect.getPrototypeOf({}) === Object.prototype, Object.getPrototypeOf({}) === Object.prototype);
            """, "true true\ntrue true\ntrue true\n"];
        yield return ["custom-parent-replacement-and-default-restoration", """
            const first = { marker: 1 }, second = { marker: 2 }, target = { own: 3 };
            Object.setPrototypeOf(target, first);
            console.log(Reflect.getPrototypeOf(target) === first, Object.getPrototypeOf(target) === first);
            Object.setPrototypeOf(target, second);
            console.log(Reflect.getPrototypeOf(target) === second, Object.getPrototypeOf(target) === second);
            const created = Object.create(first);
            console.log(Reflect.getPrototypeOf(created) === first, Object.getPrototypeOf(created) === first);
            Object.setPrototypeOf(target, Object.prototype);
            console.log(Reflect.getPrototypeOf(target) === Object.prototype, Object.getPrototypeOf(target) === Object.prototype);
            """, "true true\ntrue true\ntrue true\ntrue true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ReflectUsesTheActualOrdinaryObjectPrototype(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
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
