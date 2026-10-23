using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class NumberParseIntLiveValueTests
{
    public const string IssueSource = """
        const target: any = Number;
        const original = target.parseInt;
        let result: any;
        try {
            target.parseInt = () => 17;
            const held = Number.parseInt;
            result = held("7", 10);
        } finally { target.parseInt = original; }
        console.log(result);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["detached-number-parse-int-observes-live-replacement-issue", IssueSource, "17\n"];
        yield return ["held-values-keep-identity-and-snapshot-across-replacements-and-restoration", """
            const target: any = Number, namespaceAlias = Number;
            const original = target.parseInt, before = Number.parseInt;
            let captured: any;
            try {
                const replacement = (_text: string, _radix?: number) => 17;
                target.parseInt = replacement;
                captured = Number.parseInt;
                console.log(before === original, captured === replacement, captured === namespaceAlias.parseInt,
                    before('7', 10), captured('7', 10), Number.parseInt('7', 10), namespaceAlias.parseInt('7', 10));
                target.parseInt = () => 29;
                console.log(captured('8', 10), Number.parseInt('8', 10));
            } finally { target.parseInt = original; }
            console.log(before === Number.parseInt, captured('8', 10), Number.parseInt('12', 10));
            """, "true true true 7 17 17 17\n17 29\ntrue 17 12\n"];
        yield return ["live-getter-runs-once-per-read-and-held-value-does-not-requery", """
            const target: any = Number, token = {};
            const original = Object.getOwnPropertyDescriptor(target, 'parseInt')!;
            const replacement = (_text: string, _radix?: number) => 17;
            let reads = 0, same = true, fail = false;
            try {
                Object.defineProperty(target, 'parseInt', {
                    configurable: true,
                    get() {
                        reads++;
                        same = same && this === target;
                        if (fail) throw token;
                        return replacement;
                    }
                });
                const held = Number.parseInt;
                console.log(held === replacement, held('7', 10), held('8', 10), reads, same);
                fail = true;
                try { const fresh = Number.parseInt; console.log('unexpected', fresh); }
                catch (error) { console.log(error === token, reads, held('9', 10) === 17); }
            } finally { Object.defineProperty(target, 'parseInt', original); }
            console.log(Number.parseInt('12', 10), reads);
            """, "true 17 17 1 true\ntrue 2 true\n12 2\n"];
        yield return ["primitive-value-and-deletion-are-observed-before-descriptor-restoration", """
            const target: any = Number;
            const original = Object.getOwnPropertyDescriptor(target, 'parseInt')!;
            try {
                target.parseInt = 23;
                const value: any = Number.parseInt;
                console.log(value === 23, typeof value);
                const removed = delete target.parseInt;
                const missing: any = Number.parseInt;
                console.log(removed, typeof missing, Object.hasOwn(target, 'parseInt'));
            } finally { Object.defineProperty(target, 'parseInt', original); }
            const restored = Number.parseInt;
            console.log(typeof restored, restored === original.value, restored('12', 10));
            """, "true number\ntrue undefined false\nfunction true 12\n"];
    }

    public static IEnumerable<object[]> SavedCases()
    {
        foreach (var row in Cases()) yield return row;
        yield return IntrinsicStaticCallBindingTests.Cases().Single(row =>
            (string)row[0] == "number-and-math-live-member-replacements");
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DetachedNumberParseIntReadsTheLivePropertyValue(
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
