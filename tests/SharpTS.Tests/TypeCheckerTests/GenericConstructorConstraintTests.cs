using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GenericConstructorConstraintTests
{
    [Theory]
    [InlineData("interface F { new<T extends {value:number}>(v:T):T } declare const f:F; new f({value:'wrong'});")]
    [InlineData("interface F { new<T extends {value:number}>(v:T):T } declare const f:F; new f<{value:string}>({value:'wrong'});")]
    [InlineData("interface F { new<T extends number>(v:T):T } declare const f:F; new f('wrong');")]
    [InlineData("interface F { new<T extends {value:number}, U extends T>(a:T,b:U):U } declare const f:F; new f({value:1},{other:2});")]
    public void RejectsTypeArgumentsOutsideConstructorConstraints(string source)
    {
        var result = new TypeChecker().CheckWithRecovery(TestHarness.ParseOrThrow(source));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2769");
    }

    [Theory]
    [InlineData("interface F { new<T extends {value:number}>(v:T):T } declare const f:F; const v:number=new f({value:1,extra:true}).value;")]
    [InlineData("interface F { new<T extends {value:number}>(v:T):T } declare const f:F; new f<{value:number}>({value:1});")]
    [InlineData("interface F { new<T extends {value:number}, U extends T>(a:T,b:U):U } declare const f:F; new f({value:1},{value:2,other:3});")]
    [InlineData("interface F { new<T extends {value:number}>(v:T):T; new(v:string):string } declare const f:F; const v:string=new f('fallback');")]
    public void PreservesValidConstraintsAndLaterConstructorOverloads(string source)
    {
        var result = new TypeChecker().CheckWithRecovery(TestHarness.ParseOrThrow(source));
        Assert.Empty(result.Diagnostics);
    }
}
