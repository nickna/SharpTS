using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArrayConcatAwaitTests
{
    // #952: spread expansion must preserve the selected concat receiver across await.
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-fixture-unobserved", "\r\n            async function f() {\r\n                console.log(JSON.stringify([0].concat(...await Promise.resolve([[1, 2]]))));\r\n            }\r\n            f();\r\n        ", "[0,1,2]\n"];
        yield return ["synchronous-owner-control", "console.log(JSON.stringify([0].concat(...[[1, 2]])));\n", "[0,1,2]\n"];
        yield return ["preawait-before-concat-control", "async function f() {\n    const parts = await Promise.resolve([[1, 2]]);\n    console.log(JSON.stringify([0].concat(...parts)));\n}\nf();\n", "[0,1,2]\n"];
        yield return ["fulfilled-spread-rejection-observer", "async function f() {\n    console.log(JSON.stringify([0].concat(...await Promise.resolve([[1, 2]]))));\n}\nconst observed = f().catch((error: any) => console.log('rejected', error.name, error.message));\n", "[0,1,2]\n"];
        yield return ["pending-spread-unobserved", "let release: (value: number[][]) => void = (_value: number[][]) => {};\nconst gate = new Promise<number[][]>(resolve => { release = resolve; });\nasync function f() {\n    console.log('waiting');\n    console.log(JSON.stringify([0].concat(...await gate)));\n}\nconst pending = f();\nrelease([[1, 2]]);\n", "waiting\n[0,1,2]\n"];
        yield return ["pending-spread-rejection-observer", "let release: (value: number[][]) => void = (_value: number[][]) => {};\nconst gate = new Promise<number[][]>(resolve => { release = resolve; });\nasync function f() {\n    console.log('waiting');\n    console.log(JSON.stringify([0].concat(...await gate)));\n}\nconst pending = f();\nconst observed = pending.catch((error: any) => console.log('rejected', error.name, error.message));\nrelease([[1, 2]]);\n", "waiting\n[0,1,2]\n"];
        yield return ["pending-receiver-order-and-flattening", "const trace: string[] = [];\nconst first = [0];\nlet selected = first;\nlet release: (value: number[][]) => void = (_value: number[][]) => {};\nconst gate = new Promise<number[][]>(resolve => { release = resolve; });\nfunction receiver(): number[] { trace.push('receiver'); return selected; }\nfunction prefix(): number { trace.push('prefix'); return 1; }\nfunction parts(): Promise<number[][]> { trace.push('spread'); return gate; }\nfunction tail(): number { trace.push('tail'); return 4; }\nasync function run() {\n    const result = receiver().concat(prefix(), ...await parts(), tail());\n    console.log(JSON.stringify(result), trace.join(','), JSON.stringify(first));\n}\nconst pending = run();\nconst observed = pending.catch((error: any) => console.log('rejected', error.name, error.message));\nselected = [9];\ntrace.push('release');\nrelease([[2, 3]]);\n", "[0,1,2,3,4] receiver,prefix,spread,release,tail [0]\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SpreadCallsPreserveReceiverAndEvaluationOrder(
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
            Assert.Equal(expected, TestHarness.Run(source, mode));
        }
    }
}
