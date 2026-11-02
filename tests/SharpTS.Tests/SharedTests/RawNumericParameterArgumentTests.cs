using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class RawNumericParameterArgumentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const raw:any={};function identity(input:number):any{return input}console.log(identity(raw)===raw);", "true\n"];
        yield return ["arrow", "const raw:any={};const identity=(input:number):any=>input;console.log(identity(raw)===raw);", "true\n"];
        yield return ["const-alias", "const raw:any={};function identity(input:number):any{return input}const alias=identity;console.log(alias(raw)===raw);", "true\n"];
        yield return ["forwarded-parameter", "const raw:any={};function identity(input:number):any{return input}function forward(value:number):any{return identity(value)}console.log(forward(raw)===raw);", "true\n"];
        yield return ["erased-assertion", "const raw:any={};function identity(input:number):any{return input}console.log(identity((raw as unknown) as number)===raw);", "true\n"];
        yield return ["numeric-return-annotation", "const raw:any={};function identity(input:number):number{return input}console.log(identity(raw)===raw,typeof identity(raw));", "true object\n"];
        yield return ["raw-null", "const raw:any=null;function identity(input:number):any{return input}console.log(identity(raw)===raw,typeof identity(raw));", "true object\n"];
        yield return ["raw-undefined", "const raw:any=undefined;function identity(input:number):any{return input}console.log(identity(raw)===raw,typeof identity(raw));", "true undefined\n"];
        yield return ["raw-string", "const raw:any='text';function identity(input:number):any{return input}console.log(identity(raw)===raw,typeof identity(raw));", "true string\n"];
        yield return ["raw-boolean", "const raw:any=true;function identity(input:number):any{return input}console.log(identity(raw)===raw,typeof identity(raw));", "true boolean\n"];
        yield return ["raw-bigint", "const raw:any=7n;function identity(input:number):any{return input}console.log(identity(raw)===raw,typeof identity(raw));", "true bigint\n"];
        yield return ["raw-symbol", "const raw:any=Symbol('raw');function identity(input:number):any{return input}console.log(identity(raw)===raw,typeof identity(raw));", "true symbol\n"];
        yield return ["raw-array", "const raw:any=[1,2];function identity(input:number):any{return input}console.log(identity(raw)===raw,typeof identity(raw));", "true object\n"];
        yield return ["raw-function", "const raw:any=()=>7;function identity(input:number):any{return input}console.log(identity(raw)===raw,typeof identity(raw));", "true function\n"];
        yield return ["numeric-operation-order", "const log:string[]=[];const raw:any={[Symbol.toPrimitive](hint:string){log.push(hint);return 7}};function twice(input:number):number{log.push('body');return input*2}console.log(twice(raw),log.join(','));", "14 body,number\n"];
        yield return ["known-numeric-control", "function identity(input:number):number{return input+1}console.log(identity(7));", "8\n"];
        yield return ["defaulted-parameter-control", "const raw:any={};function identity(input:number=7):any{return input}console.log(identity(raw)===raw,identity());", "true 7\n"];
        yield return ["rest-parameter-control", "const raw:any={};function identity(...input:number[]):any{return input[0]}console.log(identity(raw)===raw);", "true\n"];
        yield return ["distinct-binding-control", "function first(){const raw:any={};function identity(input:number):any{return input}return identity(raw)===raw}function second(){function identity(input:number):number{return input+1}return identity(7)}console.log(first(),second());", "true 8\n"];
        yield return ["original-typed-key-companion", "const log:string[]=[];let calls=0;const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return ++calls===1?'0':'1'}};function run(index:number){const p=new Uint8Array([1,9]);const result=p[index]+=7;console.log(p[0],p[1],result,calls,log.join(','))}run(key);", "1 8 8 2 string,string\n"];
        yield return ["typed-local-forward", "const raw:any={};let value:number=7;value=raw;function identity(input:number):any{return input}console.log(identity(value)===raw);", "true\n"];
        yield return ["captured-parameter-forward", "function run(value:number){const raw:any={};const write=()=>{value=raw};write();function identity(input:number):any{return input}console.log(identity(value)===raw)}run(7);", "true\n"];
        yield return ["tuple-spread-raw", "const raw:any={};function identity(input:number):any{return input}const args:[number]=[raw];console.log(identity(...args)===raw);", "true\n"];
        yield return ["raw-before-empty-spread", "const raw:any={};function identity(input:number):any{return input}console.log(identity(raw,...([] as []))===raw);", "true\n"];
        yield return ["empty-spread-before-raw", "const raw:any={};function identity(input:number):any{return input}console.log(identity(...([] as []),raw)===raw);", "true\n"];
        yield return ["omitted-raw-default", "const raw:any={};function identity(input:number=raw):any{return input}console.log(identity()===raw);", "true\n"];
        yield return ["numeric-fresh-spread-control", "function update(input:number):number{return input+1}console.log(update(...([7] as [number])));", "8\n"];
        yield return ["numeric-tuple-spread-control", "const args:[number]=[7];function update(input:number):number{return input+1}console.log(update(...args));", "8\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DynamicArgumentsRetainTheirSourceValues(
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
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
