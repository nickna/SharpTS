using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UsingSuppressionValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["sync-suppression-fields", "/// <reference lib=\"esnext.disposable\" />\nconst body={},cleanup={};try{{using resource={[Symbol.dispose](){throw cleanup}};throw body}}catch(error:any){console.log(error.error===cleanup,error.suppressed===body)}", "true true\n"];
        yield return ["body-and-disposer-identity", "/// <reference lib=\"esnext.disposable\" />\nconst body={},cleanup={};async function run(){try{await using resource={async[Symbol.asyncDispose](){await 0;throw cleanup}};await 0;throw body}catch(error:any){console.log(error.error===cleanup,error.suppressed===body)}}run();", "true true\n"];
        yield return ["cleanup-only-object", "/// <reference lib=\"esnext.disposable\" />\nconst marker={};try{{using resource={[Symbol.dispose](){throw marker}};}}catch(error){console.log(error===marker)}", "true\n"];
        yield return ["cleanup-only-null", "/// <reference lib=\"esnext.disposable\" />\nconst marker=null;try{{using resource={[Symbol.dispose](){throw marker}};}}catch(error){console.log(error===marker)}", "true\n"];
        yield return ["cleanup-only-undefined", "/// <reference lib=\"esnext.disposable\" />\nconst marker=undefined;try{{using resource={[Symbol.dispose](){throw marker}};}}catch(error){console.log(error===marker)}", "true\n"];
        yield return ["cleanup-only-prefix", "/// <reference lib=\"esnext.disposable\" />\nconst marker=\"TypeError: guest\";try{{using resource={[Symbol.dispose](){throw marker}};}}catch(error){console.log(error===marker)}", "true\n"];
        yield return ["body-cleanup-null-undefined", "/// <reference lib=\"esnext.disposable\" />\nconst body=null,cleanup=undefined;try{{using resource={[Symbol.dispose](){throw cleanup}};throw body}}catch(error:any){console.log(error.error===cleanup,error.suppressed===body)}", "true true\n"];
        yield return ["body-cleanup-undefined-null", "/// <reference lib=\"esnext.disposable\" />\nconst body=undefined,cleanup=null;try{{using resource={[Symbol.dispose](){throw cleanup}};throw body}}catch(error:any){console.log(error.error===cleanup,error.suppressed===body)}", "true true\n"];
        yield return ["body-cleanup-prefix-number", "/// <reference lib=\"esnext.disposable\" />\nconst body=\"RangeError: body\",cleanup=7;try{{using resource={[Symbol.dispose](){throw cleanup}};throw body}}catch(error:any){console.log(error.error===cleanup,error.suppressed===body)}", "true true\n"];
        yield return ["body-cleanup-errors", "/// <reference lib=\"esnext.disposable\" />\nconst body=new Error(\"body\"),cleanup=new TypeError(\"cleanup\");try{{using resource={[Symbol.dispose](){throw cleanup}};throw body}}catch(error:any){console.log(error.error===cleanup,error.suppressed===body)}", "true true\n"];
        yield return ["three-cleanup-lifo", "/// <reference lib=\"esnext.disposable\" />\nconst body={},a={},b={},c={},log:string[]=[];try{{using one={[Symbol.dispose](){log.push(\"a\");throw a}},two={[Symbol.dispose](){log.push(\"b\");throw b}},three={[Symbol.dispose](){log.push(\"c\");throw c}};throw body}}catch(error:any){console.log(error.error===a,error.suppressed.error===b,error.suppressed.suppressed.error===c,error.suppressed.suppressed.suppressed===body,log.join(\",\"))}", "true true true true c,b,a\n"];
        yield return ["two-cleanup-no-body", "/// <reference lib=\"esnext.disposable\" />\nconst a={},b={};try{{using one={[Symbol.dispose](){throw a}},two={[Symbol.dispose](){throw b}};}}catch(error:any){console.log(error.error===a,error.suppressed===b)}", "true true\n"];
        yield return ["cleanup-overrides-return", "/// <reference lib=\"esnext.disposable\" />\nconst marker={};function run(){using resource={[Symbol.dispose](){throw marker}};return 7}try{run()}catch(error){console.log(error===marker)}", "true\n"];
        yield return ["body-null-cleanup-success", "/// <reference lib=\"esnext.disposable\" />\ntry{{using resource={[Symbol.dispose](){}};throw null}}catch(error){console.log(error===null)}", "true\n"];
        yield return ["body-undefined-cleanup-success", "/// <reference lib=\"esnext.disposable\" />\ntry{{using resource={[Symbol.dispose](){}};throw undefined}}catch(error){console.log(error===undefined)}", "true\n"];
        yield return ["host-body-guest-cleanup", "/// <reference lib=\"esnext.disposable\" />\nconst cleanup={};try{{using resource={[Symbol.dispose](){throw cleanup}};new ArrayBuffer(-1)}}catch(error:any){console.log(error.error===cleanup,typeof error.suppressed,error.suppressed.name)}", "true object RangeError\n"];
        yield return ["guest-body-host-cleanup", "/// <reference lib=\"esnext.disposable\" />\nconst body=\"RangeError: guest\";try{{using resource={[Symbol.dispose](){new ArrayBuffer(-1)}};throw body}}catch(error:any){console.log(typeof error.error,error.error.name,error.suppressed===body)}", "object RangeError true\n"];
        yield return ["mutable-fields-dot-and-index", "/// <reference lib=\"esnext.disposable\" />\ntry{{using resource={[Symbol.dispose](){throw 2}};throw 1}}catch(error:any){error.error=7;error[\"suppressed\"]=8;error.error+=1;console.log(error.error,error.suppressed)}", "8 8\n"];
        yield return ["field-delete", "/// <reference lib=\"esnext.disposable\" />\ntry{{using resource={[Symbol.dispose](){throw 2}};throw 1}}catch(error:any){console.log(delete error.error,error.error===undefined,Object.prototype.hasOwnProperty.call(error,\"error\"),error.suppressed)}", "true true false 1\n"];
        yield return ["field-accessor-receiver", "/// <reference lib=\"esnext.disposable\" />\ntry{{using resource={[Symbol.dispose](){throw 2}};throw 1}}catch(error:any){let reads=0;Object.defineProperty(error,\"error\",{get(){reads++;return this===error?7:99}});console.log(error.error,Reflect.get(error,\"error\"),reads)}", "7 7 2\n"];
        yield return ["async-null-undefined", "/// <reference lib=\"esnext.disposable\" />\nasync function run(){try{await using resource={async[Symbol.asyncDispose](){await 0;throw undefined}};throw null}catch(error:any){console.log(error.error===undefined,error.suppressed===null)}}run();", "true true\n"];
        yield return ["async-two-cleanup", "/// <reference lib=\"esnext.disposable\" />\nconst a={},b={};async function run(){try{await using one={async[Symbol.asyncDispose](){await 0;throw a}},two={async[Symbol.asyncDispose](){await 0;throw b}};}catch(error:any){console.log(error.error===a,error.suppressed===b)}}run();", "true true\n"];
        yield return ["async-sync-fallback-suppression", "/// <reference lib=\"esnext.disposable\" />\nconst a={},b={};async function run(){try{await using resource={[Symbol.dispose](){throw a}};await 0;throw b}catch(error:any){console.log(error.error===a,error.suppressed===b)}}run();", "true true\n"];
        yield return ["catch-inside-resource-scope", "/// <reference lib=\"esnext.disposable\" />\nconst body={},cleanup={},log:string[]=[];try{{using resource={[Symbol.dispose](){log.push(\"dispose\");throw cleanup}};try{throw body}catch(error){console.log(error===body,log.length)}}}catch(error){console.log(error===cleanup,log.join(\",\"))}", "true 0\ntrue dispose\n"];
        yield return ["nested-resource-suppression", "/// <reference lib=\"esnext.disposable\" />\nconst body={},outer={},inner={},log:string[]=[];try{{using one={[Symbol.dispose](){log.push(\"outer\");throw outer}};{using two={[Symbol.dispose](){log.push(\"inner\");throw inner}};throw body}}}catch(error:any){console.log(error.error===outer,error.suppressed.error===inner,error.suppressed.suppressed===body,log.join(\",\"))}", "true true true inner,outer\n"];
    }

    // The separately owned compiled #3361 repair consumes these same exact cases.
    [Theory, MemberData(nameof(Cases))]
    public void InterpretedCleanupPreservesTheActualCompletion(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Theory, MemberData(nameof(Cases))]
    public void CompiledCleanupPreservesTheActualCompletion(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }

    // ECMA-262 SuppressedError: error/suppressed are writable, nonenumerable, configurable.
    // Node 22 lacks the native constructor; TypeScript's fallback instead assigns enumerable
    // fields. Its discrepant raw output is retained as an oracle limitation, not our contract.
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void SuppressionFieldsHaveTheirSpecifiedOwnDescriptors(ExecutionMode mode)
    {
        const string source = "/// <reference lib=\"esnext.disposable\" />\ntry{{using resource={[Symbol.dispose](){throw 2}};throw 1}}catch(error:any){const a=Object.getOwnPropertyDescriptor(error,\"error\")!,b=Object.getOwnPropertyDescriptor(error,\"suppressed\")!;console.log(a.value,a.writable,a.enumerable,a.configurable,b.value,b.writable,b.enumerable,b.configurable,Object.keys(error).length,Object.getOwnPropertyNames(error).filter(k=>k===\"error\"||k===\"suppressed\").join(\",\"))}";
        string output;
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, compiledOutput) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            output = compiledOutput;
        }
        else output = TestHarness.Run(source, mode);
        Assert.Equal("2 true false true 1 true false true 0 error,suppressed\n", output);
    }
}
