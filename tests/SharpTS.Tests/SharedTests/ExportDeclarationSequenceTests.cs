using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ExportDeclarationSequenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "export const read=()=>later,later=11;console.log(read());\n", "import './producer';", "11\n"];
        yield return ["const-imports", "export const first=3,second=4;", "import {first,second} from './producer';import * as all from './producer';console.log(first,second,Object.keys(all).sort().join(','));", "3 4 first,second\n"];
        yield return ["let-live-bindings", "export let first=3,second=4;export function update(){first++;second++;}", "import {first,second,update} from './producer';console.log(first,second);update();console.log(first,second);", "3 4\n4 5\n"];
        yield return ["var-initialization-order", "const events:string[]=[];export var first=(events.push('first'),3),second=(events.push('second'),4);export const trace=events.join(',');", "import {first,second,trace} from './producer';console.log(first,second,trace);", "3 4 first,second\n"];
        yield return ["object-destructuring-no-spills", "export const {first,second}={first:3,second:4};", "import {first,second} from './producer';import * as all from './producer';console.log(first,second,Object.keys(all).sort().join(','));", "3 4 first,second\n"];
        yield return ["array-destructuring-no-spills", "export const [first,second]=[3,4];", "import {first,second} from './producer';import * as all from './producer';console.log(first,second,Object.keys(all).sort().join(','));", "3 4 first,second\n"];
        yield return ["computed-destructuring-no-spills", "const key='first';export const {[key]:first,second}={first:3,second:4};", "import {first,second} from './producer';import * as all from './producer';console.log(first,second,Object.keys(all).sort().join(','));", "3 4 first,second\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], row[3], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PublishesOriginalBindingsFromLoweredSequences(string name, string producer, string consumer, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        var files = new Dictionary<string, string> { ["producer.ts"] = producer, ["main.ts"] = consumer };
        if (mode == ExecutionMode.Compiled)
            Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", mode));
    }
}
