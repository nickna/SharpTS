using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DeferredParameterDefaultScopeTests
{
    [Theory]
    [InlineData("class Example{constructor(public read:()=>number=()=>later,later:number=2){}}")]
    [InlineData("const later='outer';class Example{constructor(public read:()=>number=()=>later,later:number=2){}}")]
    [InlineData("class Example<T>{constructor(public read:()=>T=()=>later,later:T=null!){}}")]
    [InlineData("class Example{constructor(public read:()=>number=()=>later,{later}:{later:number}={later:2}){}}")]
    [InlineData("class Example{constructor(public read:()=>number=()=>later,[later]:[number]=[2]){}}")]
    [InlineData("class Example{method(read:()=>number=()=>later,later:number=2){return read();}}")]
    [InlineData("class Example{static method(read:()=>number=()=>later,later:number=2){return read();}}")]
    [InlineData("function example(read:()=>number=()=>later,later:number=2){return read();}")]
    [InlineData("const example=(read:()=>number=()=>later,later:number=2)=>read();")]
    [InlineData("const example=function(read:()=>number=()=>later,later:number=2){return read();};")]
    [InlineData("const example={method(read:()=>number=()=>later,later:number=2){return read();}};")]
    [InlineData("function invoke(read:()=>number):number{return read();}class Example{constructor(public value:number=invoke(()=>later),later:number=2){}}")]
    [InlineData("class Example{constructor(public read:()=>number=()=>read()){}}")]
    public void DeferredDefaultsSeeParameterBindings(string source) => Assert.Empty(Check(source).Diagnostics);

    [Theory]
    [InlineData("class Example{constructor(public read:number=later,later:number=2){}}", "TS2373")]
    [InlineData("const later=1;class Example{constructor(public read:number=later,later:number=2){}}", "TS2373")]
    [InlineData("class Example{constructor(public read:number=(()=>later)(),later:number=2){}}", "TS2373")]
    [InlineData("class Example{constructor(read:number=read){}}", "TS2372")]
    [InlineData("function example(read:number=later,later:number=2){}", "TS2373")]
    [InlineData("const example=(read:number=later,later:number=2)=>read;", "TS2373")]
    [InlineData("class Example{constructor(public read:()=>number=()=>later,later?:number){}}", "TS2322")]
    [InlineData("class Example{constructor(public read:()=>number=()=>later,later:string='x'){}}", "TS2322")]
    [InlineData("class Example{constructor(public read:()=>number=()=>missing,later:number=2){}}", "TS2304")]
    [InlineData("class Example{constructor(public read:()=>number=()=>later('bad'),later:(value:number)=>number=(value:number)=>value){}}", "TS2345")]
    [InlineData("class Example{constructor(public read:()=>number=()=>later,{later}:{later?:number}={}){}}", "TS2322")]
    [InlineData("class Example{constructor(public read:number=later,{later}:{later:number}={later:2}){}}", "TS2373")]
    [InlineData("function run(first=later,[later]:number[]=[1]){}", "TS2373")]
    public void ImmediateAndIncorrectDeferredReadsAreRejected(string source, string code) =>
        Assert.Contains(Check(source).Diagnostics, diagnostic => diagnostic.TsCode == code);

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(TypeCheckerOptions.Strict)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}
