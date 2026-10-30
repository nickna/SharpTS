using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeDynamicConstructorSpreadTests
{
    public static IEnumerable<object[]> Cases() => DynamicClassConstructorSpreadTests.Cases();

    [Theory, MemberData(nameof(Cases))]
    public void NativeGuestKeepsExpandedConstructorArgumentsAndEffects(string name, string source, string expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), d => d.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = new ILCompiler("spread_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
        byte[] bytes = compiler.SaveToBytes(); using var image = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(image));
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
        if (Environment.GetEnvironmentVariable("SHARPTS_CONSTRUCTOR_SPREAD_NATIVE_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            string filename = string.Concat(name.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '_'));
            File.WriteAllBytes(Path.Combine(directory, filename + ".dll"), bytes);
        }
    }
}
