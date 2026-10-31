using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.IntegrationTests;
using SharpTS.TypeSystem;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeGenericMethodNormalizationTests
{
    private const BindingFlags StaticMethods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const string GenericSource = "function identity<T>(value: any): any { return value; } const alias = identity; console.log(alias(7), alias('text'));";

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void VerifiedForeignMarkersHaveAnExplicitNativeBoundaryWithAnyCompletedProducerInventory(bool native, bool producer)
    {
        using var fixture = new Fixture(producer ? GenericSource : "console.log(1);", native);
        var assembly = fixture.Assembly;
        var normalize = assembly.GetType("$TSFunction")!.GetMethod("$ErasedSourceInvokerTarget", StaticMethods)!;
        var foreign = fixture.ForeignMarkedMethod();
        Assert.True(foreign.IsGenericMethodDefinition);
        foreach (string marker in new[] { "$FunctionName", "$FunctionLength", "$PadUndefined" })
            Assert.True(foreign.IsDefined(assembly.GetType(marker)!, false));
        bool refused = native;
        if (refused)
        {
            AssertGuestTypeError(assembly, () => normalize.Invoke(null, [foreign]));
            AssertGuestTypeError(assembly, () => Activator.CreateInstance(assembly.GetType("$TSFunction")!, [null, foreign]));
        }
        else
        {
            var normalized = Assert.IsAssignableFrom<MethodInfo>(normalize.Invoke(null, [foreign]));
            Assert.False(normalized.IsGenericMethodDefinition);
            Assert.Equal(typeof(object), Assert.Single(normalized.GetGenericArguments()));
            var wrapper = Activator.CreateInstance(assembly.GetType("$TSFunction")!, [null, foreign])!;
            Assert.Same(foreign, wrapper.GetType().GetField("_method", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(wrapper));
            Assert.Equal(42d, wrapper.GetType().GetMethod("Invoke")!.Invoke(wrapper, [new object[] { 42d }]));
        }
        var unmarked = typeof(Enumerable).GetMethod(nameof(Enumerable.Empty))!;
        Assert.Same(unmarked, normalize.Invoke(null, [unmarked]));
        var closed = foreign.MakeGenericMethod(typeof(int));
        Assert.Same(closed, normalize.Invoke(null, [closed]));
        var ordinary = typeof(Math).GetMethod(nameof(Math.Abs), [typeof(double)])!;
        Assert.Same(ordinary, normalize.Invoke(null, [ordinary]));
        if (native)
        {
            Assert.True(fixture.Compiler.NativeGenericMethods!.IsComplete);
            Assert.False(fixture.Compiler.NativeGenericMethods.UsesLegacy);
            var late = assembly.GetType("$NativeGenericMethodNormalization")!.GetMethod("Normalize", StaticMethods)!;
            Assert.DoesNotContain(Instructions(late), instruction => instruction.Member?.Name == "MakeGenericMethod");
        }
        else Assert.Null(assembly.GetType("$NativeGenericMethodNormalization"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedProducerInventoryIncludesNewTargetClonesAndIgnoresFeatureOverrides(bool newTarget)
    {
        string source = newTarget
            ? "function identity<T>(value:any):any { const target = new.target; return value; } const alias = identity; console.log(alias(7));"
            : GenericSource;
        using var fixture = new Fixture(source, native: true, overrideFeatures: !newTarget);
        var session = fixture.Compiler.NativeGenericMethods!;
        Assert.False(session.UsesLegacy);
        Assert.Empty(session.Blockers);
        Assert.Equal(1, session.NormalizedDefinitionCount);
        Assert.Equal(newTarget ? 1 : 0, session.ConstructedPairCount);
        int actual = fixture.Assembly.GetTypes().Sum(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Count(method => method.IsGenericMethodDefinition));
        Assert.Equal(newTarget ? 2 : 1, actual);
        Assert.Equal(actual, session.GenericDefinitionCount);
        Assert.Equal(actual, session.Definitions.Count);
        Assert.Equal(newTarget ? "7\n" : "7 text\n", Run(fixture.Assembly));
    }

    [Fact]
    public void GenericClassesAloneDoNotBecomeMethodGenericProducers()
    {
        using var fixture = new Fixture("class Box<T> { constructor(public value: T) {} } console.log(new Box(7).value);", native: true);
        Assert.Equal("7\n", Run(fixture.Assembly));
        Assert.Equal(0, fixture.Compiler.NativeGenericMethods!.GenericDefinitionCount);
        Assert.False(fixture.Compiler.NativeGenericMethods.UsesLegacy);
    }

    [Fact]
    public void CanonicalGenericConstructorsPreserveNewTargetArgumentsAndExactIdentity()
    {
        const string source = """
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
        using var fixture = new Fixture(source, native: true);
        Assert.Equal("7 true true 1 0\nbound true true 4 2\nreflect true true 1 0\ncall true true 1\n", Run(fixture.Assembly));
        var session = fixture.Compiler.NativeGenericMethods!;
        Assert.False(session.UsesLegacy); Assert.Empty(session.Blockers);
        Assert.Equal(1, session.NormalizedDefinitionCount); Assert.Equal(1, session.ConstructedPairCount);
        var assembly = fixture.Assembly;
        var entry = Assert.Single(assembly.GetType("$Program")!.GetMethods(StaticMethods),
            method => method.Name == "Target" && method.IsGenericMethodDefinition);
        var core = Assert.Single(assembly.GetType("$Program")!.GetMethods(StaticMethods),
            method => method.Name.StartsWith("<>NewTargetBody", StringComparison.Ordinal) && method.IsGenericMethodDefinition);
        var normalizer = assembly.GetType("$NativeGenericMethodNormalization")!.GetMethod("Normalize", StaticMethods)!;
        var resolver = assembly.GetType("$NativeGenericMethodNormalization")!.GetMethod("ResolveConstructed", StaticMethods)!;
        var canonicalEntry = entry.MakeGenericMethod(typeof(object));
        var canonicalCore = core.MakeGenericMethod(typeof(object));
        Assert.Equal(canonicalEntry, normalizer.Invoke(null, [entry]));
        Assert.Same(core, normalizer.Invoke(null, [core]));
        var otherClosedEntry = entry.MakeGenericMethod(typeof(string));
        Assert.Same(otherClosedEntry, normalizer.Invoke(null, [otherClosedEntry]));
        Assert.Equal(canonicalCore, resolver.Invoke(null, [core, entry]));
        Assert.Equal(canonicalCore, resolver.Invoke(null, [core, canonicalEntry]));
        AssertGuestTypeError(assembly, () => resolver.Invoke(null, [core, otherClosedEntry]), NativeGenericMethodNormalization.ConstructedBoundaryMessage);
        var hostile = new ForeignMethodInfo();
        Assert.Same(hostile, normalizer.Invoke(null, [hostile]));
        AssertGuestTypeError(assembly, () => resolver.Invoke(null, [core, hostile]), NativeGenericMethodNormalization.ConstructedBoundaryMessage);
        AssertGuestTypeError(assembly, () => resolver.Invoke(null, [hostile, entry]), NativeGenericMethodNormalization.ConstructedBoundaryMessage);
        foreach (var method in assembly.GetType("$NativeGenericMethodNormalization")!.GetMethods(StaticMethods)
            .Append(assembly.GetType("$TSFunction")!.GetMethod("InvokeConstructed")!))
            Assert.DoesNotContain(Instructions(method), instruction => instruction.Member?.Name == "MakeGenericMethod");
        foreach (string marker in new[] { "$FunctionName", "$FunctionLength", "$PadUndefined" })
        {
            Assert.True(entry.IsDefined(assembly.GetType(marker)!, false));
            Assert.False(core.IsDefined(assembly.GetType(marker)!, false));
        }
    }

    [Fact]
    public void CompletedConstraintsAreRecheckedAgainstTheCanonicalTarget()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var session = new NativeGenericMethodNormalization(module, TypeProvider.Runtime);
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, Features());
        var owner = module.DefineType("Source");
        var method = owner.DefineMethod("Identity", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object)]);
        var parameters = method.DefineGenericParameters("T");
        var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
        session.RecordDefinition(method, parameters);
        method.SetCustomAttribute(runtime.FunctionAttributes.PadUndefinedCtor, CustomAttributeEncoder.EmptyBlob);
        method.SetCustomAttribute(runtime.FunctionAttributes.FunctionNameCtor, CustomAttributeEncoder.Encode(runtime.FunctionAttributes.FunctionNameCtor, "Identity"));
        method.SetCustomAttribute(runtime.FunctionAttributes.FunctionLengthCtor, CustomAttributeEncoder.Encode(runtime.FunctionAttributes.FunctionLengthCtor, 1));
        foreach (var marker in Enum.GetValues<NativeGenericMethodNormalization.SourceMarker>()) session.RecordSourceMarker(method, marker);
        var registry = new FunctionGenericParameterRegistry(); registry.Declare(method, parameters);
        registry.DeclareErasedValueInvocationTarget(method, [typeof(object)]);
        parameters[0].SetBaseTypeConstraint(typeof(Exception));
        registry.CompleteEmission(); session.CompleteEmission(registry, runtime.FunctionAttributes, runtime.Errors);
        Assert.True(session.UsesLegacy); Assert.Single(session.Blockers); Assert.Equal(0, session.NormalizedDefinitionCount);
        owner.CreateType();
        using var bytes = new MemoryStream(); builder.Save(bytes);
        var saved = VerifyLoad(bytes.ToArray());
        var original = saved.GetType("Source")!.GetMethod("Identity")!;
        var normalize = saved.GetType("$NativeGenericMethodNormalization")!.GetMethod("Normalize", StaticMethods)!;
        var closed = Assert.IsAssignableFrom<MethodInfo>(normalize.Invoke(null, [original]));
        Assert.Equal(typeof(Exception), Assert.Single(closed.GetGenericArguments()));
        Assert.Contains(Instructions(normalize), instruction => instruction.Member?.Name == "MakeGenericMethod");
        var resolve = saved.GetType("$NativeGenericMethodNormalization")!.GetMethod("ResolveConstructed", StaticMethods)!;
        Assert.Equal(closed, resolve.Invoke(null, [original, closed]));
        var foreignAssembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var foreignOwner = foreignAssembly.DefineDynamicModule("foreign").DefineType("Foreign");
        var foreignMethod = foreignOwner.DefineMethod("Identity", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object)]);
        foreignMethod.DefineGenericParameters("T");
        var foreignIL = foreignMethod.GetILGenerator(); foreignIL.Emit(OpCodes.Ldarg_0); foreignIL.Emit(OpCodes.Ret);
        foreach (var (marker, argument) in new (string Marker, object? Argument)[]
            { ("$FunctionName", "foreign"), ("$FunctionLength", 1), ("$PadUndefined", null) })
        {
            var constructor = Assert.Single(saved.GetType(marker)!.GetConstructors());
            foreignMethod.SetCustomAttribute(new CustomAttributeBuilder(constructor, argument is null ? [] : [argument]));
        }
        var foreign = foreignOwner.CreateType()!.GetMethod("Identity")!;
        AssertGuestTypeError(saved, () => normalize.Invoke(null, [foreign]));
        AssertGuestTypeError(saved, () => resolve.Invoke(null, [foreign, closed]), NativeGenericMethodNormalization.ConstructedBoundaryMessage);
    }

    [Theory]
    [InlineData("unconstrained", true)]
    [InlineData("base", true)]
    [InlineData("interface", true)]
    [InlineData("multiple", false)]
    [InlineData("special", false)]
    [InlineData("dependent", false)]
    public void CompletedTargetsMatchTheLegacyConstraintAndAttributePolicy(string shape, bool eligible)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var session = new NativeGenericMethodNormalization(module, TypeProvider.Runtime);
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, Features());
        var owner = module.DefineType("Source");
        var method = owner.DefineMethod("Identity", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object)]);
        var parameter = method.DefineGenericParameters("T")[0];
        Type canonical = shape is "base" or "multiple" ? typeof(Exception)
            : shape == "interface" ? typeof(IDisposable) : typeof(object);
        if (shape is "base" or "multiple") parameter.SetBaseTypeConstraint(typeof(Exception));
        if (shape is "interface" or "multiple") parameter.SetInterfaceConstraints(typeof(IDisposable));
        if (shape == "special") parameter.SetGenericParameterAttributes(GenericParameterAttributes.ReferenceTypeConstraint);
        if (shape == "dependent") parameter.SetInterfaceConstraints(EmitGenerics.MakeGenericType(typeof(IEnumerable<>), parameter));
        var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
        session.RecordDefinition(method, [parameter]);
        method.SetCustomAttribute(runtime.FunctionAttributes.PadUndefinedCtor, CustomAttributeEncoder.EmptyBlob);
        method.SetCustomAttribute(runtime.FunctionAttributes.FunctionNameCtor, CustomAttributeEncoder.Encode(runtime.FunctionAttributes.FunctionNameCtor, "Identity"));
        method.SetCustomAttribute(runtime.FunctionAttributes.FunctionLengthCtor, CustomAttributeEncoder.Encode(runtime.FunctionAttributes.FunctionLengthCtor, 1));
        foreach (var marker in Enum.GetValues<NativeGenericMethodNormalization.SourceMarker>()) session.RecordSourceMarker(method, marker);
        var registry = new FunctionGenericParameterRegistry(); registry.Declare(method, [parameter]);
        registry.DeclareErasedValueInvocationTarget(method, [canonical]); registry.CompleteEmission();
        session.CompleteEmission(registry, runtime.FunctionAttributes, runtime.Errors);
        Assert.Equal(!eligible, session.UsesLegacy); Assert.Equal(eligible ? 1 : 0, session.NormalizedDefinitionCount);
        owner.CreateType(); using var bytes = new MemoryStream(); builder.Save(bytes);
        var saved = VerifyLoad(bytes.ToArray());
        var original = saved.GetType("Source")!.GetMethod("Identity")!;
        var normalize = saved.GetType("$NativeGenericMethodNormalization")!.GetMethod("Normalize", StaticMethods)!;
        var normalized = Assert.IsAssignableFrom<MethodInfo>(normalize.Invoke(null, [original]));
        if (eligible) Assert.Equal(canonical, Assert.Single(normalized.GetGenericArguments()));
        else Assert.Same(original, normalized);
        Assert.Equal(!eligible, Instructions(normalize).Any(instruction => instruction.Member?.Name == "MakeGenericMethod"));
    }

    [Fact]
    public void ImportedDefinitionsJoinTheCompletedModuleInventory()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string entry = directory.GetPath("main.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [directory.GetPath("lib.ts")] = "export function identity<T>(value:any):any { return value; }",
            [entry] = "import {identity} from './lib'; const alias = identity; console.log(alias(7), alias('text'));"
        };
        var resolver = new ModuleResolver(entry, files);
        var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry));
        var checker = new TypeChecker(); var types = checker.CheckModules(modules, resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = Compiler(); compiler.CompileModules(modules, resolver, types);
        var assembly = VerifyLoad(compiler.SaveToBytes());
        Assert.Equal("7 text\n", Run(assembly));
        Assert.True(compiler.NativeGenericMethods!.IsComplete);
        Assert.False(compiler.NativeGenericMethods.UsesLegacy);
        Assert.Equal(1, compiler.NativeGenericMethods.GenericDefinitionCount);
    }

    [Fact]
    public void DefaultNativeRuntimeEmitterHasNoCertificateAndKeepsTheLegacyBody()
    {
        var builder = NewAssembly();
        new RuntimeEmitter(TypeProvider.Runtime, false, true).EmitAll(builder.DefineDynamicModule("main"), Features());
        using var stream = new MemoryStream(); builder.Save(stream);
        var assembly = VerifyLoad(stream.ToArray());
        Assert.Null(assembly.GetType("$NativeGenericMethodNormalization"));
        var method = assembly.GetType("$TSFunction")!.GetMethod("$ErasedSourceInvokerTarget", StaticMethods)!;
        Assert.Contains(Instructions(method), instruction => instruction.Member?.Name == "MakeGenericMethod");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteAndMismatchedProducerMetadataCannotCompleteTheLateBody(bool missingRecord)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var session = new NativeGenericMethodNormalization(module, TypeProvider.Runtime);
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, Features());
        var registry = new FunctionGenericParameterRegistry();
        Assert.Throws<InvalidOperationException>(() => session.CompleteEmission(registry, runtime.FunctionAttributes, runtime.Errors));
        var owner = module.DefineType("Source");
        var method = owner.DefineMethod("Identity", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object)]);
        var parameters = method.DefineGenericParameters("T");
        if (missingRecord) registry.Declare(method, parameters);
        else session.RecordDefinition(method, parameters);
        registry.CompleteEmission();
        Assert.Throws<InvalidOperationException>(() => session.CompleteEmission(registry, runtime.FunctionAttributes, runtime.Errors));
        Assert.False(session.IsComplete);
    }

    [Fact]
    public void SuccessfulCertificateCompletesOnceAndRejectsForeignOrLateDefinitions()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var session = new NativeGenericMethodNormalization(module, TypeProvider.Runtime);
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, Features());
        var foreign = NewAssembly().DefineDynamicModule("other").DefineType("Foreign").DefineMethod("Method", MethodAttributes.Static);
        var parameters = foreign.DefineGenericParameters("T");
        Assert.Throws<InvalidOperationException>(() => session.RecordDefinition(foreign, parameters));
        var registry = new FunctionGenericParameterRegistry(); registry.CompleteEmission();
        var foreignRuntime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(NewAssembly().DefineDynamicModule("foreign"), Features());
        Assert.Throws<InvalidOperationException>(() => session.CompleteEmission(registry, foreignRuntime.FunctionAttributes, foreignRuntime.Errors));
        Assert.False(session.IsComplete);
        session.CompleteEmission(registry, runtime.FunctionAttributes, runtime.Errors);
        Assert.True(session.IsComplete); Assert.False(session.UsesLegacy);
        Assert.Throws<InvalidOperationException>(() => session.RecordDefinition(foreign, parameters));
        Assert.Throws<InvalidOperationException>(() => session.CompleteEmission(registry, runtime.FunctionAttributes, runtime.Errors));
        using var bytes = new MemoryStream(); builder.Save(bytes);
        VerifyLoad(bytes.ToArray());
    }

    private static void AssertGuestTypeError(Assembly assembly, Action action, string message = ErasedSourceInvocationEmission.ForeignMarkersMessage)
    {
        var exception = Assert.Throws<TargetInvocationException>(action).InnerException;
        var error = assembly.GetType("$Runtime")!.GetMethod("WrapException")!.Invoke(null, [exception])!;
        Assert.Equal("$TypeError", error.GetType().Name);
        Assert.Equal(message, error.GetType().GetProperty("Message")!.GetValue(error));
    }

    private sealed class ForeignMethodInfo : MethodInfo
    {
        public override bool Equals(object? obj) => true;
        public override int GetHashCode() => 0;
        public override bool IsGenericMethodDefinition => true;
        public override string Name => "Target";
        public override Type DeclaringType => typeof(NativeGenericMethodNormalizationTests);
        public override Type ReflectedType => DeclaringType;
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
        public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters,
            System.Globalization.CultureInfo? culture) => null;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly IDisposable _directory;
        private readonly string _root;
        public ILCompiler Compiler { get; }
        public Assembly Assembly { get; }

        public Fixture(string source, bool native, bool overrideFeatures = false)
        {
            var directory = CliTestHelper.CreateTempDirectory(); _directory = directory; _root = directory.Path;
            var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var checker = new TypeChecker(); var types = checker.Check(statements);
            Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
            string name = "generic_normalizer_" + Guid.NewGuid().ToString("N");
            Compiler = new ILCompiler(name, false, true, null, null, null, OutputTarget.Dll);
            if (native) Compiler.EnableNativeAotProjectOutput();
            if (overrideFeatures) Compiler.SetRuntimeFeatures(Features());
            Compiler.Compile(statements, types);
            byte[] bytes = Compiler.SaveToBytes(); Verify(bytes);
            string path = directory.GetPath(name + ".dll"); File.WriteAllBytes(path, bytes);
            Assembly = Assembly.LoadFrom(path);
            if (native)
            {
                var actual = Assembly.GetTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)).Where(method => method.IsGenericMethodDefinition);
                Assert.Equal(actual.Select(method => method.MetadataToken).Order(),
                    Compiler.NativeGenericMethods!.Definitions.Select(method => method.MetadataToken).Order());
            }
        }

        public MethodInfo ForeignMarkedMethod()
        {
            var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
            var owner = module.DefineType("Foreign", TypeAttributes.Public);
            var method = owner.DefineMethod("Identity", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object)]);
            method.DefineGenericParameters("T");
            var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
            method.SetCustomAttribute(new CustomAttributeBuilder(Assembly.GetType("$FunctionName")!.GetConstructor([typeof(string)])!, ["foreign"]));
            method.SetCustomAttribute(new CustomAttributeBuilder(Assembly.GetType("$FunctionLength")!.GetConstructor([typeof(int)])!, [1]));
            method.SetCustomAttribute(new CustomAttributeBuilder(Assembly.GetType("$PadUndefined")!.GetConstructor(Type.EmptyTypes)!, []));
            owner.CreateType();
            using var bytes = new MemoryStream(); builder.Save(bytes);
            Verify(bytes.ToArray(), _root);
            string path = Path.Combine(_root, builder.GetName().Name + ".dll"); File.WriteAllBytes(path, bytes.ToArray());
            return Assembly.LoadFrom(path).GetType("Foreign")!.GetMethod("Identity")!;
        }
        public void Dispose() => _directory.Dispose();
    }

    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName("generic_normalizer_runtime_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
    private static ILCompiler Compiler() { var compiler = new ILCompiler("generic_normalizer_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll); compiler.EnableNativeAotProjectOutput(); return compiler; }
    private static RuntimeFeatureSet Features() => new RuntimeFeatureDetector().Detect(new Parser(new Lexer("console.log(1);").ScanTokens()).ParseOrThrow());
    private static string Run(Assembly assembly) => TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20));
    private static Assembly VerifyLoad(byte[] bytes) { Verify(bytes); return Assembly.Load(bytes); }
    private static void Verify(byte[] bytes, string? extra = null)
    {
        using var stream = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: extra is null ? [AppContext.BaseDirectory] : [AppContext.BaseDirectory, extra]);
        Assert.Empty(verifier.Verify(stream));
    }
}
