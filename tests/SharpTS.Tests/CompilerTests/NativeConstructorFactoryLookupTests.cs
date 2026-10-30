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

public sealed class NativeConstructorFactoryLookupTests
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private const string Source = """
        class Parent {
            value:any; rest:any; target:any;
            constructor(value:any=7,...rest:any[]){
                this.value=value; this.rest=rest.join(','); this.target=new.target;
            }
        }
        class Child extends Parent {}
        class Alternate {}
        const P:any=Parent, C:any=Child;
        const Bound:any=C.bind(null,'bound');
        const first:any=new C(undefined,1,2);
        const second:any=new Bound(3,4);
        const third:any=Reflect.construct(P,['reflect',5],Alternate);
        console.log(first.value,first.rest,first.target===Child);
        console.log(second.value,second.rest,second.target===Child);
        console.log(third.value,third.rest,third.target===Alternate);
        """;

    public static IEnumerable<object[]> ConstructorCases() =>
        // Native Array.from(arguments) profile limitation is tracked separately in #3094.
        ClassExpressionConstructorRestTests.Cases().Where(row => (string)row[0] is not ("shapes" or "alternate-new-target"))
            .Concat(ConstructorPrimitiveReturnTests.Cases());

    [Theory, MemberData(nameof(ConstructorCases))]
    public void ExistingConstructionCorpusPreservesFactoryCallingSemantics(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var saved = Compile(source, true, out var compiler);
        Assert.Equal(expected, Run(saved));
        Assert.True(compiler.NativeConstructorFactories!.IsComplete);
        AssertActualMarkers(saved, compiler.NativeConstructorFactories);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultsRestInheritanceAndNewTargetKeepTheirOrder(bool native)
    {
        var saved = Compile(Source, native, out var compiler);
        Assert.Equal("7 1,2 true\nbound 3,4 true\nreflect 5 true\n", Run(saved));
        if (native)
        {
            Assert.False(compiler.NativeConstructorFactories!.UsesLegacy);
            AssertActualMarkers(saved, compiler.NativeConstructorFactories);
        }
        else { Assert.Null(compiler.NativeConstructorFactories); Assert.Null(saved.GetType("$NativeConstructorFactoryLookup")); }
    }

    [Fact]
    public void ExactFactoryMetadataPreservesCollidingSourceNamesAndRefusesOtherProtocols()
    {
        var saved = Compile("class Owner { static $ConstructFromArguments(){return 1;} constructor(public value:number=7){} }", true, out var compiler);
        Assert.False(compiler.NativeConstructorFactories!.UsesLegacy);
        var owner = NativeSourceTypeLookup.Find(saved, "Owner")!;
        var marker = Assert.Single(owner.GetCustomAttributes(saved.GetType("$GuestConstructorFactory")!, false));
        string name = (string)marker.GetType().GetField("Name")!.GetValue(marker)!;
        Assert.Equal("$ConstructFromArguments_", name);
        Assert.Equal(owner.GetMethod(name, Flags), Lookup(saved).Invoke(null, [owner, name, Flags]));
        AssertGuestTypeError(saved, () => Lookup(saved).Invoke(null, [owner, "$ConstructFromArguments", Flags]));
        AssertGuestTypeError(saved, () => Lookup(saved).Invoke(null, [owner, name, BindingFlags.Public | BindingFlags.Static]));
        AssertGuestTypeError(saved, () => Lookup(saved).Invoke(null, [typeof(object), name, Flags]));
        Assert.DoesNotContain(Instructions(Lookup(saved)), instruction => instruction.Member?.Name == "GetMethod");
    }

    [Fact]
    public void OwnedGenericFamiliesKeepTheWholeBinderOperationAndDoNotCloseNewTokens()
    {
        var saved = Compile("class Box<T> { constructor(public value:T){} static visible(){return 1;} } class Plain {}", true, out var compiler);
        Assert.True(compiler.NativeConstructorFactories!.UsesLegacy); Assert.NotEmpty(compiler.NativeConstructorFactories.Blockers);
        var definition = Assert.Single(saved.GetTypes(), type => type.IsGenericTypeDefinition && type.Name.StartsWith("Box", StringComparison.Ordinal));
        var marker = Assert.Single(definition.GetCustomAttributes(saved.GetType("$GuestConstructorFactory")!, false));
        string name = (string)marker.GetType().GetField("Name")!.GetValue(marker)!;
        foreach (var owner in new[] { definition, definition.MakeGenericType(typeof(object)), definition.MakeGenericType(typeof(double)) })
            Assert.Equal(owner.GetMethod(name, Flags), Lookup(saved).Invoke(null, [owner, name, Flags]));
        var plain = NativeSourceTypeLookup.Find(saved, "Plain")!;
        Assert.Null(Lookup(saved).Invoke(null, [plain, "<missing>", Flags]));
        Assert.Equal(definition.GetMethod("visible", BindingFlags.Public | BindingFlags.Static),
            Lookup(saved).Invoke(null, [definition, "visible", BindingFlags.Public | BindingFlags.Static]));
        var hostile = new HostileType(definition);
        AssertGuestTypeError(saved, () => Lookup(saved).Invoke(null, [hostile, name, Flags]));
        Assert.Equal(0, hostile.EqualsCalls); Assert.Equal(0, hostile.MetadataCalls);
        AssertGuestTypeError(saved, () => Lookup(saved).Invoke(null, [typeof(List<object>), name, Flags]));
        Assert.Contains(Instructions(Lookup(saved)), instruction => instruction.Member?.Name == "GetMethod");
        Assert.DoesNotContain(Instructions(Lookup(saved)), instruction => instruction.Member?.Name == "MakeGenericType");
    }

    [Fact]
    public void ModuleAliasesCompleteTheSameActualProducerInventory()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string entry = directory.GetPath("main.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [directory.GetPath("lib.ts")] = "export class Parent {constructor(public value:any=7,...rest:any[]){this.value+=':'+rest.join(',');}}",
            [entry] = "import {Parent} from './lib'; class Child extends Parent {} const C:any=Child; console.log(new C(undefined,1,2).value);"
        };
        var resolver = new ModuleResolver(entry, files); var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry));
        var checker = new TypeChecker(); var types = checker.CheckModules(modules, resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = Compiler(true); compiler.CompileModules(modules, resolver, types);
        var saved = VerifyLoad(compiler.SaveToBytes());
        Assert.Equal("7:1,2\n", Run(saved));
        Assert.True(compiler.NativeConstructorFactories!.IsComplete); Assert.False(compiler.NativeConstructorFactories.UsesLegacy);
        AssertActualMarkers(saved, compiler.NativeConstructorFactories);
    }

    [Fact]
    public void LifecycleAndModeValidateTheActualDeclaration()
    {
        var saved = Compile("class Owner {}", true, out var compiler); AssertActualMarkers(saved, compiler.NativeConstructorFactories!);
        var runtime = (EmittedRuntime)typeof(ILCompiler).GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(compiler)!;
        Assert.Throws<InvalidOperationException>(() => compiler.NativeConstructorFactories!.CompleteEmission(compiler.NativeSourceOwners!, runtime));
        var module = new PersistedAssemblyBuilder(new AssemblyName("factory_lifecycle"), typeof(object).Assembly).DefineDynamicModule("main");
        var lookup = new NativeConstructorFactoryLookup(module, TypeProvider.Runtime);
        Assert.Throws<InvalidOperationException>(() => lookup.ValidateForEmitter(module, false));
        Assert.Throws<InvalidOperationException>(() => lookup.CompleteEmission(compiler.NativeSourceOwners!, runtime));
        var owner = module.DefineType("Other");
        var method = owner.DefineMethod("Factory", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object[]), typeof(object)]);
        Assert.Throws<InvalidOperationException>(() => lookup.Record(owner, method));
        Assert.Throws<InvalidOperationException>(() => compiler.NativeConstructorFactories!.Record(owner, method));
        lookup.FindFactory.SetReturnType(typeof(object));
        Assert.Throws<InvalidOperationException>(() => lookup.ValidateForEmitter(module, true));
    }

    [Fact]
    public void SyntheticNativeProbePreservesInitializerTimingAndRefusesForgedNominalOwners()
    {
        const string source = "class Effects {static count=0;} class Owner {static stamp=++Effects.count; constructor(){Effects.count+=10;}}";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = Compiler(true); compiler.Compile(statements, types);
        var fields = BindingFlags.NonPublic | BindingFlags.Instance;
        var module = (ModuleBuilder)typeof(ILCompiler).GetField("_moduleBuilder", fields)!.GetValue(compiler)!;
        var runtime = (EmittedRuntime)typeof(ILCompiler).GetField("_runtime", fields)!.GetValue(compiler)!;
        var owners = compiler.NativeSourceOwners!.Implementers.ToArray();
        var owner = Assert.Single(owners, type => type.Name == "Owner"); var effects = Assert.Single(owners, type => type.Name == "Effects");
        var count = effects.GetField("count", BindingFlags.Public | BindingFlags.Static)!;
        var foreign = module.DefineType("Forged", TypeAttributes.Public, owner);
        foreign.DefineDefaultConstructor(MethodAttributes.Public);
        var bad = foreign.DefineMethod("Factory", MethodAttributes.Private | MethodAttributes.Static, typeof(object), [typeof(object[]), typeof(object)]);
        var body = bad.GetILGenerator(); body.Emit(OpCodes.Ldc_R8, 999d);
        if (!count.FieldType.IsValueType) body.Emit(OpCodes.Box, typeof(double));
        body.Emit(OpCodes.Stsfld, count); body.Emit(OpCodes.Ldnull); body.Emit(OpCodes.Ret);
        foreign.SetCustomAttribute(runtime.DynamicConstruction.FactoryAttributeConstructor,
            CustomAttributeEncoder.Encode(runtime.DynamicConstruction.FactoryAttributeConstructor, "Factory")); foreign.CreateType();
        var probe = module.DefineType("NativeFactoryProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var read = probe.DefineMethod("Count", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
        read.GetILGenerator().Emit(OpCodes.Ldsfld, count);
        if (count.FieldType.IsValueType) read.GetILGenerator().Emit(OpCodes.Box, count.FieldType);
        read.GetILGenerator().Emit(OpCodes.Ret);
        var meta = probe.DefineMethod("Metadata", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), Type.EmptyTypes);
        var il = meta.GetILGenerator(); il.Emit(OpCodes.Ldtoken, owner); il.Emit(OpCodes.Call, TypeProvider.Runtime.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ldstr, "$ConstructFromArguments"); il.Emit(OpCodes.Ldc_I4, (int)Flags);
        il.Emit(OpCodes.Call, compiler.NativeConstructorFactories!.FindFactory); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Cgt_Un); il.Emit(OpCodes.Ret);
        foreach (bool known in new[] { true, false })
        {
            var method = probe.DefineMethod(known ? "Construct" : "Foreign", MethodAttributes.Public | MethodAttributes.Static,
                known ? typeof(object) : typeof(bool), Type.EmptyTypes);
            il = method.GetILGenerator(); var done = il.DefineLabel(); var result = known ? null : il.DeclareLocal(typeof(bool));
            if (!known) il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldtoken, known ? owner : foreign); il.Emit(OpCodes.Call, TypeProvider.Runtime.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newarr, typeof(object));
            il.Emit(OpCodes.Ldtoken, known ? owner : foreign); il.Emit(OpCodes.Call, TypeProvider.Runtime.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Call, runtime.DynamicConstruction.ConstructWithNewTarget);
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
        var refuse = probe.DefineMethod("Refuse", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), [typeof(Type)]);
        il = refuse.GetILGenerator(); var end = il.DefineLabel(); var refused = il.DeclareLocal(typeof(bool));
        il.BeginExceptionBlock(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, "$ConstructFromArguments"); il.Emit(OpCodes.Ldc_I4, (int)Flags);
        il.Emit(OpCodes.Call, compiler.NativeConstructorFactories.FindFactory); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, refused); il.Emit(OpCodes.Leave, end); il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Call, runtime.Errors.WrapException); il.Emit(OpCodes.Isinst, runtime.Errors.TypeErrorType);
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Cgt_Un); il.Emit(OpCodes.Stloc, refused); il.Emit(OpCodes.Leave, end); il.EndExceptionBlock();
        il.MarkLabel(end); il.Emit(OpCodes.Ldloc, refused); il.Emit(OpCodes.Ret); probe.CreateType();
        byte[] bytes = compiler.SaveToBytes(); var saved = VerifyLoad(bytes); var savedProbe = saved.GetType("NativeFactoryProbe")!;
        Assert.Equal(0d, savedProbe.GetMethod("Count")!.Invoke(null, null));
        Assert.Equal(true, savedProbe.GetMethod("Metadata")!.Invoke(null, null));
        Assert.Equal(0d, savedProbe.GetMethod("Count")!.Invoke(null, null));
        Assert.NotNull(savedProbe.GetMethod("Construct")!.Invoke(null, null));
        Assert.Equal(11d, savedProbe.GetMethod("Count")!.Invoke(null, null));
        Assert.Equal(true, savedProbe.GetMethod("Foreign")!.Invoke(null, null));
        Assert.Equal(11d, savedProbe.GetMethod("Count")!.Invoke(null, null));
        var hostile = new HostileType(NativeSourceTypeLookup.Find(saved, "Owner")!);
        Assert.Equal(true, savedProbe.GetMethod("Refuse")!.Invoke(null, [hostile])); Assert.Equal(0, hostile.EqualsCalls); Assert.Equal(0, hostile.MetadataCalls);
        if (Environment.GetEnvironmentVariable("SHARPTS_FACTORY_NATIVE_FIXTURE_PATH") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllBytes(path, bytes);
        }
    }

    private static void AssertActualMarkers(Assembly saved, NativeConstructorFactoryLookup lookup)
    {
        var marker = saved.GetType("$GuestConstructorFactory")!;
        var owners = saved.GetTypes().Where(type => type.IsDefined(marker, false)).ToArray();
        Assert.Equal(owners.Length, lookup.FactoryCount);
        foreach (var owner in owners)
        {
            var attribute = Assert.Single(owner.GetCustomAttributes(marker, false));
            var name = (string)marker.GetField("Name")!.GetValue(attribute)!;
            Assert.Equal(owner.GetMethod(name, Flags), Lookup(saved).Invoke(null, [owner, name, Flags]));
        }
    }

    private sealed class HostileType(Type type) : TypeDelegator(type)
    {
        internal int EqualsCalls { get; private set; }
        internal int MetadataCalls { get; private set; }
        public override bool Equals(object? other) { EqualsCalls++; return true; }
        public override int GetHashCode() => 0;
        public override Type GetGenericTypeDefinition() { MetadataCalls++; throw new InvalidOperationException("Untrusted generic metadata."); }
        protected override MethodInfo? GetMethodImpl(string name, BindingFlags flags, Binder? binder, CallingConventions convention,
            Type[]? types, ParameterModifier[]? modifiers) { MetadataCalls++; throw new InvalidOperationException("Untrusted factory lookup."); }
    }
    private static void AssertGuestTypeError(Assembly saved, Action action)
    {
        var failure = Assert.Throws<TargetInvocationException>(action).InnerException!;
        var error = saved.GetType("$Runtime")!.GetMethod("WrapException")!.Invoke(null, [failure])!;
        Assert.Equal("$TypeError", error.GetType().Name);
        Assert.Equal(NativeConstructorFactoryLookup.ForeignFactoryMessage, error.GetType().GetProperty("Message")!.GetValue(error));
    }
    private static MethodInfo Lookup(Assembly saved) => saved.GetType("$NativeConstructorFactoryLookup")!.GetMethod("FindFactory", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static string Run(Assembly saved) => TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20));
    private static ILCompiler Compiler(bool native)
    {
        var compiler = new ILCompiler("factory_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); return compiler;
    }
    private static Assembly Compile(string source, bool native, out ILCompiler compiler)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        compiler = Compiler(native); compiler.Compile(statements, types); return VerifyLoad(compiler.SaveToBytes());
    }
    private static Assembly VerifyLoad(byte[] bytes)
    {
        using var image = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(image)); return Assembly.Load(bytes);
    }
}