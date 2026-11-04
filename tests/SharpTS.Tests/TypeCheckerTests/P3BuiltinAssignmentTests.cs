using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class P3BuiltinAssignmentTests
{
    [Fact]
    public void ExistingArrayBindingAcceptsReplacementWithoutLibraries()
        => Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(
            TestHarness.ParseOrThrow("const replacement:any=function(){}; Array=replacement;")).Diagnostics);

    [Fact]
    public void LexicalShadowStillUsesItsDeclaredType()
        => Assert.Contains(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(
            TestHarness.ParseOrThrow("let Array:number=1; Array='wrong';")).Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2322");
}
