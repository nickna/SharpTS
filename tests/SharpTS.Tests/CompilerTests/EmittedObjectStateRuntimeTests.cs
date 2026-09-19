using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedObjectStateRuntimeTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static PropertyInfo[] Handles => typeof(EmittedObjectStateRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType)).ToArray();
    public static IEnumerable<object[]> Declarations => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingDeclarationIsRepairableAndCompletionFreezesMetadata(string missing)
    {
        var owner = new EmittedObjectStateRuntime();
        var property = typeof(EmittedObjectStateRuntime).GetProperty(missing)!;
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        Fill(owner, missing); owner.MarkIsExtensibleBodyEmitted();
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        var handle = Handle(property.PropertyType); property.SetValue(owner, handle);
        Assert.Same(handle, property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, Handle(property.PropertyType)));
        Assert.Same(handle, property.GetValue(owner));
        owner.CompleteEmission(); AssertFrozen(owner);
    }

    [Fact]
    public void ForwardDeclarationRemainsAvailableWhileItsBodyCompletionIsRequired()
    {
        var owner = new EmittedObjectStateRuntime(); Fill(owner);
        var forward = owner.IsExtensible;
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        Assert.Same(forward, owner.IsExtensible);
        owner.MarkIsExtensibleBodyEmitted();
        Assert.Throws<InvalidOperationException>(owner.MarkIsExtensibleBodyEmitted);
        owner.CompleteEmission(); AssertFrozen(owner);
        Assert.Throws<InvalidOperationException>(owner.MarkIsExtensibleBodyEmitted);
    }

    [Fact]
    public void RequiredOwnerBelongsToOneCompilation()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.ObjectState, second.ObjectState);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ObjectState))!.SetMethod);
        Assert.Equal(12, Handles.Length); Assert.False(first.ObjectState.IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterKeepsGuestTablesSeparateAndMutableAfterMetadataCompletion(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new List<EmittedObjectStateRuntime>(); var tables = new List<object>();
        foreach (bool usesState in new[] { false, true, false, true })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"),
                Detect(usesState ? "const value = { a: 1 }; Object.freeze(value);" : "const value = 1;"));
            var owner = runtime.ObjectState; Assert.DoesNotContain(owner, owners); owners.Add(owner); AssertFrozen(owner);
            foreach (var property in Handles)
                Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(owner)).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var references = loaded.GetReferencedAssemblies().Select(r => r.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references); Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("object_state_runtime_", StringComparison.Ordinal));
            var type = loaded.GetType("$Runtime")!;
            foreach (var property in Handles.Where(property => property.PropertyType == typeof(FieldBuilder)))
            {
                var field = type.GetField(((FieldBuilder)property.GetValue(owner)!).Name)!;
                Assert.True(field.IsStatic); Assert.True(field.IsInitOnly);
                var table = field.GetValue(null)!; Assert.DoesNotContain(tables, earlier => ReferenceEquals(earlier, table)); tables.Add(table);
            }
            foreach (object? primitive in new object?[] { null, "value", 1d, true })
            {
                Assert.Equal(true, Call(type, "ObjectIsFrozen", primitive));
                Assert.Equal(true, Call(type, "ObjectIsSealed", primitive));
                Assert.Equal(false, Call(type, "ObjectIsExtensible", primitive));
            }
            var frozen = new Dictionary<string, object> { ["a"] = 1d };
            var sealedValue = new Dictionary<string, object> { ["a"] = 1d };
            var prevented = new Dictionary<string, object> { ["a"] = 1d };
            Assert.Equal(true, Call(type, "ObjectIsExtensible", frozen));
            Assert.Same(frozen, Call(type, "ObjectFreeze", frozen));
            Assert.Equal(true, Call(type, "ObjectIsFrozen", frozen)); Assert.Equal(true, Call(type, "ObjectIsSealed", frozen));
            Assert.Equal(false, Call(type, "ObjectIsExtensible", frozen));
            Call(type, "SetProperty", frozen, "a", 2d); Call(type, "SetProperty", frozen, "b", 3d);
            Assert.Equal(1d, frozen["a"]); Assert.False(frozen.ContainsKey("b"));
            Assert.Same(sealedValue, Call(type, "ObjectSeal", sealedValue));
            Assert.Equal(true, Call(type, "ObjectIsSealed", sealedValue)); Assert.Equal(false, Call(type, "ObjectIsFrozen", sealedValue));
            Assert.Equal(false, Call(type, "ObjectIsExtensible", sealedValue));
            Call(type, "SetProperty", sealedValue, "a", 2d); Call(type, "SetProperty", sealedValue, "b", 3d);
            Assert.Equal(2d, sealedValue["a"]); Assert.False(sealedValue.ContainsKey("b"));
            Assert.Same(prevented, Call(type, "ObjectPreventExtensions", prevented)); Assert.Equal(false, Call(type, "ObjectIsExtensible", prevented));
            Call(type, "SetProperty", prevented, "a", 2d); Call(type, "SetProperty", prevented, "b", 3d);
            Assert.Equal(2d, prevented["a"]); Assert.False(prevented.ContainsKey("b"));
            var marked = new object(); var untouched = new object();
            Assert.Equal(false, Call(type, "IsBuiltinDeleted", marked, "name"));
            Call(type, "MarkBuiltinDeleted", marked, "name"); Call(type, "MarkBuiltinDeleted", marked, "name");
            Assert.Equal(true, Call(type, "IsBuiltinDeleted", marked, "name"));
            Assert.Equal(false, Call(type, "IsBuiltinDeleted", marked, "length")); Assert.Equal(false, Call(type, "IsBuiltinDeleted", untouched, "name"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntegrityHelperUsesSuppliedStateTables(bool freeze)
    {
        var builder = NewAssembly(); var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect("const value = 1;"));
        var type = runtime.RuntimeClass.Type.DefineNestedType("IntegrityProbe", TypeAttributes.NestedPublic);
        string operation = freeze ? nameof(EmittedObjectStateRuntime.Freeze) : nameof(EmittedObjectStateRuntime.Seal);
        var supplied = CopyWithNewTables(runtime.ObjectState, type, operation);
        var method = typeof(RuntimeEmitter).GetMethod(freeze ? "EmitObjectFreeze" : "EmitObjectSeal", InstanceMembers)!;
        var inputs = Activator.CreateInstance(method.GetParameters()[2].ParameterType,
            runtime.ArrayStorage, runtime.DescriptorStorage, runtime.ObjectStorage)!;
        method.Invoke(emitter, [type, supplied, inputs]); supplied.MarkIsExtensibleBodyEmitted(); supplied.CompleteEmission();
        type.CreateType(); var loaded = SaveVerifyLoad(builder); var probe = loaded.GetType(type.FullName!)!;
        var value = new Dictionary<string, object> { ["a"] = 1d };
        Assert.Same(value, Call(probe, freeze ? "ObjectFreeze" : "ObjectSeal", value));
        Assert.True(Table(probe, supplied.SealedObjects.Name).TryGetValue(value, out _));
        Assert.Equal(freeze, Table(probe, supplied.FrozenObjects.Name).TryGetValue(value, out _));
        var actualRuntime = loaded.GetType("$Runtime")!;
        Assert.False(Table(actualRuntime, runtime.ObjectState.FrozenObjects.Name).TryGetValue(value, out _));
        Assert.False(Table(actualRuntime, runtime.ObjectState.SealedObjects.Name).TryGetValue(value, out _));
    }

    [Fact]
    public void DeletionHelpersUseSuppliedTableAfterMetadataCompletion()
    {
        var builder = NewAssembly(); var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect("const value = 1;"));
        var type = runtime.RuntimeClass.Type.DefineNestedType("DeletionProbe", TypeAttributes.NestedPublic);
        var supplied = CopyWithNewTables(runtime.ObjectState, type, nameof(EmittedObjectStateRuntime.MarkBuiltinDeleted), nameof(EmittedObjectStateRuntime.IsBuiltinDeleted));
        typeof(RuntimeEmitter).GetMethod("EmitDeletedBuiltinsHelpers", InstanceMembers)!.Invoke(emitter, [type, supplied]);
        supplied.MarkIsExtensibleBodyEmitted(); supplied.CompleteEmission(); type.CreateType();
        var loaded = SaveVerifyLoad(builder); var probe = loaded.GetType(type.FullName!)!;
        var receiver = new object(); Call(probe, "MarkBuiltinDeleted", receiver, "name");
        Assert.Equal(true, Call(probe, "IsBuiltinDeleted", receiver, "name"));
        Assert.Equal(false, Call(probe, "IsBuiltinDeleted", receiver, "length"));
        Assert.True(Table(probe, supplied.DeletedBuiltins.Name).TryGetValue(receiver, out _));
        Assert.False(Table(loaded.GetType("$Runtime")!, runtime.ObjectState.DeletedBuiltins.Name).TryGetValue(receiver, out _));
    }

    [Fact]
    public void ScopedHelpersDoNotRetainTheWholeRuntimeHolderOrFlatAliases()
    {
        foreach (var name in new[] { "EmitDeletedBuiltinsHelpers", "EmitMarkBuiltinDeleted", "EmitIsBuiltinDeleted", "EmitObjectFreeze", "EmitObjectSeal", "EmitObjectIsFrozen", "EmitObjectIsSealed", "EmitObjectPreventExtensions", "EmitObjectIsExtensible" })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, InstanceMembers)!;
            Assert.Contains(method.GetParameters(), p => p.ParameterType == typeof(EmittedObjectStateRuntime));
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet) || p.ParameterType == typeof(FieldBuilder));
        }
        foreach (var name in new[] { "FrozenObjectsField", "SealedObjectsField", "NonExtensibleObjectsField", "DeletedBuiltinsField", "MarkBuiltinDeletedMethod", "IsBuiltinDeletedMethod", "ObjectFreeze", "ObjectSeal", "ObjectIsFrozen", "ObjectIsSealed", "ObjectPreventExtensions", "ObjectIsExtensible" })
            Assert.Null(typeof(EmittedRuntime).GetProperty(name));
    }

    private static EmittedObjectStateRuntime CopyWithNewTables(EmittedObjectStateRuntime original, TypeBuilder type, params string[] omitted)
    {
        var owner = new EmittedObjectStateRuntime(); var il = type.DefineTypeInitializer().GetILGenerator();
        foreach (var property in Handles.Where(property => !omitted.Contains(property.Name)))
        {
            if (property.PropertyType == typeof(FieldBuilder))
            {
                var field = type.DefineField(property.Name, typeof(ConditionalWeakTable<object, object>), FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
                il.Emit(OpCodes.Newobj, typeof(ConditionalWeakTable<object, object>).GetConstructor(Type.EmptyTypes)!);
                il.Emit(OpCodes.Stsfld, field); property.SetValue(owner, field);
            }
            else property.SetValue(owner, property.GetValue(original));
        }
        il.Emit(OpCodes.Ret); return owner;
    }
    private static ConditionalWeakTable<object, object> Table(Type type, string name) => Assert.IsType<ConditionalWeakTable<object, object>>(type.GetField(name)!.GetValue(null));
    private static void Fill(EmittedObjectStateRuntime owner, string? omitted = null)
    {
        foreach (var property in Handles.Where(property => property.Name != omitted)) property.SetValue(owner, Handle(property.PropertyType));
    }
    private static object Handle(Type kind)
    {
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Declaration");
        return kind == typeof(FieldBuilder) ? type.DefineField("Value", typeof(object), FieldAttributes.Public)
            : type.DefineMethod("Invoke", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
    }
    private static void AssertFrozen(EmittedObjectStateRuntime owner)
    {
        Assert.True(owner.IsComplete); Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        foreach (var property in Handles)
        {
            Assert.False(property.SetMethod!.IsPublic);
            Expect<InvalidOperationException>(() => property.SetValue(owner, property.GetValue(owner)));
        }
    }
    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"object_state_runtime_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
    private static object? Call(Type type, string name, params object?[] values) => type.GetMethod(name, StaticMembers)!.Invoke(null, values);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
}
