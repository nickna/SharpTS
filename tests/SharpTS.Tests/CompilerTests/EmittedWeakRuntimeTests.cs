using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedWeakRuntimeTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly Type[] OwnerTypes =
    [
        typeof(EmittedWeakMapRuntime), typeof(EmittedWeakSetRuntime), typeof(EmittedWeakRefRuntime),
        typeof(EmittedFinalizationRegistryRuntime), typeof(EmittedFinalizationRegistryImplementation)
    ];

    private static PropertyInfo[] Handles(Type type) => type.GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType)).ToArray();

    public static IEnumerable<object[]> Declarations => OwnerTypes.SelectMany(type =>
        Handles(type).Select(property => new object[] { type, property.Name }));

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingDeclarationIsRepairableAndCompletedMetadataIsFrozen(Type type, string missing)
    {
        var owner = Activator.CreateInstance(type, nonPublic: true)!;
        var property = type.GetProperty(missing)!;
        var complete = type.GetMethod("CompleteEmission", InstanceMembers)!;
        var read = Assert.Throws<TargetInvocationException>(() => property.GetValue(owner));
        Assert.Contains($"'{missing}'", Assert.IsType<InvalidOperationException>(read.InnerException).Message);
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        var builder = NewAssembly().DefineDynamicModule("main").DefineType("Declarations", TypeAttributes.Public);
        foreach (var handle in Handles(type).Where(handle => handle.Name != missing))
            handle.SetValue(owner, Declare(builder, handle));
        Expect<InvalidOperationException>(() => complete.Invoke(owner, null));
        Assert.Equal(false, type.GetProperty("IsComplete")!.GetValue(owner));
        property.SetValue(owner, Declare(builder, property));
        complete.Invoke(owner, null);
        AssertFrozen(owner);
    }

    [Fact]
    public void IndependentOptionalOwnersAndRequiredTableHaveExplicitAvailability()
    {
        var runtime = new EmittedRuntime();
        Assert.Equal(21, Declarations.Count());
        Assert.NotSame(runtime.FinalizationRegistry, new EmittedRuntime().FinalizationRegistry);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.FinalizationRegistry))!.SetMethod);
        Assert.Null(runtime.WeakMap);
        Assert.Null(runtime.WeakSet);
        Assert.Null(runtime.WeakRef);
        Assert.Null(runtime.FinalizationRegistry.Implementation);
        Assert.Throws<InvalidOperationException>(runtime.RequireWeakMap);
        Assert.Throws<InvalidOperationException>(runtime.RequireWeakSet);
        Assert.Throws<InvalidOperationException>(runtime.RequireWeakRef);
        Assert.Throws<InvalidOperationException>(runtime.FinalizationRegistry.RequireImplementation);
        runtime.BeginWeakMapEmission();
        Assert.Same(runtime.WeakMap, runtime.RequireWeakMap());
        Assert.Null(runtime.WeakSet);
        Assert.Throws<InvalidOperationException>(runtime.BeginWeakMapEmission);
        runtime.BeginWeakSetEmission();
        Assert.Same(runtime.WeakSet, runtime.RequireWeakSet());
        Assert.Null(runtime.WeakRef);
        Assert.Throws<InvalidOperationException>(runtime.BeginWeakSetEmission);
        runtime.BeginWeakRefEmission();
        Assert.Same(runtime.WeakRef, runtime.RequireWeakRef());
        Assert.Throws<InvalidOperationException>(runtime.BeginWeakRefEmission);
        runtime.FinalizationRegistry.BeginImplementationEmission();
        Assert.Same(runtime.FinalizationRegistry.Implementation, runtime.FinalizationRegistry.RequireImplementation());
        Assert.Throws<InvalidOperationException>(runtime.FinalizationRegistry.BeginImplementationEmission);
    }

    [Theory]
    [InlineData("PokeTable")]
    [InlineData("EntryType")]
    [InlineData("EntryConstructor")]
    [InlineData("SuppressEntry")]
    [InlineData("Create")]
    [InlineData("Register")]
    [InlineData("Unregister")]
    public void FailedParentCompletionKeepsBothOwnersRepairable(string missing)
    {
        var root = new EmittedRuntime().FinalizationRegistry;
        root.BeginImplementationEmission();
        var child = root.RequireImplementation();
        var builder = NewAssembly().DefineDynamicModule("main").DefineType("Declarations");
        foreach (object owner in new object[] { root, child })
        foreach (var property in Handles(owner.GetType()).Where(property => property.Name != missing))
            property.SetValue(owner, Declare(builder, property));
        Assert.Throws<InvalidOperationException>(root.CompleteEmission);
        Assert.False(root.IsComplete);
        Assert.False(child.IsComplete);
        object repair = missing == "PokeTable" ? root : child;
        var missingProperty = repair.GetType().GetProperty(missing)!;
        missingProperty.SetValue(repair, Declare(builder, missingProperty));
        root.CompleteEmission();
        AssertFrozen(root);
        AssertFrozen(child);
        Assert.Throws<InvalidOperationException>(root.BeginImplementationEmission);
    }

    [Fact]
    public void RequiredOnlyFinalizationOwnerRejectsLateSelection()
    {
        var root = new EmittedRuntime().FinalizationRegistry;
        root.PokeTable = NewAssembly().DefineDynamicModule("main").DefineType("Declarations")
            .DefineField("PokeTable", typeof(object), FieldAttributes.Public);
        root.CompleteEmission();
        AssertFrozen(root);
        Assert.Null(root.Implementation);
        Assert.Throws<InvalidOperationException>(root.BeginImplementationEmission);
    }

    [Fact]
    public void EntryDeclarationsAreAvailableBeforeRuntimeOperationsAndCompleteLater()
    {
        var module = NewAssembly().DefineDynamicModule("main");
        var root = new EmittedRuntime().FinalizationRegistry;
        root.BeginImplementationEmission();
        var implementation = root.RequireImplementation();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        typeof(RuntimeEmitter).GetMethod("EmitFinRegEntryTypeDefinition", InstanceMembers)!
            .Invoke(emitter, [module, implementation]);
        Assert.NotNull(implementation.EntryType);
        Assert.NotNull(implementation.EntryConstructor);
        Assert.NotNull(implementation.SuppressEntry);
        Assert.Throws<InvalidOperationException>(() => implementation.Register);
        Assert.Throws<InvalidOperationException>(root.CompleteEmission);
        Assert.False(implementation.IsComplete);
        var builder = module.DefineType("Runtime");
        root.PokeTable = builder.DefineField("PokeTable", typeof(ConditionalWeakTable<object, object>), FieldAttributes.Static);
        var undefined = builder.DefineField("Undefined", typeof(object), FieldAttributes.Static);
        // The helper contract is supplied metadata; its global analysis flag can be false.
        typeof(RuntimeEmitter).GetField("_features", InstanceMembers)!.SetValue(emitter, Features(0));
        typeof(RuntimeEmitter).GetMethod("EmitFinalizationRegistryMethods", InstanceMembers)!
            .Invoke(emitter, [builder, implementation, root.PokeTable, undefined]);
        root.CompleteEmission();
        AssertFrozen(root);
        AssertFrozen(implementation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterIsolatesSelectionHandlesAndGuestState(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new List<object>();
        var tables = new List<object>();
        foreach (int mask in new[] { 0, 1, 2, 4, 8, 15, 0, 15 })
        {
            var builder = NewAssembly();
            var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Features(mask));
            Assert.Equal((mask & 1) != 0, runtime.WeakMap is not null);
            Assert.Equal((mask & 2) != 0, runtime.WeakSet is not null);
            Assert.Equal((mask & 4) != 0, runtime.WeakRef is not null);
            Assert.Equal((mask & 8) != 0, runtime.FinalizationRegistry.Implementation is not null);
            foreach (object owner in new object?[] { runtime.WeakMap, runtime.WeakSet, runtime.WeakRef,
                         runtime.FinalizationRegistry, runtime.FinalizationRegistry.Implementation }.OfType<object>())
            {
                Assert.DoesNotContain(owner, owners);
                owners.Add(owner);
                AssertFrozen(owner);
                foreach (var property in Handles(owner.GetType()))
                    Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(owner)).Module.Assembly);
            }
            var assembly = SaveVerifyLoad(builder);
            var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references);
            Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("weak_runtime_", StringComparison.Ordinal));
            var runtimeType = assembly.GetType("$Runtime")!;
            object? Call(string method, params object?[] args) => runtimeType.GetMethod(method, StaticMembers)!.Invoke(null, args);
            var table = runtimeType.GetField("_finRegPokeTable", StaticMembers)!.GetValue(null)!;
            Assert.DoesNotContain(tables, previous => ReferenceEquals(previous, table));
            tables.Add(table);
            object target = new(), other = new();
            if (runtime.WeakMap is not null)
            {
                var map = Call("CreateWeakMap")!;
                Assert.Same(map, Call("WeakMapSet", map, target, "value"));
                Assert.Equal("value", Call("WeakMapGet", map, target));
                Assert.Equal(false, Call("WeakMapHas", map, other));
                Assert.Equal(true, Call("WeakMapDelete", map, target));
                Assert.Equal(false, Call("WeakMapHas", map, target));
            }
            if (runtime.WeakSet is not null)
            {
                var set = Call("CreateWeakSet")!;
                Assert.Same(set, Call("WeakSetAdd", set, target));
                Assert.Equal(true, Call("WeakSetHas", set, target));
                Assert.Equal(false, Call("WeakSetHas", set, other));
                Assert.Equal(true, Call("WeakSetDelete", set, target));
            }
            if (runtime.WeakRef is not null)
                Assert.Same(target, Call("WeakRefDeref", Call("CreateWeakRef", target)));
            var entryType = assembly.GetType("$FinRegEntry");
            Assert.Equal((mask & 8) != 0, entryType is not null);
            if (entryType is not null)
            {
                var registry = Call("CreateFinalizationRegistry", new object())!;
                var second = Call("CreateFinalizationRegistry", new object())!;
                var token = new object();
                Call("FinalizationRegistryRegister", registry, target, "held", token);
                Assert.Equal(false, Call("FinalizationRegistryUnregister", second, token));
                Assert.Equal(true, Call("FinalizationRegistryUnregister", registry, token));
                Assert.Equal(false, Call("FinalizationRegistryUnregister", registry, token));
                foreach (bool suppressed in new[] { false, true })
                {
                    var queue = new ConcurrentQueue<object>();
                    var entry = Activator.CreateInstance(entryType, ["held", queue])!;
                    if (suppressed) entryType.GetMethod("Suppress")!.Invoke(entry, null);
                    // Exercise the emitted finalizer body deterministically, without relying on GC scheduling.
                    entryType.GetMethod("Finalize", InstanceMembers)!.Invoke(entry, null);
                    GC.SuppressFinalize(entry);
                    Assert.Equal(suppressed ? 0 : 1, queue.Count);
                    if (!suppressed) Assert.Equal("held", Assert.Single(queue));
                }
            }
            GC.KeepAlive(target);
        }
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(1, 14)]
    [InlineData(2, 13)]
    [InlineData(3, 12)]
    [InlineData(4, 11)]
    [InlineData(8, 7)]
    [InlineData(15, 0)]
    public void GenericPropertyDispatchUsesProvidedOwnersWhenAnalysisFlagsDiffer(int supplied, int flags)
    {
        var features = Features(supplied);
        var builder = NewAssembly();
        var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(module, features);
        typeof(RuntimeEmitter).GetField("_features", InstanceMembers)!.SetValue(emitter, Features(flags));
        var probe = module.DefineType("WeakDispatch", TypeAttributes.Public);
        runtime.GetProperty = probe.DefineMethod("GetProperty", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [typeof(object), typeof(string)]);
        typeof(RuntimeEmitter).GetMethod("EmitGetProperty", InstanceMembers)!.Invoke(emitter, [probe, runtime]);
        probe.CreateType();
        var assembly = SaveVerifyLoad(builder);
        var runtimeType = assembly.GetType("$Runtime")!;
        object? Call(string method, params object?[] args) => runtimeType.GetMethod(method, StaticMembers)!.Invoke(null, args);
        object? Get(object receiver, string name) => assembly.GetType("WeakDispatch")!.GetMethod("GetProperty")!.Invoke(null, [receiver, name]);
        object? Invoke(object receiver, string name, params object?[] args) => Call("InvokeValue", Get(receiver, name), args);
        var target = new object();
        if ((supplied & 1) != 0)
        {
            var map = Call("CreateWeakMap")!;
            Assert.Same(map, Invoke(map, "set", target, "value"));
            Assert.Equal("value", Invoke(map, "get", target));
            Assert.Equal(true, Invoke(map, "has", target));
            Assert.Equal(true, Invoke(map, "delete", target));
        }
        if ((supplied & 2) != 0)
        {
            var set = Call("CreateWeakSet")!;
            Assert.Same(set, Invoke(set, "add", target));
            Assert.Equal(true, Invoke(set, "has", target));
            Assert.Equal(true, Invoke(set, "delete", target));
        }
        if ((supplied & 4) != 0)
            Assert.Same(target, Invoke(Call("CreateWeakRef", target)!, "deref"));
        if ((supplied & 8) != 0)
        {
            var registry = Call("CreateFinalizationRegistry", new object())!;
            var token = new object();
            Invoke(registry, "register", target, "held", token);
            Assert.Equal(true, Invoke(registry, "unregister", token));
        }
        GC.KeepAlive(target);
    }

    [Fact]
    public void FamilyHelpersExposeOnlyScopedDependenciesAndRemoveDuplicateStorage()
    {
        string[] helpers =
        [
            "EmitWeakMapMethods",
            "EmitCreateWeakMap",
            "EmitWeakMapGet",
            "EmitWeakMapSet",
            "EmitWeakMapHas",
            "EmitWeakMapDelete",
            "EmitWeakSetMethods",
            "EmitCreateWeakSet",
            "EmitWeakSetAdd",
            "EmitWeakSetHas",
            "EmitWeakSetDelete",
            "EmitWeakRefMethods",
            "EmitCreateWeakRef",
            "EmitWeakRefDeref",
            "EmitWeakTargetValidator",
            "EmitFinRegEntryTypeDefinition",
            "EmitFinalizationRegistryMethods",
            "EmitFinRegPokeTableInit",
            "EmitCreateFinalizationRegistry",
            "EmitFinalizationRegistryRegister",
            "EmitFinalizationRegistryUnregister",
        ];
        foreach (string name in helpers)
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, InstanceMembers | BindingFlags.Static)!;
            Assert.NotNull(method);
            foreach (var parameter in method.GetParameters())
            {
                Assert.NotEqual(typeof(EmittedRuntime), parameter.ParameterType);
                Assert.NotEqual(typeof(RuntimeFeatureSet), parameter.ParameterType);
            }
        }
        string[] formerHandles =
        [
            "CreateWeakMap",
            "WeakMapGet",
            "WeakMapSet",
            "WeakMapHas",
            "WeakMapDelete",
            "ValidateWeakMapKey",
            "CreateWeakSet",
            "WeakSetAdd",
            "WeakSetHas",
            "WeakSetDelete",
            "ValidateWeakSetValue",
            "CreateWeakRef",
            "WeakRefDeref",
            "ValidateWeakRefTarget",
            "FinRegPokeTableField",
            "FinRegEntryType",
            "FinRegEntryCtor",
            "FinRegEntrySuppress",
            "CreateFinalizationRegistry",
            "FinalizationRegistryRegister",
            "FinalizationRegistryUnregister",
        ];
        foreach (string name in formerHandles) Assert.Null(typeof(EmittedRuntime).GetProperty(name));
        foreach (string name in new[] { "_finRegEntryHeldValueField", "_finRegEntryQueueField", "_finRegEntrySuppressedField" })
            Assert.Null(typeof(RuntimeEmitter).GetField(name, InstanceMembers));
    }

    private static RuntimeFeatureSet Features(int mask)
    {
        var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer("const value=1;").ScanTokens()).ParseOrThrow());
        features.UsesWeakMap = (mask & 1) != 0;
        features.UsesWeakSet = (mask & 2) != 0;
        features.UsesWeakRef = (mask & 4) != 0;
        features.UsesFinalizationRegistry = (mask & 8) != 0;
        return features;
    }

    private static PersistedAssemblyBuilder NewAssembly() => new(
        new AssemblyName($"weak_runtime_{Guid.NewGuid():N}"), typeof(object).Assembly);

    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream();
        builder.Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        return Assembly.Load(bytes.ToArray());
    }

    private static object Declare(TypeBuilder builder, PropertyInfo property)
    {
        if (property.PropertyType == typeof(Type)) return builder;
        if (property.PropertyType == typeof(FieldBuilder)) return builder.DefineField(property.Name, typeof(object), FieldAttributes.Public);
        if (property.PropertyType == typeof(ConstructorBuilder)) return builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        if (property.PropertyType == typeof(MethodBuilder)) return builder.DefineMethod(property.Name, MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        throw new InvalidOperationException("Unexpected metadata declaration.");
    }

    private static void AssertFrozen(object owner)
    {
        Assert.Equal(true, owner.GetType().GetProperty("IsComplete")!.GetValue(owner));
        Expect<InvalidOperationException>(() => owner.GetType().GetMethod("CompleteEmission", InstanceMembers)!.Invoke(owner, null));
        foreach (var property in Handles(owner.GetType()))
        {
            Assert.False(property.SetMethod!.IsPublic);
            var value = property.GetValue(owner);
            Expect<InvalidOperationException>(() => property.SetValue(owner, value));
        }
    }

    private static void Expect<T>(Action action) where T : Exception =>
        Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
}
