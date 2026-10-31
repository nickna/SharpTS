using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ModuleNamespaceLiveReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-held-namespace", "export let value=1;export function increment(){value++;}", "", "import * as ns from './producer';function read(view:any){return view.value;}console.log(read(ns));ns.increment();console.log(read(ns));", "1\n2\n"];
        yield return ["closure-held-namespace", "export let value=1;export function increment(){value++;}", "", "import * as ns from './producer';const read=()=>ns;console.log(read().value);ns.increment();console.log(read().value);", "1\n2\n"];
        yield return ["data-descriptor-live-value", "export let value=1;export function increment(){value++;}", "", "import * as ns from './producer';function read(view:any){const d=Object.getOwnPropertyDescriptor(view,'value')!;console.log(d.value,'get' in d,'set' in d,d.writable,d.enumerable);}read(ns);ns.increment();read(ns);", "1 false false true true\n2 false false true true\n"];
        yield return ["values-live-data", "export let value=1;export function increment(){value++;}", "", "import * as ns from './producer';function read(view:any){return Object.values(view).filter(value=>typeof value==='number').join(',');}console.log(read(ns));ns.increment();console.log(read(ns));", "1\n2\n"];
        yield return ["entries-live-data", "export let value=1;export function increment(){value++;}", "", "import * as ns from './producer';function read(view:any){return Object.entries(view).find(entry=>entry[0]==='value')![1];}console.log(read(ns));ns.increment();console.log(read(ns));", "1\n2\n"];
        yield return ["spread-live-data", "export let value=1;export function increment(){value++;}", "", "import * as ns from './producer';function read(view:any){return {...view}.value;}console.log(read(ns));ns.increment();console.log(read(ns));", "1\n2\n"];
        yield return ["destructure-live-data", "export let value=1;export function increment(){value++;}", "", "import * as ns from './producer';function read(view:any){const {value}=view;return value;}console.log(read(ns));ns.increment();console.log(read(ns));", "1\n2\n"];
        yield return ["default-alias-and-dollar-live-data", "const $default=9;let value=1;export {value as default,$default};export function increment(){value++;}", "", "import * as ns from './producer';function read(view:any){console.log(view.default,view.$default);}read(ns);ns.increment();read(ns);", "1 9\n2 9\n"];
        yield return ["multiple-export-aliases", "let value=1;export {value as first,value as second};export function increment(){value++;}", "", "import * as ns from './producer';function read(view:any){console.log(view.first,view.second);}read(ns);ns.increment();read(ns);", "1 1\n2 2\n"];
        yield return ["own-names-versus-object-keys-tdz", "import * as ns from './producer';console.log(Reflect.ownKeys(ns).includes('value'),Object.getOwnPropertyNames(ns).includes('value'));try{Object.keys(ns);}catch(error){console.log(error instanceof ReferenceError);}export let value=1;", "", "import './producer';", "true true\ntrue\n"];
        yield return ["held-value-read-tdz", "import * as ns from './producer';function read(view:any){return view.value;}try{read(ns);}catch(error){console.log(error instanceof ReferenceError);}export let value=1;console.log(read(ns));", "", "import './producer';", "true\n1\n"];
        yield return ["ordinary-accessor-and-fake-reader", "", "", "export {};let current=7;const view:any={get value(){return current;},_moduleNamespaceReader:()=>99};function read(value:any){return value.value;}console.log(read(view));current=8;console.log(read(view));", "7\n8\n"];
        yield return ["async-held-namespace", "export let value=1;export function increment(){value++;}", "", "import * as ns from './producer';function read(view:any){return view.value;}async function run(){console.log(read(ns));await Promise.resolve();ns.increment();console.log(read(ns));}run();", "1\n2\n"];
        yield return ["explicit-default-expression-snapshot", "let value=1;export default value;export {value};export function increment(){value++;}", "", "import * as ns from './producer';function read(view:any){console.log(view.default,view.value);}read(ns);ns.increment();read(ns);", "1 1\n1 2\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void HeldNamespacesReadTheirActualExportBindings(string name, string producer, string barrel, string consumer, string expected)
    {
        Assert.NotEmpty(name);
        var files = new Dictionary<string, string> { ["producer.ts"] = producer, ["barrel.ts"] = barrel, ["main.ts"] = consumer };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled));
    }

    public static IEnumerable<object[]> AdditionalCases()
    {
        yield return ["namespace-own-proto-name", "let value=1;export {value as __proto__};export function increment(){value++;}", "", "import * as ns from './producer';function read(view:any){return view.__proto__;}console.log(read(ns));ns.increment();console.log(read(ns));", "1\n2\n"];
        yield return ["namespace-own-inherited-method-names", "let a=1,b=2;export {a as toString,b as hasOwnProperty};export function increment(){a++;b++;}", "", "import * as ns from './producer';function read(view:any){console.log(view.toString,view.hasOwnProperty);}read(ns);ns.increment();read(ns);", "1 2\n2 3\n"];
        yield return ["json-live-data", "export let value=1;export function increment(){value++;}", "", "import * as ns from './producer';function read(view:any){return JSON.stringify(view);}console.log(read(ns));ns.increment();console.log(read(ns));", "{\"value\":1}\n{\"value\":2}\n"];
    }

    [Theory, MemberData(nameof(AdditionalCases))]
    public void RelatedDataConsumersReadLiveValues(string name, string producer, string barrel, string consumer, string expected)
        => HeldNamespacesReadTheirActualExportBindings(name, producer, barrel, consumer, expected);
}
