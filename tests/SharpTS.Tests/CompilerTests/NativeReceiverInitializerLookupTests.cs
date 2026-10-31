using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeReceiverInitializerLookupTests
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const string Source = """
        class Parent {
            value:any;
            constructor(value:any=7,...rest:any[]){this.value=value+":"+rest.join(",");}
        }
        const P:any=Parent;
        const Child=class extends P {};
        const C:any=Child;
        console.log(new C(undefined,1,2).value);
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualTemplateAndAdapterProducersPreserveReceiverConstruction(bool native)
    {
        var saved = Compile(Source, native, out var compiler);
        Assert.Equal("7:1,2\n", TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
        if (!native)
        {
            Assert.Null(compiler.NativeReceiverInitializers);
            Assert.Null(saved.GetType("$NativeReceiverInitializerLookup"));
            return;
        }
        var lookup = compiler.NativeReceiverInitializers!;
        Assert.True(lookup.IsComplete); Assert.False(lookup.UsesLegacy);
        Assert.NotEmpty(lookup.TemplateOwners); Assert.True(lookup.AdapterCount > 0);
        foreach (var owner in lookup.ClosedOwners)
        {
            Type actual = ReferenceEquals(owner.Module, lookup.Find.Module) ? saved.GetType(owner.FullName!)! : owner;
            Assert.Equal(actual.GetMethod("$InitializeReceiver"), Query(saved).Invoke(null, [actual]));
        }
        Assert.DoesNotContain(Instructions(Query(saved)), row => row.Member?.Name == "MakeGenericType");
    }

    [Fact]
    public void FullLoadedQueryPreservesAmbiguityInheritedMethodsAndNamedMiss()
    {
        var fixture = new Fixture();
        var parent = fixture.Owner("Parent");
        var adapter = parent.DefineMethod("$InitializeReceiver", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [typeof(object), typeof(object[]), typeof(object), typeof(object)]);
        adapter.GetILGenerator().Emit(OpCodes.Ldarg_0); adapter.GetILGenerator().Emit(OpCodes.Ret);
        fixture.Lookup.Record(parent, adapter); fixture.Adapters.Add(parent, adapter);
        var other = parent.DefineMethod("$InitializeReceiver", MethodAttributes.Public, typeof(object), Type.EmptyTypes);
        other.GetILGenerator().Emit(OpCodes.Ldnull); other.GetILGenerator().Emit(OpCodes.Ret);
        parent.CreateType();
        var child = fixture.Owner("Child", parent); child.CreateType();
        var empty = fixture.Owner("Empty"); empty.CreateType();
        fixture.Value(parent); fixture.Value(child); fixture.Value(empty);
        var saved = fixture.Complete();
        var actualParent = saved.GetType("Parent")!;
        Assert.Throws<AmbiguousMatchException>(() => actualParent.GetMethod("$InitializeReceiver"));
        Assert.IsType<AmbiguousMatchException>(Assert.Throws<TargetInvocationException>(() => Query(saved).Invoke(null, [actualParent])).InnerException);
        foreach (string name in new[] { "Child", "Empty" })
        {
            var owner = saved.GetType(name)!;
            Assert.Equal(owner.GetMethod("$InitializeReceiver"), Query(saved).Invoke(null, [owner]));
        }
        Assert.Null(Query(saved).Invoke(null, [saved.GetType("Empty")!]));
    }

    [Fact]
    public void OwnedGenericFamiliesRetainWholeQueryWithoutManufacturingClosures()
    {
        var saved = Compile("class Generic<T>{value:T;constructor(value:T){this.value=value;}} const G:any=Generic; console.log(new G(7).value);", true, out var compiler);
        var lookup = compiler.NativeReceiverInitializers!;
        Assert.True(lookup.UsesLegacy); Assert.NotEmpty(lookup.Blockers);
        var definition = Assert.Single(saved.GetTypes(), type => type.IsGenericTypeDefinition && type.Name.StartsWith("Generic", StringComparison.Ordinal));
        foreach (var owner in new[] { definition, definition.MakeGenericType(typeof(object)), definition.MakeGenericType(typeof(string)) })
            Assert.Equal(owner.GetMethod("$InitializeReceiver"), Query(saved).Invoke(null, [owner]));
        Assert.Contains(Instructions(Query(saved)), row => row.Member?.Name == "GetMethod");
        Assert.DoesNotContain(Instructions(Query(saved)), row => row.Member?.Name == "MakeGenericType");
        Refused(saved, () => Query(saved).Invoke(null, [typeof(List<string>)]));
        var hostile = new HostileType(definition);
        Refused(saved, () => Query(saved).Invoke(null, [hostile]));
        Refused(saved, () => Query(saved).Invoke(null, [null]));
        Assert.Equal(0, hostile.Callbacks);
    }

    [Fact]
    public void ClosedInventoryRejectsForeignAndHostileTypesBeforeMetadata()
    {
        var saved = Compile(Source, true, out _);
        Refused(saved, () => Query(saved).Invoke(null, [typeof(Uri)]));
        var hostile = new HostileType(saved.GetTypes().First(type => type.Name == "Parent"));
        Refused(saved, () => Query(saved).Invoke(null, [hostile]));
        Assert.Equal(0, hostile.Callbacks);
    }

    [Fact]
    public void TemplateTokensWithoutActualCreateCallsCannotBecomeEvidence()
    {
        var fixture = new Fixture();
        var owner = fixture.Owner("UncreatedTemplate"); owner.CreateType();
        var producer = fixture.Module.DefineType("Producer");
        var method = producer.DefineMethod("Value", MethodAttributes.Public | MethodAttributes.Static, typeof(Type), Type.EmptyTypes);
        var body = method.GetILGenerator();
        NativeReceiverInitializerLookup.EmitTemplateValue(fixture.Lookup, TypeProvider.Runtime, body, owner, method, null);
        body.Emit(OpCodes.Ret); producer.CreateType();
        Assert.Throws<InvalidOperationException>(() => fixture.Complete());
    }

    [Fact]
    public void ActualDeclarationInventoryAndAbiMustCompleteTogether()
    {
        var fixture = new Fixture(); var owner = fixture.Owner("Invalid");
        var wrong = owner.DefineMethod("$InitializeReceiver", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [typeof(object)]);
        Assert.Throws<InvalidOperationException>(() => fixture.Lookup.Record(owner, wrong));
        Assert.Throws<InvalidOperationException>(() => fixture.Lookup.ValidateForEmitter(fixture.Module, false));
        var saved = Compile(Source, true, out var compiler);
        Assert.NotNull(saved);
        Assert.Throws<InvalidOperationException>(() => compiler.NativeReceiverInitializers!.Record(owner, wrong));
    }

    private sealed class Fixture
    {
        private readonly PersistedAssemblyBuilder _assembly = new(new AssemblyName("receiver_query_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        private readonly ClassGenericParameterRegistry _parameters = new();
        private readonly List<TypeBuilder> _owners = [];
        private readonly List<NativeConstructorValueOwnerSnapshot> _values = [];
        private readonly NativeSourceOwnerCatalog _sources;
        private readonly EmittedRuntime _runtime;
        internal Fixture()
        {
            Module = _assembly.DefineDynamicModule("main"); Lookup = new(Module, TypeProvider.Runtime);
            _sources = new(Module, TypeProvider.Runtime);
            var statements = new Parser(new Lexer("console.log(42);").ScanTokens()).ParseOrThrow();
            _runtime = new RuntimeEmitter(TypeProvider.Runtime, false, true, false, nativeReceiverInitializers: Lookup)
                .EmitAll(Module, new RuntimeFeatureDetector().Detect(statements), FunctionEmissionPolicy.Full, _sources);
        }
        internal ModuleBuilder Module { get; }
        internal NativeReceiverInitializerLookup Lookup { get; }
        internal Dictionary<TypeBuilder, MethodBuilder> Adapters { get; } = [];
        internal TypeBuilder Owner(string name, Type? parent = null)
        {
            var owner = Module.DefineType(name, TypeAttributes.Public, parent);
            NativeSourceOwnerCatalog.Implement(owner, _runtime.ObjectFields.Interface, _sources);
            foreach (var member in _runtime.ObjectFields.Interface.GetMethods())
            {
                var method = owner.DefineMethod(member.Name, MethodAttributes.Public | MethodAttributes.Virtual |
                    MethodAttributes.Final | MethodAttributes.NewSlot, member.ReturnType, member.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
                var body = method.GetILGenerator();
                if (member.ReturnType != typeof(void))
                {
                    if (member.ReturnType.IsValueType)
                    {
                        var zero = body.DeclareLocal(member.ReturnType);
                        body.Emit(OpCodes.Ldloca, zero); body.Emit(OpCodes.Initobj, member.ReturnType); body.Emit(OpCodes.Ldloc, zero);
                    }
                    else body.Emit(OpCodes.Ldnull);
                }
                body.Emit(OpCodes.Ret);
                owner.DefineMethodOverride(method, member);
            }
            owner.DefineDefaultConstructor(MethodAttributes.Public);
            _parameters.Declare(owner, []); _owners.Add(owner); return owner;
        }
        internal void Value(Type owner)
        {
            var producer = Module.DefineType("Value" + _values.Count);
            var method = producer.DefineMethod("Get", MethodAttributes.Public | MethodAttributes.Static, typeof(Type), Type.EmptyTypes);
            var body = method.GetILGenerator();
            NativeConstructorActivationLookup.EmitValue(null, TypeProvider.Runtime, body, owner, NativeConstructorValueProducerKind.SourceClass);
            int offset = body.ILOffset; body.Emit(OpCodes.Ret); producer.CreateType();
            _values.Add(new(owner, [new(owner, NativeConstructorValueProducerKind.SourceClass, body, offset, method, null)],
                NativeConstructorValueClosureKind.NongenericLiteral, null, false, true, [], []));
        }
        private void EmitNativeProbe()
        {
            var probe = Module.DefineType("NativeReceiverProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var find = probe.DefineMethod("Find", MethodAttributes.Public | MethodAttributes.Static, typeof(MethodInfo), [typeof(Type)]);
            var il = find.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, Lookup.Find); il.Emit(OpCodes.Ret);
            foreach (bool ambiguous in new[] { false, true })
            {
                var method = probe.DefineMethod(ambiguous ? "Ambiguous" : "Refuses", MethodAttributes.Public | MethodAttributes.Static,
                    typeof(bool), ambiguous ? Type.EmptyTypes : [typeof(Type)]);
                il = method.GetILGenerator();
                var done = il.DefineLabel(); var result = il.DeclareLocal(typeof(bool));
                il.BeginExceptionBlock();
                if (ambiguous)
                {
                    il.Emit(OpCodes.Ldtoken, _owners.Single(owner => owner.Name == "Parent"));
                    il.Emit(OpCodes.Call, TypeProvider.Runtime.TypeGetTypeFromHandle);
                }
                else il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, Lookup.Find); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Stloc, result); il.Emit(OpCodes.Leave, done);
                il.BeginCatchBlock(ambiguous ? typeof(AmbiguousMatchException) : typeof(Exception));
                if (ambiguous) { il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldc_I4_1); }
                else
                {
                    il.Emit(OpCodes.Call, _runtime.Errors.WrapException);
                    il.Emit(OpCodes.Isinst, _runtime.Errors.TypeErrorType); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Cgt_Un);
                }
                il.Emit(OpCodes.Stloc, result); il.Emit(OpCodes.Leave, done); il.EndExceptionBlock();
                il.MarkLabel(done); il.Emit(OpCodes.Ldloc, result); il.Emit(OpCodes.Ret);
            }
            probe.CreateType();
        }
        internal Assembly Complete()
        {
            if (_owners.Any(owner => owner.Name == "Parent")) EmitNativeProbe();
            _parameters.CompleteEmission(_owners); _sources.CompleteEmission(_parameters, _owners);
            Lookup.CompleteEmission(_sources, new(Module, _values, []), _runtime, Adapters);
            using var image = new MemoryStream(); _assembly.Save(image);
            byte[] bytes = AssemblyReferenceRewriteCompatibility.Rewrite(image.ToArray(), PEPacker.EmbeddedReferenceAssemblyIndex.Default,
                PEPacker.ReferencePolicy.DroppingReferences("SharpTS"));
            if (Environment.GetEnvironmentVariable("SHARPTS_RECEIVER_LOOKUP_NATIVE_FIXTURE") is { Length: > 0 } path)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllBytes(path, bytes);
            }
            return VerifyLoad(bytes);
        }
    }

    private sealed class HostileType(Type type) : TypeDelegator(type)
    {
        internal int Callbacks { get; private set; }
        public override bool Equals(object? other) { Callbacks++; return true; }
        public override int GetHashCode() => 0;
        public override Type UnderlyingSystemType { get { Callbacks++; throw new InvalidOperationException("Hostile type metadata."); } }
        public override Type GetGenericTypeDefinition() { Callbacks++; throw new InvalidOperationException("Hostile generic metadata."); }
        protected override MethodInfo? GetMethodImpl(string name, BindingFlags flags, Binder? binder, CallingConventions convention,
            Type[]? types, ParameterModifier[]? modifiers) { Callbacks++; throw new InvalidOperationException("Hostile method metadata."); }
    }
    private static MethodInfo Query(Assembly saved) => saved.GetType("$NativeReceiverInitializerLookup")!.GetMethod("Find", Flags)!;
    private static void Refused(Assembly saved, Action action)
    {
        var exception = Assert.Throws<TargetInvocationException>(action).InnerException!;
        var error = saved.GetType("$Runtime")!.GetMethod("WrapException")!.Invoke(null, [exception])!;
        Assert.Equal("$TypeError", error.GetType().Name);
        Assert.Equal(NativeReceiverInitializerLookup.ForeignOwnerMessage, error.GetType().GetProperty("Message")!.GetValue(error));
    }
    private static Assembly Compile(string source, bool native, out ILCompiler compiler)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        compiler = new("receiver_source_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, types); return VerifyLoad(compiler.SaveToBytes());
    }
    private static Assembly VerifyLoad(byte[] bytes)
    {
        using var image = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(image)); return Assembly.Load(bytes);
    }
}