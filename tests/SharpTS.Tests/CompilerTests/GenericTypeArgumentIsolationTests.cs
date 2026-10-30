using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class GenericTypeArgumentIsolationTests
{
    private const string Original = "class Box<T>{next(){return new Box<T[]>();}} console.log(new Box<number>());";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OwnedClassParameterShadowsOuterClrAlias(bool native, bool composite)
    {
        string source = "@DotNetType(\"System.Text.StringBuilder\") declare class T{} " +
            "class Box<T>{next(){return new Box<" + (composite ? "T[]" : "T") + ">();}}";
        var saved = Build(source, native);
        var definition = Assert.Single(saved.GetTypes(), type => type.Name == "Box" && type.IsGenericTypeDefinition);
        var owner = definition.MakeGenericType(typeof(double));
        var next = owner.GetMethod("next")!.Invoke(Activator.CreateInstance(owner), null)!;
        Assert.Equal(composite ? typeof(object) : typeof(double), Assert.Single(next.GetType().GetGenericArguments()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EarlierGuestTypesCannotSupplyCompositeSourceTypeArguments(bool native)
    {
        var earlier = Build("class T{} class U{}", native: false);
        // Warm the same process-wide lookup that used to select this unrelated T.
        Assert.NotNull(SharpTS.Runtime.DotNet.DotNetTypeRegistry.Resolve("T"));
        var saved = Build(Original, native);
        Assert.DoesNotContain(saved.GetReferencedAssemblies(), reference =>
            reference.Name == earlier.GetName().Name || reference.Name!.StartsWith("test_", StringComparison.Ordinal));
        var definition = Assert.Single(saved.GetTypes(), type => type.IsGenericTypeDefinition && type.Name.StartsWith("Box", StringComparison.Ordinal));
        var box = Activator.CreateInstance(definition.MakeGenericType(typeof(double)))!;
        var next = box.GetType().GetMethod("next")!.Invoke(box, null)!;
        Assert.Equal(typeof(object), Assert.Single(next.GetType().GetGenericArguments()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclaredFrameworkAliasesStillResolveInsideArrays(bool native)
    {
        const string source = """
            @DotNetType("System.Text.StringBuilder") declare class T {}
            class Box<V>{}
            console.log(new Box<T[]>());
            """;
        Build("class T{}", native: false);
        var saved = Build(source, native, inspect: compiler =>
        {
            if (!native) return;
            var family = Assert.Single(compiler.NativeClosedFamilies!.Completed.Families);
            Assert.Contains(family.Contexts, context => context.GetGenericArguments() is [var argument]
                && argument.IsArray && argument.GetElementType() == typeof(System.Text.StringBuilder));
        });
        Assert.DoesNotContain(saved.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("test_", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitReferenceAliasesStillResolveInsideArrays(bool native)
    {
        const string source = """
            @DotNetType("ExternalSignatureFixture.Api") declare class T {}
            class Box<V>{}
            console.log(new Box<T[]>());
            """;
        Build("class T{}", native: false);
        string reference = Path.Combine(AppContext.BaseDirectory, "ExternalSignatureFixture.dll");
        var saved = Build(source, native, [reference], compiler =>
        {
            if (!native) return;
            var family = Assert.Single(compiler.NativeClosedFamilies!.Completed.Families);
            Assert.Contains(family.Contexts, context => context.GetGenericArguments() is [var argument]
                && argument.IsArray && argument.GetElementType()!.FullName == "ExternalSignatureFixture.Api");
        });
        Assert.Contains(saved.GetReferencedAssemblies(), reference => reference.Name == "ExternalSignatureFixture");
        Assert.DoesNotContain(saved.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("test_", StringComparison.Ordinal));
    }

    private static Assembly Build(string source, bool native, IReadOnlyList<string>? references = null, Action<ILCompiler>? inspect = null)
    {
        var statements = new Parser(new Lexer(source).ScanTokens(), DecoratorMode.Legacy).ParseOrThrow();
        var checker = new TypeChecker();
        checker.SetDecoratorMode(DecoratorMode.Legacy);
        var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var closure = native && references is not null ? NativeManagedReferenceClosure.Capture(AppContext.BaseDirectory, references, null) : null;
        using var loader = closure?.CreateLoader();
        var compiler = native
            ? new ILCompiler("scoped_type_arg_" + Guid.NewGuid().ToString("N"), false, true, null, null, references, OutputTarget.Dll)
            : new ILCompiler("test_" + Guid.NewGuid().ToString("N"), false, false, null, null, references);
        compiler.SetDecoratorMode(DecoratorMode.Legacy);
        if (native) compiler.EnableNativeAotProjectOutput(closure, loader);
        compiler.Compile(statements, types);
        var bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        inspect?.Invoke(compiler);
        return Assembly.Load(bytes);
    }
}
