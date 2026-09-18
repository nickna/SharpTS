using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedUnionRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly PropertyInfo[] Handles = typeof(EmittedUnionRuntime).GetProperties()
        .Where(p => p.PropertyType == typeof(Type) || p.PropertyType == typeof(MethodInfo)).ToArray();
    public static IEnumerable<object[]> HandleNames => Handles.Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void DeclarationsRejectMissingNullAndDuplicateValues(string name)
    {
        var owner = new EmittedRuntime().UnionValues;
        var property = Handles.Single(p => p.Name == name);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.GetValue(owner)).InnerException);
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
        var owner = new EmittedRuntime().UnionValues;
        foreach (var property in Handles.Where(p => p.Name != missing)) property.SetValue(owner, Handle(property.Name));
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => Complete(owner)).InnerException);
        Assert.False(owner.IsComplete);
        Handles.Single(p => p.Name == missing).SetValue(owner, Handle(missing)); Complete(owner);
        Assert.True(owner.IsComplete);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => Complete(owner)).InnerException);
        foreach (var property in Handles)
            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, Handle(property.Name))).InnerException);
    }

    [Fact]
    public void ScopedEmitterPublishesTheBakedInterfaceAndGetterBeforeCompletion()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var owner = new EmittedRuntime().UnionValues;
        typeof(RuntimeEmitter).GetMethod("EmitIUnionTypeInterface", Members)!.Invoke(emitter, [module, owner]);
        Assert.Same(builder, owner.Interface.Assembly); Assert.Same(owner.Interface, owner.ValueGetter.DeclaringType);
        Assert.False(owner.IsComplete); Complete(owner); Assert.True(owner.IsComplete);
        VerifyInterface(SaveVerifyLoad(builder).GetType("$IUnionType")!);
    }

    [Fact]
    public void HelperAcceptsOnlyTheModuleAndRequiredOwner()
    {
        Assert.Equal(2, Handles.Length);
        Assert.NotSame(new EmittedRuntime().UnionValues, new EmittedRuntime().UnionValues);
        Assert.Null(typeof(EmittedRuntime).GetProperty("UnionValues")!.SetMethod);
        foreach (string name in new[] { "IUnionTypeInterface", "IUnionTypeValueGetter" })
            Assert.Null(typeof(EmittedRuntime).GetProperty(name));
        Assert.Equal(new[] { typeof(ModuleBuilder), typeof(EmittedUnionRuntime) },
            typeof(RuntimeEmitter).GetMethod("EmitIUnionTypeInterface", Members)!.GetParameters().Select(p => p.ParameterType));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterOwnsFreshUnionContractsAndTypeOfUnwrapsValues(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedUnionRuntime>(); var contracts = new HashSet<Type>();
        foreach (string source in new[] { "const n=1;", "function get(x:boolean):number|string{return x?1:'one';}new Map();Buffer.from('x');", "const n=2;" })
        {
            var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
            var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var runtime = emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
            var owner = runtime.UnionValues;
            Assert.True(owners.Add(owner)); Assert.True(contracts.Add(owner.Interface)); Assert.True(owner.IsComplete);
            Assert.Same(builder, owner.Interface.Assembly); Assert.Same(owner.Interface, owner.ValueGetter.DeclaringType);
            DefineWrapper(module, owner.Interface, owner.ValueGetter);
            var loaded = SaveVerifyLoad(builder); var contract = loaded.GetType("$IUnionType")!;
            VerifyInterface(contract); var wrapper = loaded.GetType("UnionProbe")!; Assert.True(contract.IsAssignableFrom(wrapper));
            var typeOf = loaded.GetType(runtime.Operators.TypeOf.DeclaringType!.FullName!)!.GetMethod(runtime.Operators.TypeOf.Name)!;
            var undefined = loaded.GetType("$Undefined")!.GetField("Instance")!.GetValue(null);
            object?[] values = [4.0, "value", true, null, undefined];
            string[] expected = ["number", "string", "boolean", "object", "undefined"];
            for (int i = 0; i < values.Length; i++)
            {
                var instance = Activator.CreateInstance(wrapper, [values[i]])!;
                Assert.Same(values[i], contract.GetProperty("Value")!.GetValue(instance));
                Assert.Equal(expected[i], typeOf.Invoke(null, [instance]));
                var nested = Activator.CreateInstance(wrapper, [instance])!;
                Assert.Equal(expected[i], typeOf.Invoke(null, [nested]));
            }
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    private static object Handle(string name) => name == "Interface"
        ? typeof(object) : typeof(object).GetMethod(nameof(ToString))!;
    private static void Complete(EmittedUnionRuntime owner) => typeof(EmittedUnionRuntime).GetMethod("CompleteEmission", Members)!.Invoke(owner, null);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"union_values_{Guid.NewGuid():N}"), typeof(object).Assembly);
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
        var property = Assert.Single(contract.GetProperties()); Assert.Equal("Value", property.Name);
        Assert.Equal(typeof(object), property.PropertyType); Assert.Null(property.SetMethod);
        var getter = Assert.Single(contract.GetMethods()); Assert.Equal(property.GetMethod, getter);
        Assert.Equal("get_Value", getter.Name); Assert.Equal(typeof(object), getter.ReturnType); Assert.Empty(getter.GetParameters());
        Assert.True(getter.IsPublic && getter.IsAbstract && getter.IsVirtual && getter.IsSpecialName);
    }
    private static void DefineWrapper(ModuleBuilder module, Type contract, MethodInfo contractGetter)
    {
        var type = module.DefineType("UnionProbe", TypeAttributes.Public | TypeAttributes.Sealed, typeof(object), [contract]);
        var field = type.DefineField("_value", typeof(object), FieldAttributes.Private | FieldAttributes.InitOnly);
        var ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(object)]);
        var il = ctor.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, field); il.Emit(OpCodes.Ret);
        var getter = type.DefineMethod("get_Value", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.NewSlot | MethodAttributes.SpecialName | MethodAttributes.HideBySig, typeof(object), Type.EmptyTypes);
        il = getter.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
        var property = type.DefineProperty("Value", PropertyAttributes.None, typeof(object), null);
        property.SetGetMethod(getter); type.DefineMethodOverride(getter, contractGetter); type.CreateType();
    }
}
