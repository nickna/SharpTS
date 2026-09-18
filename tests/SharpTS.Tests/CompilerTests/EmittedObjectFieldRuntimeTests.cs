using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedObjectFieldRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly PropertyInfo[] Handles = typeof(EmittedObjectFieldRuntime).GetProperties()
        .Where(p => p.PropertyType == typeof(Type) || p.PropertyType == typeof(MethodInfo)).ToArray();
    public static IEnumerable<object[]> HandleNames => Handles.Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void DeclarationsRejectMissingNullAndDuplicateValues(string name)
    {
        var owner = new EmittedRuntime().ObjectFields;
        var property = Handles.Single(p => p.Name == name);
        var missing = Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.GetValue(owner)).InnerException);
        Assert.Contains("'" + name + "'", missing.Message);
        Assert.IsType<ArgumentNullException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, null)).InnerException);
        var value = Handle(name); property.SetValue(owner, value);
        Assert.Same(value, property.GetValue(owner));
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, value)).InnerException);
        Assert.False(owner.IsComplete);
    }

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void FailedCompletionCanBeRepairedAndCompletedMetadataIsFrozen(string missing)
    {
        var owner = new EmittedRuntime().ObjectFields;
        foreach (var property in Handles.Where(p => p.Name != missing)) property.SetValue(owner, Handle(property.Name));
        var error = Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => Complete(owner)).InnerException);
        Assert.Contains("'" + missing + "'", error.Message);
        Assert.False(owner.IsComplete);
        Handles.Single(p => p.Name == missing).SetValue(owner, Handle(missing)); Complete(owner);
        Assert.True(owner.IsComplete);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => Complete(owner)).InnerException);
        foreach (var property in Handles)
            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, Handle(property.Name))).InnerException);
    }

    [Fact]
    public void ScopedEmitterPublishesBakedInterfaceMethodsBeforeCompletion()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var owner = new EmittedRuntime().ObjectFields;
        typeof(RuntimeEmitter).GetMethod("EmitHasFieldsInterface", Members)!.Invoke(emitter, [module, owner]);
        Assert.Same(builder, owner.Interface.Assembly);
        foreach (var property in Handles.Where(p => p.PropertyType == typeof(MethodInfo)))
            Assert.Same(owner.Interface, Assert.IsAssignableFrom<MethodInfo>(property.GetValue(owner)).DeclaringType);
        Assert.False(owner.IsComplete); Complete(owner); Assert.True(owner.IsComplete);
        VerifyInterface(SaveVerifyLoad(builder).GetType("$IHasFields")!);
    }

    [Fact]
    public void HelperAcceptsOnlyTheModuleAndRequiredOwner()
    {
        Assert.Equal(5, Handles.Length);
        Assert.NotSame(new EmittedRuntime().ObjectFields, new EmittedRuntime().ObjectFields);
        Assert.Null(typeof(EmittedRuntime).GetProperty("ObjectFields")!.SetMethod);
        foreach (string name in new[] { "IHasFieldsInterface", "IHasFieldsGetProperty", "IHasFieldsSetProperty", "IHasFieldsHasProperty", "IHasFieldsFieldsGetter" })
            Assert.Null(typeof(EmittedRuntime).GetProperty(name));
        Assert.Equal(new[] { typeof(ModuleBuilder), typeof(EmittedObjectFieldRuntime) },
            typeof(RuntimeEmitter).GetMethod("EmitHasFieldsInterface", Members)!.GetParameters().Select(p => p.ParameterType));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterOwnsFreshContractsAndInterfaceCallsPreserveGuestStorage(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedObjectFieldRuntime>(); var contracts = new HashSet<Type>();
        foreach (string source in new[] { "const n=1;", "const row={x:1,y:2};Object.keys(row);new Map();Buffer.from('x');", "const n=2;" })
        {
            var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
            var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var runtime = emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
            var owner = runtime.ObjectFields;
            Assert.True(owners.Add(owner)); Assert.True(contracts.Add(owner.Interface)); Assert.True(owner.IsComplete);
            Assert.Same(builder, owner.Interface.Assembly);
            foreach (var property in Handles.Where(p => p.PropertyType == typeof(MethodInfo)))
                Assert.Same(owner.Interface, Assert.IsAssignableFrom<MethodInfo>(property.GetValue(owner)).DeclaringType);
            var loaded = SaveVerifyLoad(builder); var contract = loaded.GetType("$IHasFields")!;
            VerifyInterface(contract); var objectType = loaded.GetType("$Object")!; Assert.True(contract.IsAssignableFrom(objectType));
            var data = new Dictionary<string, object> { ["number"] = 4d };
            var instance = Activator.CreateInstance(objectType, [data])!;
            Assert.Same(data, contract.GetProperty("Fields")!.GetValue(instance));
            Assert.Equal(4d, contract.GetMethod("GetProperty")!.Invoke(instance, ["number"]));
            Assert.Equal(true, contract.GetMethod("HasProperty")!.Invoke(instance, ["number"]));
            Assert.Equal(false, contract.GetMethod("HasProperty")!.Invoke(instance, ["missing"]));
            // The native field protocol returns null for a missing dictionary entry.
            // Guest property dispatch separately produces the undefined singleton.
            Assert.Null(contract.GetMethod("GetProperty")!.Invoke(instance, ["missing"]));
            var marker = new object(); contract.GetMethod("SetProperty")!.Invoke(instance, ["marker", marker]);
            Assert.Same(marker, data["marker"]); Assert.Same(marker, contract.GetMethod("GetProperty")!.Invoke(instance, ["marker"]));
            var undefined = loaded.GetType("$Undefined")!.GetField("Instance")!.GetValue(null);
            contract.GetMethod("SetProperty")!.Invoke(instance, ["undefined", undefined]);
            Assert.Same(undefined, contract.GetMethod("GetProperty")!.Invoke(instance, ["undefined"]));
            Assert.Equal(true, contract.GetMethod("HasProperty")!.Invoke(instance, ["undefined"]));
            data.Remove("marker"); Assert.Equal(false, contract.GetMethod("HasProperty")!.Invoke(instance, ["marker"]));
            Assert.True(owner.IsComplete);
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    private static object Handle(string name) => name == "Interface"
        ? typeof(object) : typeof(object).GetMethod(nameof(ToString))!;
    private static void Complete(EmittedObjectFieldRuntime owner) => typeof(EmittedObjectFieldRuntime).GetMethod("CompleteEmission", Members)!.Invoke(owner, null);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"object_fields_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
    private static void VerifyInterface(Type contract)
    {
        Assert.True(contract.IsPublic && contract.IsInterface && contract.IsAbstract);
        var property = Assert.Single(contract.GetProperties()); Assert.Equal("Fields", property.Name);
        Assert.Equal(typeof(Dictionary<string, object>), property.PropertyType); Assert.Null(property.SetMethod);
        var methods = contract.GetMethods().OrderBy(m => m.MetadataToken).ToArray();
        Assert.Equal(new[] { "get_Fields", "GetProperty", "SetProperty", "HasProperty" }, methods.Select(m => m.Name));
        Assert.All(methods, m => Assert.True(m.IsPublic && m.IsAbstract && m.IsVirtual && !m.IsStatic));
        Assert.Same(property.GetMethod, methods[0]); Assert.True(methods[0].IsSpecialName);
        Assert.Equal(typeof(Dictionary<string, object>), methods[0].ReturnType); Assert.Empty(methods[0].GetParameters());
        Assert.Equal(typeof(object), methods[1].ReturnType); Assert.Equal(new[] { typeof(string) }, methods[1].GetParameters().Select(p => p.ParameterType));
        Assert.Equal(typeof(void), methods[2].ReturnType); Assert.Equal(new[] { typeof(string), typeof(object) }, methods[2].GetParameters().Select(p => p.ParameterType));
        Assert.Equal(typeof(bool), methods[3].ReturnType); Assert.Equal(new[] { typeof(string) }, methods[3].GetParameters().Select(p => p.ParameterType));
    }
}
