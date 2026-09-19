using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedReflectRuntimeTests
{
    private static readonly string[] WrapperNames =
    [
        "apply", "construct", "defineProperty", "deleteProperty", "get", "getOwnPropertyDescriptor",
        "getPrototypeOf", "has", "isExtensible", "ownKeys", "preventExtensions", "set", "setPrototypeOf"
    ];
    private static readonly int[] WrapperLengths = [3, 2, 3, 2, 2, 2, 1, 2, 1, 1, 1, 3, 2];

    private static PropertyInfo[] Handles(Type type) => type.GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType)).ToArray();

    private static IEnumerable<(string Name, object Owner)> Groups(EmittedReflectRuntime owner)
    {
        yield return ("Required", owner);
        if (owner.Assignment is { } assignment) yield return ("Assignment", assignment);
        if (owner.Namespace is { } reflectNamespace) yield return ("Namespace", reflectNamespace);
        if (owner.Metadata is { } metadata) yield return ("Metadata", metadata);
    }

    public static IEnumerable<object[]> HandleNames => new[]
    {
        ("Required", typeof(EmittedReflectRuntime)), ("Assignment", typeof(EmittedReflectAssignment)),
        ("Namespace", typeof(EmittedReflectNamespace)), ("Metadata", typeof(EmittedReflectMetadata))
    }.SelectMany(group => Handles(group.Item2).Select(property => new object[] { group.Item1, property.Name }));

    public static IEnumerable<object[]> RequiredWrappers => WrapperNames.Select(name => new object[] { name });

    public static IEnumerable<object[]> IndependentSelections => Enumerable.Range(0, 8)
        .SelectMany(mask => new[] { new object[] { mask, false }, new object[] { mask, true } });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingHandleCanBeRepairedWithoutFreezingAnySelectedCapability(string group, string name)
    {
        var owner = CreateDeclarations(group, name);
        var selected = Groups(owner).Single(item => item.Name == group).Owner;
        var property = selected.GetType().GetProperty(name)!;
        var read = Assert.Throws<TargetInvocationException>(() => property.GetValue(selected));
        Assert.Contains($"'{name}'", Assert.IsType<InvalidOperationException>(read.InnerException).Message);
        var write = Assert.Throws<TargetInvocationException>(() => property.SetValue(selected, null));
        Assert.IsType<ArgumentNullException>(write.InnerException);
        Assert.Contains($"'{name}'", Assert.Throws<InvalidOperationException>(owner.CompleteEmission).Message);
        AssertAllMutable(owner);
        var replacement = Groups(CreateDeclarations()).Single(item => item.Name == group).Owner;
        property.SetValue(selected, property.GetValue(replacement));
        owner.CompleteEmission();
        AssertFrozen(owner);
    }

    [Theory]
    [MemberData(nameof(RequiredWrappers))]
    public void MissingWrapperCanBeRepairedWithoutFreezingEarlierCapabilities(string name)
    {
        var owner = CreateDeclarations(wrapperOmission: name);
        var reflectNamespace = owner.RequireNamespace();
        var view = reflectNamespace.ValueFormMethods;
        Assert.Contains($"'{name}'", Assert.Throws<InvalidOperationException>(owner.CompleteEmission).Message);
        AssertAllMutable(owner);
        reflectNamespace.RegisterValueFormMethod(name, CreateDeclarations().RequireNamespace().ValueFormMethods[name]);
        Assert.Equal(13, view.Count);
        owner.CompleteEmission();
        AssertFrozen(owner);
    }

    [Fact]
    public void RequiredOwnerAndOptionalCapabilitiesHaveExplicitAvailability()
    {
        var owner = new EmittedRuntime().Reflect;
        Assert.NotSame(owner, new EmittedRuntime().Reflect);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Reflect))!.SetMethod);
        Assert.Equal(19, HandleNames.Count());
        Assert.Null(owner.Assignment);
        Assert.Null(owner.Namespace);
        Assert.Null(owner.Metadata);
        Assert.Throws<InvalidOperationException>(() => owner.RequireAssignment());
        Assert.Throws<InvalidOperationException>(() => owner.RequireNamespace());
        Assert.Throws<InvalidOperationException>(() => owner.RequireMetadata());
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        owner.Get = CreateDeclarations().Get;
        owner.CompleteEmission();
        AssertFrozen(owner);

        var selected = new EmittedReflectRuntime();
        selected.BeginAssignmentEmission();
        selected.BeginNamespaceEmission();
        selected.BeginMetadataEmission();
        Assert.Throws<InvalidOperationException>(selected.BeginAssignmentEmission);
        Assert.Throws<InvalidOperationException>(selected.BeginNamespaceEmission);
        Assert.Throws<InvalidOperationException>(selected.BeginMetadataEmission);
    }

    [Fact]
    public void NamespaceRegistryIsOrdinalAndRejectsAliasesDuplicatesAndInvalidWrites()
    {
        var owner = CreateDeclarations();
        var reflectNamespace = owner.RequireNamespace();
        var view = reflectNamespace.ValueFormMethods;
        var method = view["get"];
        Assert.Equal(WrapperNames, view.Keys);
        Assert.False(view.ContainsKey("GET"));
        Assert.Throws<ArgumentException>(() => reflectNamespace.RegisterValueFormMethod("GET", method));
        Assert.Throws<ArgumentNullException>(() => reflectNamespace.RegisterValueFormMethod(null!, method));
        Assert.Throws<ArgumentNullException>(() => reflectNamespace.RegisterValueFormMethod("get", null!));
        Assert.Throws<InvalidOperationException>(() => reflectNamespace.RegisterValueFormMethod("get", method));
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, MethodBuilder>>(view);
        Assert.True(dictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => dictionary.Remove("get"));
        Assert.Throws<NotSupportedException>(() => dictionary["get"] = method);
        Assert.Same(method, view["get"]);
        owner.CompleteEmission();
        AssertFrozen(owner);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PhaseOneAssignmentShellsFollowProvidedCapability(bool selected, bool globalReflectFlag)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("reflect_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var runtime = new EmittedRuntime();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var features = Detect();
        features.UsesReflect = globalReflectFlag;
        typeof(RuntimeEmitter).GetField("_features", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(emitter, features);
        if (selected) runtime.Reflect.BeginAssignmentEmission();
        typeof(RuntimeEmitter).GetMethod("DefineRuntimeClassPhase1", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(emitter, [module, runtime]);
        Assert.Throws<InvalidOperationException>(() => runtime.Reflect.Get);
        Assert.Null(runtime.Reflect.Namespace);
        Assert.Null(runtime.Reflect.Metadata);
        if (selected)
        {
            var assignment = runtime.Reflect.RequireAssignment();
            Assert.Same(runtime.RuntimeClass.Type, assignment.Set.DeclaringType);
            Assert.Same(runtime.RuntimeClass.Type, assignment.DefineProperty.DeclaringType);
            Assert.Equal(0, assignment.Set.GetILGenerator().ILOffset);
            Assert.Equal(0, assignment.DefineProperty.GetILGenerator().ILOffset);
            Assert.Throws<InvalidOperationException>(() => assignment.DefinePropertyObjectAdapter);
            var caller = runtime.RuntimeClass.Type.DefineMethod("ForwardCaller", MethodAttributes.Public | MethodAttributes.Static,
                typeof(bool), [typeof(object), typeof(object), typeof(object)]);
            var il = caller.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, assignment.DefineProperty);
            il.Emit(OpCodes.Ret);
            Assert.True(il.ILOffset > 0);
        }
        else Assert.Null(runtime.Reflect.Assignment);
        Assert.Throws<InvalidOperationException>(runtime.Reflect.CompleteEmission);
        AssertAllMutable(runtime.Reflect);
    }

    [Theory]
    [MemberData(nameof(IndependentSelections))]
    public void IndependentSelectionsPreserveSavedSignaturesHostingAndMutableGuestState(int mask, bool hosted)
    {
        var runtime = EmitRuntime(mask, hosted);
        Assert.Equal((mask & 3) != 0, runtime.Reflect.Assignment is not null);
        Assert.Equal((mask & 1) != 0, runtime.Reflect.Namespace is not null);
        Assert.Equal((mask & 4) != 0, runtime.Reflect.Metadata is not null);
        Assert.Equal((mask & 2) != 0, runtime.RequiredSharpTSRuntimeReasons.Contains("Proxy"));
        AssertFrozen(runtime.Reflect);
        using var bytes = Save(runtime);
        Verify(bytes);
        var assembly = Assembly.Load(bytes.ToArray());
        AssertReferences(assembly, hosted);
        AssertSavedContracts(runtime, assembly, new Dictionary<string, object> { ["x"] = 7d }, []);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullEmissionPreservesAllCapabilities(bool hosted)
    {
        var runtime = EmitRuntime(null, hosted);
        AssertFrozen(runtime.Reflect);
        using var bytes = Save(runtime);
        Verify(bytes);
        var assembly = Assembly.Load(bytes.ToArray());
        AssertReferences(assembly, hosted);
        AssertSavedContracts(runtime, assembly, new Dictionary<string, object> { ["x"] = 7d }, []);
    }

    [Fact]
    public void ReusedEmitterKeepsOwnersRegistriesAndMetadataInTheirOwnSavedOutputs()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var owners = new List<EmittedReflectRuntime>();
        var stores = new List<object>();
        var target = new Dictionary<string, object> { ["x"] = 7d };
        foreach (var mask in new[] { 7, 0, 4, 2, 7 })
        {
            var runtime = EmitRuntime(mask, emitter: emitter);
            Assert.DoesNotContain(runtime.Reflect, owners);
            if (runtime.Reflect.Namespace is { } current)
                Assert.DoesNotContain(owners, previous => ReferenceEquals(previous.Namespace?.ValueFormMethods, current.ValueFormMethods));
            owners.Add(runtime.Reflect);
            AssertFrozen(runtime.Reflect);
            using var bytes = Save(runtime);
            Verify(bytes);
            var assembly = Assembly.Load(bytes.ToArray());
            AssertReferences(assembly, false);
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
                reference.Name!.StartsWith("reflect_storage_", StringComparison.Ordinal));
            target["x"] = 7d;
            AssertSavedContracts(runtime, assembly, target, stores);
        }
    }

    private static void AssertSavedContracts(EmittedRuntime runtime, Assembly assembly,
        Dictionary<string, object> target, List<object> stores)
    {
        var type = assembly.GetType("$Runtime")!;
        var owner = runtime.Reflect;
        var get = type.GetMethod("ReflectGet")!;
        Assert.Equal(typeof(object), get.ReturnType);
        Assert.Equal(new[] { typeof(object), typeof(string), typeof(object) }, get.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(7d, Call(type, "ReflectGet", target, "x", target));
        Assert.Equal(owner.Assignment is not null, type.GetMethod("ReflectSet") is not null);
        if (owner.Assignment is { } assignment)
        {
            Assert.Equal(typeof(bool), type.GetMethod(assignment.Set.Name)!.ReturnType);
            Assert.Equal(typeof(bool), type.GetMethod(assignment.DefineProperty.Name)!.ReturnType);
            Assert.Equal(typeof(object), type.GetMethod(assignment.DefinePropertyObjectAdapter.Name)!.ReturnType);
            Assert.True(type.GetMethod(assignment.Set.Name)!.MetadataToken < get.MetadataToken);
            Assert.True(type.GetMethod(assignment.DefineProperty.Name)!.MetadataToken < get.MetadataToken);
            Assert.Equal(true, Call(type, "ReflectSet", target, "x", 9d, target));
            Assert.Equal(9d, Call(type, "ReflectGet", target, "x", target));
        }
        const BindingFlags fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        Assert.Equal(owner.Namespace is not null, type.GetField("_reflectSingleton", fields) is not null);
        if (owner.Namespace is { } reflectNamespace)
        {
            Assert.Equal(WrapperNames, reflectNamespace.ValueFormMethods.Keys);
            var singleton = Assert.IsType<Dictionary<string, object>>(type.GetField(reflectNamespace.SingletonField.Name, fields)!.GetValue(null));
            var tokens = new List<int>();
            for (int index = 0; index < WrapperNames.Length; index++)
            {
                string name = WrapperNames[index];
                var wrapper = type.GetMethod("ReflectValueForm_" + name)!;
                Assert.True(wrapper.IsPublic && wrapper.IsStatic);
                Assert.Equal(typeof(object), wrapper.ReturnType);
                Assert.Equal(typeof(object[]), Assert.Single(wrapper.GetParameters()).ParameterType);
                tokens.Add(wrapper.MetadataToken);
                Assert.Equal((double)WrapperLengths[index], Call(type, "GetProperty", singleton[name], "length"));
            }
            Assert.Equal(tokens.OrderBy(token => token), tokens);
            singleton["get"] = "replacement";
            Assert.Equal("replacement", Call(type, "GetProperty", singleton, "get"));
            Assert.True(singleton.Remove("get"));
            Assert.Equal("$Undefined", Call(type, "GetProperty", singleton, "get")!.GetType().Name);
        }
        else Assert.DoesNotContain(type.GetMethods(), method => method.Name.StartsWith("ReflectValueForm_", StringComparison.Ordinal));

        var field = type.GetField("_metadataStore", fields);
        Assert.Equal(owner.Metadata is not null, field is not null);
        Assert.Equal(owner.Metadata is not null, assembly.GetType("$ReflectMetadataDecorator") is not null);
        if (owner.Metadata is null) return;
        Assert.Null(field!.GetValue(null));
        Assert.Null(Call(type, "ReflectGetMetadata", "key", target, null));
        Assert.Null(field.GetValue(null));
        Call(type, "ReflectDefineMetadata", "key", "value", target, null);
        Assert.Equal("value", Call(type, "ReflectGetMetadata", "key", target, null));
        Assert.Equal(true, Call(type, "ReflectHasMetadata", "key", target, null));
        Assert.Equal("$Array", Call(type, "ReflectGetMetadataKeys", target, null)!.GetType().Name);
        var store = field.GetValue(null)!;
        Assert.DoesNotContain(stores, old => ReferenceEquals(old, store));
        stores.Add(store);
        Assert.Equal(true, Call(type, "ReflectDeleteMetadata", "key", target, null));
        Assert.Null(Call(type, "ReflectGetMetadata", "key", target, null));
        var decorator = assembly.GetType("$ReflectMetadataDecorator")!;
        var closure = Activator.CreateInstance(decorator, ["decorated", "closure-value"]);
        Assert.Same(target, decorator.GetMethod("Invoke")!.Invoke(closure, [new object[] { target }]));
        Assert.Equal("closure-value", Call(type, "ReflectGetMetadata", "decorated", target, null));
    }

    private static EmittedReflectRuntime CreateDeclarations(string? groupOmission = null, string? handleOmission = null,
        string? wrapperOmission = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"reflect_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("ReflectDeclarations");
        var owner = new EmittedReflectRuntime();
        owner.BeginAssignmentEmission();
        owner.BeginNamespaceEmission();
        owner.BeginMetadataEmission();
        foreach (var (group, selected) in Groups(owner))
            foreach (var property in Handles(selected.GetType()))
            {
                if (group == groupOmission && property.Name == handleOmission) continue;
                object value = property.PropertyType == typeof(FieldBuilder)
                    ? type.DefineField(group + property.Name, typeof(object), FieldAttributes.Public | FieldAttributes.Static)
                    : property.PropertyType == typeof(ConstructorBuilder)
                        ? type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes)
                        : type.DefineMethod(group + property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
                property.SetValue(selected, value);
            }
        foreach (var name in WrapperNames.Where(name => name != wrapperOmission))
            owner.RequireNamespace().RegisterValueFormMethod(name,
                type.DefineMethod("Wrapper_" + name, MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object[])]));
        return owner;
    }

    private static void AssertAllMutable(EmittedReflectRuntime owner)
    {
        foreach (var (_, selected) in Groups(owner))
            Assert.Equal(false, selected.GetType().GetProperty("IsComplete")!.GetValue(selected));
    }

    private static void AssertFrozen(EmittedReflectRuntime owner)
    {
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.Throws<InvalidOperationException>(owner.BeginAssignmentEmission);
        Assert.Throws<InvalidOperationException>(owner.BeginNamespaceEmission);
        Assert.Throws<InvalidOperationException>(owner.BeginMetadataEmission);
        foreach (var (_, selected) in Groups(owner))
        {
            Assert.Equal(true, selected.GetType().GetProperty("IsComplete")!.GetValue(selected));
            foreach (var property in Handles(selected.GetType()))
            {
                var value = property.GetValue(selected);
                Assert.NotNull(value);
                Assert.False(property.SetMethod!.IsPublic);
                var write = Assert.Throws<TargetInvocationException>(() => property.SetValue(selected, value));
                Assert.IsType<InvalidOperationException>(write.InnerException);
            }
        }
        if (owner.Namespace is { } reflectNamespace)
            Assert.Throws<InvalidOperationException>(() => reflectNamespace.RegisterValueFormMethod("get", reflectNamespace.ValueFormMethods["get"]));
    }

    private static RuntimeFeatureSet Detect() => new RuntimeFeatureDetector()
        .Detect(new Parser(new Lexer("const value=1;").ScanTokens()).ParseOrThrow());

    private static EmittedRuntime EmitRuntime(int? mask, bool hosted = false, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"reflect_storage_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (mask is null) return emitter.EmitAll(module);
        var features = Detect();
        features.UsesReflect = (mask & 1) != 0;
        features.UsesProxy = (mask & 2) != 0;
        features.UsesReflectMetadata = (mask & 4) != 0;
        return emitter.EmitAll(module, features);
    }

    private static object? Call(Type type, string name, params object?[] arguments) => type.GetMethod(name)!.Invoke(null, arguments);

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    private static void Verify(MemoryStream bytes)
    {
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        bytes.Position = 0;
    }

    private static void AssertReferences(Assembly assembly, bool hosted)
    {
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }
}
