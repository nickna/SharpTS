using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class P3DefaultParameterInferenceTests
{
    [Theory]
    [InlineData("class Earlier{constructor(later=2,public read:()=>string=()=>later){}}")]
    [InlineData("function f(value=2):string{return value;}")]
    [InlineData("const f=(value=2):string=>value;")]
    [InlineData("const f=function(value=2):string{return value;};")]
    [InlineData("function f(value=2){}f('wrong');")]
    [InlineData("const f=(value=2)=>value;f('wrong');")]
    [InlineData("class C{constructor(value=2){}}new C('wrong');")]
    public void InferredDefaultsRejectIncompatibleReturnsAndCalls(string source)
        => Assert.NotEmpty(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);

    [Theory]
    [InlineData("function f(value=2):number{return value;}f();f(3);f(undefined);")]
    [InlineData("const f=(value=2):number=>value;f();f(3);f(undefined);")]
    [InlineData("const f=function(value='first'):string{return value;};f('next');")]
    [InlineData("class C{constructor(later=2,public read:()=>number=()=>later){}}new C();new C(3);")]
    public void WidenedDefaultsRemainOptionalAndSupplyTheirBodyTypes(string source)
        => Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
}
