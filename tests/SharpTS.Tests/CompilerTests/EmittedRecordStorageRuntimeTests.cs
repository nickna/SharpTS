using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedRecordStorageRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static IEnumerable<object[]> RequiredDeclarations => new[]
    {
        "MarkerInterface", "Scalars.Type", "Scalars.ArrayConstructor", "Scalars.ShapeGetter",
        "Scalars.GetValue", "Scalars.IsMaterializedGetter", "ScalarInlineTypes", "ScalarInlineCtors",
        "ScalarInlineGetters", "TypedScalarTypes", "TypedScalarCtors", "TypedScalarShapeFields",
        "TypedScalarValueFields", "CompactTypes", "CompactCtors", "CompactValueFields",
        "CompactAnyMaterializedFields", "CompactIsMaterializedGetters", "CompactTryGetMaterializedDictionary"
    }.Select(name => new object[] {name});

    [Theory]
    [MemberData(nameof(RequiredDeclarations))]
    public void MissingDeclarationCanBeRepairedBeforeEitherOwnerFreezes(string missing)
    {
        var (owner, repair, _) = Declarations(missing);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.False(owner.IsComplete);
        Assert.False(owner.RequireScalars().IsComplete);
        repair();
        owner.CompleteEmission();
        Assert.True(owner.IsComplete);
        Assert.True(owner.RequireScalars().IsComplete);
        Assert.Equal(6, owner.TypedScalarValueFields.Count);
        Assert.Equal(2, owner.CompactValueFields.Count);
    }

    [Fact]
    public void AbsentStorageKeepsEmptyReadOnlyLookupsAndRequiredMarker()
    {
        var first = new EmittedRuntime();
        var second = new EmittedRuntime();
        Assert.NotSame(first.Records, second.Records);
        Assert.NotSame(first.JsonShapes, second.JsonShapes);
        var owner = first.Records;
        Assert.Null(owner.Scalars);
        Assert.Throws<InvalidOperationException>(owner.RequireScalars);
        Assert.Throws<InvalidOperationException>(() => owner.MarkerInterface);
        Assert.Throws<ArgumentNullException>(() => owner.MarkerInterface = null!);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.False(owner.CompactTypes.TryGetValue("absent", out _));
        Assert.False(owner.TypedScalarTypes.TryGetValue("absent", out _));
        foreach (var view in Registries(owner)) Assert.Empty(view);
        owner.MarkerInterface = typeof(IDisposable);
        owner.CompleteEmission();
        Assert.Throws<InvalidOperationException>(owner.BeginScalarEmission);
        Assert.Throws<InvalidOperationException>(() => owner.MarkerInterface = typeof(ICloneable));
        Assert.False(first.JsonShapes.IsComplete);
    }

    [Fact]
    public void PublishedViewsCannotMutateRegistriesAndEveryWriterFreezes()
    {
        var (owner, _, writes) = Declarations();
        var views = Registries(owner).ToArray();
        Assert.Equal(13, views.Length);
        foreach (var view in views) Assert.Throws<NotSupportedException>(view.Clear);
        Assert.Throws<InvalidOperationException>(owner.BeginScalarEmission);
        owner.CompleteEmission();
        foreach (var write in writes) Assert.Throws<InvalidOperationException>(write);
        foreach (var view in views) Assert.Throws<NotSupportedException>(view.Clear);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.Throws<InvalidOperationException>(owner.RequireScalars().CompleteEmission);
        Assert.Throws<InvalidOperationException>(owner.BeginScalarEmission);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void CompactArityGuardRejectsUnsupportedLayoutsWithoutRegistration(int count)
    {
        var (owner, _, _) = Declarations();
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.AddCompactTypes("invalid", owner.RequireScalars().Type, count));
        Assert.False(owner.CompactTypes.ContainsKey("invalid"));
        owner.CompleteEmission();
    }

    [Fact]
    public void UnexpectedInlineSlotCannotMasqueradeAsACompleteLayout()
    {
        var (owner, _, _) = Declarations();
        owner.AddScalarInlineGetters((4, 4), owner.RequireScalars().GetValue);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.False(owner.IsComplete);
        Assert.False(owner.RequireScalars().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesSelectionAssemblyOwnershipAndInstanceIsolation(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new List<EmittedRecordStorageRuntime>();
        var guestStores = new List<object>();
        foreach (string source in new[]
        {
            "const a = {x:7,name:'n'}; console.log(JSON.stringify(a));",
            "const value = 1;",
            "const a = {x:7,name:'n'}; console.log(a.x);",
            "console.log(JSON.parse('1'));",
            "const a = {x:7,name:'n'}; console.log(JSON.stringify(a));"
        })
        {
            var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var features = new RuntimeFeatureDetector().Detect(statements, new TypeChecker().Check(statements));
            var builder = new PersistedAssemblyBuilder(new AssemblyName($"record_owner_{Guid.NewGuid():N}"), typeof(object).Assembly);
            var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), features);
            var owner = runtime.Records;
            Assert.DoesNotContain(owner, owners);
            owners.Add(owner);
            Assert.True(owner.IsComplete);
            Assert.False(runtime.JsonShapes.IsComplete);
            Assert.Empty(runtime.JsonShapes.Fields);
            Assert.Equal(features.UsesJSON || features.UsesCompactObjectRecords, owner.Scalars is not null);
            Assert.Equal(features.UsesJSON, runtime.Json.Implementation is not null);
            Assert.Equal(owner.Scalars is null ? 0 : 4, owner.ScalarInlineTypes.Count);
            Assert.Equal(owner.Scalars is null ? 0 : 10, owner.ScalarInlineGetters.Count);
            Assert.Same(builder, owner.MarkerInterface.Module.Assembly);
            foreach (var view in Registries(owner))
                foreach (MemberInfo handle in view.Values) Assert.Same(builder, handle.Module.Assembly);
            if (owner.Scalars is { } scalars)
            {
                Assert.True(scalars.IsComplete);
                foreach (var property in typeof(EmittedScalarRecordRuntime).GetProperties()
                             .Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)))
                    Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(scalars)).Module.Assembly);
            }
            using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
            using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
            Assert.Empty(verifier.Verify(bytes));
            var assembly = Assembly.Load(bytes.ToArray());
            var references = assembly.GetReferencedAssemblies().Select(r => r.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references);
            Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("record_owner_", StringComparison.Ordinal));
            foreach (string typeName in owner.CompactTypes.Values.Select(type => type.Name))
            {
                var type = assembly.GetType(typeName)!;
                var first = Activator.CreateInstance(type, [7d, "n"])!;
                var second = Activator.CreateInstance(type, [7d, "n"])!;
                var guard = type.GetMethod("get_IsMaterialized", Members)!;
                var get = type.GetMethod("GetProperty", Members)!;
                Assert.Equal(false, guard.Invoke(first, null));
                Assert.Equal(false, guard.Invoke(second, null));
                type.GetMethod("SetProperty", Members)!.Invoke(first, ["x", 9d]);
                Assert.Equal(true, guard.Invoke(first, null));
                Assert.Equal(false, guard.Invoke(second, null));
                Assert.Equal(9d, get.Invoke(first, ["x"]));
                Assert.Equal(7d, get.Invoke(second, ["x"]));
                var store = type.GetField("_materialized", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                Assert.DoesNotContain(guestStores, prior => ReferenceEquals(prior, store));
                guestStores.Add(store);
            }
        }
    }

    private static IEnumerable<IDictionary> Registries(EmittedRecordStorageRuntime owner) =>
        typeof(EmittedRecordStorageRuntime).GetProperties()
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
            .Select(p => (IDictionary)p.GetValue(owner)!);

    private static (EmittedRecordStorageRuntime Owner, Action Repair, List<Action> Writes) Declarations(string? missing = null)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName($"record_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = builder.DefineDynamicModule("main").DefineType("Declarations");
        var ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var method = type.DefineMethod("Method", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        var field = type.DefineField("Value", typeof(object), FieldAttributes.Public);
        var owner = new EmittedRecordStorageRuntime();
        owner.BeginScalarEmission();
        Action repair = () => { };
        var writes = new List<Action>();
        void Add(string name, Action action, bool last = true)
        {
            writes.Add(action);
            if (name == missing && last) repair = action;
            else action();
        }
        Add("MarkerInterface", () => owner.MarkerInterface = typeof(IDisposable));
        Add("Scalars.Type", () => owner.RequireScalars().Type = type);
        Add("Scalars.ArrayConstructor", () => owner.RequireScalars().ArrayConstructor = ctor);
        Add("Scalars.ShapeGetter", () => owner.RequireScalars().ShapeGetter = method);
        Add("Scalars.GetValue", () => owner.RequireScalars().GetValue = method);
        Add("Scalars.IsMaterializedGetter", () => owner.RequireScalars().IsMaterializedGetter = method);
        for (int arity = 1; arity <= 4; arity++)
        {
            int capturedArity = arity;
            Add("ScalarInlineTypes", () => owner.AddScalarInlineTypes(capturedArity, type), arity == 4);
            Add("ScalarInlineCtors", () => owner.AddScalarInlineCtors(capturedArity, ctor), arity == 4);
            for (int index = 0; index < arity; index++)
            {
                int slot = index;
                Add("ScalarInlineGetters", () => owner.AddScalarInlineGetters((capturedArity, slot), method), arity == 4 && index == 3);
            }
        }
        Add("TypedScalarTypes", () => owner.AddTypedScalarTypes("wide", type, 6));
        Add("TypedScalarCtors", () => owner.AddTypedScalarCtors("wide", ctor));
        Add("TypedScalarShapeFields", () => owner.AddTypedScalarShapeFields("wide", field));
        for (int index = 0; index < 6; index++)
        {
            int slot = index;
            Add("TypedScalarValueFields", () => owner.AddTypedScalarValueFields(("wide", slot), field), index == 5);
        }
        Add("CompactTypes", () => owner.AddCompactTypes("small", type, 2));
        Add("CompactCtors", () => owner.AddCompactCtors("small", ctor));
        Add("CompactAnyMaterializedFields", () => owner.AddCompactAnyMaterializedFields("small", field));
        Add("CompactIsMaterializedGetters", () => owner.AddCompactIsMaterializedGetters("small", method));
        Add("CompactTryGetMaterializedDictionary", () => owner.AddCompactTryGetMaterializedDictionary("small", method));
        for (int index = 0; index < 2; index++)
        {
            int slot = index;
            Add("CompactValueFields", () => owner.AddCompactValueFields(("small", slot), field), index == 1);
        }
        return (owner, repair, writes);
    }
}
