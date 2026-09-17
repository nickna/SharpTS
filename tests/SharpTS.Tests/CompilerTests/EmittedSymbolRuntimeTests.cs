using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedSymbolRuntimeTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type[] OwnerTypes = [typeof(EmittedSymbolRuntime), typeof(EmittedSymbolAccessorRuntime)];

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
        var read = Assert.Throws<TargetInvocationException>(() => property.GetValue(owner));
        Assert.Contains($"'{missing}'", Assert.IsType<InvalidOperationException>(read.InnerException).Message);
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        var builder = NewAssembly().DefineDynamicModule("main").DefineType("Declarations");
        foreach (var handle in Handles(type).Where(handle => handle.Name != missing))
            handle.SetValue(owner, Declare(builder, handle));
        if (owner is EmittedSymbolAccessorRuntime accessors)
        {
            accessors.MarkInitializerEmitted();
            accessors.MarkBodiesEmitted();
            Assert.Throws<InvalidOperationException>(accessors.MarkInitializerEmitted);
            Assert.Throws<InvalidOperationException>(accessors.MarkBodiesEmitted);
        }
        Expect<InvalidOperationException>(() => Complete(owner));
        Assert.Equal(false, type.GetProperty("IsComplete")!.GetValue(owner));
        property.SetValue(owner, Declare(builder, property));
        Complete(owner);
        AssertFrozen(owner);
    }

    [Fact]
    public void BothRequiredOwnersArePerCompilationAndCannotBeReplaced()
    {
        var first = new EmittedRuntime();
        var second = new EmittedRuntime();
        Assert.Equal(27, Handles(typeof(EmittedSymbolRuntime)).Length);
        Assert.Equal(9, Handles(typeof(EmittedSymbolAccessorRuntime)).Length);
        Assert.NotSame(first.Symbols, second.Symbols);
        Assert.NotSame(first.SymbolAccessors, second.SymbolAccessors);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Symbols))!.SetMethod);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.SymbolAccessors))!.SetMethod);
        Assert.False(first.Symbols.IsComplete);
        Assert.False(first.SymbolAccessors.IsComplete);
    }

    [Fact]
    public void SymbolClassPublishesEarlyHandlesBeforeStorageAndPrototypeBodies()
    {
        var module = NewAssembly().DefineDynamicModule("main");
        var undefined = module.DefineType("Undefined");
        var instance = undefined.DefineField("Instance", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
        undefined.CreateType();
        var symbols = new EmittedRuntime().Symbols;
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        InvokeEmitter(emitter, "EmitTSSymbolClass", module, symbols, instance);
        Assert.True(symbols.Type.IsCreated());
        Assert.NotNull(symbols.Constructor);
        Assert.NotNull(symbols.For);
        Assert.NotNull(symbols.KeyFor);
        Assert.NotNull(symbols.Iterator);
        Assert.NotNull(symbols.AsyncIterator);
        Assert.Throws<InvalidOperationException>(() => symbols.GetStorage);
        Assert.Throws<InvalidOperationException>(() => symbols.PopulatePrototype);
        Assert.Throws<InvalidOperationException>(symbols.CompleteEmission);
        Assert.False(symbols.IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardDeclarationsRequireInitializerAndBodiesBeforeCompletion(bool omitInitializer)
    {
        var assembly = NewAssembly();
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(module, Detect("const value=1;"));
        var builder = module.DefineType("StagedRegistry", TypeAttributes.Public);
        // IsSymbol is private to its generated type, so emit the real helper
        // beside this staged registry, just as it is beside the production registry.
        var localSymbols = new EmittedSymbolRuntime();
        InvokeEmitter(emitter, "EmitIsSymbol", builder, localSymbols);
        var accessors = new EmittedSymbolAccessorRuntime();
        InvokeEmitter(emitter, "DefineSymbolAccessorRegistry", builder, accessors);
        foreach (var handle in Handles(accessors.GetType())) Assert.NotNull(handle.GetValue(accessors));
        Assert.Contains("initialization", Assert.Throws<InvalidOperationException>(accessors.CompleteEmission).Message);
        var cctor = builder.DefineTypeInitializer().GetILGenerator();
        void EmitInitializer() => InvokeEmitter(emitter, "InitSymbolAccessorRegistry", cctor, accessors);
        void EmitBodies() => InvokeEmitter(emitter, "EmitSymbolAccessorRegistryBodies", accessors, localSymbols, runtime.StringCoercion);
        if (omitInitializer) EmitBodies(); else EmitInitializer();
        var error = Assert.Throws<InvalidOperationException>(accessors.CompleteEmission);
        Assert.Contains(omitInitializer ? "initialization" : "bodies", error.Message);
        Assert.False(accessors.IsComplete);
        if (omitInitializer) EmitInitializer(); else EmitBodies();
        cctor.Emit(OpCodes.Ret);
        accessors.CompleteEmission();
        AssertFrozen(accessors);
        builder.CreateType();
        var loaded = SaveVerifyLoad(assembly);
        var registryType = loaded.GetType(builder.Name)!;
        var method = new object();
        // Guest registration occurs after compiler metadata is frozen.
        Call(registryType, "RegisterSymbolMethod", typeof(ProbeOwner), "key", method, false);
        Assert.Same(method, Call(registryType, "FindSymbolMethodFor", new ProbeDerived(), "key"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterIsolatesRequiredHandlesAndMutableGuestRegistries(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new List<object>();
        var previousSymbols = new List<object>();
        var previousRegistries = new List<object>();
        foreach (string source in new[] { "const value=1;", "const value=Symbol.for('key');", "const value=1;" })
        {
            var builder = NewAssembly();
            var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(source));
            foreach (object owner in new object[] { runtime.Symbols, runtime.SymbolAccessors })
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
            Assert.DoesNotContain(references, name => name!.StartsWith("symbol_runtime_", StringComparison.Ordinal));
            var symbols = assembly.GetType("$TSSymbol")!;
            var runtimeType = assembly.GetType("$Runtime")!;
            var registered = CheckPrimitiveRegistry(symbols, runtimeType, previousSymbols);
            CheckSymbolStorage(runtimeType, registered);
            var registry = Assert.IsAssignableFrom<IDictionary>(runtimeType.GetField(runtime.SymbolAccessors.Registry.Name, StaticMembers)!.GetValue(null));
            Assert.Empty(registry);
            Assert.DoesNotContain(previousRegistries, previous => ReferenceEquals(previous, registry));
            CheckAccessorRegistry(runtimeType, registry, registered);
            previousSymbols.Add(registered);
            previousRegistries.Add(registry);
        }
    }

    private static object CheckPrimitiveRegistry(Type symbols, Type runtime, List<object> previous)
    {
        string[] wellKnown = ["iterator", "asyncIterator", "toStringTag", "hasInstance", "isConcatSpreadable",
            "toPrimitive", "species", "unscopables", "dispose", "asyncDispose", "match", "matchAll", "replace", "search", "split"];
        var fields = symbols.GetFields(BindingFlags.Public | BindingFlags.Static);
        Assert.Equal(wellKnown.Order(StringComparer.Ordinal), fields.Select(field => field.Name).Order(StringComparer.Ordinal));
        var identities = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var field in fields)
        {
            Assert.True(field.IsInitOnly);
            Assert.Equal(symbols, field.FieldType);
            var value = field.GetValue(null)!;
            Assert.True(identities.Add(value));
            Assert.Equal("Symbol." + field.Name, symbols.GetProperty("description")!.GetValue(value));
        }
        var first = Activator.CreateInstance(symbols, ["same"])!;
        var second = Activator.CreateInstance(symbols, ["same"])!;
        var empty = symbols.GetConstructor([typeof(string)])!.Invoke([null]);
        Assert.NotEqual(first, second);
        Assert.Equal("Symbol(same)", first.ToString());
        Assert.Equal("Symbol()", empty.ToString());
        Assert.Same(first, symbols.GetMethod("valueOf")!.Invoke(first, null));
        Assert.Equal("$Undefined", symbols.GetProperty("description")!.GetValue(empty)!.GetType().Name);
        var registered = Call(symbols, "For", "shared")!;
        Assert.Same(registered, Call(symbols, "For", "shared"));
        Assert.Equal("shared", Call(symbols, "KeyFor", registered));
        Assert.Null(Call(symbols, "KeyFor", first));
        var concurrent = new object?[32];
        Parallel.For(0, concurrent.Length, i => concurrent[i] = Call(symbols, "For", "concurrent"));
        Assert.All(concurrent, value => Assert.Same(concurrent[0], value));
        Assert.Equal("Symbol(same)", Call(runtime, "SymbolPrototypeToString", first));
        Assert.Same(first, Call(runtime, "SymbolPrototypeValueOf", first));
        Assert.Equal("same", Call(runtime, "SymbolPrototypeDescription", first));
        foreach (var foreign in previous)
        {
            Assert.Equal(true, Call(runtime, "IsSymbol", foreign));
            Assert.False(symbols.IsInstanceOfType(foreign));
            Assert.NotEqual(registered, foreign);
        }
        return registered;
    }

    private static void CheckSymbolStorage(Type runtime, object symbol)
    {
        var target = new object();
        Assert.Null(Call(runtime, "TryGetSymbolDict", target));
        Assert.Null(Call(runtime, "TryGetSymbolDict", target));
        var dictionary = Assert.IsAssignableFrom<IDictionary>(Call(runtime, "GetSymbolDict", target));
        dictionary[symbol] = "value";
        Assert.Same(dictionary, Call(runtime, "TryGetSymbolDict", target));
        Assert.Same(dictionary, Call(runtime, "GetSymbolDict", target));
        Assert.Equal("value", dictionary[symbol]);
        Assert.Null(Call(runtime, "TryGetSymbolDict", new object()));
        Assert.Equal(true, Call(runtime, "IsSymbol", symbol));
        Assert.Equal(false, Call(runtime, "IsSymbol", target));
        Assert.Equal(false, Call(runtime, "IsSymbol", (object?)null));
    }

    private static void CheckAccessorRegistry(Type runtime, IDictionary registry, object symbol)
    {
        var getter = new object(); var setter = new object();
        var staticGetter = new object(); var staticSetter = new object();
        var method = new object(); var staticMethod = new object();
        Call(runtime, "RegisterSymbolAccessor", typeof(ProbeOwner), symbol, getter, setter, false);
        Call(runtime, "RegisterSymbolAccessor", typeof(ProbeOwner), symbol, staticGetter, staticSetter, true);
        Call(runtime, "RegisterSymbolMethod", typeof(ProbeOwner), symbol, method, false);
        Call(runtime, "RegisterSymbolMethod", typeof(ProbeOwner), symbol, staticMethod, true);
        var entries = Assert.IsAssignableFrom<IDictionary>(registry[typeof(ProbeOwner)]);
        Assert.Equal(new[] { getter, setter, staticGetter, staticSetter, method, staticMethod }, Assert.IsType<object[]>(entries[symbol]));
        var receiver = new ProbeDerived();
        Assert.Same(getter, Call(runtime, "FindSymbolGetterFor", receiver, symbol));
        Assert.Same(setter, Call(runtime, "FindSymbolSetterFor", receiver, symbol));
        Assert.Same(method, Call(runtime, "FindSymbolMethodFor", receiver, symbol));
        Assert.Same(staticGetter, Call(runtime, "FindSymbolGetterFor", typeof(ProbeDerived), symbol));
        Assert.Same(staticSetter, Call(runtime, "FindSymbolSetterFor", typeof(ProbeDerived), symbol));
        Assert.Same(staticMethod, Call(runtime, "FindSymbolMethodFor", typeof(ProbeDerived), symbol));
        Call(runtime, "RegisterSymbolAccessor", typeof(ProbeOwner), symbol, null, null, false);
        Assert.Same(getter, Call(runtime, "FindSymbolGetterFor", receiver, symbol));
        Call(runtime, "RegisterSymbolMethod", typeof(ProbeOwner), 1d, method, false);
        Assert.Same(method, Call(runtime, "FindSymbolMethodFor", receiver, "1"));
        int count = entries.Count;
        Call(runtime, "RegisterSymbolMethod", typeof(ProbeOwner), null, method, false);
        Assert.Equal(count, entries.Count);
        Assert.Equal(typeof(ProbeGeneric<>), Call(runtime, "SymbolRegistryKey", typeof(ProbeGeneric<string>)));
        Assert.Equal(typeof(ProbeGeneric<object>), Call(runtime, "SymbolClosedOwner", typeof(ProbeGeneric<>)));
        var open = typeof(ProbeGeneric<>).GetMethod(nameof(ProbeGeneric<object>.Echo))!;
        var closed = Assert.IsAssignableFrom<MethodInfo>(Call(runtime, "CloseSymbolAccessor", open, typeof(ProbeGeneric<string>)));
        Assert.Equal(typeof(ProbeGeneric<string>), closed.DeclaringType);
        Assert.Equal("roundtrip", closed.Invoke(new ProbeGeneric<string>(), ["roundtrip"]));
    }

    [Fact]
    public void FamilyHelpersExposeScopedDependenciesWithoutDuplicateHandleStorage()
    {
        string[] helpers =
        [
            "DefineSymbolAccessorRegistry",
            "InitSymbolAccessorRegistry",
            "EmitSymbolAccessorRegistryBodies",
            "EmitRegisterSymbolMethodBody",
            "EmitSymbolGenericHelperBodies",
            "EmitRegisterSymbolAccessorBody",
            "EmitStoreSlotIfPresent",
            "EmitFindSymbolAccessorBody",
            "DefineSymbolPrototypePopulateShell",
            "EmitSymbolPrototypePopulate",
            "EmitSymbolPrototypeValueOf",
            "EmitSymbolPrototypeToString",
            "EmitSymbolPrototypeDescription",
            "EmitGetSymbolDict",
            "EmitIsSymbol",
            "EmitTSSymbolClass",
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
            "TSSymbolType",
            "TSSymbolCtor",
            "SymbolToStringMethod",
            "SymbolFor",
            "SymbolKeyFor",
            "SymbolDescriptionGetter",
            "SymbolPrototypeField",
            "SymbolPrototypePopulateMethod",
            "SymbolPrototypeDescription",
            "GetSymbolDictMethod",
            "TryGetSymbolDictMethod",
            "IsSymbolMethod",
            "SymbolIterator",
            "SymbolAsyncIterator",
            "SymbolToStringTag",
            "SymbolHasInstance",
            "SymbolIsConcatSpreadable",
            "SymbolToPrimitive",
            "SymbolSpecies",
            "SymbolUnscopables",
            "SymbolDispose",
            "SymbolAsyncDispose",
            "SymbolMatch",
            "SymbolMatchAll",
            "SymbolReplace",
            "SymbolSearch",
            "SymbolSplit",
            "SymbolAccessorRegistryField",
            "RegisterSymbolAccessor",
            "FindSymbolGetter",
            "FindSymbolSetter",
            "RegisterSymbolMethod",
            "FindSymbolMethod",
            "SymbolRegistryKey",
            "SymbolClosedOwner",
            "CloseSymbolAccessor",
            "SymbolPrototypeToString",
            "SymbolPrototypeValueOf",
        ];
        foreach (string name in formerHandles) Assert.Null(typeof(EmittedRuntime).GetProperty(name));
    }

    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector()
        .Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    private static PersistedAssemblyBuilder NewAssembly() => new(
        new AssemblyName($"symbol_runtime_{Guid.NewGuid():N}"), typeof(object).Assembly);

    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream();
        builder.Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }

    private static object Declare(TypeBuilder builder, PropertyInfo property)
    {
        if (property.PropertyType == typeof(TypeBuilder)) return builder;
        if (property.PropertyType == typeof(FieldBuilder)) return builder.DefineField(property.Name, typeof(object), FieldAttributes.Public);
        if (property.PropertyType == typeof(ConstructorBuilder)) return builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        if (property.PropertyType == typeof(MethodBuilder)) return builder.DefineMethod(property.Name, MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        throw new InvalidOperationException("Unexpected Symbol metadata declaration.");
    }

    private static object? Call(Type type, string method, params object?[] arguments) =>
        type.GetMethod(method, StaticMembers)!.Invoke(null, arguments);

    private static void InvokeEmitter(RuntimeEmitter emitter, string name, params object?[] arguments) =>
        typeof(RuntimeEmitter).GetMethod(name, InstanceMembers)!.Invoke(emitter, arguments);

    private static void Complete(object owner) => owner.GetType().GetMethod("CompleteEmission", InstanceMembers)!.Invoke(owner, null);

    private static void AssertFrozen(object owner)
    {
        Assert.Equal(true, owner.GetType().GetProperty("IsComplete")!.GetValue(owner));
        Expect<InvalidOperationException>(() => Complete(owner));
        foreach (var property in Handles(owner.GetType()))
        {
            Assert.False(property.SetMethod!.IsPublic);
            Expect<InvalidOperationException>(() => property.SetValue(owner, property.GetValue(owner)));
        }
        if (owner is EmittedSymbolAccessorRuntime accessors)
        {
            Assert.Throws<InvalidOperationException>(accessors.MarkInitializerEmitted);
            Assert.Throws<InvalidOperationException>(accessors.MarkBodiesEmitted);
        }
    }

    private static void Expect<T>(Action action) where T : Exception =>
        Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);

    public class ProbeOwner { }
    public sealed class ProbeDerived : ProbeOwner { }
    public sealed class ProbeGeneric<T>
    {
        public T Echo(T value) => value;
    }
}
