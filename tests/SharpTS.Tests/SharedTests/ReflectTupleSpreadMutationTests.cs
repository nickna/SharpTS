using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ReflectTupleSpreadMutationTests
{
    private const string Source = "const target:any={slot:7};const args:[any,any,any]=[target,'slot',8];console.log(Reflect.set(...args),target.slot);";

    [Theory, ModeData]
    public void ExpandedArgumentsUpdateTheActualTarget(ExecutionMode mode)
    {
        // The backend is also exercised with the builtin checker; the real
        // default-library overload contract is independently tracked by #3163.
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(Source);
            Assert.Empty(errors);
            Assert.Equal("true 8\n", output);
        }
        else Assert.Equal("true 8\n", TestHarness.RunInterpreted(Source));
    }

    [Fact]
    public void NativePreparedCallVerifiesAndUpdatesTheActualTarget()
    {
        var statements = TestHarness.ParseOrThrow(Source);
        var types = new TypeChecker().Check(statements);
        var compiler = new ILCompiler("reflect_spread_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
        var bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        Assert.Equal("true 8\n", TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
    }
}
