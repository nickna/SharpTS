using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ImmediateGlobalChecker3233Tests
{
    [Theory]
    [InlineData("setImmediate(()=>{},1,2);clearImmediate();", true)]
    [InlineData("const setImmediate=(value:number)=>value+1;const result:number=setImmediate(6);", true)]
    [InlineData("const clearImmediate=()=>9;const result:number=clearImmediate();", true)]
    [InlineData("setImmediate();", false)]
    [InlineData("setImmediate(7);", false)]
    public void ChecksActualGlobalsWithoutInterceptingLexicalFunctions(string source, bool valid)
    {
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        if (valid) Assert.Empty(diagnostics);
        else Assert.NotEmpty(diagnostics);
    }
}
