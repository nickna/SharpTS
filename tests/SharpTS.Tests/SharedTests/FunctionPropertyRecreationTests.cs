using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class FunctionPropertyRecreationTests
{
    public const string IssueSource = """
        function original() {}
        const target: any = original;
        Object.defineProperty(target, 'slot', { value: 1, writable: true, enumerable: true, configurable: true });
        console.log(delete target.slot, Object.hasOwn(target, 'slot'), Object.getOwnPropertyDescriptor(target, 'slot') === undefined);
        Object.defineProperty(target, 'slot', { value: 2, writable: true, enumerable: true, configurable: true });
        console.log(Object.hasOwn(target, 'slot'), Object.prototype.hasOwnProperty.call(target, 'slot'),
            target.slot, Object.getOwnPropertyDescriptor(target, 'slot')!.value, 'slot' in target,
            Object.prototype.propertyIsEnumerable.call(target, 'slot'));
        """;

    public static IEnumerable<object[]> Cases()
    {
        const string expected = "true false true\ntrue true 2 2 true true\n";
        yield return ["original-declaration-custom-property", IssueSource, expected];
        yield return ["function-expression-custom-property",
            IssueSource.Replace("function original() {}", "const original = function() {};"), expected];
        yield return ["arrow-custom-property",
            IssueSource.Replace("function original() {}", "const original = () => {};"), expected];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void RecreatedFunctionPropertiesUseTheirCurrentOwnDescriptors(
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
