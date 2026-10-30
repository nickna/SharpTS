using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ConstructorTupleSpreadTests
{
    [Theory]
    [InlineData("class Box<T>{constructor(public value:T){}} const value=new Box<number>(...[42] as const); const n:number=value.value;")]
    [InlineData("class Box<T>{constructor(public value:T){}} const value=new Box(...[42] as const); const n:42=value.value;")]
    [InlineData("class Box<T>{constructor(public value:T){}} const C=Box; new C<number>(...([42] as const));")]
    [InlineData("class Pair{constructor(value:number,name:string){}} const args:readonly [number,string]=[1,'x']; new Pair(...args);")]
    [InlineData("class Pair{constructor(value:number,name:string){}} new Pair(...[1] as const,...['x'] as const);")]
    [InlineData("class Pair{constructor(value:number,name?:string){}} declare const args:readonly [number,string?]; new Pair(...args);")]
    [InlineData("class Pair{constructor(value:number,...names:string[]){}} declare const args:readonly [number,...string[]]; new Pair(...args);")]
    [InlineData("class Pair{constructor(...args:[number,string]){}} new Pair(...[1,'x'] as const);")]
    [InlineData("class Box{constructor(value:number);constructor(value:string);constructor(value:any){}} new Box(...[42] as const);")]
    [InlineData("class Base<T>{constructor(public value:T){}} class Child extends Base<number>{} new Child(...[42] as const);")]
    [InlineData("class Box{constructor(value=1){}} new Box(...[] as const);")]
    [InlineData("class Box{} new Box(...[] as const);")]
    [InlineData("interface Factory{new(value:number):{value:number}} declare const C:Factory; const n:number=new C(...[42] as const).value;")]
    [InlineData("interface Factory{new<T>(value:T):{value:T}} declare const C:Factory; const n:number=new C<number>(...[42] as const).value;")]
    [InlineData("interface Factory{new(value?:number):{}} declare const C:Factory; const args:[]=[]; new C(...args);")]
    [InlineData("interface Factory{new<T>(value?:T):{}} declare const C:Factory; new C<number>(...[] as const);")]
    [InlineData("class Box{constructor(value:number,fn:(n:number)=>number){}} new Box(...[1] as const,n=>n+1);")]
    public void TupleSpreadsSupplyTheirElementsToConstructorSlots(string source)
    {
        var result = Check(source);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("class Box{constructor(value:number){}} new Box(...['bad'] as const);", "TS2345")]
    [InlineData("class Box{constructor(value:number){}} new Box(...[1,2] as const);", "TS2554")]
    [InlineData("class Box{constructor(a:number,b:string){}} new Box(...[1] as const);", "TS2554")]
    [InlineData("class Box{constructor(value:number){}} const args:number[]=[1]; new Box(...args);", "TS2556")]
    [InlineData("class Box{constructor(...values:number[]){}} new Box(...[1,'bad'] as const);", "TS2345")]
    [InlineData("class Box{constructor(value:number);constructor(value:string);constructor(value:any){}} new Box(...[true] as const);", "TS2769")]
    [InlineData("class Box{} new Box(...[1] as const);", "TS2554")]
    [InlineData("class Box{constructor(value:number){}} new Box(...1);", "TS2488")]
    [InlineData("class Box{constructor(value:number,fn:(n:number)=>number){}} new Box(...[1] as const,n=>n.missing);", "TS2339")]
    public void ExpandedConstructorArgumentsStillEnforceTypesArityAndIterability(string source, string code)
    {
        Assert.Contains(Check(source).Diagnostics, diagnostic => diagnostic.TsCode == code);
    }

    private static TypeCheckDiagnosticResult Check(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements);
    }
}
