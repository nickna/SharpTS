using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeConstructorActivationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntrinsicAndClosedBclValuesHandleBoundReflectAndNewTargetBeforeFallback(bool native)
    {
        const string source = """
            const nativeCtorMatrix:any=globalThis;
            const M:any=nativeCtorMatrix.Map, S:any=nativeCtorMatrix.Set, P:any=nativeCtorMatrix.Promise;
            const BoundMap:any=M.bind(null);
            const map:any=new BoundMap(); map.set('answer',42);
            const set:any=Reflect.construct(S,[]); set.add(map.get('answer'));
            console.log(map.get('answer'),set.has(42));
            const Alt:any=function Alternate(){};
            const array:any=Reflect.construct(Array,[2],Alt);
            console.log(array.length,Object.getPrototypeOf(array)===Alt.prototype);
            let calls=0;
            const BoundPromise:any=P.bind(null,(resolve:any)=>{calls++;resolve(41);});
            const first:any=new BoundPromise(); console.log(calls,typeof first.then);
            const second:any=Reflect.construct(P,[(resolve:any)=>{calls++;resolve(7);}],Alt);
            console.log(calls,Object.getPrototypeOf(second)===Alt.prototype);
            """;
        var saved = Compile(source, native, out _, "intrinsic-matrix");
        // #3099 records the preserved compiler's alternate-Promise-newTarget
        // prototype defect. This admission slice preserves that existing path.
        Assert.Equal("42 true\n2 true\n1 function\n2 false\n", Run(saved));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceFactoryDefaultsRestBoundReflectAndConstructorValuesKeepTheirEffects(bool native)
    {
        const string source = """
            class Effects {static count=0;}
            class Parent {
                static stamp=++Effects.count;
                value:any; rest:any; target:any;
                constructor(value:any=7,...rest:any[]){this.value=value;this.rest=rest.join(',');this.target=new.target;}
            }
            class Child extends Parent {}
            class Alternate {}
            const C:any=Child;
            const Bound:any=C.bind(null,'bound');
            const first:any=Reflect.construct(C,[undefined,1,2]);
            const second:any=new Bound(3,4);
            const third:any=Reflect.construct(C,['reflect'],Alternate);
            const ctor:any=first.constructor;
            const fourth:any=Reflect.construct(ctor,['again']);
            console.log(first.value,first.rest,first.target===Child);
            console.log(second.value,second.rest,second.target===Child);
            console.log(third.value,third.target===Alternate,Object.getPrototypeOf(third)===Alternate.prototype);
            console.log(fourth.value,fourth.target===Child,Effects.count);
            """;
        var saved = Compile(source, native, out var compiler);
        Assert.Equal("7 1,2 true\nbound 3,4 true\nreflect true true\nagain true 1\n", Run(saved));
        if (!native) { Assert.Null(compiler.NativeConstructorActivation); Assert.Null(saved.GetType("$NativeConstructorActivation")); return; }
        var lookup = compiler.NativeConstructorActivation!;
        Assert.True(lookup.IsComplete); Assert.False(lookup.UsesLegacy);
        Assert.All(lookup.CompletedSnapshot.Owners.Where(o => o.Producers.Any(p => p.Kind == NativeConstructorValueProducerKind.SourceClass)),
            owner => { Assert.True(owner.PublicConstructorsProved); Assert.NotEmpty(owner.PublicConstructors); });
        var construct = saved.GetType("$Runtime")!.GetMethod("ConstructWithNewTarget")!;
        var calls = Instructions(construct).ToArray();
        Assert.True(Array.FindIndex(calls, i => i.Member?.Name == "AdmitType") <
            Array.FindIndex(calls, i => i.Member?.Name == "GetCustomAttributes"));
        Assert.DoesNotContain(calls, i => i.Member?.DeclaringType == typeof(Activator));
    }

    [Fact]
    public void ClosedFrameworkTokensAreActualProducerValuesAndUseLiteralActivatorCalls()
    {
        var saved = Compile("const root:any=globalThis; console.log(typeof root.Map,typeof root.Set);", true, out var compiler);
        var snapshot = compiler.NativeConstructorActivation!.CompletedSnapshot;
        foreach (var type in new[] { typeof(Dictionary<object, object>), typeof(HashSet<object>), typeof(System.Runtime.CompilerServices.ConditionalWeakTable<object, object>) })
        {
            var owner = Assert.Single(snapshot.Owners, o => o.ExactTokenOwner == type);
            Assert.Equal(NativeConstructorValueClosureKind.ExactClosedBclToken, owner.ClosureKind);
            Assert.False(owner.IntrinsicHandled); Assert.True(owner.PublicConstructorsProved);
            Assert.All(owner.Producers, p => Assert.True(p.ValueOffset > 0 && p.Body.ILOffset > p.ValueOffset));
        }
        Assert.False(compiler.NativeConstructorActivation.UsesLegacy);
        var helper = Helper(saved, "Activate");
        Assert.IsType<Dictionary<object, object>>(helper.Invoke(null, [typeof(Dictionary<object, object>), Array.Empty<object>()]));
        var expected = Assert.Throws<MissingMethodException>(() => Activator.CreateInstance(typeof(Dictionary<object, object>), ["wrong"]));
        var failure = Assert.Throws<TargetInvocationException>(() => helper.Invoke(null, [typeof(Dictionary<object, object>), new object[] { "wrong" }]));
        Assert.IsType(expected.GetType(), failure.InnerException);
        Assert.DoesNotContain(Instructions(helper), i => i.Member?.Name == "MakeGenericType");
        Assert.Contains(Instructions(helper), i => i.Code == OpCodes.Ldtoken && i.Member == typeof(Dictionary<object, object>));
    }

    [Fact]
    public void SourceGenericValuesRetainTheirActualNormalizedFamilyAndWholeActivatorOperation()
    {
        var saved = Compile("class Box<T>{constructor(public value:T){}} const C:any=Box; console.log(typeof C);", true, out var compiler);
        var lookup = compiler.NativeConstructorActivation!;
        Assert.True(lookup.UsesLegacy);
        var families = lookup.CompletedSnapshot.Owners.Where(o => o.ExactTokenOwner.Name.StartsWith("Box", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(families);
        Assert.All(families, family =>
        {
            Assert.Equal(NativeConstructorValueClosureKind.UnprovedSourceGenericFamily, family.ClosureKind);
            Assert.NotNull(family.SourceGenericDefinition);
            Assert.All(family.Producers, p => Assert.Equal(NativeConstructorValueProducerKind.SourceClass, p.Kind));
        });
        var definition = Assert.Single(saved.GetTypes(), t => t.IsGenericTypeDefinition && t.Name.StartsWith("Box", StringComparison.Ordinal));
        Assert.DoesNotContain(Instructions(Helper(saved, "AdmitType")), i => i.Code == OpCodes.Ldtoken &&
            i.Member is Type { IsConstructedGenericType: true, ContainsGenericParameters: true });
        foreach (var type in new[] { definition, definition.MakeGenericType(typeof(object)), definition.MakeGenericType(typeof(double)) })
            Assert.Null(Helper(saved, "AdmitType").Invoke(null, [type]));
        var hostile = new HostileType(definition);
        AssertGuestTypeError(saved, () => Helper(saved, "AdmitType").Invoke(null, [hostile]));
        Assert.Equal(0, hostile.Callbacks);
        AssertGuestTypeError(saved, () => Helper(saved, "AdmitType").Invoke(null, [typeof(List<object>)]));
        Assert.DoesNotContain(Instructions(Helper(saved, "Activate")), i => i.Member?.Name == "MakeGenericType");
    }

    [Fact]
    public void LifecycleRequiresCompletedActualOutputAndRejectsAlteredDeclarations()
    {
        var first = new PersistedAssemblyBuilder(new AssemblyName("activation_first"), typeof(object).Assembly).DefineDynamicModule("main");
        var lookup = new NativeConstructorActivationLookup(first, TypeProvider.Runtime);
        Assert.Throws<InvalidOperationException>(() => lookup.CompletedSnapshot);
        Assert.Throws<InvalidOperationException>(() => lookup.ValidateForEmitter(first, false));
        var second = new PersistedAssemblyBuilder(new AssemblyName("activation_second"), typeof(object).Assembly).DefineDynamicModule("main");
        Assert.Throws<InvalidOperationException>(() => new RuntimeEmitter(TypeProvider.Runtime, false, true, false,
            nativeConstructorActivation: lookup).EmitAll(second));
        lookup.Activate.SetReturnType(typeof(void));
        Assert.Throws<InvalidOperationException>(() => lookup.ValidateForEmitter(first, true));
        Compile("class C{}", true, out var compiler);
        var runtime = (EmittedRuntime)typeof(ILCompiler).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(compiler)!;
        Assert.Throws<InvalidOperationException>(() => compiler.NativeConstructorActivation!.CompleteEmission(compiler.NativeSourceOwners!, runtime));
    }

    [Fact]
    public void EarlyAdmissionRefusesHostileAndForgedTypesBeforeMetadataOrInitialization()
    {
        var saved = Compile("class Effects{static count=0;} class Owner{static stamp=++Effects.count;constructor(){Effects.count+=10;}}", true, out _);
        var runtime = saved.GetType("$Runtime")!;
        var construct = runtime.GetMethod("ConstructWithNewTarget")!;
        var owner = NativeSourceTypeLookup.Find(saved, "Owner")!;
        var hostile = new HostileType(owner);
        AssertGuestTypeError(saved, () => construct.Invoke(null, [hostile, Array.Empty<object>(), hostile]));
        Assert.Equal(0, hostile.Callbacks);
        AssertGuestTypeError(saved, () => construct.Invoke(null, [typeof(Foreign), Array.Empty<object>(), typeof(Foreign)]));
        Assert.Equal(0, ForeignEffects.Count);
        Assert.NotNull(construct.Invoke(null, [owner, Array.Empty<object>(), owner]));
        Assert.Equal(11d, NativeSourceTypeLookup.Find(saved, "Effects")!.GetField("count")!.GetValue(null));
    }

    private static class ForeignEffects { internal static int Count; }
    private sealed class Foreign { static Foreign() { ForeignEffects.Count++; } public Foreign() { ForeignEffects.Count += 100; } }
    private sealed class HostileType(Type type) : TypeDelegator(type)
    {
        internal int Callbacks;
        public override bool Equals(object? other) { Callbacks++; return true; }
        public override int GetHashCode() => 0;
        public override object[] GetCustomAttributes(Type type, bool inherit) { Callbacks++; throw new InvalidOperationException("Foreign attributes"); }
        public override bool IsGenericType { get { Callbacks++; throw new InvalidOperationException("Foreign generic metadata"); } }
        public override Type GetGenericTypeDefinition() { Callbacks++; throw new InvalidOperationException("Foreign definition"); }
    }
    private static MethodInfo Helper(Assembly saved, string name) => saved.GetType("$NativeConstructorActivation")!
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
    private static void AssertGuestTypeError(Assembly saved, Action action)
    {
        var failure = Assert.Throws<TargetInvocationException>(action).InnerException!;
        var error = saved.GetType("$Runtime")!.GetMethod("WrapException")!.Invoke(null, [failure])!;
        Assert.Equal("$TypeError", error.GetType().Name);
        Assert.Equal(NativeConstructorActivationLookup.ForeignConstructorMessage, error.GetType().GetProperty("Message")!.GetValue(error));
    }
    private static string Run(Assembly saved) => TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20));
    private static Assembly Compile(string source, bool native, out ILCompiler compiler, string? fixtureName = null)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), d => d.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        compiler = new ILCompiler("activation_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
        byte[] bytes = compiler.SaveToBytes(); using var image = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); Assert.Empty(verifier.Verify(image));
        if (native && fixtureName is not null && Environment.GetEnvironmentVariable("SHARPTS_ACTIVATION_NATIVE_FIXTURE_DIR") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(path); File.WriteAllBytes(Path.Combine(path, fixtureName + ".dll"), bytes);
        }
        return Assembly.Load(bytes);
    }
}
