using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeTypedArrayBufferLookupTests
{
    public static IEnumerable<object[]> SourceCases()
    {
        foreach (bool native in new[] { false, true })
        {
            yield return new object[] { "array", "const v=new Uint8Array([1,2]); console.log(v.length,v[0],v[1]);", "2 1 2\n", native };
            yield return new object[] { "array-buffer", "const b=new ArrayBuffer(4); const a=new Uint8Array(b); a[1]=7; const c=new Uint8Array(b,1,2); console.log(c.length,c[0],c[1]);", "2 7 0\n", native };
            yield return new object[] { "shared-buffer", "const b=new SharedArrayBuffer(4); const a=new Uint8Array(b); a[1]=7; const c=new Uint8Array(b,1,2); c[1]=9; console.log(c.length,c[0],a[2]);", "2 7 9\n", native };
            // The managed foreign-name bridge is retained explicitly. Native source
            // names have a separate CLR namespace (#3132), so ordinary object rules apply.
            yield return new object[] { "source-name", "class $SharedArrayBuffer { GetBuffer(){console.log('fake-getter');throw new Error('fake');} } try{new Uint8Array(new $SharedArrayBuffer() as any);}catch(e){console.log((e as any).message);}", native ? "" : "fake-getter\nfake\n", native };
        }
    }
    [Theory, MemberData(nameof(SourceCases))]
    public void ActualSourceArgumentsKeepTheirConstructionSemantics(string name, string source, string expected, bool native)
    {
        var (compiler, saved) = Build(source, native);
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
        if (!native) { Assert.Null(compiler.NativeTypedArrayBuffers); Assert.Null(saved.GetType("$NativeTypedArrayBufferLookup")); return; }
        Assert.True(compiler.NativeTypedArrayBuffers!.IsComplete);
        if (name == "array")
        {
            foreach (string element in new[] { "Int8", "Uint8", "Uint8Clamped", "Int16", "Uint16", "Int32", "Uint32", "Float32", "Float64", "BigInt64", "BigUint64" })
                Assert.Contains(Instructions(saved.GetType("$" + element + "Array")!.GetConstructor([typeof(object), typeof(int), typeof(int?)])!),
                    row => row.Member?.DeclaringType?.Name == "$NativeTypedArrayBufferLookup" && row.Member.Name == "Find");
        }
        Assert.False(compiler.NativeTypedArrayBuffers.UsesLegacy, name + ": " + string.Join("; ", compiler.NativeTypedArrayBuffers.Blockers));
    }
    [Fact]
    public void ActualAllocationsKeepTheLiteralMissingInheritedAndAmbiguousBinder()
    {
        var f = new Fixture();
        var parent = f.Owner("Parent"); f.Getter(parent, typeof(byte[])); parent.CreateType();
        var inherited = f.Owner("Inherited.$SharedArrayBuffer", parent); inherited.CreateType();
        var missing = f.Owner("Missing.$SharedArrayBuffer"); missing.CreateType();
        var overload = f.Owner("Overload.$SharedArrayBuffer"); f.Getter(overload, typeof(byte[]), [typeof(int)]); overload.CreateType();
        var ambiguous = f.Owner("Ambiguous.$SharedArrayBuffer"); f.Getter(ambiguous, typeof(byte[])); f.Getter(ambiguous, typeof(object)); ambiguous.CreateType();
        foreach (var owner in new[] { inherited, missing, overload, ambiguous }) f.Allocate(owner);
        var saved = f.Complete(); Assert.False(f.Lookup.UsesLegacy, string.Join("; ", f.Lookup.Blockers)); var query = Query(saved);
        foreach (string name in new[] { "Inherited", "Missing", "Overload" })
        {
            var type = saved.GetType(name + ".$SharedArrayBuffer")!;
            Assert.Equal(type.GetMethod("GetBuffer", Type.EmptyTypes), query.Invoke(null, [type]));
        }
        var actual = saved.GetType("Ambiguous.$SharedArrayBuffer")!;
        Assert.Throws<AmbiguousMatchException>(() => actual.GetMethod("GetBuffer", Type.EmptyTypes));
        Assert.IsType<AmbiguousMatchException>(Assert.Throws<TargetInvocationException>(() => query.Invoke(null, [actual])).InnerException);
        Assert.Contains(Instructions(query), row => row.Member?.Name == "GetMethod");
    }
    [Theory]
    [InlineData("valid")]
    [InlineData("null")]
    [InlineData("malformed")]
    [InlineData("throws")]
    [InlineData("missing")]
    public void NameBridgeKeepsInvocationCastAndExceptionOrder(string mode)
    {
        var f = new Fixture(); var owner = f.Owner("Actual.$SharedArrayBuffer");
        var calls = owner.DefineField("Calls", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        if (mode != "missing")
        {
            var getter = owner.DefineMethod("GetBuffer", MethodAttributes.Public, typeof(object), Type.EmptyTypes); var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, calls); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stsfld, calls);
            if (mode == "throws") { il.Emit(OpCodes.Ldstr, "getter failed"); il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!); il.Emit(OpCodes.Throw); }
            else { if (mode == "valid") { il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Newarr, typeof(byte)); } else if (mode == "malformed") il.Emit(OpCodes.Ldstr, "wrong"); else il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret); }
        }
        owner.CreateType(); f.Allocate(owner); var saved = f.Complete(); Assert.False(f.Lookup.UsesLegacy, string.Join("; ", f.Lookup.Blockers));
        var actual = saved.GetType("Actual.$SharedArrayBuffer")!; var constructor = BufferConstructor(saved); object instance = Activator.CreateInstance(actual)!;
        if (mode == "valid") Assert.NotNull(constructor.Invoke([instance, 0, null]));
        else
        {
            var error = Assert.Throws<TargetInvocationException>(() => constructor.Invoke([instance, 0, null])).InnerException!;
            if (mode == "throws") Assert.Equal("getter failed", Assert.IsType<InvalidOperationException>(Assert.IsType<TargetInvocationException>(error).InnerException).Message);
            else if (mode == "malformed") Assert.IsType<InvalidCastException>(error);
            else if (mode == "null") Assert.IsType<NullReferenceException>(error);
            else Assert.Equal(InvalidBuffer, Assert.IsType<InvalidOperationException>(error).Message);
        }
        Assert.Equal(mode == "missing" ? 0 : 1, actual.GetField("Calls")!.GetValue(null));
    }
    [Fact]
    public void ArbitraryObjectsAndNullCannotEnterTheNameBridge()
    {
        var (_, saved) = Build("new Uint8Array([1]);", true);
        foreach (object? value in new object?[] { null, new object(), new Dictionary<string, object> { ["length"] = 2, ["0"] = 3 } })
            Assert.Equal(InvalidBuffer, Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => BufferConstructor(saved).Invoke([value, 0, null])).InnerException).Message);
    }
    [Fact]
    public void UnproducedMatchingDeclarationRetainsWholeLegacy()
    {
        var f = new Fixture(); var owner = f.Owner("Unproduced.$SharedArrayBuffer"); f.Getter(owner, typeof(byte[])); owner.CreateType(); var saved = f.Complete();
        Assert.True(f.Lookup.UsesLegacy); Assert.Contains(f.Lookup.Blockers, row => row.Contains("No actual allocation producer for relevant own declaration"));
        var hostile = new HostileType(); Assert.Equal("original query", Assert.Throws<TargetInvocationException>(() => Query(saved).Invoke(null, [hostile])).InnerException!.Message); Assert.Equal(1, hostile.Callbacks);
    }
    [Fact]
    public void MissingIngressKeepsOriginalMetadataCallbacks()
    {
        var f = new Fixture(); var saved = f.Complete(false); Assert.True(f.Lookup.UsesLegacy); Assert.Contains(f.Lookup.Blockers, row => row.Contains("CLR ingress"));
        var hostile = new HostileType(); Assert.Equal("original query", Assert.Throws<TargetInvocationException>(() => Query(saved).Invoke(null, [hostile])).InnerException!.Message); Assert.Equal(1, hostile.Callbacks);
    }
    [Fact]
    public void FiniteQueryRejectsForeignMetadataBeforeCallbacks()
    {
        var (_, saved) = Build("new Uint8Array([1]);", true); var hostile = new HostileType();
        foreach (Type? type in new Type?[] { hostile, typeof(Uri), null }) Assert.Contains(NativeTypedArrayBufferLookup.UnsupportedMessage, Assert.Throws<TargetInvocationException>(() => Query(saved).Invoke(null, [type])).InnerException!.Message);
        Assert.Equal(0, hostile.Callbacks);
    }
    [Fact]
    public void ActualTaskSourceResultsDischargeOnlyTheBufferQueryObligation()
    {
        var (compiler, _) = Build("Promise.resolve(1); console.log(new Uint8Array([1]).length);", true);
        Assert.False(compiler.NativeTypedArrayBuffers!.UsesLegacy);
        Assert.Equal(3, compiler.NativePrototypeFields!.CompletedUnprovedResults.Producers.Count);
        Assert.True(compiler.NativePrototypeFields.UsesLegacy);
        Assert.Contains(compiler.NativePrototypeFields.Blockers, row => row.Contains("Unproved actual carrier result:"));
        Assert.DoesNotContain(compiler.NativeTypedArrayBuffers.Owners, type => typeof(Task).IsAssignableFrom(type));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpaqueTaskResultsAndMatchingTaskSubclassesKeepTheWholeLegacyBinder(bool matchingSubclass)
    {
        var fixture = new Fixture(); fixture.OpaqueTaskResult(matchingSubclass);
        var saved = fixture.Complete();
        Assert.True(fixture.Lookup.UsesLegacy);
        Assert.Contains(fixture.Lookup.Blockers, row => row.Contains("Unproved actual carrier result: OpaqueTaskSource.get_Task"));
        var hostile = new HostileType();
        Assert.Equal("original query", Assert.Throws<TargetInvocationException>(() => Query(saved).Invoke(null, [hostile])).InnerException!.Message);
        Assert.Equal(1, hostile.Callbacks);
        if (matchingSubclass)
        {
            var actual = saved.GetType("OpaqueTask.$SharedArrayBuffer")!;
            Assert.True(typeof(Task<object>).IsAssignableFrom(actual));
            Assert.NotNull(BufferConstructor(saved).Invoke([Activator.CreateInstance(actual), 0, null]));
        }
    }
    [Fact]
    public void EveryFactoryOverloadIsTrackedAndLateMatchingDefinitionsAreRejected()
    {
        var module = Module(); var ledger = NativeOwnedTypeDeclarations.Begin(module);
        var one = EmitTypeDefinitions.DefineType(module, "One", TypeAttributes.Public);
        var two = EmitTypeDefinitions.DefineType(module, "Two", TypeAttributes.Public, typeof(object));
        EmitTypeDefinitions.DefineType(module, "Three", TypeAttributes.Public, typeof(object), Type.EmptyTypes);
        EmitTypeDefinitions.DefineNestedType(one, "Nested", TypeAttributes.NestedPublic);
        EmitTypeDefinitions.DefineNestedType(two, "Nested", TypeAttributes.NestedPublic, typeof(object), Type.EmptyTypes);
        Assert.Equal(5, ledger.TrackedDefinitionCount); Assert.Empty(ledger.CompleteNameCoverage("$SharedArrayBuffer", new HashSet<Type>()));
        Assert.Throws<InvalidOperationException>(() => EmitTypeDefinitions.DefineType(module, "Late.$SharedArrayBuffer", TypeAttributes.Public));
        Assert.Throws<InvalidOperationException>(() => EmitTypeDefinitions.DefineNestedType(one, "$SharedArrayBuffer", TypeAttributes.NestedPublic));
        Assert.NotNull(EmitTypeDefinitions.DefineType(module, "Late.Other", TypeAttributes.Public));
        Assert.Throws<InvalidOperationException>(() => ledger.CompleteNameCoverage("$SharedArrayBuffer", new HashSet<Type>()));
        Assert.Throws<ArgumentException>(() => NativeOwnedTypeDeclarations.Begin(module));
        Assert.Throws<InvalidOperationException>(() => NativeOwnedTypeDeclarations.Record(module, Module().DefineType("Foreign")));
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void UncreatedNestedOrGenericMatchingDeclarationsCannotAuthorizeAllocation(bool nested, bool generic)
    {
        var module = Module(); var ledger = NativeOwnedTypeDeclarations.Begin(module);
        var owner = nested ? EmitTypeDefinitions.DefineNestedType(EmitTypeDefinitions.DefineType(module, "Outer", TypeAttributes.Public), "$SharedArrayBuffer", TypeAttributes.NestedPublic) : EmitTypeDefinitions.DefineType(module, "Missing.$SharedArrayBuffer", TypeAttributes.Public);
        if (generic) { owner.DefineGenericParameters("T"); owner.CreateType(); }
        Assert.Contains(ledger.CompleteNameCoverage("$SharedArrayBuffer", new HashSet<Type>()), row => row.StartsWith("Unproved relevant own declaration:"));
    }
    [Fact]
    public void LookupRequiresExactNativeModuleAndFreezesAfterCompletion()
    {
        var f = new Fixture(); Assert.Throws<InvalidOperationException>(() => f.Lookup.ValidateForEmitter(f.Module, false)); Assert.Throws<InvalidOperationException>(() => f.Lookup.ValidateForEmitter(Module(), true)); f.Complete();
        Assert.Throws<InvalidOperationException>(() => f.Lookup.ValidateForEmitter(f.Module, true)); Assert.Throws<InvalidOperationException>(() => f.Complete());
    }
    [Fact]
    public void NativeBinderProbePreservesNamespacedOwnersAndAllFailurePaths()
    {
        var fixture = new Fixture(true); fixture.EmitNativeProbe();
        var saved = fixture.Complete(exportDirectory: Environment.GetEnvironmentVariable("SHARPTS_TYPEDARRAY_NATIVE_FIXTURE"));
        Assert.False(fixture.Lookup.UsesLegacy, string.Join("; ", fixture.Lookup.Blockers));
        Assert.Equal("valid\nNullReferenceException\nInvalidCastException\nTargetInvocationException\nInvalidOperationException\nAmbiguousMatchException\ninherited\n5\n",
            TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
    }
    private sealed class Fixture
    {
        private readonly PersistedAssemblyBuilder _assembly;
        private readonly NativeSourceOwnerCatalog _sources; private readonly NativeConstructorActivationLookup _activation;
        private readonly NativePrototypeFieldsLookup _allocations; private readonly EmittedRuntime _runtime; private readonly NativeOwnedTypeDeclarations _declarations; private int _producer;
        internal Fixture(bool nativeProbe = false)
        {
            _assembly = new(new AssemblyName(nativeProbe ? "Guest" : "buffer_lookup_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
            Module = _assembly.DefineDynamicModule("main"); _declarations = NativeOwnedTypeDeclarations.Begin(Module); Lookup = new(Module, TypeProvider.Runtime); _activation = new(Module, TypeProvider.Runtime); _allocations = new(Module, TypeProvider.Runtime); _sources = new(Module, TypeProvider.Runtime);
            var statements = new Parser(new Lexer("new Uint8Array([1]);").ScanTokens()).ParseOrThrow();
            _runtime = new RuntimeEmitter(TypeProvider.Runtime, false, true, false, nativeConstructorActivation: _activation, nativePrototypeFields: _allocations, nativeTypedArrayBuffers: Lookup).EmitAll(Module, new RuntimeFeatureDetector().Detect(statements), FunctionEmissionPolicy.Full, _sources);
        }
        internal ModuleBuilder Module { get; }
        internal NativeTypedArrayBufferLookup Lookup { get; }
        internal TypeBuilder Owner(string name, Type? parent = null) { var owner = EmitTypeDefinitions.DefineType(Module, name, TypeAttributes.Public, parent); owner.DefineDefaultConstructor(MethodAttributes.Public); return owner; }
        internal void Getter(TypeBuilder owner, Type result, Type[]? parameters = null) { var method = owner.DefineMethod("GetBuffer", MethodAttributes.Public, result, parameters ?? Type.EmptyTypes); method.GetILGenerator().Emit(OpCodes.Ldnull); method.GetILGenerator().Emit(OpCodes.Ret); }
        internal void Allocate(TypeBuilder owner)
        {
            var producer = EmitTypeDefinitions.DefineType(Module, "Producer" + _producer++, TypeAttributes.Abstract | TypeAttributes.Sealed);
            var method = producer.DefineMethod("Create", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
            NativePrototypeFieldsLookup.EmitCarrierCreate(_allocations, method.GetILGenerator(), owner.GetConstructor(Type.EmptyTypes)!); method.GetILGenerator().Emit(OpCodes.Ret); producer.CreateType();
        }
        internal void OpaqueTaskResult(bool matchingSubclass)
        {
            ConstructorBuilder? taskConstructor = null;
            if (matchingSubclass)
            {
                var task = EmitTypeDefinitions.DefineType(Module, "OpaqueTask.$SharedArrayBuffer", TypeAttributes.Public, typeof(Task<object>));
                var work = task.DefineMethod("Work", MethodAttributes.Private | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
                work.GetILGenerator().Emit(OpCodes.Ldnull); work.GetILGenerator().Emit(OpCodes.Ret);
                taskConstructor = task.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
                var il = taskConstructor.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldftn, work);
                il.Emit(OpCodes.Newobj, typeof(Func<object>).GetConstructor([typeof(object), typeof(IntPtr)])!);
                il.Emit(OpCodes.Call, typeof(Task<object>).GetConstructor([typeof(Func<object>)])!); il.Emit(OpCodes.Ret);
                var buffer = task.DefineMethod("GetBuffer", MethodAttributes.Public, typeof(byte[]), Type.EmptyTypes);
                buffer.GetILGenerator().Emit(OpCodes.Ldc_I4_2); buffer.GetILGenerator().Emit(OpCodes.Newarr, typeof(byte)); buffer.GetILGenerator().Emit(OpCodes.Ret);
                task.CreateType();
            }
            var owner = Owner("OpaqueTaskSource");
            var getter = owner.DefineMethod("get_Task", MethodAttributes.Public, typeof(Task<object>), Type.EmptyTypes);
            var body = getter.GetILGenerator();
            if (taskConstructor is null) body.Emit(OpCodes.Ldnull);
            else NativePrototypeFieldsLookup.EmitCarrierCreate(_allocations, body, taskConstructor);
            body.Emit(OpCodes.Ret); owner.CreateType();
            var producer = EmitTypeDefinitions.DefineType(Module, "OpaqueTaskConsumer", TypeAttributes.Abstract | TypeAttributes.Sealed);
            var consume = producer.DefineMethod("Consume", MethodAttributes.Static | MethodAttributes.Public, typeof(void), Type.EmptyTypes);
            var call = consume.GetILGenerator(); NativePrototypeFieldsLookup.EmitCarrierCreate(_allocations, call, owner.GetConstructor(Type.EmptyTypes)!);
            NativePrototypeFieldsLookup.EmitUnprovedCarrierResult(_allocations, call, getter);
            call.Emit(OpCodes.Pop); call.Emit(OpCodes.Ret); producer.CreateType();
        }
        internal void EmitNativeProbe()
        {
            var program = EmitTypeDefinitions.DefineType(Module, "$Program", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var calls = program.DefineField("Calls", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
            var main = program.DefineMethod("Main", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
            var il = main.GetILGenerator(); var length = il.DeclareLocal(typeof(int?));
            foreach (string mode in new[] { "valid", "null", "malformed", "throws", "missing", "ambiguous", "inherited" })
            {
                TypeBuilder owner;
                if (mode == "inherited")
                {
                    var parent = Owner("NativeParent"); AddGetter(parent, "valid", typeof(byte[])); parent.CreateType();
                    owner = Owner("Native_inherited.$SharedArrayBuffer", parent);
                }
                else
                {
                    owner = Owner("Native_" + mode + ".$SharedArrayBuffer");
                    if (mode != "missing") AddGetter(owner, mode, typeof(object));
                    if (mode == "ambiguous") AddGetter(owner, mode, typeof(byte[]));
                }
                owner.CreateType();
                il.BeginExceptionBlock();
                NativePrototypeFieldsLookup.EmitCarrierCreate(_allocations, il, owner.GetConstructor(Type.EmptyTypes)!);
                il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, length);
                NativePrototypeFieldsLookup.EmitCarrierCreate(_allocations, il, _runtime.TypedArrays.RequireImplementation().Uint8ArrayBufferCtor);
                il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldstr, mode);
                il.Emit(OpCodes.Call, typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(string)])!);
                il.BeginCatchBlock(typeof(Exception));
                il.Emit(OpCodes.Callvirt, typeof(object).GetMethod(nameof(GetType))!);
                il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty(nameof(Type.Name))!.GetMethod!);
                il.Emit(OpCodes.Call, typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(string)])!);
                il.EndExceptionBlock();
            }
            il.Emit(OpCodes.Ldsfld, calls); il.Emit(OpCodes.Call, typeof(Console).GetMethod(nameof(Console.WriteLine), [typeof(int)])!);
            il.Emit(OpCodes.Ret); program.CreateType();
            var facade = EmitTypeDefinitions.DefineType(Module, "SharpTS.Generated.EntryPoint", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var run = facade.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
            run.GetILGenerator().Emit(OpCodes.Call, main); run.GetILGenerator().Emit(OpCodes.Ldc_I4_0); run.GetILGenerator().Emit(OpCodes.Ret); facade.CreateType();

            void AddGetter(TypeBuilder owner, string mode, Type result)
            {
                var method = owner.DefineMethod("GetBuffer", MethodAttributes.Public, result, Type.EmptyTypes); var body = method.GetILGenerator();
                body.Emit(OpCodes.Ldsfld, calls); body.Emit(OpCodes.Ldc_I4_1); body.Emit(OpCodes.Add); body.Emit(OpCodes.Stsfld, calls);
                if (mode == "throws") { body.Emit(OpCodes.Ldstr, "getter failed"); body.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!); body.Emit(OpCodes.Throw); }
                else { if (mode == "valid") { body.Emit(OpCodes.Ldc_I4_2); body.Emit(OpCodes.Newarr, typeof(byte)); } else if (mode == "malformed") body.Emit(OpCodes.Ldstr, "wrong"); else body.Emit(OpCodes.Ldnull); body.Emit(OpCodes.Ret); }
            }
        }
        internal Assembly Complete(bool ingress = true, string? exportDirectory = null)
        {
            var parameters = new ClassGenericParameterRegistry(); parameters.CompleteEmission([]); _sources.CompleteEmission(parameters, []); _activation.CompleteEmission(_sources, _runtime);
            _allocations.CompleteEmission(_sources, ingress ? new(Module) : null, _runtime, typeof(object).GetType()); Lookup.CompleteEmission(_sources, ingress ? new(Module) : null, _runtime, _declarations);
            using var image = new MemoryStream(); _assembly.Save(image);
            byte[] bytes = AssemblyReferenceRewriteCompatibility.Rewrite(image.ToArray(), PEPacker.EmbeddedReferenceAssemblyIndex.Default,
                PEPacker.ReferencePolicy.DroppingReferences("SharpTS"));
            var saved = VerifyLoad(bytes);
            if (exportDirectory is not null)
                NativeAotProjectExporter.Export(new(exportDirectory, "win-x64"),
                    NativeAotCompatibilityReport.Analyze(_runtime.Deployment.Requirements, _runtime.Deployment.Reasons, false),
                    path => File.WriteAllBytes(path, bytes));
            return saved;
        }
    }
    private const string InvalidBuffer = "TypedArray buffer constructor requires emitted ArrayBuffer/SharedArrayBuffer.";
    private static ModuleBuilder Module() => new PersistedAssemblyBuilder(new AssemblyName("declarations_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly).DefineDynamicModule("main");
    private static ConstructorInfo BufferConstructor(Assembly saved) => saved.GetType("$Uint8Array")!.GetConstructor([typeof(object), typeof(int), typeof(int?)])!;
    private static MethodInfo Query(Assembly saved) => saved.GetType("$NativeTypedArrayBufferLookup")!.GetMethod("Find", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static (ILCompiler Compiler, Assembly Saved) Build(string source, bool native)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), row => row.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = new ILCompiler("buffer_source_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll); if (native) compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types); return (compiler, VerifyLoad(compiler.SaveToBytes()));
    }
    private static Assembly VerifyLoad(byte[] bytes) { using var image = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); Assert.Empty(verifier.Verify(image)); return Assembly.Load(bytes); }
    private sealed class HostileType() : TypeDelegator(typeof(object))
    {
        internal int Callbacks;
        public override Type UnderlyingSystemType { get { Callbacks++; throw new InvalidOperationException("metadata"); } }
        protected override MethodInfo? GetMethodImpl(string name, BindingFlags flags, Binder? binder, CallingConventions convention, Type[]? types, ParameterModifier[]? modifiers) { Callbacks++; throw new InvalidOperationException("original query"); }
        public override bool Equals(object? other) { Callbacks++; throw new InvalidOperationException("equality"); }
        public override int GetHashCode() => 0;
    }
}
