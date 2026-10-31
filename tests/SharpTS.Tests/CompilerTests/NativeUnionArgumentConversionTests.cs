using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.IntegrationTests;
using SharpTS.TypeSystem;
using Xunit;
using TI = SharpTS.TypeSystem.TypeInfo;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeUnionArgumentConversionTests
{
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string Producer = "const g=(x:number,y:string)=>[x,y]; console.log(g(1,'a').length);";

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void VerifiedForeignUnionConversionHasAnExplicitBoundaryOnlyWithTheZeroProducerCertificate(bool native, bool producer)
    {
        using var fixture = new Fixture(producer ? Producer : "console.log(42);", native);
        Assembly assembly = fixture.Assembly;
        var converter = assembly.GetType("$TSFunction")!.GetMethod("ConvertArgsForUnionTypes", Static)!;
        var foreign = Foreign();
        var method = foreign.GetType("Union_ForeignConsumer")!.GetMethod("Consume")!;
        var counter = foreign.GetType("State")!.GetField("Calls")!;
        Type union = foreign.GetType("Union_Foreign")!;
        Assert.False(assembly.GetType("$IUnionType")!.IsAssignableFrom(union));
        object[] values = [42d]; counter.SetValue(null, 0);
        bool refused = native && !producer;
        if (refused) AssertGuestTypeError(assembly, () => converter.Invoke(null, [method.GetParameters(), values]));
        else
        {
            converter.Invoke(null, [method.GetParameters(), values]);
            Assert.Equal(union, values[0].GetType());
            Assert.Equal(42d, union.GetField("Value")!.GetValue(values[0]));
        }
        Assert.Equal(refused ? 0 : 1, counter.GetValue(null));

        var wrapper = Activator.CreateInstance(assembly.GetType("$TSFunction")!, [null, method])!;
        Assert.Same(method, wrapper.GetType().GetField("_method", Instance)!.GetValue(wrapper));
        Assert.Equal(1, wrapper.GetType().GetField("_argConversionKinds", Instance)!.GetValue(wrapper));
        var cached = Assert.IsType<ParameterInfo[]>(wrapper.GetType().GetField("_conversionParameters", Instance)!.GetValue(wrapper));
        Assert.Equal(union, Assert.Single(cached).ParameterType);
        object[] originalArgs = [42d];
        if (refused) AssertGuestTypeError(assembly, () => wrapper.GetType().GetMethod("Invoke")!.Invoke(wrapper, [originalArgs]));
        else Assert.Equal(42d, wrapper.GetType().GetMethod("Invoke")!.Invoke(wrapper, [originalArgs]));
        Assert.Equal(refused ? typeof(double) : union, originalArgs[0].GetType());
        Assert.Same(cached, wrapper.GetType().GetField("_conversionParameters", Instance)!.GetValue(wrapper));

        foreach ((MethodInfo target, object? value) in new[]
        {
            (method, (object?)null),
            (method, Activator.CreateInstance(union)),
            (foreign.GetType("PlainForeignConsumer")!.GetMethod("Consume")!, (object?)42d),
            (foreign.GetType("ReferenceConsumer")!.GetMethod("Consume")!, (object?)42d)
        })
        {
            counter.SetValue(null, 0);
            object?[] unchanged = [value];
            converter.Invoke(null, [target.GetParameters(), unchanged]);
            Assert.Same(value, unchanged[0]); Assert.Equal(0, counter.GetValue(null));
        }
        converter.Invoke(null, [method.GetParameters(), Array.Empty<object>()]);
        object[] extraArgs = [42d];
        converter.Invoke(null, [Array.Empty<ParameterInfo>(), extraArgs]); Assert.Equal(42d, extraArgs[0]);
        Assert.Equal(producer ? "2\n" : "42\n", Run(assembly));
        if (native)
        {
            Assert.True(fixture.Compiler.NativeUnionArguments!.IsComplete);
            Assert.Equal(producer, fixture.Compiler.NativeUnionArguments.UsesLegacy);
            var late = assembly.GetType("$NativeUnionArgumentConversion")!.GetMethod("Convert", Static)!;
            Assert.Equal(producer, Instructions(late).Any(instruction => instruction.Member?.Name == "GetMethod"));
        }
        else Assert.Null(assembly.GetType("$NativeUnionArgumentConversion"));
    }

    [Fact]
    public void CompletedSnapshotIncludesActualStrictMappingsEvenWhenTheirSlotsWidenAndFeaturesAreOverridden()
    {
        using var fixture = new Fixture(Producer, native: true, overrideFeatures: true);
        var session = fixture.Compiler.NativeUnionArguments!;
        Assert.True(session.IsComplete); Assert.True(session.UsesLegacy);
        Type contract = fixture.Assembly.GetType("$IUnionType")!;
        var actual = fixture.Assembly.GetTypes().Where(type => type.IsValueType && contract.IsAssignableFrom(type));
        Assert.Equal(actual.Select(type => type.MetadataToken).Order(), session.Declarations.Select(type => type.MetadataToken).Order());
        Assert.Single(session.Declarations);
        Assert.Equal("2\n", Run(fixture.Assembly));
    }

    [Fact]
    public void ImportedStrictMappingsJoinTheCompletedModuleSnapshot()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string entry = directory.GetPath("main.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [directory.GetPath("lib.ts")] = "export const g=(x:number,y:string)=>[x,y];",
            [entry] = "import {g} from './lib'; console.log(g(1,'a').length);"
        };
        var resolver = new ModuleResolver(entry, files);
        var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry));
        var checker = new TypeChecker(); var types = checker.CheckModules(modules, resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = Compiler(true); compiler.CompileModules(modules, resolver, types);
        var assembly = VerifyLoad(compiler.SaveToBytes());
        Assert.Equal("2\n", Run(assembly));
        Assert.True(compiler.NativeUnionArguments!.IsComplete); Assert.True(compiler.NativeUnionArguments.UsesLegacy);
        Assert.Single(compiler.NativeUnionArguments.Declarations);
        Assert.Single(assembly.GetTypes(), type => type.IsValueType && assembly.GetType("$IUnionType")!.IsAssignableFrom(type));
    }

    [Fact]
    public void DefaultNativeRuntimeEmissionIsUncertifiedAndRetainsForeignConversion()
    {
        var builder = NewAssembly();
        new RuntimeEmitter(TypeProvider.Runtime, false, true).EmitAll(builder.DefineDynamicModule("main"), Features());
        using var bytes = new MemoryStream(); builder.Save(bytes);
        var assembly = VerifyLoad(bytes.ToArray());
        Assert.Null(assembly.GetType("$NativeUnionArgumentConversion"));
        var converter = assembly.GetType("$TSFunction")!.GetMethod("ConvertArgsForUnionTypes", Static)!;
        Assert.Contains(Instructions(converter), instruction => instruction.Member?.Name == "GetMethod");
        var method = Foreign().GetType("Union_ForeignConsumer")!.GetMethod("Consume")!;
        object[] values = [42d]; converter.Invoke(null, [method.GetParameters(), values]);
        Assert.Equal("Union_Foreign", values[0].GetType().Name);
    }

    [Fact]
    public void CompletionRequiresTheExactCompletedGeneratorMapperModuleAndInterface()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var session = new NativeUnionArgumentConversion(module, TypeProvider.Runtime);
        Assert.Throws<InvalidOperationException>(() => session.Declarations);
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, Features());
        var mapper = new TypeMapper(module);
        var generator = new UnionTypeGenerator(mapper, runtime.UnionValues.Interface);
        Assert.Throws<InvalidOperationException>(() => session.CompleteEmission(generator, mapper, runtime.UnionValues.Interface, runtime.Errors));
        generator.FinalizeAllUnionTypes();
        Assert.Throws<InvalidOperationException>(() => session.CompleteEmission(generator, new TypeMapper(module), runtime.UnionValues.Interface, runtime.Errors));
        Assert.Throws<InvalidOperationException>(() => session.CompleteEmission(generator, mapper, typeof(IUnionType), runtime.Errors));
        var foreignModule = NewAssembly().DefineDynamicModule("other");
        var foreignMapper = new TypeMapper(foreignModule);
        var foreign = new UnionTypeGenerator(foreignMapper, runtime.UnionValues.Interface); foreign.FinalizeAllUnionTypes();
        Assert.Throws<InvalidOperationException>(() => session.CompleteEmission(foreign, foreignMapper, runtime.UnionValues.Interface, runtime.Errors));
        Assert.False(session.IsComplete);
        session.CompleteEmission(generator, mapper, runtime.UnionValues.Interface, runtime.Errors);
        Assert.True(session.IsComplete); Assert.False(session.UsesLegacy); Assert.Empty(session.Declarations);
        Assert.Throws<InvalidOperationException>(() => session.CompleteEmission(generator, mapper, runtime.UnionValues.Interface, runtime.Errors));
        Assert.Throws<InvalidOperationException>(() => generator.GetOrCreateUnionType(new TI.Union([TI.Primitive.Number, TI.String.Shared]), module));
        Assert.Throws<NotSupportedException>(() => ((IList<Type>)session.Declarations).Add(typeof(int)));
        using var bytes = new MemoryStream(); builder.Save(bytes); VerifyLoad(bytes.ToArray());
    }

    private static void AssertGuestTypeError(Assembly assembly, Action action)
    {
        var exception = Assert.Throws<TargetInvocationException>(action).InnerException;
        var error = assembly.GetType("$Runtime")!.GetMethod("WrapException")!.Invoke(null, [exception])!;
        Assert.Equal("$TypeError", error.GetType().Name);
        Assert.Equal(UnionArgumentConversionEmission.ForeignUnionMessage, error.GetType().GetProperty("Message")!.GetValue(error));
    }

    private static Assembly Foreign()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var state = module.DefineType("State", TypeAttributes.Public);
        var calls = state.DefineField("Calls", typeof(int), FieldAttributes.Public | FieldAttributes.Static); state.CreateType();
        foreach (string name in new[] { "Union_Foreign", "PlainForeign" })
        {
            var owner = module.DefineType(name, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout, typeof(ValueType));
            var value = owner.DefineField("Value", typeof(double), FieldAttributes.Public);
            var op = owner.DefineMethod("op_Implicit", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig, owner, [typeof(double)]);
            var il = op.GetILGenerator(); var result = il.DeclareLocal(owner);
            il.Emit(OpCodes.Ldloca, result); il.Emit(OpCodes.Initobj, owner); il.Emit(OpCodes.Ldloca, result);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Stfld, value);
            il.Emit(OpCodes.Ldsfld, calls); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stsfld, calls);
            il.Emit(OpCodes.Ldloc, result); il.Emit(OpCodes.Ret);
            var consumer = module.DefineType(name + "Consumer", TypeAttributes.Public);
            var method = consumer.DefineMethod("Consume", MethodAttributes.Public | MethodAttributes.Static, typeof(double), [owner]);
            il = method.GetILGenerator(); il.Emit(OpCodes.Ldarga, (short)0); il.Emit(OpCodes.Ldfld, value); il.Emit(OpCodes.Ret);
            owner.CreateType(); consumer.CreateType();
        }
        var reference = module.DefineType("Union_Reference", TypeAttributes.Public); reference.CreateType();
        var refConsumer = module.DefineType("ReferenceConsumer", TypeAttributes.Public);
        var consume = refConsumer.DefineMethod("Consume", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [reference]);
        consume.GetILGenerator().Emit(OpCodes.Ldarg_0); consume.GetILGenerator().Emit(OpCodes.Ret); refConsumer.CreateType();
        using var bytes = new MemoryStream(); builder.Save(bytes); return VerifyLoad(bytes.ToArray());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable _directory;
        public ILCompiler Compiler { get; }
        public Assembly Assembly { get; }
        public Fixture(string source, bool native, bool overrideFeatures = false)
        {
            var directory = CliTestHelper.CreateTempDirectory(); _directory = directory;
            var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var checker = new TypeChecker(); var types = checker.Check(statements);
            Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
            Compiler = NativeUnionArgumentConversionTests.Compiler(native);
            if (overrideFeatures) Compiler.SetRuntimeFeatures(Features());
            Compiler.Compile(statements, types);
            byte[] bytes = Compiler.SaveToBytes(); Verify(bytes);
            string path = directory.GetPath("Guest.dll"); File.WriteAllBytes(path, bytes); Assembly = Assembly.LoadFrom(path);
        }
        public void Dispose() => _directory.Dispose();
    }

    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName("union_conversion_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
    private static ILCompiler Compiler(bool native)
    {
        var compiler = new ILCompiler("union_conversion_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); return compiler;
    }
    private static RuntimeFeatureSet Features() => new RuntimeFeatureDetector().Detect(new Parser(new Lexer("console.log(1);").ScanTokens()).ParseOrThrow());
    private static string Run(Assembly assembly) => TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20));
    private static Assembly VerifyLoad(byte[] bytes) { Verify(bytes); return Assembly.Load(bytes); }
    private static void Verify(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream));
    }
}
