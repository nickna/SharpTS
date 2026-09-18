using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedMapSetRuntimeTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    private static Type OwnerType(string family) => family switch
    {
        "Keys" => typeof(EmittedCollectionKeysRuntime),
        "Map" => typeof(EmittedMapRuntime),
        "Set" => typeof(EmittedSetRuntime),
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };

    private static PropertyInfo[] Handles(Type type) => type.GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType)).ToArray();

    public static IEnumerable<object[]> Declarations => new[] { "Keys", "Map", "Set" }
        .SelectMany(family => Handles(OwnerType(family)).Select(property => new object[] { family, property.Name }));

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingHandleCanBeRepairedAndAllWritersFreeze(string family, string missing)
    {
        var type = OwnerType(family);
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
    public void RequiredKeysAndIndependentOptionalOwnersHaveExplicitAvailability()
    {
        var runtime = new EmittedRuntime();
        Assert.Equal(49, Declarations.Count());
        Assert.NotSame(runtime.CollectionKeys, new EmittedRuntime().CollectionKeys);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.CollectionKeys))!.SetMethod);
        Assert.Null(runtime.Map);
        Assert.Null(runtime.Set);
        Assert.Throws<InvalidOperationException>(runtime.RequireMap);
        Assert.Throws<InvalidOperationException>(runtime.RequireSet);
        runtime.BeginMapEmission();
        Assert.Same(runtime.Map, runtime.RequireMap());
        Assert.Null(runtime.Set);
        Assert.Throws<InvalidOperationException>(runtime.BeginMapEmission);
        runtime.BeginSetEmission();
        Assert.Same(runtime.Set, runtime.RequireSet());
        Assert.Throws<InvalidOperationException>(runtime.BeginSetEmission);
        Assert.Throws<InvalidOperationException>(runtime.CollectionKeys.CompleteEmission);
    }

    [Theory]
    [InlineData("Map")]
    [InlineData("Set")]
    public void BoundDeclarationsSupportEarlyPropertyDispatchBeforeOperations(string family)
    {
        var module = NewAssembly().DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        object owner = Activator.CreateInstance(OwnerType(family), nonPublic: true)!;
        typeof(RuntimeEmitter).GetMethod("EmitBound" + family + "MethodTypeDefinition", InstanceMembers)!
            .Invoke(emitter, [module, owner]);
        foreach (string name in new[] { "BoundMethodType", "BoundMethodConstructor", "BoundMethodInvoke", "BoundReceiverField", "BoundNameField" })
            Assert.NotNull(owner.GetType().GetProperty(name)!.GetValue(owner));
        var operation = owner.GetType().GetProperty("Size")!;
        Expect<InvalidOperationException>(() => operation.GetValue(owner));
        // Property dispatch only needs the forward-declared bound constructor and BCL Count.
        // This is a construction-stage test; saved runtime execution is verified separately.
        typeof(RuntimeEmitter).GetMethod("EmitGet" + family + "Property", InstanceMembers)!
            .Invoke(emitter, [module.DefineType("Dispatch"), owner]);
        Assert.NotNull(owner.GetType().GetProperty("GetProperty")!.GetValue(owner));
        Expect<InvalidOperationException>(() => operation.GetValue(owner));
        Expect<InvalidOperationException>(() => owner.GetType().GetMethod("CompleteEmission", InstanceMembers)!.Invoke(owner, null));
        Assert.Equal(false, owner.GetType().GetProperty("IsComplete")!.GetValue(owner));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterKeepsSelectionHandlesAndGuestKeyIdentityWithinEachOutput(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new List<object>();
        var sentinels = new List<object>();
        var comparers = new List<object>();
        foreach (int mask in new[] { 0, 1, 2, 3, 0, 3 })
        {
            var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer("const value=1;").ScanTokens()).ParseOrThrow());
            features.UsesMap = (mask & 1) != 0;
            features.UsesSet = (mask & 2) != 0;
            var builder = NewAssembly();
            var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), features);
            Assert.Equal(features.UsesMap, runtime.Map is not null);
            Assert.Equal(features.UsesSet, runtime.Set is not null);
            foreach (object owner in new object?[] { runtime.CollectionKeys, runtime.Map, runtime.Set }.OfType<object>())
            {
                Assert.DoesNotContain(owner, owners);
                owners.Add(owner);
                AssertFrozen(owner);
                foreach (var property in Handles(owner.GetType()))
                    Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(owner)).Module.Assembly);
            }
            using var bytes = new MemoryStream();
            builder.Save(bytes);
            bytes.Position = 0;
            using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
            Assert.Empty(verifier.Verify(bytes));
            var assembly = Assembly.Load(bytes.ToArray());
            var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references);
            Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("map_set_owner_", StringComparison.Ordinal));
            var runtimeType = assembly.GetType("$Runtime")!;
            object? Call(string method, params object?[] args) => runtimeType.GetMethod(method, StaticMembers)!.Invoke(null, args);
            var sentinel = runtimeType.GetField("_mapNullSentinel", StaticMembers)!.GetValue(null)!;
            var comparer = Assert.IsAssignableFrom<IEqualityComparer<object>>(assembly.GetType("$ReferenceEqualityComparer")!
                .GetField("Instance", StaticMembers)!.GetValue(null));
            Assert.DoesNotContain(sentinels, prior => ReferenceEquals(prior, sentinel));
            Assert.DoesNotContain(comparers, prior => ReferenceEquals(prior, comparer));
            sentinels.Add(sentinel);
            comparers.Add(comparer);
            Assert.True(comparer.Equals(double.NaN, double.NaN));
            Assert.True(comparer.Equals(-0.0, 0.0));
            Assert.Equal(comparer.GetHashCode(-0.0), comparer.GetHashCode(0.0));
            object first = new(), second = new();
            Assert.True(comparer.Equals(first, first));
            Assert.False(comparer.Equals(first, second));
            foreach (string family in new[] { "Map", "Set" })
            {
                bool selected = family == "Map" ? features.UsesMap : features.UsesSet;
                Assert.Equal(selected, assembly.GetType("$Bound" + family + "Method") is not null);
                Assert.Equal(selected, assembly.GetType("$" + family + "CollectionIterator") is not null);
            }
            if (runtime.Map is not null)
            {
                var map = Call("CreateMap")!;
                var undefined = assembly.GetType("$Undefined")!.GetField("Instance", StaticMembers)!.GetValue(null)!;
                foreach (var (key, value) in new (object?, string)[] { (null, "null"), (undefined, "undefined"), (double.NaN, "nan"), (-0.0, "zero"), (first, "first"), (second, "second") })
                    Assert.Same(map, Call("MapSet", map, key, value));
                Assert.Equal(6, Convert.ToInt32(Call("MapSize", map)));
                Assert.Equal("null", Call("MapGet", map, null));
                Assert.Equal("undefined", Call("MapGet", map, undefined));
                Assert.Equal("nan", Call("MapGet", map, double.NaN));
                Assert.Equal("zero", Call("MapGet", map, 0.0));
                var keys = Assert.IsAssignableFrom<IEnumerable>(Call("MapKeys", map)).Cast<object?>().ToArray();
                Assert.Null(keys[0]);
                Assert.Same(undefined, keys[1]);
                var values = Assert.IsAssignableFrom<IEnumerable>(Call("MapValues", map));
                Call("MapDelete", map, second);
                Call("MapSet", map, first, "changed");
                Assert.Equal(new object?[] { "null", "undefined", "nan", "zero", "changed" }, values.Cast<object?>());
                Call("MapClear", map);
                Assert.Equal(0, Convert.ToInt32(Call("MapSize", map)));
            }
            if (runtime.Set is not null)
            {
                var set = Call("CreateSet")!;
                foreach (var value in new object[] { "a", "b", double.NaN, -0.0, first, second })
                    Assert.Same(set, Call("SetAdd", set, value));
                Assert.Equal(6, Convert.ToInt32(Call("SetSize", set)));
                Assert.Equal(true, Call("SetHas", set, 0.0));
                var values = Assert.IsAssignableFrom<IEnumerable>(Call("SetValues", set));
                Assert.Equal(true, Call("SetDelete", set, second));
                var remaining = values.Cast<object?>().ToArray();
                Assert.Equal(5, remaining.Length);
                Assert.DoesNotContain(remaining, value => ReferenceEquals(value, second));
                Call("SetClear", set);
                Assert.Equal(0, Convert.ToInt32(Call("SetSize", set)));
            }
        }
    }

    [Fact]
    public void FamilyHelpersExposeScopedDependenciesWithoutFlatAliases()
    {
        string[] helpers =
        [
            "EmitArrayIteratorWrapper",
            "EmitBoundMapMethodFinalize",
            "EmitBoundMapMethodTypeDefinition",
            "EmitCreateMap",
            "EmitCreateMapFromEntries",
            "EmitDenormalizeMapKey",
            "EmitMapClear",
            "EmitMapDelete",
            "EmitMapEntries",
            "EmitMapForEach",
            "EmitMapGet",
            "EmitMapHas",
            "EmitMapKeys",
            "EmitMapMethods",
            "EmitMapSet",
            "EmitMapSize",
            "EmitMapValues",
            "EmitNormalizeMapKey",
            "EmitBoundSetMethodFinalize",
            "EmitBoundSetMethodTypeDefinition",
            "EmitCreateSet",
            "EmitCreateSetFromArray",
            "EmitRequireSetReceiver",
            "EmitSetAdd",
            "EmitSetClear",
            "EmitSetDelete",
            "EmitSetDifference",
            "EmitSetEntries",
            "EmitSetForEach",
            "EmitSetHas",
            "EmitSetIntersection",
            "EmitSetIsDisjointFrom",
            "EmitSetIsSubsetOf",
            "EmitSetIsSupersetOf",
            "EmitSetIteratorBody",
            "EmitSetKeys",
            "EmitSetMethods",
            "EmitSetSize",
            "EmitSetSymmetricDifference",
            "EmitSetUnion",
            "EmitSetUnionLoop",
            "EmitSetValues",
            "EmitReferenceEqualityComparerClass",
            "EmitReferenceEqualityComparerEquals",
            "EmitReferenceEqualityComparerGetHashCode",
            "EmitMapGroupBy",
            "EmitMapCollectionIteratorType",
            "EmitSetCollectionIteratorType",
            "EmitGetMapProperty",
            "EmitGetSetProperty",
        ];
        foreach (string name in helpers)
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, InstanceMembers | BindingFlags.Static)!;
            Assert.NotNull(method);
            foreach (var parameter in method.GetParameters())
            {
                Assert.NotEqual(typeof(EmittedRuntime), parameter.ParameterType);
                Assert.NotEqual(typeof(RuntimeFeatureSet), parameter.ParameterType);
                if (parameter.ParameterType.DeclaringType != typeof(RuntimeEmitter)) continue;
                Assert.DoesNotContain(parameter.ParameterType.GetProperties(), property =>
                    property.PropertyType == typeof(EmittedRuntime) || property.PropertyType == typeof(RuntimeFeatureSet));
            }
        }
        string[] formerHandles =
        [
            "ReferenceEqualityComparerInstance",
            "MapNullSentinel",
            "BoundMapMethodType",
            "BoundMapMethodCtor",
            "BoundMapMethodInvoke",
            "BoundMapMethodMapField",
            "BoundMapMethodNameField",
            "NormalizeMapKey",
            "DenormalizeMapKey",
            "CreateMap",
            "CreateMapFromEntries",
            "MapSize",
            "MapGet",
            "MapSet",
            "MapHas",
            "MapDelete",
            "MapClear",
            "MapKeys",
            "MapValues",
            "MapEntries",
            "MapForEach",
            "MapCollectionIteratorCtor",
            "GetMapProperty",
            "MapGroupBy",
            "BoundSetMethodType",
            "BoundSetMethodCtor",
            "BoundSetMethodInvoke",
            "BoundSetMethodSetField",
            "BoundSetMethodNameField",
            "CreateSet",
            "CreateSetFromArray",
            "SetSize",
            "SetAdd",
            "SetHas",
            "SetDelete",
            "SetClear",
            "SetKeys",
            "SetValues",
            "SetEntries",
            "SetForEach",
            "SetUnion",
            "SetIntersection",
            "SetDifference",
            "SetSymmetricDifference",
            "SetIsSubsetOf",
            "SetIsSupersetOf",
            "SetIsDisjointFrom",
            "SetCollectionIteratorCtor",
            "GetSetProperty",
        ];
        foreach (string name in formerHandles) Assert.Null(typeof(EmittedRuntime).GetProperty(name));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    public void GenericDispatchUsesProvidedComponentsWhenAnalysisFlagsDiffer(int supplied, int flags)
    {
        var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer("const value=1;").ScanTokens()).ParseOrThrow());
        features.UsesMap = (supplied & 1) != 0;
        features.UsesSet = (supplied & 2) != 0;
        var builder = NewAssembly();
        var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(module, features);
        features.UsesMap = (flags & 1) != 0;
        features.UsesSet = (flags & 2) != 0;
        var probe = module.DefineType("CollectionDispatch", TypeAttributes.Public);
        ObjectReadTestSupport.EmitProperty(emitter, probe, runtime);
        var typeOfHelper = typeof(RuntimeEmitter).GetMethod("EmitTypeOf", InstanceMembers)!;
        var inputConstructor = Assert.Single(typeOfHelper.GetParameters()[2].ParameterType.GetConstructors());
        var typeOfInputs = inputConstructor.Invoke(inputConstructor.GetParameters()
            .Select(parameter => (parameter.Name switch { "BoundAnyFunctionType" => runtime.FunctionBindings.AnyType, "BoundTSFunctionType" => runtime.FunctionBindings.BoundType, "FunctionApplyWrapperType" => runtime.FunctionBindings.ApplyType, "FunctionBindWrapperType" => runtime.FunctionBindings.BindType, "FunctionCallWrapperType" => runtime.FunctionBindings.CallType, "TSFunctionType" => runtime.FunctionValues.Type, _ => (parameter.Name switch { "InvokeValue" => runtime.Invocation.Value, "InvokeMethodValue" => runtime.Invocation.Method, "InvokeMethodValue0" => runtime.Invocation.Method0, _ => (parameter.Name! switch { "IUnionTypeInterface" => runtime.UnionValues.Interface, "IUnionTypeValueGetter" => runtime.UnionValues.ValueGetter, "UndefinedType" => runtime.Sentinels.UndefinedType, "UndefinedInstance" => runtime.Sentinels.UndefinedInstance, _ => typeof(EmittedRuntime).GetProperty(parameter.Name!)!.GetValue(runtime) }) }) })).ToArray());
        typeOfHelper.Invoke(emitter, [probe, new EmittedOperatorRuntime(), typeOfInputs]);
        var invokeHelper = typeof(RuntimeEmitter).GetMethod("EmitInvokeValue", InstanceMembers)!;
        var invokeConstructor = invokeHelper.GetParameters()[2].ParameterType.GetConstructors().Single();
        var invokeInputs = invokeConstructor.Invoke(invokeConstructor.GetParameters().Select(parameter =>
            parameter.ParameterType == typeof(bool)
                ? typeof(RuntimeFeatureSet).GetProperty(parameter.Name!)!.GetValue(features)
                : (parameter.Name! switch { "UndefinedType" => runtime.Sentinels.UndefinedType, "UndefinedInstance" => runtime.Sentinels.UndefinedInstance, _ => typeof(EmittedRuntime).GetProperty(parameter.Name!)!.GetValue(runtime) })).ToArray());
        invokeHelper.Invoke(emitter, [probe, new EmittedInvocationRuntime { Method = runtime.Invocation.Method }, invokeInputs]);
        probe.CreateType();
        using var bytes = new MemoryStream();
        builder.Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        var dispatch = assembly.GetType("CollectionDispatch")!;
        object? Call(string method, params object?[] args) => dispatch.GetMethod(method)!.Invoke(null, args);
        if ((supplied & 1) != 0)
        {
            var bound = Call("GetProperty", new Dictionary<object, object?> { ["key"] = 7d }, "get");
            Assert.Equal("function", Call("TypeOf", bound));
            Assert.Equal(7d, Call("InvokeValue", bound, new object[] { "key" }));
        }
        if ((supplied & 2) != 0)
        {
            var bound = Call("GetProperty", new HashSet<object> { "key" }, "has");
            Assert.Equal("function", Call("TypeOf", bound));
            Assert.Equal(true, Call("InvokeValue", bound, new object[] { "key" }));
        }
    }

    private static PersistedAssemblyBuilder NewAssembly() => new(
        new AssemblyName($"map_set_owner_{Guid.NewGuid():N}"), typeof(object).Assembly);

    private static object Declare(TypeBuilder builder, PropertyInfo property)
    {
        if (property.PropertyType == typeof(TypeBuilder)) return builder;
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
