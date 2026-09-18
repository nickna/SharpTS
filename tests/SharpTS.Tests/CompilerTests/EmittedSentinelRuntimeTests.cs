using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedSentinelRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly PropertyInfo[] Handles = typeof(EmittedSentinelRuntime).GetProperties()
        .Where(p => p.PropertyType == typeof(Type) || p.PropertyType == typeof(FieldInfo)).ToArray();
    public static IEnumerable<object[]> HandleNames => Handles.Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void DeclarationsRejectMissingNullAndDuplicateValues(string name)
    {
        var owner = new EmittedRuntime().Sentinels;
        var property = Handles.Single(p => p.Name == name);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.GetValue(owner)).InnerException);
        Assert.IsType<ArgumentNullException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, null)).InnerException);
        var value = Handle(name);
        property.SetValue(owner, value);
        Assert.Same(value, property.GetValue(owner));
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, value)).InnerException);
        Assert.False(owner.IsComplete);
    }

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void FailedCompletionCanBeRepairedAndCompletedMetadataIsFrozen(string missing)
    {
        var owner = new EmittedRuntime().Sentinels;
        foreach (var property in Handles.Where(p => p.Name != missing)) property.SetValue(owner, Handle(property.Name));
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => Complete(owner)).InnerException);
        Assert.False(owner.IsComplete);
        Handles.Single(p => p.Name == missing).SetValue(owner, Handle(missing));
        Complete(owner);
        Assert.True(owner.IsComplete);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => Complete(owner)).InnerException);
        foreach (var property in Handles)
            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, Handle(property.Name))).InnerException);
    }

    [Fact]
    public void UndefinedIsAvailableBeforeLexicalDeclarationsAndCompletion()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var runtime = new EmittedRuntime();
        var owner = runtime.Sentinels;
        Assert.Throws<InvalidOperationException>(() => runtime.UndefinedInstance);
        Emit(emitter, "EmitUndefinedClass", module, owner);
        Assert.Equal("$Undefined", owner.UndefinedType.Name);
        Assert.Equal("Instance", owner.UndefinedInstance.Name);
        Assert.Same(owner.UndefinedInstance, runtime.UndefinedInstance);
        Assert.Same(owner.UndefinedType, owner.UndefinedInstance.DeclaringType);
        Assert.False(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(() => owner.LexicalUninitializedType);
        Assert.Throws<InvalidOperationException>(() => owner.LexicalUninitializedInstance);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => Complete(owner)).InnerException);
        Emit(emitter, "EmitLexicalUninitializedClass", module, owner);
        Complete(owner);
        Assert.True(owner.IsComplete);
        VerifySingletons(SaveVerifyLoad(builder));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterOwnsDistinctSentinelsForEveryAssembly(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedSentinelRuntime>(); var instances = new HashSet<object>();
        foreach (string source in new[] { "const n=1;", "const a:number[]=[1,2];a.shift();Buffer.from('x');new Map();", "const n=2;" })
        {
            var builder = NewAssembly(); var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), new RuntimeFeatureDetector().Detect(statements));
            var owner = runtime.Sentinels;
            Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete);
            Assert.Same(builder, owner.UndefinedType.Assembly); Assert.Same(builder, owner.LexicalUninitializedType.Assembly);
            Assert.Same(owner.UndefinedType, owner.UndefinedInstance.DeclaringType);
            Assert.Same(owner.LexicalUninitializedType, owner.LexicalUninitializedInstance.DeclaringType);
            var loaded = SaveVerifyLoad(builder);
            foreach (object value in VerifySingletons(loaded)) Assert.True(instances.Add(value));
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Fact]
    public void HelpersAcceptOnlyTheModuleAndRequiredOwner()
    {
        Assert.Equal(4, Handles.Length);
        Assert.NotSame(new EmittedRuntime().Sentinels, new EmittedRuntime().Sentinels);
        Assert.Null(typeof(EmittedRuntime).GetProperty("Sentinels")!.SetMethod);
        foreach (var property in Handles.Where(p => p.Name != nameof(EmittedRuntime.UndefinedInstance)))
            Assert.Null(typeof(EmittedRuntime).GetProperty(property.Name));
        // The next migration step removes this getter after moving its remaining consumers.
        var forwardingAccessor = typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.UndefinedInstance))!;
        Assert.Null(forwardingAccessor.SetMethod);
        Assert.Null(typeof(EmittedRuntime).GetField("<UndefinedInstance>k__BackingField", Members));
        foreach (string name in new[] { "EmitUndefinedClass", "EmitLexicalUninitializedClass" })
            Assert.Equal(new[] { typeof(ModuleBuilder), typeof(EmittedSentinelRuntime) }, typeof(RuntimeEmitter).GetMethod(name, Members)!.GetParameters().Select(p => p.ParameterType));
    }

    private static object Handle(string name) => name.EndsWith("Type", StringComparison.Ordinal)
        ? typeof(object) : typeof(string).GetField(nameof(string.Empty))!;
    private static void Complete(EmittedSentinelRuntime owner) => typeof(EmittedSentinelRuntime).GetMethod("CompleteEmission", Members)!.Invoke(owner, null);
    private static void Emit(RuntimeEmitter emitter, string name, ModuleBuilder module, EmittedSentinelRuntime owner) =>
        typeof(RuntimeEmitter).GetMethod(name, Members)!.Invoke(emitter, [module, owner]);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"sentinels_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
    private static object[] VerifySingletons(Assembly assembly)
    {
        var values = new List<object>();
        foreach (string name in new[] { "$Undefined", "$LexicalUninitialized" })
        {
            var type = assembly.GetType(name)!;
            Assert.True(type.IsPublic && type.IsSealed && (type.Attributes & TypeAttributes.BeforeFieldInit) != 0);
            Assert.Empty(type.GetConstructors()); Assert.Single(type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance));
            var field = type.GetField("Instance")!;
            Assert.True(field.IsPublic && field.IsStatic && field.IsInitOnly); Assert.Equal(type, field.FieldType);
            var value = field.GetValue(null)!; Assert.Same(value, field.GetValue(null)); values.Add(value);
        }
        Assert.NotSame(values[0], values[1]); Assert.NotEqual(values[0].GetType(), values[1].GetType());
        Assert.Equal("undefined", values[0].ToString());
        return values.ToArray();
    }
}
