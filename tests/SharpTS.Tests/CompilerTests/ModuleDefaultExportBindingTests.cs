using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ModuleDefaultExportBindingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["default-local-alias", "let value=1;export {value as default};export function increment(){value++;}", "", "import current,{increment} from './producer';console.log(current);increment();console.log(current);", "1\n2\n"];
        yield return ["named-default-import", "let value=1;export {value as default};export function increment(){value++;}", "", "import {default as current,increment} from './producer';console.log(current);increment();console.log(current);", "1\n2\n"];
        yield return ["default-reexport", "let value=1;export {value as default};export function increment(){value++;}", "export {default,increment} from './producer';", "import current,{increment} from './barrel';console.log(current);increment();console.log(current);", "1\n2\n"];
        yield return ["named-to-default-reexport", "export let value=1;export function increment(){value++;}", "export {value as default,increment} from './producer';", "import current,{increment} from './barrel';console.log(current);increment();console.log(current);", "1\n2\n"];
        yield return ["default-import-reexport-alias", "let value=1;export {value as default};export function increment(){value++;}", "import current,{increment} from './producer';export {current as live,increment};", "import {live,increment} from './barrel';console.log(live);increment();console.log(live);", "1\n2\n"];
        yield return ["mutable-default-function-alias", "let read=()=>1;export {read as default};export function replace(){read=()=>2;}", "", "import read,{replace} from './producer';console.log(read());replace();console.log(read());", "1\n2\n"];
        yield return ["default-and-named-share-binding", "let value=1;export {value as default,value as current};export function increment(){value++;}", "", "import initial,{current,increment} from './producer';console.log(initial,current);increment();console.log(initial,current);", "1 1\n2 2\n"];
        yield return ["default-expression-remains-snapshot", "let value=1;export default value;export {value};export function increment(){value++;}", "", "import initial,{value,increment} from './producer';console.log(initial,value);increment();console.log(initial,value);", "1 1\n1 2\n"];
        yield return ["default-alias-and-dollar-named-export", "let value=1;const $default=9;export {value as default,$default};export function increment(){value++;}", "", "import current,{$default,increment} from './producer';console.log(current,$default);increment();console.log(current,$default);", "1 9\n2 9\n"];
        yield return ["explicit-default-and-dollar-named-export", "export const $default=9;let value=1;export default value;export {value};export function increment(){value++;}", "", "import initial,{$default,value,increment} from './producer';console.log(initial,$default,value);increment();console.log(initial,$default,value);", "1 9 1\n1 9 2\n"];
        yield return ["default-function-alias-identity", "function read():any{return read;}export {read as default};", "", "import read from './producer';console.log(read()===read);", "true\n"];
        yield return ["default-class-alias", "class Box{value=7;}export {Box as default};", "", "import Box from './producer';console.log(new Box().value);", "7\n"];
        yield return ["explicit-default-class-and-dollar-name", "export const $default=9;export default class Box{value=7;}", "", "import Box,{$default} from './producer';console.log(new Box().value,$default);", "7 9\n"];
        yield return ["default-alias-namespace-initial-presence", "let value=1;export {value as default};", "", "import * as view from './producer';function read(ns:any){return ns.default;}console.log(read(view));", "1\n"];
        yield return ["explicit-default-namespace-initial-presence", "export default 1;", "", "import * as view from './producer';function read(ns:any){return ns.default;}console.log(read(view));", "1\n"];
        yield return ["default-and-dollar-namespace-initial-presence", "const $default=9;let value=1;export {value as default,$default};", "", "import * as view from './producer';function read(ns:any){console.log(ns.default,ns.$default);}read(view);", "1 9\n"];
        yield return ["explicit-default-before-dollar-named-export", "let value=1;export default value;export const $default=9;export {value};export function increment(){value++;}", "", "import initial,{$default,value,increment} from './producer';console.log(initial,$default,value);increment();console.log(initial,$default,value);", "1 9 1\n1 9 2\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void DefaultBindingsKeepTheirActualPublicExportRole(string name, string producer, string barrel, string consumer, string expected)
    {
        Assert.NotEmpty(name);
        var files = new Dictionary<string, string> { ["producer.ts"] = producer, ["barrel.ts"] = barrel, ["main.ts"] = consumer };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled));
    }

    [Theory, MemberData(nameof(Cases))]
    public void InterpreterUsesTheSameDefaultBindingContract(string name, string producer, string barrel, string consumer, string expected)
    {
        Assert.NotEmpty(name);
        var files = new Dictionary<string, string> { ["producer.ts"] = producer, ["barrel.ts"] = barrel, ["main.ts"] = consumer };
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted));
    }
}
