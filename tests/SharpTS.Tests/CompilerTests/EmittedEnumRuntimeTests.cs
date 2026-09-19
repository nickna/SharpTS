using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedEnumRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly PropertyInfo Handle = typeof(EmittedEnumRuntime).GetProperty("GetMemberName")!;

    [Fact]
    public void DeclarationRejectsMissingNullAndDuplicateValues()
    {
        var owner = new EmittedRuntime().Enums;
        Assert.Throws<InvalidOperationException>(() => owner.GetMemberName);
        Expect<ArgumentNullException>(() => Handle.SetValue(owner, null));
        var value = NewAssembly().DefineDynamicModule("main").DefineType("Handles")
            .DefineMethod("Lookup", MethodAttributes.Public | MethodAttributes.Static, typeof(string),
                [typeof(string), typeof(double), typeof(double[]), typeof(string[])]);
        Handle.SetValue(owner, value);
        Assert.Same(value, owner.GetMemberName);
        Expect<InvalidOperationException>(() => Handle.SetValue(owner, value));
        Assert.False(owner.IsComplete);
    }

    [Fact]
    public void MissingDeclarationCanBeSuppliedAfterFailedCompletionThenMetadataFreezes()
    {
        var owner = new EmittedRuntime().Enums;
        Expect<InvalidOperationException>(() => Complete(owner));
        Assert.False(owner.IsComplete);
        var value = NewAssembly().DefineDynamicModule("main").DefineType("Handles")
            .DefineMethod("Lookup", MethodAttributes.Public | MethodAttributes.Static, typeof(string),
                [typeof(string), typeof(double), typeof(double[]), typeof(string[])]);
        Handle.SetValue(owner, value); Complete(owner);
        Assert.True(owner.IsComplete);
        Expect<InvalidOperationException>(() => Complete(owner));
        Expect<InvalidOperationException>(() => Handle.SetValue(owner, value));
    }

    [Fact]
    public void ScopedHelperUsesOnlyItsDestinationAndEnumOwner()
    {
        Assert.NotSame(new EmittedRuntime().Enums, new EmittedRuntime().Enums);
        Assert.Null(typeof(EmittedRuntime).GetProperty("Enums")!.SetMethod);
        Assert.Null(typeof(EmittedRuntime).GetProperty("GetEnumMemberName"));
        var helper = typeof(RuntimeEmitter).GetMethod("EmitGetEnumMemberName", Members)!;
        Assert.Equal(new[] { typeof(TypeBuilder), typeof(EmittedEnumRuntime) }, helper.GetParameters().Select(p => p.ParameterType));
        var builder = NewAssembly(); var type = builder.DefineDynamicModule("main").DefineType("Probe", TypeAttributes.Public);
        var owner = new EmittedRuntime().Enums;
        helper.Invoke(new RuntimeEmitter(TypeProvider.Runtime), [type, owner]);
        Assert.Same(type, owner.GetMemberName.DeclaringType); Assert.False(owner.IsComplete);
        type.CreateType(); Complete(owner);
        var method = SaveVerifyLoad(builder).GetType("Probe")!.GetMethod("GetEnumMemberName")!;
        Assert.Equal("Second", method.Invoke(null, ["Values", 3.0, new[] { 2.0, 3.0 }, new[] { "First", "Second" }]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterOwnsFreshDeclarationsAndPreservesLookupAndDeployment(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedEnumRuntime>(); var handles = new HashSet<MethodBuilder>();
        foreach (string source in new[] { "const n=1;", "new Map();Buffer.from('x');", "const n=2;" })
        {
            var builder = NewAssembly(); var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), new RuntimeFeatureDetector().Detect(statements));
            var owner = runtime.Enums;
            Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete); Assert.True(handles.Add(owner.GetMemberName));
            Assert.Same(builder, owner.GetMemberName.Module.Assembly); Assert.Same(runtime.RuntimeType, owner.GetMemberName.DeclaringType);
            var loaded = SaveVerifyLoad(builder); var type = loaded.GetType("$Runtime")!; var method = type.GetMethod("GetEnumMemberName")!;
            Assert.True(method.IsPublic && method.IsStatic); Assert.Equal(typeof(string), method.ReturnType);
            Assert.Equal(new[] { typeof(string), typeof(double), typeof(double[]), typeof(string[]) }, method.GetParameters().Select(p => p.ParameterType));
            Assert.True(method.MetadataToken < type.GetMethod("ConcatTemplate")!.MetadataToken);
            string? Lookup(string? name, double key, double[] keys, string[] values) => (string?)method.Invoke(null, [name, key, keys, values]);
            Assert.Equal("Second", Lookup("Values", 3, [2, 3], ["First", "Second"]));
            Assert.Equal("Negative", Lookup(null, -2, [-2, .5], ["Negative", "Half"]));
            Assert.Equal("Half", Lookup("UnusedName", .5, [-2, .5], ["Negative", "Half"]));
            Assert.Equal("First", Lookup("DuplicateArray", 1, [1, 1], ["First", "Second"]));
            Assert.Equal("Zero", Lookup("Zero", -0.0, [0], ["Zero"]));
            // This is the preserved helper contract; missing guest enum keys have separate conformance coverage.
            foreach (double key in new[] { 99.0, double.NaN })
            {
                var error = Assert.IsType<Exception>(Assert.Throws<TargetInvocationException>(() => Lookup("Missing", key, [1], ["One"])).InnerException);
                Assert.Equal("Value not found in enum", error.Message);
            }
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    private static void Expect<T>(Action action) where T : Exception =>
        Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static void Complete(EmittedEnumRuntime owner) =>
        typeof(EmittedEnumRuntime).GetMethod("CompleteEmission", Members)!.Invoke(owner, null);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"enum_reverse_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
}
