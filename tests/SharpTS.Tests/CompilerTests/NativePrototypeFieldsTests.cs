using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativePrototypeFieldsTests
{
    [Fact]
    public void UnprovedResultEvidenceIsUnavailableBeforeCompletion()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("unfinished_results"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var lookup = new NativePrototypeFieldsLookup(module, TypeProvider.Runtime);
        Assert.Throws<InvalidOperationException>(() => lookup.CompletedUnprovedResults);
    }

    [Fact]
    public void CompletedUnprovedResultsExposeActualMethodsAndImmutableCompletionEvidence()
    {
        var (compiler, _) = Build("class C{} const p=Promise.resolve(1);", true);
        var lookup = compiler.NativePrototypeFields!;
        var snapshot = lookup.CompletedUnprovedResults;
        Assert.Same(lookup.Find.Module, snapshot.Module);
        Assert.Same(snapshot, lookup.CompletedUnprovedResults);
        Assert.NotEmpty(snapshot.Producers);
        var expected = typeof(TaskCompletionSource<object>).GetProperty("Task")!.GetGetMethod();
        Assert.All(snapshot.Producers, result =>
        {
            Assert.Equal(expected, result.Producer);
            Assert.True(result.CallOffset >= 0);
            Assert.True(result.CompletedBodyLength > result.CallOffset);
        });
        var mutableView = Assert.IsAssignableFrom<IList<NativeUnprovedCarrierResultProducer>>(snapshot.Producers);
        Assert.Throws<NotSupportedException>(() => mutableView.Clear());
        Assert.NotEmpty(snapshot.Producers);
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-class", "class Counter { value=7; } console.log(new Counter().value);", "7\n"];
        yield return ["dynamic-store", "class C{} const c:any=new C(); (C.prototype as any).value=7; console.log(c.value,c.absent);", "7 undefined\n"];
        yield return ["descriptor-receiver-and-setter-only", """
            class C{value=9;} const c:any=new C();
            Object.defineProperty(C.prototype,"getter",{get:function(this:any){return this.value;}});
            Object.defineProperty(C.prototype,"setter",{set:function(v:any){}});
            console.log(c.getter,c.setter,c.absent);
            """, "9 undefined undefined\n"];
        yield return ["source-instance-installed", """
            class P{value=8;} class C{}
            const p:any=new P(),c:any=new C(); p.extra=7;
            Object.setPrototypeOf(C.prototype,p);
            console.log(c.value,c.extra,c.absent);
            """, "8 7 undefined\n"];
        yield return ["generic-base", "class P<T>{} class C extends P<number>{} const c:any=new C(); (Object.getPrototypeOf(C.prototype) as any).value=4; console.log(c.value,c.absent);", "4 undefined\n"];
        yield return ["abstract-prototype", "abstract class P{} class C extends P{} const c:any=new C(); (Object.getPrototypeOf(C.prototype) as any).value=4; console.log(c.value,c.absent);", "4 undefined\n"];
        foreach (var (name, prototype) in new (string, string)[]
        {
            ("object", "{value:3}"),
            ("accessor-object", "{get value(){return 3;}}"),
            ("object-create", "Object.create({value:3})"),
            ("null-object", "Object.create(null)"),
            ("function", "function(){}"),
            ("arrow", "()=>1"),
            ("bound-function", "(function(){}).bind(null)"),
            ("function-prototype", "Function.prototype"),
            ("source-constructor", "C"),
            ("builtin-constructor", "Number"),
            ("boxed-number", "new Number(2)"),
            ("boxed-boolean", "new Boolean(true)"),
            ("boxed-string", "new String('x')"),
            ("numeric-array", "[1,2]"),
            ("mixed-array", "[1,'x']"),
            ("map", "new Map<any,any>()"),
            ("set", "new Set<any>()"),
            ("regexp", "/x/"),
            ("date", "new Date(0)"),
            ("error", "new Error('x')"),
            ("promise", "Promise.resolve(1)"),
            ("typed-array", "new Uint8Array([1])"),
            ("buffer", "Buffer.from('x')"),
            ("generator", "(function*(){yield 1;})()"),
            ("iterator", "[1,2].values()")
        })
            yield return [name, $"class C{{}} const Base:any=C; class D extends Base{{read(){{return super.absent;}}}} const p:any={prototype}; Object.setPrototypeOf(D.prototype,p); console.log(new D().read());", "undefined\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row => new[] { false, true }
        .Select(native => new object[] { row[0], row[1], row[2], native }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ActualPrototypeCarriersPreserveThePrivateStoreQuery(string name, string source, string expected, bool native)
    {
        var (compiler, saved) = Build(source, native);
        if (native && name == "iterator")
        {
            // #3121: immutable83e has this separate downstream property-query
            // refusal after the private-store query succeeds. Keep it explicit.
            var error = Assert.ThrowsAny<Exception>(() => TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
            Assert.Contains(NativePropertyLookupCatalog.ForeignOwnerMessage, error.Message);
        }
        else Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
        if (!native) { Assert.Null(compiler.NativePrototypeFields); return; }
        Assert.True(compiler.NativePrototypeFields!.IsComplete);
        if (name is "generic-base" or "promise") Assert.True(compiler.NativePrototypeFields.UsesLegacy);
        if (name == "ordinary-class") Assert.False(compiler.NativePrototypeFields.UsesLegacy);
        // Prototype mutation conservatively enables Promise protocol bodies.
        // Their actual Task results require legacy until subclass closure is proved.
        Assert.All(compiler.NativePrototypeFields.Blockers, blocker => Assert.True(
            blocker.StartsWith("Unproved actual carrier result:", StringComparison.Ordinal) ||
            blocker.StartsWith("Unproved owned generic prototype family:", StringComparison.Ordinal) ||
            blocker.StartsWith("Unproved owned prototype carrier:", StringComparison.Ordinal), blocker));
    }

    [Theory]
    [InlineData("new Map<any,any>()")]
    [InlineData("new Set<any>()")]
    [InlineData("new Date(0)")]
    [InlineData("/x/")]
    [InlineData("new Error('x')")]
    [InlineData("[1,2]")]
    [InlineData("Buffer.from('x')")]
    [InlineData("new Uint8Array([1])")]
    [InlineData("new Object()")]
    [InlineData("new C()")]
    [InlineData("C")]
    [InlineData("function(){}")]
    public void FiniteRowsRequireActualCarrierCreationAndKeepMissingResults(string expression)
    {
        var (compiler, saved) = Build($"class C{{}} class Holder{{static value:any={expression};}}", true);
        Assert.False(compiler.NativePrototypeFields!.UsesLegacy, string.Join("; ", compiler.NativePrototypeFields.Blockers));
        object value = NativeSourceTypeLookup.Find(saved, "Holder")!.GetField("value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var query = saved.GetType("$NativePrototypeFieldsLookup")!.GetMethod("Find", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(value.GetType().GetMethod("$EnsureFields", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            query.Invoke(null, [value.GetType()]));
    }

    [Fact]
    public void FiniteQueryRetainsTheLiteralBinderAndRefusesForeignMetadataBeforeCallbacks()
    {
        var (_, saved) = Build("class C{}", true);
        var query = saved.GetType("$NativePrototypeFieldsLookup")!.GetMethod("Find", BindingFlags.NonPublic | BindingFlags.Static)!;
        var owner = NativeSourceTypeLookup.Find(saved, "C")!;
        Assert.Equal(owner.GetMethod("$EnsureFields", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            query.Invoke(null, [owner]));
        var hostile = new HostileType();
        var error = Assert.Throws<TargetInvocationException>(() => query.Invoke(null, [hostile]));
        Assert.Contains(NativePrototypeFieldsLookup.UnsupportedMessage, error.InnerException!.Message);
        Assert.Equal(0, hostile.Callbacks);
        Assert.Contains(Instructions(query), instruction => instruction.Member?.Name == "GetMethod");
    }

    [Fact]
    public void MissingIngressRetainsTheWholeLegacyQueryAndItsMetadataBehavior()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("prototype_legacy_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var types = TypeProvider.Runtime;
        var sources = new NativeSourceOwnerCatalog(module, types);
        var activation = new NativeConstructorActivationLookup(module, types);
        var lookup = new NativePrototypeFieldsLookup(module, types);
        var runtime = new RuntimeEmitter(types, false, true, false,
            nativeConstructorActivation: activation, nativePrototypeFields: lookup)
            .EmitAll(module, RuntimeFeatureSet.EmitEverything(), FunctionEmissionPolicy.Full, sources);
        var generics = new ClassGenericParameterRegistry(); generics.CompleteEmission([]);
        sources.CompleteEmission(generics, []); activation.CompleteEmission(sources, runtime);
        lookup.CompleteEmission(sources, null, runtime, typeof(object).GetType());
        Assert.True(lookup.UsesLegacy);
        Assert.Contains(lookup.Blockers, blocker => blocker.Contains("CLR ingress"));
        using var image = new MemoryStream(); assembly.Save(image);
        byte[] bytes = image.ToArray();
        using var stream = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream));
        var query = Assembly.Load(bytes).GetType("$NativePrototypeFieldsLookup")!
            .GetMethod("Find", BindingFlags.NonPublic | BindingFlags.Static)!;
        var hostile = new HostileType();
        var error = Assert.Throws<TargetInvocationException>(() => query.Invoke(null, [hostile]));
        Assert.Equal("foreign query", error.InnerException!.Message);
        Assert.Equal(1, hostile.Callbacks);
    }

    private static (ILCompiler Compiler, Assembly Saved) Build(string source, bool native)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = new ILCompiler("prototype_fields_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, types);
        byte[] bytes = compiler.SaveToBytes();
        using var stream = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream));
        return (compiler, Assembly.Load(bytes));
    }

    private sealed class HostileType() : TypeDelegator(typeof(object))
    {
        internal int Callbacks;
        public override Type UnderlyingSystemType { get { Callbacks++; throw new InvalidOperationException("foreign metadata"); } }
        public override MethodInfo[] GetMethods(BindingFlags flags) { Callbacks++; throw new InvalidOperationException("foreign query"); }
        protected override MethodInfo? GetMethodImpl(string name, BindingFlags flags, Binder? binder,
            CallingConventions convention, Type[]? types, ParameterModifier[]? modifiers)
        { Callbacks++; throw new InvalidOperationException("foreign query"); }
        public override bool Equals(object? other) { Callbacks++; throw new InvalidOperationException("foreign equality"); }
        public override int GetHashCode() => base.GetHashCode();
    }
}
