using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class TypedArrayElementTypeTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-minimal", "const a=new BigInt64Array(1);a[0]=7n;console.log(a[0]===7n);", false];
        yield return ["unsigned-element", "const a=new BigUint64Array(1);a[0]=7n;const value:bigint=a[0];console.log(value===7n);", false];
        yield return ["number-variable-index", "const a=new BigInt64Array(1);let index:number=0;a[index]=7n;const value:bigint=a[index];console.log(value===7n);", false];
        yield return ["bigint-union-elements", "const a=Math.random()?new BigInt64Array(1):new BigUint64Array(1);a[0]=7n;const value:bigint=a[0];console.log(value===7n);", false];
        yield return ["existing-any-value", "const value:any=7n;const a=new BigInt64Array(1);a[0]=value;console.log(a[0]===7n);", false];
        yield return ["existing-number-elements", "const a=new Int32Array(1);a[0]=7;const value:number=a[0];console.log(value===7);", false];
        yield return ["compound-bigint-element", "const a=new BigInt64Array(1);a[0]=7n;a[0]+=1n;console.log(a[0]===8n);", false];
        yield return ["bigint-read-as-number", "const a=new BigInt64Array(1);const value:number=a[0];", true];
        yield return ["signed-number-write", "const a=new BigInt64Array(1);a[0]=7;", true];
        yield return ["unsigned-number-write", "const a=new BigUint64Array(1);a[0]=7;", true];
        yield return ["number-bigint-write", "const a=new Int32Array(1);a[0]=7n;", true];
        yield return ["float-bigint-write", "const a=new Float64Array(1);a[0]=7n;", true];
        yield return ["mixed-union-bigint-write", "const a=Math.random()<0?new Int32Array(1):new BigInt64Array(1);a[0]=7n;console.log(a[0]===7n);", false];
        yield return ["mixed-union-number-write", "const a=Math.random()<0?new BigInt64Array(1):new Int32Array(1);a[0]=7;console.log(a[0]===7);", false];
        yield return ["string-bigint-write", "const a=new BigInt64Array(1);a[0]=\"7\";", true];
        yield return ["boolean-bigint-write", "const a=new BigInt64Array(1);a[0]=true;", true];
        yield return ["mixed-union-string-write", "const a=Math.random()?new BigInt64Array(1):new Int32Array(1);a[0]=\"7\";", true];
        yield return ["mixed-union-boolean-write", "const a=Math.random()?new BigInt64Array(1):new Int32Array(1);a[0]=true;", true];
        yield return ["mixed-target-and-value-unions", "const a=Math.random()<0?new Int32Array(1):new BigInt64Array(1);const value=Math.random()<0?7:7n;a[0]=value;console.log(a[0]===7n);", false];
        yield return ["mixed-value-union-string-rejected", "const a=Math.random()?new Int32Array(1):new BigInt64Array(1);const value=Math.random()?7n:\"wrong\";a[0]=value;", true];
        yield return ["numeric-targets-reject-bigint-union", "const a=Math.random()?new Int32Array(1):new Uint8Array(1);const value=Math.random()?7:7n;a[0]=value;", true];
    }

    [Theory, MemberData(nameof(Cases))]
    public void IndexReadsAndWritesUseTheActualElementKind(string name, string source, bool reject)
    {
        Assert.NotEmpty(name);
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        var modules = GenericInstantiationDiagnosticTests.CollectStrictModules(source);
        if (reject)
        {
            Assert.NotEmpty(diagnostics);
            Assert.NotEmpty(modules);
        }
        else
        {
            Assert.Empty(diagnostics);
            Assert.Empty(modules);
        }
    }

    [Fact]
    public void UncheckedBigIntReadsRetainTheExistingUndefinedPolicy()
    {
        const string source = "const a=new BigInt64Array(1);const value:bigint=a[0];";
        var statements = TestHarness.ParseOrThrow(source);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
        var options = TypeCheckerOptions.Strict with { NoUncheckedIndexedAccess = true };
        Assert.Contains(new TypeChecker(options).CheckWithRecovery(statements).Diagnostics, d => d.TsCode == "TS2322");
        Assert.Empty(new TypeChecker(options with { StrictNullChecks = false }).CheckWithRecovery(statements).Diagnostics);
    }
}
