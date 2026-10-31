using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class PromiseResolveSubclassNativeOutputTests
{
    [Theory, MemberData(nameof(PromiseResolveSubclassThenTests.NativeCases), MemberType = typeof(PromiseResolveSubclassThenTests))]
    public void SavedNativeModeOutputPreservesTheLiveThenProtocol(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker();
        var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = new ILCompiler("promise_resolve_subclass_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, types);
        var bytes = compiler.SaveToBytes();
        using var image = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(image));
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(30)));
    }
}
