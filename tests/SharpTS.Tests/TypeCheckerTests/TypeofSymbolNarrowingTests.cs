using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class TypeofSymbolNarrowingTests
{
    [Theory]
    [InlineData("const p:any=String.prototype;const s1=Symbol('one'),s2=Symbol('two');Object.defineProperty(p,'b',{value:1,configurable:true});p[s1]=1;Object.defineProperty(p,'10',{value:1,configurable:true});Object.defineProperty(p,'2',{value:1,configurable:true});Object.defineProperty(p,'a',{value:1,enumerable:true,configurable:true});p[s2]=2;const names=['b','10','2','a'];const keys=Reflect.ownKeys(p);let selected='',count=0;for(let i=0;i<keys.length;i++){const k=keys[i];if(k===s1||k===s2||(typeof k==='string'&&names.includes(k))){if(count++)selected+=',';selected+=typeof k==='symbol'?(k===s1?'s1':'s2'):k}}console.log(selected);\n")]
    [InlineData("function f(k:string|symbol):string{return typeof k==='symbol'?'s':k}")]
    [InlineData("function f(k:string|symbol):string{return typeof k!=='symbol'?k:'s'}")]
    [InlineData("function f(k:string|symbol):string{return 'symbol'===typeof k?'s':k}")]
    [InlineData("function f(k:string|symbol):string{return typeof k=='symbol'?'s':k}")]
    [InlineData("declare const key:unique symbol;function f(k:typeof key|string):string{return typeof k==='symbol'?'s':k}")]
    [InlineData("function f(o:{key:string|symbol}):string{return typeof o.key==='symbol'?'s':o.key}")]
    [InlineData("function f(k:string|number|symbol):string|number{return typeof k==='symbol'?'s':k}")]
    [InlineData("function f(k:string|symbol):string{if(typeof k==='symbol')return 's';return k}")]
    public void PreservesSymbolAndUniqueSymbolGuardBranches(string source)
    {
        var result = new TypeChecker().CheckWithRecovery(TestHarness.ParseOrThrow(source));
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("const s=Symbol();let out='';out+=s;")]
    [InlineData("function f(k:string|symbol){let out='';out+=typeof k==='symbol'?k:'s'}")]
    [InlineData("function f(k:string|symbol){const result:number=typeof k==='symbol'?0:k}")]
    [InlineData("function f(k:string|symbol){if(typeof k==='symbol'){const result:string=k}}")]
    [InlineData("declare const key:unique symbol;function f(k:typeof key|string){const result:string=typeof k!=='symbol'?k:key}")]
    [InlineData("function f(k:string|symbol,other:string|symbol){let out='';out+=typeof other==='symbol'?'s':k}")]
    public void RejectsRemainingSymbolOperandsAndInvalidBranchResults(string source)
    {
        var result = new TypeChecker().CheckWithRecovery(TestHarness.ParseOrThrow(source));
        Assert.NotEmpty(result.Diagnostics);
    }

}
