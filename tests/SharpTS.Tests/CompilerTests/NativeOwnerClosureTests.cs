using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeOwnerClosureTests
{
    private const BindingFlags Methods = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary", "class Counter { value=7; } console.log(new Counter().value);", "7\n"];
        yield return ["source-static-symbols-and-generic-base", """
            const events:string[]=[];
            function note(s:string){events.push(s);return s;}
            class Base<T> {
                static value=note("base");
                static get [Symbol.toStringTag](){return "tag";}
                static [Symbol.iterator](){return "method";}
            }
            class Child<U> extends Base<U>{static child=note("child");}
            const C:any=Child;
            console.log(C.value,C.child,C[Symbol.toStringTag],C[Symbol.iterator]());
            console.log(Object.getPrototypeOf(Child)===Base,events.join(","));
            """, "base child tag method\ntrue base,child\n"];
        yield return ["computed-key-canonical-storage", """
            const key=Symbol("key"),events:string[]=[];
            class Box<T> { static [key]=7; static value=8; }
            const C:any=Box;
            console.log(C[key],C.value);
            C.value=9;
            console.log(C.value,Object.getOwnPropertyDescriptor(C,"value")!.value);
            """, "7 8\n9 9\n"];
        yield return ["constructor-base-and-instance-identity", """
            class Base<T>{value=3;}
            class Child<U> extends Base<U>{}
            const C:any=Child,instance:any=new C();
            console.log(Object.getPrototypeOf(Child)===Base,instance instanceof Base,instance instanceof Child,
                Object.getPrototypeOf(instance)===Child.prototype);
            """, "true true true true\n"];
        yield return ["promise-species-getter-and-expando", """
            let trace="";
            class P<T> extends Promise<T>{
                static get [Symbol.species](){trace+="g";return Promise;}
            }
            const p=new P<number>(resolve=>resolve(4));
            p.then(value=>console.log(value,trace));
            """, "4 g\n"];
        yield return ["initialization-order-and-thrown-value", """
            const events:string[]=[],token={};
            function fail(){events.push("fail");throw token;}
            class Parent { static x=events.push("parent"); }
            class Child extends Parent { static y=events.push("child"); }
            console.log((Child as any).x,(Child as any).y,events.join(","));
            try { class Bad { static x=fail(); } }
            catch(error){console.log(error===token,events.join(","));}
            """, "1 2 parent,child\ntrue parent,child,fail\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row => new[] { false, true }
        .Select(native => new object[] { row[0], row[1], row[2], native }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SavedSourceOperationsKeepTheirRuntimeSemantics(string name, string source, string expected, bool native)
    {
        var (compiler, saved) = Build(source, native);
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
        if (!native) { Assert.Null(saved.GetType("<>NativeOwnerClosure")); return; }
        Assert.True(compiler.NativeOwnerClosures!.IsComplete);
        if (name == "ordinary") Assert.Empty(compiler.NativeOwnerClosures.Blockers);
    }

    [Fact]
    public void RealClosedOwnersKeepIdentityWithoutInitializingTypesAndHostileTypesHaveNoCallbacks()
    {
        var (_, saved) = Build("class C{}", true);
        var helper = saved.GetType("<>NativeOwnerClosure")!;
        foreach (string name in new[] { "StaticMemberOwner", "StaticStorageOwner", "SymbolOwner" })
        {
            MethodInfo method = helper.GetMethod(name, Methods)!;
            Assert.Same(typeof(WithInitializer), method.Invoke(null, [typeof(WithInitializer)]));
            Assert.Equal(0, State.Count);
            var hostile = new HostileType();
            var refusal = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [hostile]));
            Assert.Contains(NativeOwnerClosure.ForeignOwnerMessage, refusal.InnerException!.Message);
            Assert.Equal(0, hostile.Callbacks);
            var open = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [typeof(ForeignGeneric<>)]));
            Assert.Contains(NativeOwnerClosure.ForeignOwnerMessage, open.InnerException!.Message);
        }
        RuntimeHelpers.RunClassConstructor(typeof(WithInitializer).TypeHandle);
        Assert.Equal(1, State.Count);
    }

    [Fact]
    public void GeneralHelpersStillCloseForeignOwnersAndCanonicalizeClosedForeignGenerics()
    {
        var (_, saved) = Build("class C{}", true);
        MethodInfo resolve = saved.GetType("<>NativeSourceOwnerCatalog")!.GetMethod("Resolve", Methods)!;
        MethodInfo symbols = saved.GetType("$Runtime")!.GetMethod("SymbolClosedOwner", Methods)!;
        Assert.Equal(typeof(ForeignGeneric<object>), resolve.Invoke(null, [typeof(ForeignGeneric<string>)]));
        Assert.Equal(typeof(ForeignGeneric<object>), symbols.Invoke(null, [typeof(ForeignGeneric<>)]));
        Assert.Equal(typeof(ForeignGeneric<string>), symbols.Invoke(null, [typeof(ForeignGeneric<string>)]));
        Assert.Contains(Instructions(resolve), instruction => instruction.Member?.Name == "MakeGenericType");
        Assert.Contains(Instructions(symbols), instruction => instruction.Member?.Name == "MakeGenericType");
    }

    [Fact]
    public void ClosedGenericStaticOwnersRetainTheirDistinctStorage()
    {
        var (compiler, saved) = Build("class Box<T>{static value=1;} console.log((Box as any).value);", true);
        Assert.False(compiler.NativeOwnerClosures!.UsesLegacy);
        Assert.Equal("1\n", TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
        Type definition = saved.GetTypes().Single(type => type.Name == "Box" && type.IsGenericTypeDefinition);
        Type first = definition.MakeGenericType(typeof(string)), second = definition.MakeGenericType(typeof(double));
        RuntimeHelpers.RunClassConstructor(first.TypeHandle); RuntimeHelpers.RunClassConstructor(second.TypeHandle);
        FieldInfo firstField = first.GetField("value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        FieldInfo secondField = second.GetField("value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        firstField.SetValue(null, 41d); secondField.SetValue(null, 42d);
        Type helper = saved.GetType("<>NativeOwnerClosure")!;
        foreach (string name in new[] { "StaticMemberOwner", "StaticStorageOwner", "SymbolOwner" })
        {
            MethodInfo method = helper.GetMethod(name, Methods)!;
            Assert.Same(first, method.Invoke(null, [first])); Assert.Same(second, method.Invoke(null, [second]));
        }
        Assert.Equal(41d, firstField.GetValue(null)); Assert.Equal(42d, secondField.GetValue(null));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void MissingCompletedIngressOrActualProducerLedgerKeepsTheWholeLegacyOperation(bool suppliedIngress)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("unproved_closure_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var sources = new NativeSourceOwnerCatalog(module, TypeProvider.Runtime);
        var closure = new NativeOwnerClosure(module, TypeProvider.Runtime);
        var runtime = new RuntimeEmitter(TypeProvider.Runtime, false, true, false, nativeOwnerClosure: closure)
            .EmitAll(module, RuntimeFeatureSet.EmitEverything(), FunctionEmissionPolicy.Full, sources);
        var generics = new ClassGenericParameterRegistry(); generics.CompleteEmission([]); sources.CompleteEmission(generics, []);
        closure.CompleteEmission(sources, new NativeConstructorValueProducerSnapshot(module, [], []),
            suppliedIngress ? new NativeClrAdmission.CompletedIngressProof(module) : null, runtime);
        Assert.True(closure.StaticMemberUsesLegacy); Assert.True(closure.StaticStorageUsesLegacy); Assert.True(closure.SymbolUsesLegacy);
        Assert.Contains(closure.Blockers, blocker => blocker.Contains("actual emitted constructor-value producer ledger"));
        if (!suppliedIngress) Assert.Contains(closure.Blockers, blocker => blocker.Contains("CLR ingress/escape"));
        using var image = new MemoryStream(); assembly.Save(image);
        var saved = LoadVerified(image.ToArray());
        var helper = saved.GetType("<>NativeOwnerClosure")!;
        Assert.Contains(Instructions(helper.GetMethod("StaticMemberOwner", Methods)!), instruction => instruction.Member?.Name == "Resolve");
        Assert.Contains(Instructions(helper.GetMethod("StaticStorageOwner", Methods)!), instruction => instruction.Member?.Name == "Resolve");
        Assert.Contains(Instructions(helper.GetMethod("SymbolOwner", Methods)!), instruction => instruction.Member?.Name == "SymbolClosedOwner");
        foreach (string name in new[] { "StaticMemberOwner", "StaticStorageOwner", "SymbolOwner" })
        {
            var hostile = new HostileType();
            var failure = Assert.Throws<TargetInvocationException>(() => helper.GetMethod(name, Methods)!.Invoke(null, [hostile]));
            Assert.Equal("foreign metadata", failure.InnerException!.Message);
            Assert.Equal(1, hostile.Callbacks);
        }
    }

    [Fact]
    public void NongenericSourceOwnerWithForeignGenericBaseRetainsTheWholeOperation()
    {
        var types = TypeProvider.Runtime;
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("foreign_base_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var sources = new NativeSourceOwnerCatalog(module, types);
        var activation = new NativeConstructorActivationLookup(module, types);
        var closure = new NativeOwnerClosure(module, types);
        var runtime = new RuntimeEmitter(types, false, true, false,
            nativeConstructorActivation: activation, nativeOwnerClosure: closure)
            .EmitAll(module, RuntimeFeatureSet.EmitEverything(), FunctionEmissionPolicy.Full, sources);
        var owner = module.DefineType("NongenericOwner", TypeAttributes.Public | TypeAttributes.Abstract, typeof(ForeignBase<int>));
        NativeSourceOwnerCatalog.Implement(owner, sources.Contract, sources);
        var getter = owner.DefineMethod(runtime.ObjectFields.FieldsGetter.Name,
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.SpecialName,
            runtime.ObjectFields.FieldsGetter.ReturnType, Type.EmptyTypes);
        getter.GetILGenerator().Emit(OpCodes.Ldnull); getter.GetILGenerator().Emit(OpCodes.Ret);
        owner.DefineMethodOverride(getter, runtime.ObjectFields.FieldsGetter);
        owner.CreateType();
        var holder = module.DefineType("Values", TypeAttributes.Public);
        var value = holder.DefineMethod("Read", MethodAttributes.Public | MethodAttributes.Static, typeof(Type), Type.EmptyTypes);
        NativeConstructorActivationLookup.EmitValue(activation, types, value.GetILGenerator(), owner,
            NativeConstructorValueProducerKind.SourceClass, value);
        value.GetILGenerator().Emit(OpCodes.Ret); holder.CreateType();
        var parameters = new ClassGenericParameterRegistry(); parameters.Declare(owner, []); parameters.CompleteEmission([owner]);
        sources.CompleteEmission(parameters, [owner]); activation.CompleteEmission(sources, runtime);
        Assert.False(owner.IsGenericType); Assert.Empty(sources.CanonicalMappings);
        closure.CompleteEmission(sources, activation.CompletedSnapshot,
            new NativeClrAdmission.CompletedIngressProof(module), runtime);
        Assert.Contains(closure.Blockers, blocker => blocker.Contains("Unproved generic constructor base normalization"));
        Assert.True(closure.StaticMemberUsesLegacy); Assert.True(closure.StaticStorageUsesLegacy); Assert.True(closure.SymbolUsesLegacy);
        using var image = new MemoryStream(); assembly.Save(image);
        var saved = LoadVerified(image.ToArray());
        var helper = saved.GetType("<>NativeOwnerClosure")!;
        Assert.Contains(Instructions(helper.GetMethod("StaticMemberOwner", Methods)!), instruction => instruction.Member?.Name == "Resolve");
        Assert.Contains(Instructions(helper.GetMethod("SymbolOwner", Methods)!), instruction => instruction.Member?.Name == "SymbolClosedOwner");
    }

    private static (ILCompiler Compiler, Assembly Saved) Build(string source, bool native)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = new ILCompiler("owner_closure_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, types);
        return (compiler, LoadVerified(compiler.SaveToBytes()));
    }

    private static Assembly LoadVerified(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream)); return Assembly.Load(bytes);
    }

    private static class State { internal static int Count; }
    private sealed class WithInitializer { static WithInitializer() { State.Count++; } }
    private sealed class ForeignGeneric<T> { }
    public class ForeignBase<T> { }
    private sealed class HostileType() : TypeDelegator(typeof(object))
    {
        internal int Callbacks;
        public override bool ContainsGenericParameters { get { Callbacks++; throw new InvalidOperationException("foreign metadata"); } }
        public override bool IsGenericType { get { Callbacks++; throw new InvalidOperationException("foreign metadata"); } }
        public override Type UnderlyingSystemType { get { Callbacks++; throw new InvalidOperationException("foreign metadata"); } }
        public override Type GetGenericTypeDefinition() { Callbacks++; throw new InvalidOperationException("foreign metadata"); }
        public override bool Equals(object? other) { Callbacks++; throw new InvalidOperationException("foreign equality"); }
        public override int GetHashCode() => base.GetHashCode();
    }
}
