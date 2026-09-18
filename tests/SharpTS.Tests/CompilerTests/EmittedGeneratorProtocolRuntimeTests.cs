using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedGeneratorProtocolRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static PropertyInfo[] Slots(Type owner) => owner.GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).ToArray();
    public static IEnumerable<object[]> Declarations => new[] { typeof(EmittedGeneratorRuntime), typeof(EmittedStableIteratorResultRuntime) }
        .SelectMany(type => Slots(type).Select(p => new object[] { type, p.Name }));

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingDeclarationCanBeRepairedAndCompletionFreezesMetadata(Type ownerType, string missing)
    {
        object owner = Activator.CreateInstance(ownerType, nonPublic: true)!;
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        var method = type.DefineMethod("Method", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
        var handles = new Dictionary<Type, object>
        {
            [typeof(TypeBuilder)] = type,
            [typeof(MethodBuilder)] = method,
            [typeof(Type)] = typeof(ValueTuple<double, bool>),
            [typeof(ConstructorInfo)] = typeof(ValueTuple<double, bool>).GetConstructor([typeof(double), typeof(bool)])!,
            [typeof(FieldInfo)] = typeof(ValueTuple<double, bool>).GetField("Item1")!
        };
        var property = Slots(ownerType).Single(p => p.Name == missing);
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        foreach (var other in Slots(ownerType).Where(p => p.Name != missing)) other.SetValue(owner, handles[other.PropertyType]);
        var complete = ownerType.GetMethod("CompleteEmission", Members)!;
        Expect<InvalidOperationException>(() => complete.Invoke(owner, null));
        Assert.Equal(false, ownerType.GetProperty("IsComplete")!.GetValue(owner));
        property.SetValue(owner, handles[property.PropertyType]);
        Assert.Same(handles[property.PropertyType], property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, handles[property.PropertyType]));
        complete.Invoke(owner, null);
        Assert.Equal(true, ownerType.GetProperty("IsComplete")!.GetValue(owner));
        Expect<InvalidOperationException>(() => complete.Invoke(owner, null));
        foreach (var item in Slots(ownerType)) Expect<InvalidOperationException>(() => item.SetValue(owner, handles[item.PropertyType]));
    }

    [Fact]
    public void NumericResultsHaveIndependentOptionalAvailability()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.Generators, second.Generators);
        Assert.Null(first.StableIteratorResults);
        Assert.Throws<InvalidOperationException>(() => first.RequireStableIteratorResults());
        first.BeginStableIteratorResultsEmission(); second.BeginStableIteratorResultsEmission();
        Assert.Same(first.StableIteratorResults, first.RequireStableIteratorResults());
        Assert.NotSame(first.StableIteratorResults, second.StableIteratorResults);
        Assert.False(first.RequireStableIteratorResults().IsComplete);
        Assert.Throws<InvalidOperationException>(first.BeginStableIteratorResultsEmission);
    }

    [Fact]
    public void NumericResultEmissionCanFinishBeforeGeneratorDeclarations()
    {
        var runtime = new EmittedRuntime(); var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        runtime.BeginStableIteratorResultsEmission();
        typeof(RuntimeEmitter).GetMethod("EmitStableNumberIteratorResult", Members)!.Invoke(emitter, [module, runtime.RequireStableIteratorResults()]);
        runtime.RequireStableIteratorResults().CompleteEmission();
        Assert.True(runtime.RequireStableIteratorResults().IsComplete);
        Assert.False(runtime.Generators.IsComplete);
        Assert.Throws<InvalidOperationException>(() => runtime.Generators.Type);
        Assert.Throws<InvalidOperationException>(runtime.BeginStableIteratorResultsEmission);
        typeof(RuntimeEmitter).GetMethod("EmitGeneratorInterface", Members)!.Invoke(emitter, [module, runtime.Generators]);
        runtime.Generators.CompleteEmission();
        Assert.True(runtime.Generators.IsComplete);
        var loaded = SaveVerifyLoad(builder);
        Assert.NotNull(loaded.GetType("$StableNumberIteratorResult"));
        Assert.NotNull(loaded.GetType("$INativeNumberGenerator"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesGeneratorInterfacesAndOptionalResultLayout(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var generatorOwners = new HashSet<object>(); var resultOwners = new HashSet<object>();
        foreach (bool enabled in new[] { false, true, false, true })
        {
            var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer("const n=1;").ScanTokens()).ParseOrThrow());
            if (enabled) features.CompactObjectRecordStableIteratorShapes.Add("protocol-fixture");
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), features);
            Assert.True(generatorOwners.Add(runtime.Generators)); Assert.True(runtime.Generators.IsComplete);
            foreach (var slot in Slots(typeof(EmittedGeneratorRuntime))) Assert.Same(builder, ((MemberInfo)slot.GetValue(runtime.Generators)!).Module.Assembly);
            Assert.Equal(enabled, runtime.StableIteratorResults is not null);
            if (enabled)
            {
                var owner = runtime.RequireStableIteratorResults(); Assert.True(resultOwners.Add(owner)); Assert.True(owner.IsComplete);
                foreach (var slot in Slots(typeof(EmittedStableIteratorResultRuntime))) Assert.Same(builder, ((MemberInfo)slot.GetValue(owner)!).Module.Assembly);
            }
            else Assert.Throws<InvalidOperationException>(() => runtime.RequireStableIteratorResults());
            var loaded = SaveVerifyLoad(builder); var generator = loaded.GetType("$IGenerator")!;
            Assert.True(generator.IsPublic && generator.IsInterface);
            Assert.Contains(typeof(IEnumerator<object>), generator.GetInterfaces());
            Assert.Contains(typeof(IEnumerable<object>), generator.GetInterfaces());
            Assert.Equal(typeof(object), generator.GetMethod("iterator")!.ReturnType);
            Assert.Empty(generator.GetMethod("iterator")!.GetParameters());
            foreach (string name in new[] { "next", "return", "throw" })
            {
                var method = generator.GetMethod(name)!;
                Assert.Equal(typeof(object), method.ReturnType);
                Assert.Equal(typeof(object), Assert.Single(method.GetParameters()).ParameterType);
                Assert.True(method.IsPublic && method.IsVirtual && method.IsAbstract);
            }
            var numeric = loaded.GetType("$INativeNumberGenerator")!;
            Assert.True(numeric.IsNotPublic && numeric.IsInterface);
            Assert.Contains(generator, numeric.GetInterfaces());
            Assert.Equal(typeof(bool), numeric.GetMethod("$moveNextForOf")!.ReturnType);
            Assert.Equal(typeof(double), numeric.GetMethod("$getCurrentNumber")!.ReturnType);
            Assert.All(numeric.GetMethods(), m => Assert.Empty(m.GetParameters()));
            var result = loaded.GetType("$StableNumberIteratorResult"); Assert.Equal(enabled, result is not null);
            if (result is not null)
            {
                Assert.True(result.IsValueType && result.IsSealed && result.IsLayoutSequential);
                Assert.Equal(2, result.GetFields().Length);
                Assert.Equal(typeof(double), result.GetField("Value")!.FieldType);
                Assert.Equal(typeof(bool), result.GetField("Done")!.FieldType);
                var ctor = result.GetConstructor([typeof(double), typeof(bool)])!;
                var value = ctor.Invoke([12.5d, false]);
                Assert.Equal(12.5d, result.GetField("Value")!.GetValue(value)); Assert.Equal(false, result.GetField("Done")!.GetValue(value));
                value = ctor.Invoke([-0d, true]);
                Assert.Equal(BitConverter.DoubleToInt64Bits(-0d), BitConverter.DoubleToInt64Bits((double)result.GetField("Value")!.GetValue(value)!));
                Assert.Equal(true, result.GetField("Done")!.GetValue(value));
            }
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Fact]
    public void ThreeHelpersReceiveTheirOwnersWithoutTheAggregateOrFeatureFlags()
    {
        Assert.Equal(8, Slots(typeof(EmittedGeneratorRuntime)).Length);
        Assert.Equal(4, Slots(typeof(EmittedStableIteratorResultRuntime)).Length);
        Assert.Null(typeof(EmittedRuntime).GetProperty("Generators")!.SetMethod);
        Assert.True(typeof(EmittedRuntime).GetProperty("StableIteratorResults")!.SetMethod!.IsPrivate);
        foreach (string name in new[] { "EmitGeneratorInterface", "EmitNativeNumberGeneratorInterface", "EmitStableNumberIteratorResult" })
        {
            var parameters = typeof(RuntimeEmitter).GetMethod(name, Members)!.GetParameters();
            Assert.Equal(2, parameters.Length); Assert.Equal(typeof(ModuleBuilder), parameters[0].ParameterType);
            Assert.Equal(name == "EmitStableNumberIteratorResult" ? typeof(EmittedStableIteratorResultRuntime) : typeof(EmittedGeneratorRuntime), parameters[1].ParameterType);
        }
    }

    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"generator_protocol_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}
