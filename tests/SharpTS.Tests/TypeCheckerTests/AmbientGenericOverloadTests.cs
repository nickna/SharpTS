using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class AmbientGenericOverloadTests
{
    [Theory]
    [InlineData("declare function pick<T extends number>(x:T):T; declare function pick(x:string):string; function check(){const n:number=pick(1);const s:string=pick('ok');}")]
    [InlineData("declare function pick<T extends number>(x:T):T; declare function pick<S extends string>(x:S):S; function check(){const n:number=pick(1);const s:string=pick('ok');}")]
    [InlineData("declare function pick(x:string):string; declare function pick<T extends number>(x:T):T; function check(){const s:string=pick('ok');const n:number=pick<number>(1);}")]
    [InlineData("declare namespace A { function pick<T>(x:T):T; function pick(x:string):string; } declare namespace B { function pick(x:number):string; } function check(){const a:number=A.pick(1);const b:string=B.pick(1);}")]
    [InlineData("declare function invoke<A extends readonly any[],R>(target:(...args:A)=>R,args:Readonly<A>):R; function label(a:number,b:number):string{return 'ok';} function check(){const args:readonly [number,number]=[2,3];const s:string=invoke(label,args);}")]
    public void EachAmbientSignatureRetainsItsOwnBinderAndScope(string declarations)
    {
        Assert.Equal("ok\n", TestHarness.RunInterpreted(declarations + "\nconsole.log('ok');"));
    }

    [Theory]
    [InlineData("declare function pick<T extends number>(x:T):T; declare function pick(x:string):string; function check(){const bad:string=pick(1);}")]
    [InlineData("declare function pick<T extends number>(x:T):T; declare function pick<S extends string>(x:S):S; function check(){const bad:number=pick('ok');}")]
    [InlineData("declare function pick<T extends number>(x:T):T; declare function pick(x:string):string; function check(){pick<string>('ok');}")]
    [InlineData("declare function invoke<A extends readonly any[],R>(target:(...args:A)=>R,args:Readonly<A>):R; function label(a:number,b:number):string{return 'ok';} function check(){const args:[number,number]=[2,3];const bad:number=invoke(label,args);}")]
    [InlineData("function check(args:Readonly<[number,string]>){args[0]=2;}")]
    [InlineData("function check(args:Readonly<number[]>){args[0]=2;}")]
    public void TypedResultsExplicitConstraintsAndReadonlyWritesRemainChecked(string declarations)
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(declarations));
    }
}
