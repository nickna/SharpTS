using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ClassSignaturePreparationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedStaticClassPreparationKeepsOneCompleteAccessorDeclaration(bool native)
    {
        const string source = """
            class Parent { static inherited=1; }
            class Child extends Parent {
                static first=2;
                static method() {}
                static get label(){return 'tag';}
                static last=3;
            }
            const child:any=Child;
            console.log(child.label);
            """;
        var saved = Compile(source, native);
        var methods = saved.GetType("Child")!.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var accessor = Assert.Single(methods, method => method.Name == "$static_get_Label");
        Assert.NotNull(accessor.GetMethodBody());
        Assert.Equal("tag", accessor.Invoke(null, null));
        Assert.Equal("tag\n", TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeparateOwnersWithTheSameGetterSpellingKeepTheirOwnBodies(bool native)
    {
        const string source = """
            class Parent { static get value(){return 'first';} }
            class Child extends Parent { static get value(){return 'last';} }
            const parent:any=Parent; const child:any=Child;
            console.log(parent.value, child.value);
            """;
        var saved = Compile(source, native);
        var accessors = new[] { "Parent", "Child" }.Select(name => Assert.Single(saved.GetType(name)!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly),
            method => method.Name == "$static_get_Value")).ToArray();
        Assert.All(accessors, accessor => Assert.NotNull(accessor.GetMethodBody()));
        Assert.Equal(new[] { "first", "last" }, accessors.Select(accessor => Assert.IsType<string>(accessor.Invoke(null, null))));
        Assert.Equal("first last\n", TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
    }

    private static Assembly Compile(string source, bool native)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = new ILCompiler("signature_owners_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
        byte[] bytes = compiler.SaveToBytes(); using var stream = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); Assert.Empty(verifier.Verify(stream)); return Assembly.Load(bytes);
    }
}
