using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.IntegrationTests;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeConstructedBodyLookupTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    private const string Source = """
        function Target<T>(value: T, label: any = new.target, ...rest: any[]): any {
            return { value, target: new.target, same: label === new.target,
                count: arguments.length, rest: rest.length };
        }
        function Other() {}
        const Constructor: any = Target;
        const Bound: any = Constructor.bind(null, 'bound');
        const first = new Constructor(7);
        const second = new Bound(undefined, 1, 2);
        const third: any = Reflect.construct(Target, ['reflect'], Other);
        const ordinary = Target('call');
        console.log(first.value, first.target === Target, first.same, first.count, first.rest);
        console.log(second.value, second.target === Target, second.same, second.count, second.rest);
        console.log(third.value, third.target === Other, third.same, third.count, third.rest);
        console.log(ordinary.value, ordinary.target === undefined, ordinary.same, ordinary.count);
        """;

    public static IEnumerable<object[]> ConstructorCases() =>
        NewTargetTests.Cases().Where(row => (string)row[0] != "proxy")
            .Concat(BoundConstructorTests.Cases());

    [Theory, MemberData(nameof(ConstructorCases))]
    public void ExistingNewTargetAndBoundConstructionCorpusUsesTheNativeInventory(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var saved = Compile(source, true, out var compiler);
        Assert.Equal(expected, Run(saved));
        Assert.True(compiler.NativeConstructedBodies!.IsComplete);
        Assert.False(compiler.NativeConstructedBodies.UsesLegacy);
        Assert.Empty(compiler.NativeConstructedBodies.Blockers);
        AssertActualProducerMarkers(saved, compiler.NativeConstructedBodies);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryEntriesKeepTheManagedAndNativeCallingConvention(bool native)
    {
        var saved = Compile(Source.Replace("Target<T>(value: T", "Target(value: any"), native, out var compiler);
        Assert.Equal("7 true true 1 0\nbound true true 4 2\nreflect true true 1 0\ncall true true 1\n", Run(saved));
        if (native) AssertActualProducerMarkers(saved, compiler.NativeConstructedBodies!);
        else { Assert.Null(compiler.NativeConstructedBodies); Assert.Null(saved.GetType("$NativeConstructedBodyLookup")); }
    }

    [Fact]
    public void CanonicalEntriesKeepIdentityDefaultsRestArgumentsAndOrdinaryCalls()
    {
        var saved = Compile(Source, true, out var compiler);
        Assert.Equal("7 true true 1 0\nbound true true 4 2\nreflect true true 1 0\ncall true true 1\n", Run(saved));
        var lookup = Lookup(saved);
        AssertActualProducerMarkers(saved, compiler.NativeConstructedBodies!);
        var entry = Assert.Single(saved.GetType("$Program")!.GetMethods(Members), method => method.Name == "Target" && method.IsGenericMethodDefinition);
        var coreName = (string)entry.GetCustomAttributes(saved.GetType("$NewTargetCore")!, false)[0].GetType().GetField("Name")!.GetValue(
            entry.GetCustomAttributes(saved.GetType("$NewTargetCore")!, false)[0])!;
        var core = entry.DeclaringType!.GetMethod(coreName, Members)!;
        Assert.Equal(core, lookup.Invoke(null, [entry.MakeGenericMethod(typeof(object)), coreName]));
        AssertGuestTypeError(saved, () => lookup.Invoke(null, [entry.MakeGenericMethod(typeof(string)), coreName]));
        foreach (var method in saved.GetType("$NativeConstructedBodyLookup")!.GetMethods(Members)
            .Append(saved.GetType("$TSFunction")!.GetMethod("InvokeConstructed")!))
            Assert.DoesNotContain(Instructions(method), instruction => instruction.Member?.Name is "GetMethod" or "MakeGenericMethod");
    }

    [Fact]
    public void ActualAbortControllerProducerSupportsNewBoundAndReflectConstruction()
    {
        const string source = """
            const C:any=AbortController;
            const Bound:any=C.bind(null);
            function Alternate(){}
            const first:any=new C();
            const second:any=new Bound();
            const third:any=Reflect.construct(C,[],Alternate);
            first.abort('first'); second.abort('second'); third.abort('third');
            console.log(C===AbortController,C.name,C.length,first.signal.reason,second.signal.reason,third.signal.reason);
            console.log(Object.getPrototypeOf(first)===C.prototype,Object.getPrototypeOf(second)===C.prototype,
                Object.getPrototypeOf(third)===Alternate.prototype);
            """;
        var saved = Compile(source, true, out var compiler);
        Assert.Equal("true AbortController 0 first second third\ntrue true true\n", Run(saved));
        Assert.False(compiler.NativeConstructedBodies!.UsesLegacy);
        AssertActualProducerMarkers(saved, compiler.NativeConstructedBodies);
        Assert.Contains(saved.GetTypes().SelectMany(type => type.GetMethods(Members)),
            method => method.DeclaringType!.Name == "$AbortControllerConstructor" && method.Name == "AbortController");
    }

    [Fact]
    public void ForeignMarkersAndHostileEqualsCannotSelectAnOwnedCore()
    {
        var saved = Compile(Source, true, out _);
        var foreignBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("foreign_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var owner = foreignBuilder.DefineDynamicModule("foreign").DefineType("Foreign");
        var entry = owner.DefineMethod("Target", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
        entry.GetILGenerator().Emit(OpCodes.Ldnull); entry.GetILGenerator().Emit(OpCodes.Ret);
        var core = owner.DefineMethod("Core", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object)]);
        core.GetILGenerator().Emit(OpCodes.Ldnull); core.GetILGenerator().Emit(OpCodes.Ret);
        entry.SetCustomAttribute(new CustomAttributeBuilder(saved.GetType("$NewTargetCore")!.GetConstructor([typeof(string)])!, ["Core"]));
        var foreign = owner.CreateType()!.GetMethod("Target")!;
        AssertGuestTypeError(saved, () => Lookup(saved).Invoke(null, [foreign, "Core"]));
        var hostile = new HostileMethodInfo();
        AssertGuestTypeError(saved, () => Lookup(saved).Invoke(null, [hostile, "Core"]));
        Assert.Equal(0, hostile.EqualsCalls);
        var wrapperType = saved.GetType("$TSFunction")!;
        var wrapper = Activator.CreateInstance(wrapperType, [null, foreign])!;
        Assert.Same(foreign, wrapperType.GetField("_method", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(wrapper));
        AssertGuestTypeError(saved, () => wrapperType.GetMethod("InvokeConstructed")!.Invoke(wrapper, [new object(), Array.Empty<object>(), wrapper]));
    }

    [Fact]
    public void ImportedConstructorAliasesJoinTheCompletedModuleInventory()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string entry = directory.GetPath("main.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [directory.GetPath("lib.ts")] = "export function Target<T>(value:T):any{return {value,target:new.target};}",
            [entry] = "import {Target} from './lib'; const alias:any=Target; const item:any=new alias(7); console.log(item.value,item.target===Target,alias===Target);"
        };
        var resolver = new ModuleResolver(entry, files);
        var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry));
        var checker = new TypeChecker(); var types = checker.CheckModules(modules, resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = Compiler(true); compiler.CompileModules(modules, resolver, types);
        var saved = VerifyLoad(compiler.SaveToBytes());
        Assert.Equal("7 true true\n", Run(saved));
        Assert.True(compiler.NativeConstructedBodies!.IsComplete); Assert.False(compiler.NativeConstructedBodies.UsesLegacy);
        AssertActualProducerMarkers(saved, compiler.NativeConstructedBodies);
    }

    [Fact]
    public void UnprovedAbiKeepsTheWholeLegacyQueryButNotForeignEntries()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("legacy_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var lookup = new NativeConstructedBodyLookup(module, TypeProvider.Runtime);
        var generics = new NativeGenericMethodNormalization(module, TypeProvider.Runtime);
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, BasicFeatures());
        var registry = new FunctionGenericParameterRegistry();
        var bodies = new Dictionary<MethodBuilder, MethodBuilder>();
        var owner = module.DefineType("Owner");
        foreach (var name in new[] { "Valid", "Unproved" })
        {
            var entry = owner.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
            var core = owner.DefineMethod(name + "Core", MethodAttributes.Assembly | MethodAttributes.Static, typeof(object),
                [name == "Valid" ? typeof(object) : typeof(int)]);
            entry.GetILGenerator().Emit(OpCodes.Ldnull); entry.GetILGenerator().Emit(OpCodes.Ret);
            core.GetILGenerator().Emit(OpCodes.Ldnull); core.GetILGenerator().Emit(OpCodes.Ret);
            entry.SetCustomAttribute(runtime.FunctionAttributes.NewTargetCoreCtor,
                CustomAttributeEncoder.Encode(runtime.FunctionAttributes.NewTargetCoreCtor, core.Name));
            registry.Declare(core, []); lookup.RecordSourcePair(entry, core); bodies.Add(entry, core);
        }
        owner.CreateType(); registry.CompleteEmission(); generics.CompleteEmission(registry, runtime.FunctionAttributes, runtime.Errors);
        lookup.CompleteEmission(registry, generics, bodies, runtime.FunctionAttributes, runtime.Errors);
        Assert.True(lookup.UsesLegacy); Assert.Single(lookup.Blockers);
        using var image = new MemoryStream(); assembly.Save(image); var saved = VerifyLoad(image.ToArray());
        var valid = saved.GetType("Owner")!.GetMethod("Valid")!;
        Assert.Null(Lookup(saved).Invoke(null, [valid, "<missing>"]));
        Assert.Equal(saved.GetType("Owner")!.GetMethod("UnprovedCore", Members),
            Lookup(saved).Invoke(null, [valid, "UnprovedCore"]));
        Assert.Contains(Instructions(Lookup(saved)), instruction => instruction.Member?.Name == "GetMethod");
        AssertGuestTypeError(saved, () => Lookup(saved).Invoke(null, [typeof(Math).GetMethod("Abs", [typeof(double)]), "Abs"]));
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("base", false)]
    [InlineData("interface", false)]
    [InlineData("multiple", true)]
    [InlineData("special", true)]
    [InlineData("dependent", true)]
    public void ConstructorEntriesReuseOnlyAlreadyProvedConstraintTargets(string shape, bool legacy)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("constraints_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var lookup = new NativeConstructedBodyLookup(module, TypeProvider.Runtime);
        var generics = new NativeGenericMethodNormalization(module, TypeProvider.Runtime);
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, BasicFeatures());
        var registry = new FunctionGenericParameterRegistry();
        var owner = module.DefineType("Owner");
        var entry = owner.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
        var core = owner.DefineMethod("Core", MethodAttributes.Assembly | MethodAttributes.Static, typeof(object), [typeof(object)]);
        Type canonical = shape is "base" or "multiple" ? typeof(Exception) : shape == "interface" ? typeof(IDisposable) : typeof(object);
        foreach (var method in new[] { entry, core })
        {
            var parameter = method.DefineGenericParameters("T")[0];
            if (shape is "base" or "multiple") parameter.SetBaseTypeConstraint(typeof(Exception));
            if (shape is "interface" or "multiple") parameter.SetInterfaceConstraints(typeof(IDisposable));
            if (shape == "special") parameter.SetGenericParameterAttributes(GenericParameterAttributes.ReferenceTypeConstraint);
            if (shape == "dependent") parameter.SetInterfaceConstraints(EmitGenerics.MakeGenericType(typeof(IEnumerable<>), parameter));
            method.GetILGenerator().Emit(OpCodes.Ldnull); method.GetILGenerator().Emit(OpCodes.Ret);
            generics.RecordDefinition(method, [parameter]); registry.Declare(method, [parameter]);
            registry.DeclareErasedValueInvocationTarget(method, [canonical]);
        }
        foreach (var marker in Enum.GetValues<NativeGenericMethodNormalization.SourceMarker>()) generics.RecordSourceMarker(entry, marker);
        entry.SetCustomAttribute(runtime.FunctionAttributes.PadUndefinedCtor, CustomAttributeEncoder.EmptyBlob);
        entry.SetCustomAttribute(runtime.FunctionAttributes.FunctionLengthCtor, CustomAttributeEncoder.Encode(runtime.FunctionAttributes.FunctionLengthCtor, 0));
        entry.SetCustomAttribute(runtime.FunctionAttributes.FunctionNameCtor, CustomAttributeEncoder.Encode(runtime.FunctionAttributes.FunctionNameCtor, "Entry"));
        entry.SetCustomAttribute(runtime.FunctionAttributes.NewTargetCoreCtor, CustomAttributeEncoder.Encode(runtime.FunctionAttributes.NewTargetCoreCtor, "Core"));
        generics.RecordConstructedBody(entry, core); lookup.RecordSourcePair(entry, core);
        owner.CreateType(); registry.CompleteEmission(); generics.CompleteEmission(registry, runtime.FunctionAttributes, runtime.Errors);
        lookup.CompleteEmission(registry, generics, new Dictionary<MethodBuilder, MethodBuilder> { [entry] = core },
            runtime.FunctionAttributes, runtime.Errors);
        Assert.Equal(legacy, lookup.UsesLegacy);
        using var image = new MemoryStream(); assembly.Save(image); var saved = VerifyLoad(image.ToArray());
        var savedEntry = saved.GetType("Owner")!.GetMethod("Entry")!;
        var savedCore = saved.GetType("Owner")!.GetMethod("Core", Members)!;
        Assert.Equal(savedCore, Lookup(saved).Invoke(null, [savedEntry, "Core"]));
        if (!legacy) Assert.Equal(savedCore, Lookup(saved).Invoke(null, [savedEntry.MakeGenericMethod(canonical), "Core"]));
        else
        {
            Assert.NotEmpty(lookup.Blockers);
            Assert.Contains(Instructions(Lookup(saved)), instruction => instruction.Member?.Name == "GetMethod");
            AssertGuestTypeError(saved, () => Lookup(saved).Invoke(null, [new HostileMethodInfo(), "Core"]));
        }
    }
    [Fact]
    public void SyntheticNativeProbeExercisesOwnedConstructionAndRefusesAnUnrecordedMarkerBeforeItsBody()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("Guest"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main"); var types = TypeProvider.Runtime;
        var lookup = new NativeConstructedBodyLookup(module, types);
        var generics = new NativeGenericMethodNormalization(module, types);
        var policy = FunctionEmissionPolicy.Full with { NativeGenericNormalizer = generics.Normalize,
            NativeConstructedGenericResolver = generics.ResolveConstructed, NativeConstructedBodies = lookup };
        var runtime = new RuntimeEmitter(types, false, true).EmitAll(module, BasicFeatures(), policy);
        var registry = new FunctionGenericParameterRegistry();
        var state = module.DefineType("State", TypeAttributes.Public);
        var count = state.DefineField("Count", typeof(int), FieldAttributes.Public | FieldAttributes.Static); state.CreateType();
        MethodBuilder? owned = null; MethodBuilder? foreign = null;
        var bodies = new Dictionary<MethodBuilder, MethodBuilder>();
        foreach (bool known in new[] { true, false })
        {
            var owner = module.DefineType(known ? "Owned" : "Unregistered", TypeAttributes.Public);
            var entry = owner.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
            entry.GetILGenerator().Emit(OpCodes.Ldstr, "entry"); entry.GetILGenerator().Emit(OpCodes.Ret);
            var core = owner.DefineMethod("Core", MethodAttributes.Assembly | MethodAttributes.Static, typeof(object), [typeof(object)]);
            core.DefineParameter(1, ParameterAttributes.None, "__newTarget");
            var body = core.GetILGenerator();
            if (!known) { body.Emit(OpCodes.Ldc_I4_1); body.Emit(OpCodes.Stsfld, count); }
            body.Emit(OpCodes.Ldstr, known ? "owned" : "foreign"); body.Emit(OpCodes.Ret);
            entry.SetCustomAttribute(runtime.FunctionAttributes.NewTargetCoreCtor, CustomAttributeEncoder.Encode(runtime.FunctionAttributes.NewTargetCoreCtor, "Core"));
            if (known) { lookup.RecordSourcePair(entry, core); registry.Declare(core, []); bodies.Add(entry, core); owned = entry; }
            else foreign = entry;
            owner.CreateType();
        }
        var probe = module.DefineType("NativeConstructedProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        foreach (bool known in new[] { true, false })
        {
            var method = probe.DefineMethod(known ? "Construct" : "Foreign", MethodAttributes.Public | MethodAttributes.Static,
                known ? typeof(object) : typeof(bool), Type.EmptyTypes);
            var il = method.GetILGenerator();
            LocalBuilder? result = known ? null : il.DeclareLocal(typeof(bool));
            var done = il.DefineLabel(); if (!known) il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldnull); types.EmitLoadMethodInfo(il, known ? owned! : foreign!);
            il.Emit(OpCodes.Newobj, runtime.FunctionConstruction.Constructor);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newarr, typeof(object)); il.Emit(OpCodes.Ldstr, "target");
            il.Emit(OpCodes.Callvirt, runtime.FunctionValues.InvokeConstructed);
            if (!known)
            {
                il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, result!); il.Emit(OpCodes.Leave, done);
                il.BeginCatchBlock(typeof(Exception)); il.Emit(OpCodes.Call, runtime.Errors.WrapException);
                il.Emit(OpCodes.Isinst, runtime.Errors.TypeErrorType); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Cgt_Un);
                il.Emit(OpCodes.Stloc, result!); il.Emit(OpCodes.Leave, done); il.EndExceptionBlock();
                il.MarkLabel(done); il.Emit(OpCodes.Ldloc, result!);
            }
            il.Emit(OpCodes.Ret);
        }
        var refuse = probe.DefineMethod("Refuse", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), [typeof(MethodInfo)]);
        var reject = refuse.GetILGenerator(); var refused = reject.DeclareLocal(typeof(bool)); var end = reject.DefineLabel();
        reject.BeginExceptionBlock(); reject.Emit(OpCodes.Ldarg_0); reject.Emit(OpCodes.Ldstr, "Core"); reject.Emit(OpCodes.Call, lookup.FindCore);
        reject.Emit(OpCodes.Pop); reject.Emit(OpCodes.Ldc_I4_0); reject.Emit(OpCodes.Stloc, refused); reject.Emit(OpCodes.Leave, end);
        reject.BeginCatchBlock(typeof(Exception)); reject.Emit(OpCodes.Call, runtime.Errors.WrapException);
        reject.Emit(OpCodes.Isinst, runtime.Errors.TypeErrorType); reject.Emit(OpCodes.Ldnull); reject.Emit(OpCodes.Cgt_Un);
        reject.Emit(OpCodes.Stloc, refused); reject.Emit(OpCodes.Leave, end); reject.EndExceptionBlock();
        reject.MarkLabel(end); reject.Emit(OpCodes.Ldloc, refused); reject.Emit(OpCodes.Ret);
        var reads = probe.DefineMethod("Count", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        reads.GetILGenerator().Emit(OpCodes.Ldsfld, count); reads.GetILGenerator().Emit(OpCodes.Ret); probe.CreateType();
        registry.CompleteEmission(); generics.CompleteEmission(registry, runtime.FunctionAttributes, runtime.Errors);
        lookup.CompleteEmission(registry, generics, bodies, runtime.FunctionAttributes, runtime.Errors);
        using var image = new MemoryStream(); assembly.Save(image);
        byte[] bytes = AssemblyReferenceRewriteCompatibility.Rewrite(image.ToArray(), PEPacker.EmbeddedReferenceAssemblyIndex.Default,
            PEPacker.ReferencePolicy.DroppingReferences("SharpTS"));
        var saved = VerifyLoad(bytes); var savedProbe = saved.GetType("NativeConstructedProbe")!;
        Assert.Equal("owned", savedProbe.GetMethod("Construct")!.Invoke(null, null));
        Assert.Equal(true, savedProbe.GetMethod("Foreign")!.Invoke(null, null));
        var hostile = new HostileMethodInfo();
        Assert.Equal(true, savedProbe.GetMethod("Refuse")!.Invoke(null, [hostile])); Assert.Equal(0, hostile.EqualsCalls);
        Assert.Equal(0, savedProbe.GetMethod("Count")!.Invoke(null, null));
        if (Environment.GetEnvironmentVariable("SHARPTS_CONSTRUCTED_NATIVE_FIXTURE_PATH") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllBytes(path, bytes);
        }
    }
    [Fact]
    public void LifecycleAndForeignModuleDeclarationsAreRejected()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("lifecycle"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main"); var lookup = new NativeConstructedBodyLookup(module, TypeProvider.Runtime);
        Assert.Throws<InvalidOperationException>(() => lookup.ValidateForEmitter(module, false));
        var foreignModule = new PersistedAssemblyBuilder(new AssemblyName("foreign_lifecycle"), typeof(object).Assembly).DefineDynamicModule("other");
        Assert.Throws<InvalidOperationException>(() => lookup.ValidateForEmitter(foreignModule, true));
        var foreignOwner = foreignModule.DefineType("Other"); var foreign = foreignOwner.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static);
        Assert.Throws<InvalidOperationException>(() => lookup.RecordSourcePair(foreign, foreign));
        var owner = module.DefineType("Owner");
        var entry = owner.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
        var core = owner.DefineMethod("Core", MethodAttributes.Assembly, typeof(object), [typeof(object)]);
        Assert.Throws<InvalidOperationException>(() => lookup.RecordSourcePair(entry, core));
        lookup.FindCore.SetReturnType(typeof(object));
        Assert.Throws<InvalidOperationException>(() => lookup.ValidateForEmitter(module, true));
    }

    private static void AssertActualProducerMarkers(Assembly saved, NativeConstructedBodyLookup catalog)
    {
        var marker = saved.GetType("$NewTargetCore")!;
        var entries = saved.GetTypes().SelectMany(type => type.GetMethods(Members))
            .Where(method => method.IsDefined(marker, false)).ToArray();
        Assert.Equal(entries.Length, catalog.PairCount);
        foreach (var entry in entries)
        {
            var attribute = Assert.Single(entry.GetCustomAttributes(marker, false));
            var name = (string)marker.GetField("Name")!.GetValue(attribute)!;
            Assert.Equal(entry.DeclaringType!.GetMethod(name, Members), Lookup(saved).Invoke(null, [entry, name]));
        }
    }

    private static void AssertGuestTypeError(Assembly saved, Action action)
    {
        var failure = Assert.Throws<TargetInvocationException>(action).InnerException!;
        var error = saved.GetType("$Runtime")!.GetMethod("WrapException")!.Invoke(null, [failure])!;
        Assert.Equal("$TypeError", error.GetType().Name);
        Assert.Equal(NativeConstructedBodyLookup.ForeignEntryMessage, error.GetType().GetProperty("Message")!.GetValue(error));
    }

    private sealed class HostileMethodInfo : MethodInfo
    {
        internal int EqualsCalls { get; private set; }
        public override bool Equals(object? obj) { EqualsCalls++; return true; }
        public override int GetHashCode() => 0;
        public override string Name => throw new InvalidOperationException("Untrusted name read.");
        public override Type DeclaringType => throw new InvalidOperationException("Untrusted owner read.");
        public override Type ReflectedType => throw new InvalidOperationException("Untrusted reflected owner read.");
        public override Type ReturnType => typeof(object);
        public override ICustomAttributeProvider ReturnTypeCustomAttributes => typeof(object);
        public override MethodAttributes Attributes => MethodAttributes.Public | MethodAttributes.Static;
        public override RuntimeMethodHandle MethodHandle => default;
        public override MethodInfo GetBaseDefinition() => this;
        public override MethodImplAttributes GetMethodImplementationFlags() => MethodImplAttributes.IL;
        public override ParameterInfo[] GetParameters() => [];
        public override object[] GetCustomAttributes(bool inherit) => [];
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => [];
        public override bool IsDefined(Type attributeType, bool inherit) => false;
        public override object? Invoke(object? obj, BindingFlags flags, Binder? binder, object?[]? parameters,
            System.Globalization.CultureInfo? culture) => null;
    }

    private static MethodInfo Lookup(Assembly saved) => saved.GetType("$NativeConstructedBodyLookup")!.GetMethod("FindCore", Members)!;
    private static string Run(Assembly saved) => TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20));
    private static RuntimeFeatureSet BasicFeatures() => new RuntimeFeatureDetector().Detect(new Parser(new Lexer("console.log(42);").ScanTokens()).ParseOrThrow());
    private static ILCompiler Compiler(bool native)
    {
        var compiler = new ILCompiler("constructed_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); return compiler;
    }
    private static Assembly Compile(string source, bool native, out ILCompiler compiler)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        compiler = Compiler(native); compiler.Compile(statements, types); return VerifyLoad(compiler.SaveToBytes());
    }
    private static Assembly VerifyLoad(byte[] bytes)
    {
        using var image = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(image)); return Assembly.Load(bytes);
    }
}