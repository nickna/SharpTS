using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class P3TypedArrayUnionKeysTests
{
    [Theory]
    [InlineData("'0'", "7", false)]
    [InlineData("'0'", "7n", false)]
    [InlineData("'01'", "7", true)]
    [InlineData("'named'", "7", true)]
    [InlineData("'0'", "'wrong'", true)]
    public void CanonicalNumericStringKeysUseTheNumericUnionDomain(string key, string value, bool reject)
    {
        string source = $"function set(a:Int32Array|BigInt64Array){{a[{key}]={value};}}";
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(
            TestHarness.ParseOrThrow(source)).Diagnostics;
        var modules = GenericInstantiationDiagnosticTests.CollectStrictModules(source);
        if (reject) { Assert.NotEmpty(diagnostics); Assert.NotEmpty(modules); }
        else { Assert.Empty(diagnostics); Assert.Empty(modules); }
    }
}
