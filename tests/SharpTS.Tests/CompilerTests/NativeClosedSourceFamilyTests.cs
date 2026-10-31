using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeClosedSourceFamilyTests
{
    [Fact]
    public void OperandIdentityDistinguishesVectorAndRankOneArrayContexts()
    {
        var comparerType = typeof(NativeClosedSourceFamilies).GetNestedType("OperandTypeComparer", BindingFlags.NonPublic)!;
        var comparer = (IEqualityComparer<Type>)comparerType.GetField("Instance", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Type vector = typeof(int[]), rankOne = typeof(int).MakeArrayType(1);
        Assert.False(comparer.Equals(vector, rankOne));
        Assert.False(comparer.Equals(typeof(List<>).MakeGenericType(vector), typeof(List<>).MakeGenericType(rankOne)));
        Assert.True(comparer.Equals(typeof(List<int[]>), typeof(List<>).MakeGenericType(vector)));
    }

    [Theory]
    [InlineData("class Box<T>{constructor(public value:T){}} console.log(new Box<number>(7).value);", "7\n", 2)]
    [InlineData("class Box<T>{constructor(public value:T){}} console.log(new Box<number>(7).value,new Box<string>('s').value);", "7 s\n", 3)]
    [InlineData("class Box<T>{constructor(public value:T){}} class Child extends Box<number>{} console.log(new Child(7).value);", "7\n", 2)]
    public void DirectOperandsCloseTheWholeSourceFamily(string source, string expected, int contexts)
    {
        var (compiler, saved) = Build(source);
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
        var proof = compiler.NativeClosedFamilies!.Completed;
        Assert.False(proof.UsesLegacy, string.Join("; ", proof.Blockers));
        Assert.False(compiler.NativeRuntimeInitializers!.UsesLegacy);
        Assert.False(compiler.NativeStaticMembers!.UsesLegacy);
        Assert.False(compiler.NativeStaticFields!.UsesLegacy);
        Assert.False(compiler.NativeConstructorMembers!.UsesLegacy);
        Assert.False(compiler.NativeReceiverInitializers!.UsesLegacy);
        Assert.False(compiler.NativePrototypeFields!.UsesLegacy, string.Join("; ", compiler.NativePrototypeFields.Blockers));
        Assert.False(compiler.NativeDeletion!.UsesLegacy);
        var family = Assert.Single(proof.Families);
        Assert.True(contexts == family.Contexts.Count, string.Join("; ", family.Contexts.Select(type => type.ToString())));
        Assert.All(family.Contexts, context => Assert.False(context.ContainsGenericParameters));
        Assert.NotEmpty(proof.Initializers);
        Assert.All(proof.Initializers, initializer => Assert.True(initializer.CompletedBodyLength > initializer.Offset));
        Assert.All(proof.Allocations, allocation => Assert.True(allocation.CompletedBodyLength > allocation.Offset));
    }

    [Fact]
    public void LiteralInitializersRetainDistinctClosedStaticState()
    {
        var (compiler, saved) = Build("class Box<T>{static counter=1;constructor(public value:T){}} console.log(new Box<number>(7).value,new Box<string>('s').value);");
        Assert.False(compiler.NativeRuntimeInitializers!.UsesLegacy);
        var definition = Assert.Single(saved.GetTypes(), type => type.IsGenericTypeDefinition && type.Name.StartsWith("Box", StringComparison.Ordinal));
        var numbers = definition.MakeGenericType(typeof(double));
        var strings = definition.MakeGenericType(typeof(string));
        var initialize = saved.GetType(compiler.NativeRuntimeInitializers.Initialize.DeclaringType!.FullName!)!
            .GetMethod("Initialize", BindingFlags.NonPublic | BindingFlags.Static)!;
        initialize.Invoke(null, [numbers]); initialize.Invoke(null, [strings]);
        var numberField = numbers.GetField("counter", BindingFlags.Public | BindingFlags.Static)!;
        var stringField = strings.GetField("counter", BindingFlags.Public | BindingFlags.Static)!;
        Assert.Equal(1d, numberField.GetValue(null)); Assert.Equal(1d, stringField.GetValue(null));
        numberField.SetValue(null, 17d); stringField.SetValue(null, 23d);
        initialize.Invoke(null, [strings]); initialize.Invoke(null, [numbers]);
        Assert.Equal(17d, numberField.GetValue(null)); Assert.Equal(23d, stringField.GetValue(null));
    }

    [Theory]
    [InlineData("class Box<T>{constructor(public value:T){}} const C:any=Box; console.log(new C(7).value);")]
    [InlineData("class Box<T>{constructor(public value:T){}} console.log(Reflect.construct(Box,[7]).value);")]
    [InlineData("class Box<T>{constructor(public value:T){}} class Target{} console.log(Reflect.construct(Box,[7],Target).value);")]
    [InlineData("class Box<T>{constructor(public value:T){}} const C:any=Box.bind(null); console.log(new C(7).value);")]
    [InlineData("class Other<U>{} class Box<T>{next(){return new Other<T>();}} console.log(new Box<number>());")]
    public void UnknownOrExpandingIngressKeepsTheFamilyUnproved(string source)
    {
        var (compiler, _) = Build(source);
        Assert.True(compiler.NativeClosedFamilies!.Completed.UsesLegacy);
    }

    [Fact]
    public void RuntimeClassCreationRetainsItsUnprovedParentContext()
    {
        var (compiler, _) = Build("class Box<T>{constructor(public value:T){}} const Parent:any=Box; class Child extends Parent{constructor(value:number){super(value);}} console.log((new Child(7) as any).value);");
        var proof = compiler.NativeClosedFamilies!.Completed;
        Assert.True(proof.UsesLegacy);
        Assert.NotEmpty(proof.RuntimeClasses);
        Assert.All(proof.RuntimeClasses, operation =>
        {
            Assert.Same(proof.Module, operation.Create.Module);
            Assert.True(operation.CompletedBodyLength > operation.Offset);
        });
    }

    [Fact]
    public void ActualClosedGenericBaseOperandsRetainSeparateInstanceValues()
    {
        var (compiler, saved) = Build("class Box<T>{constructor(public value:T){}} class Derived<U> extends Box<U>{} console.log(new Derived<number>(7).value,new Derived<string>('s').value);");
        Assert.Equal("7 s\n", TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
        var proof = compiler.NativeClosedFamilies!.Completed;
        Assert.False(proof.UsesLegacy, string.Join("; ", proof.Blockers));
        Assert.Equal(2, proof.Families.Count);
        Assert.All(proof.Families, family => Assert.All(family.Contexts, context => Assert.False(context.ContainsGenericParameters)));
        Assert.False(compiler.NativeRuntimeInitializers!.UsesLegacy);
        Assert.False(compiler.NativeReceiverInitializers!.UsesLegacy);
        Assert.False(compiler.NativePrototypeFields!.UsesLegacy);
    }

    [Theory]
    [InlineData("class Box<T>{next(){return new Box<T[]>();}} console.log(new Box<number>());")]
    [InlineData("class Box<T>{make<U>(){return new Box<U>();}} console.log(new Box<number>());")]
    public void BackendErasureIsCertifiedFromActualClosedOperands(string source)
    {
        var (compiler, _) = Build(source);
        var proof = compiler.NativeClosedFamilies!.Completed;
        Assert.False(proof.UsesLegacy, string.Join("; ", proof.Blockers));
        Assert.All(Assert.Single(proof.Families).Contexts, context => Assert.False(context.ContainsGenericParameters));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarForeignSignaturesDoNotCertifyReflectionEffects(bool select)
    {
        const string declaration = "@DotNetType(\"ExternalSignatureFixture.FamilyReflectionEffects\") declare class Foreign { static Touch():number; } ";
        string source = declaration + "class Box<T>{constructor(public value:T){}} " +
            (select ? "console.log(Foreign.Touch());" : "") + "console.log(new Box<number>(7).value);";
        var (compiler, saved) = Build(source, [Path.Combine(AppContext.BaseDirectory, "ExternalSignatureFixture.dll")]);
        Assert.Equal(select, compiler.NativeClosedFamilies!.Completed.UsesLegacy);
        Assert.Equal(select ? "1\n7\n" : "7\n", TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
    }

    private static (ILCompiler Compiler, Assembly Saved) Build(string source, IReadOnlyList<string>? references = null)
    {
        var statements = new Parser(new Lexer(source).ScanTokens(), DecoratorMode.Legacy).ParseOrThrow();
        var checker = new TypeChecker(); checker.SetDecoratorMode(DecoratorMode.Legacy); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var closure = references is null ? null : NativeManagedReferenceClosure.Capture(AppContext.BaseDirectory, references, null);
        using var loader = closure?.CreateLoader();
        var compiler = new ILCompiler("closed_family_" + Guid.NewGuid().ToString("N"), false, true, null, null, references, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(closure, loader); compiler.Compile(statements, types);
        byte[] bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        return (compiler, Assembly.Load(bytes));
    }
}
