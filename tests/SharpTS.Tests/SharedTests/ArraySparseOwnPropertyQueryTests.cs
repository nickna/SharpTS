using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArraySparseOwnPropertyQueryTests
{
    public const string IssueSource = """
        const values: any[] = [];
        values[2147483648] = 4;
        values[4294967294] = 9;
        console.log(values[2147483648], Object.hasOwn(values, '2147483648'), Object.hasOwn(values, 2147483648));
        console.log(values[4294967294], Object.hasOwn(values, '4294967294'), Object.hasOwn(values, 4294967294));
        console.log(Object.hasOwn(values, '2147483649'), Object.hasOwn(values, 2147483649));
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["sparse-stored-indices-are-own-for-string-and-numeric-keys-issue", IssueSource, "4 true true\n9 true true\nfalse false\n"];
        yield return ["sparse-stored-index-has-descriptor-for-string-and-numeric-keys-issue", """
            const values: any[] = [];
            values[2147483648] = 4;
            const text = Object.getOwnPropertyDescriptor(values, '2147483648');
            const numeric = Object.getOwnPropertyDescriptor(values, 2147483648);
            console.log(text === undefined, text ? text.value : undefined);
            console.log(numeric === undefined, numeric ? numeric.value : undefined);
            """, "false 4\nfalse 4\n"];
        yield return ["queries-preserve-dense-holes-explicit-descriptors-names-and-arguments", """
            const values: any[] = [4];
            values.length = 3;
            values[2] = 8;
            const dense: any = Object.getOwnPropertyDescriptor(values, '0');
            console.log(dense.value, dense.writable, dense.enumerable, dense.configurable,
                Object.hasOwn(values, '1'), Object.getOwnPropertyDescriptor(values, '1') === undefined);
            let reads = 0;
            function getter() { reads++; return 9; }
            Object.defineProperty(values, '2147483648', { get: getter, enumerable: false, configurable: true });
            const accessor: any = Object.getOwnPropertyDescriptor(values, '2147483648');
            console.log(accessor.get === getter, accessor.set === undefined, accessor.enumerable,
                accessor.configurable, Object.hasOwn(values, '2147483648'), reads);
            Object.defineProperty(values, '02147483648', { value: 7, enumerable: true, configurable: true });
            const named: any = Object.getOwnPropertyDescriptor(values, '02147483648');
            console.log(named.value, Object.hasOwn(values, '02147483648'),
                Object.getOwnPropertyDescriptor(values, '2147483649') === undefined);
            const prototype = Object.create(Array.prototype);
            Object.defineProperty(prototype, '4294967294', { get: getter, configurable: true });
            Object.setPrototypeOf(values, prototype);
            console.log(Object.hasOwn(values, '4294967294'),
                Object.getOwnPropertyDescriptor(values, '4294967294') === undefined, reads);
            const lengthDescriptor: any = Object.getOwnPropertyDescriptor(values, 'length');
            console.log(lengthDescriptor.value, lengthDescriptor.writable, lengthDescriptor.enumerable, lengthDescriptor.configurable);
            function inspect(first: any, second: any) {
                const item: any = Object.getOwnPropertyDescriptor(arguments, '0');
                console.log(item.value, item.writable, item.enumerable, item.configurable,
                    Object.hasOwn(arguments, '1'), Object.getOwnPropertyDescriptor(arguments, '2') === undefined);
            }
            inspect(11, 12);
            """, "4 true true true false true\ntrue true false true true 0\n7 true true\nfalse true 0\n2147483649 true false false\n11 true true true true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void OwnQueriesPreserveSparseStoredIndicesAndDescriptorSemantics(
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
