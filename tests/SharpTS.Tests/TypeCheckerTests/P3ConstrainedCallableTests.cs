using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class P3ConstrainedCallableTests
{
    [Theory]
    [InlineData("function invoke<T extends () => number>(f:T):number{return f();}", null)]
    [InlineData("function invoke<T extends (x:string) => number>(f:T):number{return f('ok');}", null)]
    [InlineData("function invoke<T extends (x:string) => number>(f:T){return f(1);}", "TS2345")]
    [InlineData("function invoke<T extends () => number>(f:T):string{return f();}", "TS2322")]
    [InlineData("function invoke<T extends number>(f:T){return f();}", "TS2349")]
    public void ConstraintChecksArgumentsAndResultsWithoutErasingTheSourceType(string source, string? code)
    {
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(
            TestHarness.ParseOrThrow(source)).Diagnostics;
        if (code is null) Assert.Empty(diagnostics);
        else Assert.Contains(diagnostics, diagnostic => diagnostic.TsCode == code);
    }
}
