using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeEvaluatedConstructorMetadataTests
{
    private const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    [Fact]
    public void ExactClosedTokensReturnTheActualTypeAndTheLoadedFirstConstructor()
    {
        var fixture = new Fixture();
        fixture.Token(typeof(Dictionary<string, object>), NativeConstructorValueClosureKind.ExactClosedBclToken);
        fixture.Token(typeof(int), NativeConstructorValueClosureKind.NongenericLiteral);
        var saved = fixture.Complete();
        var close = Query(saved, "CloseEvaluated"); var first = Query(saved, "FirstPublicConstructor");
        Assert.Same(typeof(Dictionary<string, object>), close.Invoke(null, [typeof(Dictionary<string, object>), new[] { typeof(double) }, typeof(object)]));
        Assert.Equal(typeof(Dictionary<string, object>).GetConstructors()[0], first.Invoke(null, [typeof(Dictionary<string, object>)]));
        Assert.Same(typeof(int), close.Invoke(null, [typeof(int), null, typeof(object)]));
        Assert.IsType<IndexOutOfRangeException>(Assert.Throws<TargetInvocationException>(() => first.Invoke(null, [typeof(int)])).InnerException);
        Assert.False(fixture.Metadata.UsesLegacy);
        Assert.DoesNotContain(Instructions(close), instruction => instruction.Member?.Name == "MakeGenericType");
    }

    [Fact]
    public void ForeignAndHostileTypesCannotAuthorizeMetadataOperations()
    {
        var fixture = new Fixture(); fixture.Token(typeof(object), NativeConstructorValueClosureKind.NongenericLiteral);
        var saved = fixture.Complete();
        var hostile = new HostileType(typeof(object));
        foreach (string operation in new[] { "CloseEvaluated", "FirstPublicConstructor" })
        {
            object?[] Arguments(Type type) => operation == "CloseEvaluated" ? [type, null, typeof(object)] : [type];
            Refused(saved, () => Query(saved, operation).Invoke(null, Arguments(typeof(Uri))));
            Refused(saved, () => Query(saved, operation).Invoke(null, Arguments(hostile)));
            Refused(saved, () => Query(saved, operation).Invoke(null, Arguments(null!)));
        }
        Assert.Equal(0, hostile.EqualsCalls); Assert.Equal(0, hostile.MetadataCalls);
    }

    [Fact]
    public void TrustedUnprovedSourceFamilyKeepsCompleteClosureAndConstructorSelection()
    {
        var fixture = new Fixture();
        var owner = fixture.Module.DefineType("Source", TypeAttributes.Public);
        owner.DefineGenericParameters("T"); owner.DefineDefaultConstructor(MethodAttributes.Public);
        owner.SetCustomAttribute(fixture.Runtime.DynamicConstruction.FactoryAttributeConstructor,
            CustomAttributeEncoder.Encode(fixture.Runtime.DynamicConstruction.FactoryAttributeConstructor, "factory"));
        owner.CreateType();
        fixture.Token(owner, NativeConstructorValueClosureKind.UnprovedSourceGenericFamily, owner);
        var saved = fixture.Complete();
        var definition = Assert.Single(saved.GetTypes(), type => type.Name.StartsWith("Source", StringComparison.Ordinal));
        var close = Query(saved, "CloseEvaluated"); var first = Query(saved, "FirstPublicConstructor");
        var supplied = definition.MakeGenericType(typeof(string));
        Assert.Equal(supplied, close.Invoke(null, [definition, new[] { typeof(string) }, definition]));
        var canonical = definition.MakeGenericType(typeof(object));
        Assert.Equal(canonical, close.Invoke(null, [definition, new[] { typeof(string) }, typeof(object)]));
        Assert.Equal(supplied.GetConstructors()[0], first.Invoke(null, [supplied]));
        Assert.Same(supplied, close.Invoke(null, [supplied, new[] { typeof(double) }, definition]));
        Refused(saved, () => close.Invoke(null, [typeof(List<>), null, definition]));
        Refused(saved, () => first.Invoke(null, [typeof(List<string>)]));
        Assert.True(fixture.Metadata.UsesLegacy); Assert.NotEmpty(fixture.Metadata.Blockers);
        Assert.Contains(Instructions(close), instruction => instruction.Member?.Name == "MakeGenericType");
        var hostile = new HostileType(definition);
        Refused(saved, () => close.Invoke(null, [hostile, null, definition]));
        Refused(saved, () => first.Invoke(null, [hostile]));
        Refused(saved, () => close.Invoke(null, [null, null, definition]));
        Assert.Equal(0, hostile.EqualsCalls); Assert.Equal(0, hostile.MetadataCalls);
    }

    [Fact]
    public void MissingProducerEvidenceAndWrongOutputCannotCompleteMetadata()
    {
        var fixture = new Fixture();
        var record = new NativeConstructorValueOwnerSnapshot(typeof(object), [], NativeConstructorValueClosureKind.NongenericLiteral,
            null, false, true, typeof(object).GetConstructors(), []);
        var snapshot = new NativeConstructorValueProducerSnapshot(fixture.Module, [record], []);
        Assert.Throws<InvalidOperationException>(() => fixture.Metadata.CompleteEmission(snapshot, fixture.Runtime));
        var other = new Fixture();
        Assert.Throws<InvalidOperationException>(() => fixture.Metadata.CompleteEmission(
            new NativeConstructorValueProducerSnapshot(other.Module, [], []), fixture.Runtime));
    }

    [Fact]
    public void LiteralQueriesDoNotExecuteTypeInitializersOrConstructorBodies()
    {
        var fixture = new Fixture();
        var effects = fixture.Module.DefineType("Effects", TypeAttributes.Public);
        var count = effects.DefineField("Count", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        effects.CreateType();
        var owner = fixture.Module.DefineType("WithEffects", TypeAttributes.Public);
        var init = owner.DefineTypeInitializer().GetILGenerator();
        init.Emit(OpCodes.Ldc_I4_1); init.Emit(OpCodes.Stsfld, count); init.Emit(OpCodes.Ret);
        var constructor = owner.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var body = constructor.GetILGenerator();
        body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        body.Emit(OpCodes.Ldsfld, count); body.Emit(OpCodes.Ldc_I4, 10); body.Emit(OpCodes.Add);
        body.Emit(OpCodes.Stsfld, count); body.Emit(OpCodes.Ret);
        owner.CreateType();
        fixture.Token(owner, NativeConstructorValueClosureKind.NongenericLiteral, publicConstructors: [constructor]);
        fixture.EmitTimingProbe(owner, count);
        var saved = fixture.Complete(Environment.GetEnvironmentVariable("SHARPTS_CONSTRUCTOR_METADATA_NATIVE_FIXTURE"));
        var actual = saved.GetType("WithEffects")!;
        var savedCount = saved.GetType("Effects")!.GetField("Count")!;
        Assert.Equal(0, savedCount.GetValue(null));
        Assert.Same(actual, Query(saved, "CloseEvaluated").Invoke(null, [actual, null, typeof(object)]));
        var selected = Assert.IsAssignableFrom<ConstructorInfo>(Query(saved, "FirstPublicConstructor").Invoke(null, [actual]));
        Assert.Equal(actual.GetConstructors()[0], selected);
        Assert.Equal(0, savedCount.GetValue(null));
        selected.Invoke(null);
        Assert.Equal(11, savedCount.GetValue(null));
    }

    [Fact]
    public void CompletedSnapshotCopiesProducerCollections()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("immutable_constructor_values"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var producers = new List<NativeConstructorValueProducer>();
        var constructors = new List<ConstructorInfo> { typeof(object).GetConstructors()[0] };
        var blockers = new List<string> { "original" };
        var entry = new NativeConstructorValueOwnerSnapshot(typeof(object), producers,
            NativeConstructorValueClosureKind.NongenericLiteral, null, false, true, constructors, blockers);
        var owners = new List<NativeConstructorValueOwnerSnapshot> { entry };
        var snapshot = new NativeConstructorValueProducerSnapshot(module, owners, blockers);
        owners.Clear(); constructors.Clear(); blockers.Clear();
        Assert.Single(snapshot.Owners);
        Assert.Single(snapshot.Owners[0].PublicConstructors);
        Assert.Equal("original", Assert.Single(snapshot.Owners[0].Blockers));
        Assert.Equal("original", Assert.Single(snapshot.Blockers));
        Assert.NotSame(producers, snapshot.Owners[0].Producers);
    }
    private sealed class Fixture
    {
        private readonly PersistedAssemblyBuilder _assembly = new(new AssemblyName("constructor_metadata_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        private readonly List<NativeConstructorValueOwnerSnapshot> _records = [];
        internal Fixture()
        {
            Module = _assembly.DefineDynamicModule("main"); Metadata = new(Module, TypeProvider.Runtime);
            var statements = new Parser(new Lexer("console.log(42);").ScanTokens()).ParseOrThrow();
            var features = new RuntimeFeatureDetector().Detect(statements);
            Runtime = new RuntimeEmitter(TypeProvider.Runtime, false, true).EmitAll(Module, features, FunctionEmissionPolicy.Full,
                nativeConstructorMetadata: Metadata.Handles);
        }
        internal ModuleBuilder Module { get; }
        internal NativeEvaluatedConstructorMetadata Metadata { get; }
        internal EmittedRuntime Runtime { get; }
        internal void Token(Type owner, NativeConstructorValueClosureKind closure, Type? family = null, ConstructorInfo[]? publicConstructors = null)
        {
            var producer = Module.DefineType("Producer" + _records.Count, TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var method = producer.DefineMethod("Value", MethodAttributes.Static, typeof(Type), Type.EmptyTypes);
            var il = method.GetILGenerator(); int offset = il.ILOffset;
            il.Emit(OpCodes.Ldtoken, owner); il.Emit(OpCodes.Call, TypeProvider.Runtime.TypeGetTypeFromHandle); il.Emit(OpCodes.Ret);
            producer.CreateType();
            var evidence = new NativeConstructorValueProducer(owner, family is null ? NativeConstructorValueProducerKind.BuiltInConstructor : NativeConstructorValueProducerKind.SourceClass,
                il, offset, method, null);
            _records.Add(new(owner, [evidence], closure, family, false, !FunctionGenericParameterRegistry.ContainsUnboundType(owner),
                publicConstructors ?? (FunctionGenericParameterRegistry.ContainsUnboundType(owner) ? [] : owner.GetConstructors()), []));
        }
        internal void EmitTimingProbe(Type owner, FieldInfo count)
        {
            var probe = Module.DefineType("NativeMetadataProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var read = probe.DefineMethod("Read", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
            var il = read.GetILGenerator(); il.Emit(OpCodes.Ldsfld, count); il.Emit(OpCodes.Ret);
            foreach (bool construct in new[] { false, true })
            {
                var method = probe.DefineMethod(construct ? "Construct" : "Query", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
                il = method.GetILGenerator();
                il.Emit(OpCodes.Ldtoken, owner); il.Emit(OpCodes.Call, TypeProvider.Runtime.TypeGetTypeFromHandle);
                il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldtoken, typeof(object));
                il.Emit(OpCodes.Call, TypeProvider.Runtime.TypeGetTypeFromHandle); il.Emit(OpCodes.Call, Metadata.Handles.CloseEvaluated);
                il.Emit(OpCodes.Call, Metadata.Handles.FirstPublicConstructor);
                if (construct)
                {
                    il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newarr, typeof(object));
                    il.Emit(OpCodes.Callvirt, typeof(ConstructorInfo).GetMethod("Invoke", [typeof(object[])])!);
                }
                il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ret);
            }
            var refuse = probe.DefineMethod("Refuses", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), [typeof(Type)]);
            il = refuse.GetILGenerator();
            var done = il.DefineLabel(); var refused = il.DeclareLocal(typeof(bool));
            il.BeginExceptionBlock(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldtoken, typeof(object)); il.Emit(OpCodes.Call, TypeProvider.Runtime.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Call, Metadata.Handles.CloseEvaluated); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, refused); il.Emit(OpCodes.Leave, done);
            il.BeginCatchBlock(typeof(Exception)); il.Emit(OpCodes.Call, Runtime.Errors.WrapException);
            il.Emit(OpCodes.Isinst, Runtime.Errors.TypeErrorType); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Cgt_Un);
            il.Emit(OpCodes.Stloc, refused); il.Emit(OpCodes.Leave, done); il.EndExceptionBlock();
            il.MarkLabel(done); il.Emit(OpCodes.Ldloc, refused); il.Emit(OpCodes.Ret); probe.CreateType();
        }
        internal Assembly Complete(string? exportPath = null)
        {
            Metadata.CompleteEmission(new(Module, _records, []), Runtime);
            using var image = new MemoryStream(); _assembly.Save(image);
            byte[] bytes = AssemblyReferenceRewriteCompatibility.Rewrite(image.ToArray(), PEPacker.EmbeddedReferenceAssemblyIndex.Default,
                PEPacker.ReferencePolicy.DroppingReferences("SharpTS"));
            if (!string.IsNullOrEmpty(exportPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(exportPath))!);
                File.WriteAllBytes(exportPath, bytes);
            }
            using var verify = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
            Assert.Empty(verifier.Verify(verify)); return Assembly.Load(bytes);
        }
    }

    private sealed class HostileType(Type type) : TypeDelegator(type)
    {
        internal int EqualsCalls { get; private set; }
        internal int MetadataCalls { get; private set; }
        public override bool Equals(object? other) { EqualsCalls++; return true; }
        public override int GetHashCode() => 0;
        public override Type UnderlyingSystemType { get { MetadataCalls++; throw new InvalidOperationException("Untrusted underlying metadata."); } }
        public override Type GetGenericTypeDefinition() { MetadataCalls++; throw new InvalidOperationException("Untrusted generic metadata."); }
        public override ConstructorInfo[] GetConstructors(BindingFlags flags) { MetadataCalls++; throw new InvalidOperationException("Untrusted constructor metadata."); }
    }
    private static MethodInfo Query(Assembly saved, string name) => saved.GetType("$NativeEvaluatedConstructorMetadata")!.GetMethod(name, Methods)!;
    private static void Refused(Assembly saved, Action action)
    {
        var exception = Assert.Throws<TargetInvocationException>(action).InnerException!;
        var error = saved.GetType("$Runtime")!.GetMethod("WrapException")!.Invoke(null, [exception])!;
        Assert.Equal("$TypeError", error.GetType().Name);
        Assert.Equal(NativeEvaluatedConstructorMetadata.ForeignTypeMessage, error.GetType().GetProperty("Message")!.GetValue(error));
    }
}