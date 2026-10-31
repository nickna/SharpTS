using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Compilation.Registries;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeGuestOwnPropertyTests
{
    private const BindingFlags HiddenStatic = BindingFlags.NonPublic | BindingFlags.Static;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContractIsOptionalAndDoesNotAdmitExistingCarriers(bool enabled)
    {
        var (builder, _, runtime) = Emit(enabled);
        var assembly = LoadVerified(builder);
        var contract = assembly.GetType("$IGuestOwnProperties");
        Assert.Equal(enabled, contract != null);
        Assert.Equal(enabled, runtime.GuestOwnProperties != null);
        if (!enabled)
        {
            Assert.Null(assembly.GetType("$GuestOwnPropertyState"));
            Assert.Null(assembly.GetType("$GuestOwnState"));
            return;
        }
        Assert.True(runtime.GuestOwnProperties!.IsComplete);
        Assert.Throws<InvalidOperationException>(runtime.GuestOwnProperties.CompleteEmission);
        Assert.All(assembly.GetTypes().Where(type => !type.IsInterface), type => Assert.False(contract!.IsAssignableFrom(type)));
        Assert.Null(typeof(EmittedGuestOwnPropertyRuntime).GetProperty(nameof(EmittedGuestOwnPropertyRuntime.Contract))!.SetMethod);
        Assert.Null(typeof(EmittedGuestOwnPropertyRuntime).GetProperty(nameof(EmittedGuestOwnPropertyRuntime.GetOwnDescriptor))!.SetMethod);
    }

    [Fact]
    public void SuccessfulDefinitionsKeepActualChronologyAndActiveFieldIdentity()
    {
        var (builder, _, _) = Emit();
        var assembly = LoadVerified(builder);
        var state = assembly.GetType("$GuestOwnPropertyState")!;
        var symbol = assembly.GetType("$TSSymbol")!;
        object firstSymbol = Activator.CreateInstance(symbol, ["same"])!;
        object secondSymbol = Activator.CreateInstance(symbol, ["same"])!;
        var owner = new object(); var baseField = new object(); var derivedField = new object();
        foreach (object key in new object[] { firstSymbol, "later", "10", "2", "02", "4294967295", "4294967294", "-0", "0", secondSymbol })
            Call(state, "CommitDefinition", owner, key, baseField, true);
        Assert.Equal(new object[] { "0", "2", "10", "4294967294", "later", "02", "4294967295", "-0", firstSymbol, secondSymbol }, Keys(state, owner));
        Call(state, "CommitUpdate", owner, "later", true);
        Assert.Same(baseField, Call(state, "ActiveSourceField", owner, "later"));
        Call(state, "CommitDefinition", owner, "later", derivedField, true);
        Assert.Same(derivedField, Call(state, "ActiveSourceField", owner, "later"));
        Call(state, "CommitDefinition", owner, "failed", derivedField, false);
        Call(state, "CommitDelete", owner, "later", false);
        Assert.False((bool)Call(state, "HasOwn", owner, "failed")!);
        Assert.Same(derivedField, Call(state, "ActiveSourceField", owner, "later"));
        Call(state, "CommitDelete", owner, "later", true);
        Assert.False((bool)Call(state, "HasOwn", owner, "later")!);
        Assert.Null(Call(state, "ActiveSourceField", owner, "later"));
        Call(state, "CommitDefinition", owner, "later", null, true);
        Assert.True((bool)Call(state, "HasOwn", owner, "later")!);
        Assert.Null(Call(state, "ActiveSourceField", owner, "later"));
        Assert.Equal(new object[] { "0", "2", "10", "4294967294", "02", "4294967295", "-0", "later", firstSymbol, secondSymbol }, Keys(state, owner));
        Call(state, "CommitDelete", owner, firstSymbol, true);
        Call(state, "CommitDefinition", owner, firstSymbol, baseField, true);
        Assert.Equal(new object[] { "0", "2", "10", "4294967294", "02", "4294967295", "-0", "later", secondSymbol, firstSymbol }, Keys(state, owner));
        Keys(state, owner).Clear();
        Assert.Equal(10, Keys(state, owner).Count);
        Assert.Empty(Keys(state, new object()));
    }

    [Fact]
    public void ReusedEmitterKeepsWeakLedgersInTheirOwnSavedAssemblies()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, false, false, true);
        var source = new Parser(new Lexer("console.log(1);").ScanTokens()).ParseOrThrow();
        var shared = new object();
        var owners = new HashSet<EmittedGuestOwnPropertyRuntime>();
        var states = new List<Type>();
        for (int index = 0; index < 2; index++)
        {
            var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
            var runtime = emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(source));
            Assert.True(owners.Add(runtime.GuestOwnProperties!));
            var state = LoadVerified(builder).GetType("$GuestOwnPropertyState")!;
            states.Add(state);
            Assert.False((bool)Call(state, "HasOwn", shared, "field")!);
            Call(state, "CommitDefinition", shared, "field", null, true);
        }
        Assert.NotSame(states[0].Assembly, states[1].Assembly);
        Call(states[1], "CommitDelete", shared, "field", true);
        Assert.True((bool)Call(states[0], "HasOwn", shared, "field")!);
        Assert.False((bool)Call(states[1], "HasOwn", shared, "field")!);
    }

    [Fact]
    public void RejectedKeysAndFailedOperationsCannotCreateOwnState()
    {
        var (builder, _, _) = Emit();
        var state = LoadVerified(builder).GetType("$GuestOwnPropertyState")!;
        var owner = new object(); var key = new Coercible();
        foreach (object? invalid in new object?[] { null, 1d, true, key })
        {
            var exception = Assert.Throws<TargetInvocationException>(() => Call(state, "CommitDefinition", owner, invalid, new object(), true));
            Assert.IsType<ArgumentException>(exception.InnerException);
        }
        Assert.Equal(0, key.Reads);
        Assert.Empty(Keys(state, owner));
        Call(state, "CommitDefinition", owner, "field", new object(), false);
        Call(state, "CommitUpdate", owner, "field", false);
        Call(state, "CommitDelete", owner, "field", false);
        Assert.False((bool)Call(state, "HasOwn", owner, "field")!);
        var missing = Assert.Throws<TargetInvocationException>(() => Call(state, "CommitUpdate", owner, "field", true));
        Assert.IsType<InvalidOperationException>(missing.InnerException);
        Assert.Empty(Keys(state, owner));
    }

    [Fact]
    public void WeakStateDoesNotRetainItsReceiverEvenThroughAValueCycle()
    {
        var (builder, _, _) = Emit();
        var state = LoadVerified(builder).GetType("$GuestOwnPropertyState")!;
        var weak = CreateWeakOwner(state);
        for (int attempt = 0; attempt < 10 && weak.IsAlive; attempt++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.False(weak.IsAlive);
        Assert.Empty(Keys(state, new object()));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateWeakOwner(Type state)
    {
        var owner = new object();
        Call(state, "CommitDefinition", owner, "field", owner, true);
        return new WeakReference(owner);
    }

    [Fact]
    public void PrivateGenericAdaptersAreRegisteredAfterRuntimeFreezeAndPreserveClosedReceivers()
    {
        var (builder, module, runtime) = Emit();
        var contract = runtime.GuestOwnProperties!;
        var classes = NewClasses();
        var registry = classes.BeginNativeOwnProperties(contract);
        Assert.Throws<InvalidOperationException>(() => classes.BeginNativeOwnProperties(contract));
        var owner = module.DefineType("Model", TypeAttributes.Public);
        var parameter = owner.DefineGenericParameters("T")[0];
        var value = owner.DefineField("Value", parameter, FieldAttributes.Private);
        var constructor = owner.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [parameter]);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, value); il.Emit(OpCodes.Ret);
        var descriptor = owner.DefineMethod("ReadOwn", MethodAttributes.Private, contract.DescriptorType, [typeof(object)]);
        il = descriptor.GetILGenerator(); il.Emit(OpCodes.Newobj, runtime.DescriptorStorage.DescriptorConstructor); il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, value); il.Emit(OpCodes.Box, parameter);
        il.Emit(OpCodes.Callvirt, runtime.DescriptorStorage.DescriptorValue.GetSetMethod()!); il.Emit(OpCodes.Ret);
        var keys = owner.DefineMethod("Keys", MethodAttributes.Private, typeof(List<object>), Type.EmptyTypes);
        il = keys.GetILGenerator(); il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldstr, "field"); il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add")!); il.Emit(OpCodes.Ret);
        var prototype = owner.DefineMethod("Prototype", MethodAttributes.Private, typeof(object), Type.EmptyTypes);
        il = prototype.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
        Assert.Throws<InvalidOperationException>(() => registry.Register(owner, descriptor, keys, prototype));
        classes.RegisterGuestFieldOwner("Model", owner);
        registry.Register(owner, descriptor, keys, prototype);
        Assert.Throws<InvalidOperationException>(() => registry.Register(owner, descriptor, keys, prototype));
        Assert.Throws<InvalidOperationException>(() => registry.CompleteDeclarations([]));
        registry.CompleteDeclarations([owner]);
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
        owner.CreateType(); registry.CompleteEmission();
        Assert.True(registry.IsComplete);
        Assert.True(contract.IsComplete);
        Assert.Throws<InvalidOperationException>(() => registry.Register(owner, descriptor, keys, prototype));
        var assembly = LoadVerified(builder);
        var loadedContract = assembly.GetType("$IGuestOwnProperties")!;
        foreach (var (type, expected) in new (Type, object)[] { (typeof(double), 47d), (typeof(string), "word") })
        {
            var closed = assembly.GetType("Model")!.MakeGenericType(type);
            var instance = Activator.CreateInstance(closed, [expected])!;
            var result = loadedContract.GetMethod("GetOwnDescriptor")!.Invoke(instance, ["field"])!;
            Assert.Equal(expected, result.GetType().GetProperty("Value")!.GetValue(result));
            Assert.Same(instance, loadedContract.GetMethod("InitialPrototype")!.Invoke(instance, null));
            Assert.Equal(new object[] { "field" }, Assert.IsType<List<object>>(loadedContract.GetMethod("OwnPropertyKeys")!.Invoke(instance, null)));
            Assert.All(closed.GetInterfaceMap(loadedContract).TargetMethods, method => Assert.True(method.IsPrivate && method.IsFinal));
        }
    }

    [Fact]
    public void LateRegistrationRejectsForeignModulesWrongSignaturesAndIncompleteBodies()
    {
        var (_, module, runtime) = Emit();
        var contract = runtime.GuestOwnProperties!;
        var classes = NewClasses(); var registry = classes.BeginNativeOwnProperties(contract);
        var owner = module.DefineType("Owner", TypeAttributes.Public); classes.RegisterGuestFieldOwner("Owner", owner);
        var descriptor = owner.DefineMethod("Descriptor", MethodAttributes.Private, contract.DescriptorType, [typeof(object)]);
        var keys = owner.DefineMethod("Keys", MethodAttributes.Private, typeof(List<object>), Type.EmptyTypes);
        var prototype = owner.DefineMethod("Prototype", MethodAttributes.Private, typeof(object), Type.EmptyTypes);
        var wrong = owner.DefineMethod("Wrong", MethodAttributes.Private | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
        Assert.Throws<InvalidOperationException>(() => registry.Register(owner, descriptor, keys, wrong));
        var foreign = NewAssembly().DefineDynamicModule("foreign").DefineType("Foreign", TypeAttributes.Public);
        classes.RegisterGuestFieldOwner("Foreign", foreign);
        Assert.Throws<InvalidOperationException>(() => registry.Register(foreign, descriptor, keys, prototype));
        registry.Register(owner, descriptor, keys, prototype);
        Assert.Throws<InvalidOperationException>(() => registry.CompleteDeclarations([owner]));
        foreach (var method in new[] { descriptor, keys, prototype, wrong })
        {
            var il = method.GetILGenerator(); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
        }
        registry.CompleteDeclarations([owner]); owner.CreateType(); registry.CompleteEmission();
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
    }

    [Fact]
    public void RuntimeCompletionRequiresAnExactLocalNormalizerAndKeepsFrozenContractHandles()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, false, false, true);
        var declare = typeof(RuntimeEmitter).GetMethod("DeclareGuestOwnProperties", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var completeSnapshot = typeof(RuntimeEmitter).GetMethod("EmitGuestOwnPropertySnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var runtime = Assert.IsType<EmittedGuestOwnPropertyRuntime>(declare.Invoke(emitter, [module, typeof(Uri), typeof(Version)]));
        var contract = runtime.Contract; var descriptor = runtime.GetOwnDescriptor;
        Assert.False(runtime.IsComplete);
        Assert.Throws<InvalidOperationException>(runtime.CompleteEmission);
        Assert.Throws<InvalidOperationException>(() => NewClasses().BeginNativeOwnProperties(runtime));

        var peer = module.DefineType("SelectedNormalizer", TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var calls = peer.DefineField("Calls", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var normalize = peer.DefineMethod("Normalize", MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(List<object>), [typeof(List<object>)]);
        var il = normalize.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, calls); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stsfld, calls);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Reverse", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
        var wrong = peer.DefineMethod("Wrong", MethodAttributes.Assembly | MethodAttributes.Static, typeof(object), [typeof(object)]);
        il = wrong.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
        var foreign = NewAssembly().DefineDynamicModule("foreign").DefineType("Foreign");
        var foreignNormalize = foreign.DefineMethod("Normalize", MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(List<object>), [typeof(List<object>)]);
        foreach (var invalid in new[] { wrong, foreignNormalize })
        {
            var error = Assert.Throws<TargetInvocationException>(() => completeSnapshot.Invoke(emitter, [runtime, invalid]));
            Assert.IsType<InvalidOperationException>(error.InnerException);
            Assert.Equal(0, runtime.SnapshotKeys.GetILGenerator().ILOffset);
        }
        completeSnapshot.Invoke(emitter, [runtime, normalize]); peer.CreateType(); runtime.CompleteEmission();
        Assert.True(runtime.IsComplete);
        Assert.Same(contract, runtime.Contract); Assert.Same(descriptor, runtime.GetOwnDescriptor);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(
            () => completeSnapshot.Invoke(emitter, [runtime, normalize])).InnerException);
        var assembly = LoadVerified(builder); var state = assembly.GetType("$GuestOwnPropertyState")!;
        var owner = new object(); var symbol = new Version(1, 0);
        Call(state, "CommitDefinition", owner, "first", null, true);
        Call(state, "CommitDefinition", owner, symbol, null, true);
        Call(state, "CommitDefinition", owner, "last", null, true);
        Assert.Equal(new object[] { "last", "first", symbol }, Keys(state, owner));
        Assert.Equal(1, assembly.GetType("SelectedNormalizer")!.GetField("Calls")!.GetValue(null));
    }

    private sealed class Coercible { public int Reads; public override string ToString() { Reads++; return "field"; } }
    private static ClassRegistry NewClasses() => new([], [], [], [], [], [], [], [], [], [], new(), new());
    private static object? Call(Type state, string name, params object?[] arguments) => state.GetMethod(name, HiddenStatic)!.Invoke(null, arguments);
    private static List<object> Keys(Type state, object owner) => Assert.IsType<List<object>>(Call(state, "SnapshotKeys", owner));
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName("guest_own_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
    private static (PersistedAssemblyBuilder Builder, ModuleBuilder Module, EmittedRuntime Runtime) Emit(bool enabled = true)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var statements = new Parser(new Lexer("console.log(1);").ScanTokens()).ParseOrThrow();
        var runtime = new RuntimeEmitter(TypeProvider.Runtime, false, false, enabled).EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
        return (builder, module, runtime);
    }
    private static Assembly LoadVerified(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == "SharpTS");
        return assembly;
    }
}
