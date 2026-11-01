using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GenericConstructorDependentDefaultTests
{
    [Theory]
    [InlineData("interface F { new<T,U extends T=T>(value:T):U } declare const f:F; const result:number=new f<number>(1);")]
    [InlineData("interface F { new<T,U extends T=T>(value:T):U } declare const f:F; const result:number=new f(1);")]
    [InlineData("interface F { new<T,U extends T=T,V extends U=U>(value:T):V } declare const f:F; const result:number=new f<number>(1);")]
    [InlineData("interface F { new<T,U extends {value:T}={value:T}>(value:T):U } declare const f:F; const result:number=new f<string>('ok').value.length;")]
    [InlineData("interface F { new<T,U extends T[]=T[]>(value:T):U } declare const f:F; const result:number=new f<number>(1)[0];")]
    [InlineData("interface F { new<T,U extends T=T>(value:T):U } declare const f:F; const result:1=new f<number,1>(1);")]
    [InlineData("interface F { new<T=number,U extends T=T>(value:T):U } declare const f:F; const result:number=new f(1);")]
    [InlineData("interface F { new<T extends {value:number},U extends T=T>(value:T):U } declare const f:F; const result:number=new f<{value:number}>({value:7}).value;")]
    [InlineData("interface F { new<T extends {value:number},U extends T=T>(value:T):U; new(value:string):string } declare const f:F; const result:string=new f('ok');")]
    public void ResolvesDefaultsAgainstEarlierTypeArguments(string source)
    {
        var result = new TypeChecker().CheckWithRecovery(TestHarness.ParseOrThrow(source));
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("interface F { new<T,U extends T=T>(value:T):U } declare const f:F; new f<number,string>(1);")]
    [InlineData("interface F { new<T,U extends T=T>(value:T):U } declare const f:F; new f<number>('wrong');")]
    [InlineData("interface F { new<T,U extends T=T>(value:T):U } declare const f:F; const result:string=new f<number>(1);")]
    [InlineData("interface F { new<T,U extends T=T,V extends U=U>(value:T):V } declare const f:F; const result:string=new f<number>(1);")]
    [InlineData("interface F { new<T extends {value:number},U extends T=T>(value:T):U } declare const f:F; new f<{value:string}>({value:'wrong'});")]
    [InlineData("interface F { new<T extends {value:number},U extends T=T>(value:T):U; new(value:string):string } declare const f:F; new f(true);")]
    public void PreservesConstraintArgumentAndResultValidation(string source)
    {
        var result = new TypeChecker().CheckWithRecovery(TestHarness.ParseOrThrow(source));
        Assert.NotEmpty(result.Diagnostics);
    }

}
