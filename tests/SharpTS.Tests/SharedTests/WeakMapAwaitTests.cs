using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class WeakMapAwaitTests
{
    // #2900: native calls must preserve their receiver and argument slots across await.
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-async-weak", "async function run(){const target={id:1};const map=new WeakMap<object,number>();const ref=new WeakRef(target);map.set(target,await Promise.resolve(8));console.log(map.get(target),ref.deref()===target,target.id);}run();", "8 true 1\n"];
        yield return ["minimal-weakmap-set-await-value", "async function run() {\n    const target = {};\n    const map = new WeakMap<object, number>();\n    map.set(target, await Promise.resolve(8));\n    console.log(map.get(target));\n}\nrun().catch(() => console.log('unexpected'));\n", "8\n"];
        yield return ["preawait-value-control", "async function run() {\n    const target = {};\n    const map = new WeakMap<object, number>();\n    const value = await Promise.resolve(8);\n    map.set(target, value);\n    console.log(map.get(target));\n}\nrun().catch(() => console.log('unexpected'));\n", "8\n"];
        yield return ["nonawait-async-control", "async function run() {\n    const target = {};\n    const map = new WeakMap<object, number>();\n    map.set(target, 8);\n    console.log(map.get(target));\n}\nrun().catch(() => console.log('unexpected'));\n", "8\n"];
        yield return ["receiver-key-value-order-and-identity", "const trace: string[] = [];\nconst target = {};\nconst original = new WeakMap<object, number>();\nlet selected = original;\nfunction receiver(): WeakMap<object, number> { trace.push('receiver'); return selected; }\nfunction key(): object { trace.push('key'); return target; }\nasync function value(): Promise<number> {\n    trace.push('value');\n    await Promise.resolve(0);\n    selected = new WeakMap<object, number>();\n    trace.push('resume');\n    return 8;\n}\nasync function run() {\n    const result = receiver().set(key(), await value());\n    console.log(result === original, original.get(target), selected.has(target), trace.join(','));\n}\nrun().catch(() => console.log('unexpected'));\n", "true 8 false receiver,key,value,resume\n"];
        yield return ["pending-native-weakmap-set-value", "let release: (value: number) => void = (_value: number) => {};\nconst gate = new Promise<number>(resolve => { release = resolve; });\nconst target = { id: 1 };\nconst map = new WeakMap<object, number>();\nasync function run() {\n    console.log('waiting');\n    map.set(target, await gate);\n    console.log(map.get(target));\n}\nconst result = run();\nresult.catch((error: any) => console.log('rejected', error.name, error.message));\nrelease(8);\n", "waiting\n8\n"];
        yield return ["awaited-native-weakmap-key-slots", "async function run() {\n    const target = { id: 1 };\n    const map = new WeakMap<object, number>();\n    map.set(target, 8);\n    const value = map.get(await Promise.resolve(target));\n    const present = map.has(await Promise.resolve(target));\n    const removed = map.delete(await Promise.resolve(target));\n    console.log(value, present, removed, map.has(target));\n}\nrun().catch((error: any) => console.log('rejected', error.name, error.message));\n", "8 true true false\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void NativeCallsPreserveReceiverArgumentsAndEvaluationOrder(
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
