using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArrayLargeCanonicalStringIndexReadTests
{
    public const string IssueSource = """
        const values: any[] = [];
        values[2147483648] = 4;
        const key: any = '2147483648';
        console.log(values[key], values[2147483648]);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["large-canonical-string-read-issue", IssueSource, "4 4\n"];
        yield return ["small-boundary-upper-indices-and-holes-retain-numeric-string-parity", """
            const values: any[] = [];
            values[0] = 7;
            values[2147483647] = 8;
            values[2147483648] = 4;
            values[4294967294] = 9;
            const boundary: any = '2147483647', large: any = '2147483648', upper: any = '4294967294';
            const hole: any = '2147483649';
            console.log(values['0'], values[boundary], values[2147483647], values[large], values[2147483648],
                values[upper], values[4294967294], values[hole], values[2147483649], values.length);
            console.log(Object.hasOwn(values, large), Object.hasOwn(values, upper),
                Object.hasOwn(values, hole), Object.hasOwn(values, '1'));
            """ + "\r\n", "7 8 8 4 4 9 9 undefined undefined 4294967295\ntrue true false false\n"];
        yield return ["noncanonical-names-do-not-alias-stored-elements-and-remain-ordinary-properties", """
            const values: any[] = [];
            values[2147483648] = 4;
            const large: any = '2147483648', leading: any = '02147483648';
            const decimal: any = '2147483648.0', plus: any = '+2147483648', limit: any = '4294967295';
            console.log(values[leading], values[decimal], values[plus], values[limit],
                Object.hasOwn(values, leading), Object.hasOwn(values, decimal),
                Object.hasOwn(values, plus), Object.hasOwn(values, limit));
            Object.defineProperty(values, leading, { value: 'leading', configurable: true });
            Object.defineProperty(values, decimal, { value: 'decimal', configurable: true });
            Object.defineProperty(values, plus, { value: 'plus', configurable: true });
            Object.defineProperty(values, limit, { value: 'limit', configurable: true });
            console.log(values[large], values[2147483648], values[leading], values[decimal], values[plus],
                values[limit], values.length, Object.hasOwn(values, '0'));
            """, "undefined undefined undefined undefined false false false false\n4 4 leading decimal plus limit 2147483649 false\n"];
        yield return ["large-own-and-inherited-accessors-retain-receiver-and-once-only-reads", """
            const own: any[] = [], state = { ownGets: 0, inheritedGets: 0, same: true };
            Object.defineProperty(own, '2147483648', {
                get() { state.ownGets++; state.same = state.same && this === own; return 4; },
                configurable: true
            });
            const large: any = '2147483648';
            console.log('own', own[large], own[2147483648], state.ownGets, state.same,
                own.length, Object.hasOwn(own, large));
            const inherited: any[] = [];
            inherited.length = 4294967295;
            const prototype = Object.create(Array.prototype);
            Object.defineProperty(prototype, '4294967294', {
                get() { state.inheritedGets++; state.same = state.same && this === inherited; return 9; },
                configurable: true
            });
            Object.setPrototypeOf(inherited, prototype);
            const upper: any = '4294967294';
            console.log('inherited', inherited[upper], inherited[4294967294], state.inheritedGets,
                state.same, inherited.length, Object.hasOwn(inherited, upper));
            """, "own 4 4 2 true 2147483649 true\ninherited 9 9 2 true 4294967295 false\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void LargeCanonicalStringArrayReadsRetainStoredValuesAndKeyIdentity(
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
