using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedNamespaceRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly PropertyInfo[] Handles = typeof(EmittedNamespaceRuntime).GetProperties()
        .Where(p => p.PropertyType != typeof(bool)).ToArray();
    public static IEnumerable<object[]> HandleNames => Handles.Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void DeclarationsRejectMissingNullAndDuplicateValues(string name)
    {
        var owner = new EmittedRuntime().Namespaces;
        var property = Handles.Single(p => p.Name == name);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.GetValue(owner)).InnerException);
        Assert.IsType<ArgumentNullException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, null)).InnerException);
        var value = Handle(property.PropertyType); property.SetValue(owner, value);
        Assert.Same(value, property.GetValue(owner));
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, value)).InnerException);
        Assert.False(owner.IsComplete);
    }

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void FailedCompletionCanBeRepairedAndCompletedMetadataIsFrozen(string missing)
    {
        var owner = new EmittedRuntime().Namespaces;
        foreach (var property in Handles.Where(p => p.Name != missing)) property.SetValue(owner, Handle(property.PropertyType));
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => Complete(owner)).InnerException);
        Assert.False(owner.IsComplete);
        var omitted = Handles.Single(p => p.Name == missing); omitted.SetValue(owner, Handle(omitted.PropertyType)); Complete(owner);
        Assert.True(owner.IsComplete);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => Complete(owner)).InnerException);
        foreach (var property in Handles)
            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, Handle(property.PropertyType))).InnerException);
    }

    [Fact]
    public void HelperAcceptsOnlyItsModuleAndNamespaceOwner()
    {
        Assert.Equal(4, Handles.Length);
        Assert.NotSame(new EmittedRuntime().Namespaces, new EmittedRuntime().Namespaces);
        Assert.Null(typeof(EmittedRuntime).GetProperty("Namespaces")!.SetMethod);
        foreach (string name in new[] { "TSNamespaceType", "TSNamespaceCtor", "TSNamespaceGet", "TSNamespaceSet" })
            Assert.Null(typeof(EmittedRuntime).GetProperty(name));
        Assert.Equal(new[] { typeof(ModuleBuilder), typeof(EmittedNamespaceRuntime) },
            typeof(RuntimeEmitter).GetMethod("EmitTSNamespaceClass", Members)!.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public void ScopedHelperCreatesNamespaceWithoutOtherRuntimeOwners()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var owner = new EmittedRuntime().Namespaces;
        typeof(RuntimeEmitter).GetMethod("EmitTSNamespaceClass", Members)!
            .Invoke(new RuntimeEmitter(TypeProvider.Runtime), [module, owner]);
        Assert.False(owner.IsComplete); Assert.True(owner.Type.IsCreated());
        Assert.Same(owner.Type, owner.Constructor.DeclaringType);
        Assert.Same(owner.Type, owner.Get.DeclaringType); Assert.Same(owner.Type, owner.Set.DeclaringType);
        Complete(owner); VerifyNamespace(SaveVerifyLoad(builder).GetType("$TSNamespace")!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterOwnsFreshNamespaceDeclarationsAndPreservesDeployment(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedNamespaceRuntime>(); var handles = new HashSet<object>();
        foreach (string source in new[] { "const n=1;", "namespace Values {export const n=3;}new Map();Buffer.from('x');", "const n=2;" })
        {
            var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
            var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var runtime = emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements)); var owner = runtime.Namespaces;
            Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete); Assert.True(owner.Type.IsCreated());
            foreach (var property in Handles) Assert.True(handles.Add(property.GetValue(owner)!));
            Assert.Same(builder, owner.Type.Assembly); Assert.Same(owner.Type, owner.Constructor.DeclaringType);
            Assert.Same(owner.Type, owner.Get.DeclaringType); Assert.Same(owner.Type, owner.Set.DeclaringType);
            var loaded = SaveVerifyLoad(builder); VerifyNamespace(loaded.GetType("$TSNamespace")!);
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    private static void VerifyNamespace(Type type)
    {
        Assert.True(type.IsPublic && type.IsSealed); Assert.True((type.Attributes & TypeAttributes.BeforeFieldInit) != 0);
        var fields = type.GetFields(Members).OrderBy(f => f.MetadataToken).ToArray();
        Assert.Equal(new[] { "_members", "_name" }, fields.Select(f => f.Name));
        Assert.All(fields, f => Assert.True(f.IsPrivate && !f.IsStatic && !f.IsInitOnly));
        Assert.Equal(typeof(Dictionary<string, object>), fields[0].FieldType); Assert.Equal(typeof(string), fields[1].FieldType);
        var constructor = Assert.Single(type.GetConstructors()); Assert.Equal(typeof(string), Assert.Single(constructor.GetParameters()).ParameterType);
        var get = type.GetMethod("Get")!; var set = type.GetMethod("Set")!; var display = type.GetMethod("ToString")!;
        Assert.True(get.IsPublic && !get.IsStatic && get.ReturnType == typeof(object));
        Assert.Equal(new[] { typeof(string) }, get.GetParameters().Select(p => p.ParameterType));
        Assert.True(set.IsPublic && !set.IsStatic && set.ReturnType == typeof(void));
        Assert.Equal(new[] { typeof(string), typeof(object) }, set.GetParameters().Select(p => p.ParameterType));
        Assert.True(constructor.MetadataToken < get.MetadataToken && get.MetadataToken < set.MetadataToken && set.MetadataToken < display.MetadataToken);
        Assert.True(display.IsVirtual); Assert.Equal(typeof(string), display.ReturnType); Assert.Empty(display.GetParameters());
        var first = constructor.Invoke(["First"]); var second = constructor.Invoke(["Second"]); var value = new object();
        Assert.Null(get.Invoke(first, ["missing"]));
        set.Invoke(first, ["value", value]); Assert.Same(value, get.Invoke(first, ["value"])); Assert.Null(get.Invoke(second, ["value"]));
        set.Invoke(first, ["value", 9.0]); Assert.Equal(9.0, get.Invoke(first, ["value"]));
        set.Invoke(first, ["nil", null]); Assert.Null(get.Invoke(first, ["nil"]));
        Assert.Equal("[namespace First]", first.ToString()); Assert.Equal("[namespace Second]", second.ToString());
    }

    private static object Handle(Type type)
    {
        var builder = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        if (type == typeof(TypeBuilder)) return builder;
        if (type == typeof(ConstructorBuilder)) return builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(string)]);
        return builder.DefineMethod("Value", MethodAttributes.Public, typeof(object), [typeof(string)]);
    }
    private static void Complete(EmittedNamespaceRuntime owner) => typeof(EmittedNamespaceRuntime).GetMethod("CompleteEmission", Members)!.Invoke(owner, null);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"namespace_values_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
}
