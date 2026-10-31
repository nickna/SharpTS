using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class PromisePrimitiveNativeOutputTests
{
    [Theory, MemberData(nameof(PromisePrimitiveResolutionTests.Cases), MemberType = typeof(PromisePrimitiveResolutionTests))]
    public void NativeProfileKeepsPrimitiveAndObjectResolutionSeparate(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var bytes = Compile(source);
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void ForeignClrReceiverIsStillRefusedBeforeItsGetter()
    {
        var assembly = Assembly.Load(Compile("Promise.resolve(1).finally(()=>{});"));
        var helper = assembly.GetType("$Runtime")!.GetMethod("CoerceAwaitableToTask", BindingFlags.Public | BindingFlags.Static)!;
        var receiver = new ForeignReceiver();
        var error = Assert.Throws<TargetInvocationException>(() => helper.Invoke(null, [receiver]));
        Assert.Contains("unproved CLR receiver", error.InnerException!.Message);
        Assert.Equal(0, receiver.Calls);
    }

    private sealed class ForeignReceiver
    {
        public int Calls;
        public object? then { get { Calls++; return null; } }
    }

    private static byte[] Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker();
        var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = new ILCompiler("promise_primitives_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, types);
        var bytes = compiler.SaveToBytes();
        using var image = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(image));
        return bytes;
    }
}
