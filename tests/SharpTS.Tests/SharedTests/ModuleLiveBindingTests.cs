using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ModuleLiveBindingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "export let value=1;export function increment(){value++;}\n", "", "import {value,increment} from './producer';console.log(value);increment();console.log(value);\n", "1\n2\n"];
        yield return ["local-alias", "export let value=1;export function increment(){value++;}", "", "import {value as current,increment} from './producer';console.log(current);increment();console.log(current);", "1\n2\n"];
        yield return ["multiple-bindings", "export let first=1,second=2;export function increment(){first++;second+=2;}", "", "import {first,second,increment} from './producer';console.log(first,second);increment();console.log(first,second);", "1 2\n2 4\n"];
        yield return ["export-before-declaration", "export {value,increment};let value=1;function increment(){value++;}", "", "import {value,increment} from './producer';console.log(value);increment();console.log(value);", "1\n2\n"];
        yield return ["named-reexport", "export let value=1;export function increment(){value++;}", "export {value as current,increment} from './producer';", "import {current,increment} from './barrel';console.log(current);increment();console.log(current);", "1\n2\n"];
        yield return ["star-reexport", "export let value=1;export function increment(){value++;}", "export * from './producer';", "import {value,increment} from './barrel';console.log(value);increment();console.log(value);", "1\n2\n"];
        yield return ["same-names-distinct-modules", "export let value=1;export function increment(){value++;}", "export let value=10;export function increment(){value++;}", "import {value,increment} from './producer';import {value as other,increment as next} from './barrel';increment();console.log(value,other);next();console.log(value,other);", "2 10\n2 11\n"];
        yield return ["once-only-initialization", "console.log('init');export let value=1;export function increment(){value++;}", "", "import {value,increment} from './producer';import {value as other} from './producer';console.log(value,other);increment();console.log(value,other);", "init\n1 1\n2 2\n"];
        yield return ["replacement-value-kind", "export let value:any=1;export function replace(){value={flag:7};}", "", "import {value,replace} from './producer';console.log(typeof value);replace();console.log(typeof value,value.flag);", "number\nobject 7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], row[3], row[4], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ImportsReadTheirActualSourceBindings(string name, string producer, string barrel, string consumer, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        var files = new Dictionary<string, string> { ["producer.ts"] = producer, ["barrel.ts"] = barrel, ["main.ts"] = consumer };
        if (mode == ExecutionMode.Compiled)
            Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", mode));
    }
}
