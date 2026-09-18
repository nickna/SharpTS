using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedSuperMethodRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesParentLookupAndWrapperIsolation(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedReflectedMethodRuntime>();
        foreach (string source in new[] { "const n=1;", "class A { value(){return 1;} } class B extends A { value(){return super.value()+1;} } new B().value();", "const n=2;" })
        {
            var builder = NewAssembly(); var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), new RuntimeFeatureDetector().Detect(statements));
            var owner = runtime.ReflectedMethods;
            Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete);
            Assert.Same(builder, owner.SuperMethod.Module.Assembly); Assert.Same(runtime.RuntimeType, owner.SuperMethod.DeclaringType);
            var loaded = SaveVerifyLoad(builder); var type = loaded.GetType("$Runtime")!;
            var lookup = type.GetMethod("GetSuperMethod")!;
            Assert.True(lookup.IsPublic && lookup.IsStatic); Assert.Equal(typeof(object), lookup.ReturnType);
            Assert.Equal(new[] { typeof(object), typeof(string) }, lookup.GetParameters().Select(p => p.ParameterType));
            var instance = new Child();
            object? Get(object? target, string name) => lookup.Invoke(null, [target, name]);
            object? Invoke(object wrapper) => type.GetMethod(runtime.Invocation.Value.Name)!.Invoke(null, [wrapper, Array.Empty<object>()]);
            var direct = Get(instance, nameof(Parent.Direct))!;
            var inherited = Get(instance, nameof(Grandparent.Inherited))!;
            Assert.Equal("parent", Invoke(direct)); Assert.Equal("grandparent", Invoke(inherited));
            Assert.NotSame(direct, Get(instance, nameof(Parent.Direct)));
            Assert.Null(Get(instance, "Missing")); Assert.Null(Get(null, "Direct")); Assert.Null(Get(new object(), "ToString"));
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Fact]
    public void LookupUsesTheSuppliedWrapperConstructor()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var wrapper = module.DefineType("Wrapper", TypeAttributes.Public);
        var target = wrapper.DefineField("Target", typeof(object), FieldAttributes.Public);
        var method = wrapper.DefineField("Method", typeof(MethodInfo), FieldAttributes.Public);
        var ctor = wrapper.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(object), typeof(MethodInfo)]);
        var il = ctor.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, target);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stfld, method); il.Emit(OpCodes.Ret);
        var type = module.DefineType("Probe", TypeAttributes.Public); var owner = new EmittedRuntime().ReflectedMethods;
        typeof(RuntimeEmitter).GetMethod("EmitGetSuperMethod", Members)!.Invoke(new RuntimeEmitter(TypeProvider.Runtime), [type, owner, ctor]);
        Assert.Same(type, owner.SuperMethod.DeclaringType); Assert.False(owner.IsComplete);
        wrapper.CreateType(); type.CreateType(); var loaded = SaveVerifyLoad(builder); var lookup = loaded.GetType("Probe")!.GetMethod("GetSuperMethod")!;
        var instance = new Child();
        foreach (var (name, declaringType) in new[] { ("Direct", typeof(Parent)), ("Inherited", typeof(Grandparent)), ("Hidden", typeof(Parent)) })
        {
            var value = lookup.Invoke(null, [instance, name])!;
            Assert.Equal("Wrapper", value.GetType().Name);
            Assert.Same(instance, value.GetType().GetField("Target")!.GetValue(value));
            var found = Assert.IsAssignableFrom<MethodInfo>(value.GetType().GetField("Method")!.GetValue(value));
            Assert.Equal(name, found.Name); Assert.Equal(declaringType, found.DeclaringType);
        }
        Assert.Null(lookup.Invoke(null, [instance, "Missing"]));
    }

    [Fact]
    public void ParentLookupBoundaryHasOnlyItsOwnerAndConstructor()
    {
        Assert.Null(typeof(EmittedRuntime).GetProperty("GetSuperMethod"));
        var method = typeof(RuntimeEmitter).GetMethod("EmitGetSuperMethod", Members)!;
        Assert.Equal(new[] { typeof(TypeBuilder), typeof(EmittedReflectedMethodRuntime), typeof(ConstructorInfo) }, method.GetParameters().Select(p => p.ParameterType));
    }

    public class Grandparent { public object Inherited() => "grandparent"; }
    public class Parent : Grandparent
    {
        public object Direct() => "parent";
        protected object Hidden() => "hidden";
    }
    public sealed class Child : Parent { public new object Direct() => "child"; }

    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"super_method_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
}
