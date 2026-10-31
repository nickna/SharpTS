using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ModuleLiveBindingCompilerTests
{
    public static IEnumerable<object[]> Cases() => ModuleLiveBindingTests.Cases().Concat(AdditionalCases());

    private static IEnumerable<object[]> AdditionalCases()
    {
        yield return ["closure-and-class-methods", "export let value=1;export function increment(){value++;}", "", "import {value as current,increment} from './producer';const read=()=>current;class Reader{static read(){return current;}read(){return current;}}console.log(read(),Reader.read(),new Reader().read());increment();console.log(read(),Reader.read(),new Reader().read());", "1 1 1\n2 2 2\n"];
        yield return ["generator-import-read", "export let value=1;export function increment(){value++;}", "", "import {value,increment} from './producer';function* read(){yield value;increment();yield value;}console.log([...read()].join(','));", "1,2\n"];
        yield return ["async-import-read", "export let value=1;export function increment(){value++;}", "", "import {value,increment} from './producer';async function read(){console.log(value);await Promise.resolve();increment();console.log(value);}read();", "1\n2\n"];
        yield return ["multiple-local-export-aliases", "let value=1;export {value as first,value as second};value=2;export function increment(){value++;}", "", "import {first,second,increment} from './producer';console.log(first,second);increment();console.log(first,second);", "2 2\n3 3\n"];
        yield return ["default-expression-is-a-snapshot", "let value=1;export {value};export default value;export function increment(){value++;}", "", "import initial,{value,increment} from './producer';console.log(initial,value);increment();console.log(initial,value);", "1 1\n1 2\n"];
        yield return ["nested-shadow-keeps-own-cell", "export let value=1;export function increment(){value++;}", "", "import {value,increment} from './producer';function capture(value:number){return()=>value;}const local=capture(9);increment();console.log(value,local());", "2 9\n"];
        yield return ["export-list-before-import", "export let value=1;export function increment(){value++;}", "export {current,increment};import {value as current,increment} from './producer';", "import {current,increment} from './barrel';console.log(current);increment();console.log(current);", "1\n2\n"];
        yield return ["source-binding-tdz", "export {value};export function read(){return value;}try{read();}catch(error){console.log(error instanceof ReferenceError);}let value=1;", "", "import {value} from './producer';console.log(value);", "true\n1\n"];
        yield return ["mutable-function-value", "export let read=()=>1;export function replace(){read=()=>2;}", "", "import {read,replace} from './producer';console.log(read());replace();console.log(read());", "1\n2\n"];
        yield return ["function-alias-before-export", "function original():any{return original;}console.log(original()===original);export {original as alias};", "", "import {alias} from './producer';console.log(alias()===alias);", "true\ntrue\n"];
        yield return ["retained-original-valid-mutable-export", "export let target:(n:number)=>number=(n:number):number=>{if(n<=0)return 1;return target(n-1)+1;};export function replace(){target=(_n:number):number=>100;}", "", "import { target, replace } from './producer';\nconst original = target;\nreplace();\nconsole.log(original(2), target(2));", "101 100\n"];
        yield return ["declaration-replaced-before-export", "function f(){return 1;}function other(){return 9;}f=other;export {f as alias};console.log(f());", "", "import {alias} from './producer';console.log(alias());", "9\n9\n"];
        yield return ["function-alias-sharing", "function f():any{return f;}export {f as left,f as right};", "", "import {left,right} from './producer';console.log(left===right,left()===left,right()===right);", "true true true\n"];
        yield return ["same-function-name-second-module", "function f():any{return f;}export {f as read};", "function f():any{return f;}export {f as read};", "import {read as first} from './producer';import {read as second} from './barrel';console.log(first!==second,first()===first,second()===second);", "true true true\n"];
        yield return ["function-local-shadow", "function f(){return 1;}export {f as read};export function local(){const f=()=>9;return f();}", "", "import {read,local} from './producer';console.log(read(),local());", "1 9\n"];
        yield return ["inner-function-live-module-read", "export let value=1;export function increment(){value++;}export function reader(){function read(){return value;}return read;}", "", "import {reader,increment} from './producer';const read=reader();console.log(read());increment();console.log(read());", "1\n2\n"];
        yield return ["async-arrow-live-module-read", "export let value=1;export const read=async()=>value;export function increment(){value++;}", "", "import {read,increment} from './producer';async function run(){console.log(await read());increment();console.log(await read());}run();", "1\n2\n"];
        yield return ["raw-numeric-capture-return", "export let value:number=1;export const read=()=>value;export function replace(){value='changed' as any;}", "", "import {read,replace} from './producer';console.log(read());replace();console.log(read());", "1\nchanged\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void NamedImportsReadCanonicalSourceStorage(string name, string producer, string barrel, string consumer, string expected)
    {
        Assert.NotEmpty(name);
        var files = new Dictionary<string, string> { ["producer.ts"] = producer, ["barrel.ts"] = barrel, ["main.ts"] = consumer };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled));
    }
}
