using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class OptionalChainMethodCallTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["filed-original", "function run(value:string|undefined){console.log(value?.includes(\"x\")===undefined)}run(undefined);", "true\n"];
        yield return ["preserved-3457-nullish-control", "const value:any=null;console.log((value as string|null)?.includes(\"x\")===undefined);", "true\n"];
        yield return ["present-string", "function run(value:string|undefined){console.log(value?.includes(\"x\"))}run(\"x\");run(\"no\");", "true\nfalse\n"];
        yield return ["null-and-present", "function run(value:string|null){console.log(value?.includes(\"x\"))}run(null);run(\"x\");", "undefined\ntrue\n"];
        yield return ["target-once-argument-skipped", "let target=0,args=0;function pick():string|undefined{target++;return undefined}function arg(){args++;return \"x\"}console.log(pick()?.includes(arg()),target,args);", "undefined 1 0\n"];
        yield return ["argument-abrupt-identity", "const reason={};function arg():any{throw reason}function run(v:string|undefined){try{console.log(v?.includes(arg()))}catch(e){console.log(e===reason)}}run(undefined);run(\"x\");", "undefined\ntrue\n"];
        yield return ["interface-method", "interface Value{method():number}function run(v:Value|undefined){console.log(v?.method())}run(undefined);run({method(){return 7}});", "undefined\n7\n"];
        yield return ["method-receiver", "interface Value{n:number;method():number}function run(v:Value|undefined){console.log(v?.method())}run(undefined);run({n:7,method(){return this.n}});", "undefined\n7\n"];
        yield return ["optional-method-optional-call", "function run(v:{fn?:()=>number}|undefined){console.log(v?.fn?.())}run(undefined);run({});run({fn:()=>7});", "undefined\nundefined\n7\n"];
        yield return ["method-nonnull-assertion", "function run(v:{fn?():number}|undefined){console.log(v?.fn!())}run(undefined);run({fn(){return 7}});", "undefined\n7\n"];
        yield return ["ordinary-optional-function-control", "function run(fn:(()=>number)|undefined){console.log(fn?.())}run(undefined);run(()=>7);", "undefined\n7\n"];
        yield return ["returned-undefined-value", "function read(v:string|undefined):boolean|undefined{return v?.includes(\"x\")}console.log(read(undefined)===undefined,read(\"x\"));", "true true\n"];
        yield return ["forwarded-result", "function id(v:boolean|undefined){return v}function read(v:string|undefined){return id(v?.includes(\"x\"))}console.log(read(undefined)===undefined,read(\"x\"));", "true true\n"];
        yield return ["argument-value", "function take(v:boolean|undefined){console.log(v===undefined)}function run(v:string|undefined){take(v?.includes(\"x\"))}run(undefined);run(\"x\");", "true\nfalse\n"];
        yield return ["coalesced-result", "function run(v:string|undefined):boolean{return v?.includes(\"x\")??false}console.log(run(undefined),run(\"x\"));", "false true\n"];
        yield return ["generic-member", "function run(v:{id<T>(x:T):T}|undefined){console.log(v?.id<number>(7))}run(undefined);run({id<T>(x:T){return x}});", "undefined\n7\n"];
    }

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
        {
            yield return [ExecutionMode.Interpreted, row[0], row[1], row[2]];
            yield return [ExecutionMode.Compiled, row[0], row[1], row[2]];
        }
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void NullableReceiverSkipsInvocationAndPreservesItsResult(
        ExecutionMode mode, string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
