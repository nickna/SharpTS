using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;
using TI = SharpTS.TypeSystem.TypeInfo;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeCachedUnionConversionTests
{
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const string Producer = "const g=(x:number,y:string)=>[x,y]; console.log(g(1,'a').length);";
    private const string Iterable = """
        let i=0;
        const source:any={ get length(){throw 'length must not be read';},
          [Symbol.iterator](){return {next(){console.log('next',i);if(i++===2)return {done:true};
          return {done:false,value:{valueOf(){console.log('convert');return 7;}}};}};}};
        const value=new Uint8Array(source); console.log(value.length,value[0],value[1]);
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IterableConstructorKeepsCollectionAndConversionOrder(bool native)
    {
        var (compiler, assembly) = Compile(Iterable, native);
        Assert.Equal("next 0\nnext 1\nnext 2\nconvert\nconvert\n2 7 7\n", Run(assembly));
        if (!native) { Assert.Null(compiler.NativeUnionArguments); return; }
        var session = compiler.NativeUnionArguments!;
        Assert.False(session.SourceUsesLegacy, string.Join("; ", session.SourceBlockers));
        Assert.False(session.ReflectionUsesLegacy, string.Join("; ", session.ReflectionBlockers));
        Assert.Empty(session.SourceTargets); Assert.Empty(session.ReflectionTargets);
        Assert.True(session.UsesLegacy); // The owned return union still preserves the public converter.
        Assert.DoesNotContain(Instructions(Query(assembly, "ConvertCachedSource")), i => i.Member?.Name == "GetMethod");
        Assert.DoesNotContain(Instructions(Query(assembly, "ConvertCachedReflection")), i => i.Member?.Name == "GetMethod");
        Assert.Contains(Instructions(Query(assembly, "Convert")), i => i.Member?.Name == "GetMethod");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedFunctionConstructorAlternativesKeepAnonymousMetadataAndSloppyReceiverBinding(bool native)
    {
        var (_, assembly) = Compile("const empty=Function(); const current=Function('return this;'); const receiver={}; console.log(empty.name,empty.length,current.name,current.length,empty()===undefined,current.call(receiver)===receiver,current()===globalThis,empty===current);", native);
        Assert.Equal("anonymous 0 anonymous 0 true true true false\n", Run(assembly));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProcessFixedMethodWrappersKeepTheirIdentityAndFunctionMembers(bool native)
    {
        var (_, assembly) = Compile("const hr=process.hrtime; const memory=process.memoryUsage; console.log(hr===process.hrtime,memory===process.memoryUsage,typeof hr.bigint,typeof memory.rss,typeof process.nextTick,typeof process.report.getReport,typeof process.report.writeReport);", native);
        Assert.Equal("true true function function function function function\n", Run(assembly));
    }

    [Fact]
    public void ArrayInstallerProofRequiresEveryActualTokenAndItsCompletedPrivateCacheWriter()
    {
        var (compiler, assembly) = Compile(Iterable, true);
        var session = compiler.NativeUnionArguments!;
        var proof = Assert.IsType<NativeUnionArgumentConversion.CompletedArrayInstaller>(session.ArrayInstallerProof);
        Assert.True(session.ValidateArrayInstallerProof(proof));
        Assert.NotEmpty(proof.Contributors);
        Assert.True(proof.FactoryOffset > proof.ParameterOffset);
        Assert.Throws<NotSupportedException>(() => ((IList<NativeUnionArgumentConversion.ArrayInstallerContribution>)proof.Contributors).Clear());
        Assert.False(session.ValidateArrayInstallerProof(proof with { Contributors = proof.Contributors.Skip(1).ToArray() }));
        var unknown = proof.Contributors.ToArray(); unknown[0] = unknown[0] with { Token = null };
        Assert.False(session.ValidateArrayInstallerProof(proof with { Contributors = unknown }));
        var forged = proof.Contributors.ToArray(); forged[0] = forged[0] with { Token = forged[0].Token! with { } };
        Assert.False(session.ValidateArrayInstallerProof(proof with { Contributors = forged }));
        Assert.False(session.ValidateArrayInstallerProof(proof with { FactoryOffset = proof.FactoryOffset - 1 }));
        Assert.False(session.ReflectionUsesLegacy, string.Join("; ", session.ReflectionBlockers));
        Assert.DoesNotContain(Instructions(Query(assembly, "ConvertCachedReflection")), i => i.Member?.Name == "GetMethod");
    }

    [Fact]
    public void OriginalOpenFrameworkParametersProveANegativeWithoutGenericSubstitution()
    {
        var (compiler, assembly) = Compile(Producer, true);
        var session = compiler.NativeUnionArguments!;
        Assert.False(session.ReflectionUsesLegacy, string.Join("; ", session.ReflectionBlockers));
        var method = typeof(List<object>).GetMethod("ConvertAll")!;
        Assert.True(method.IsGenericMethodDefinition);
        Assert.Contains(session.ReflectionMembers, member => member.Equals(method));
        var parameters = method.GetParameters();
        Assert.All(parameters, parameter => Assert.False(parameter.ParameterType.IsValueType));
        object?[] values = [null];
        Query(assembly, "ConvertCachedReflection").Invoke(null, [parameters, values]);
        Assert.Null(values[0]);
        Assert.Equal("2\n", Run(assembly));
        Assert.Equal(OpCodes.Ldarg_0, Instructions(Query(assembly, "SelectParameterSource")).First().Code);
    }

    [Fact]
    public void SourceGenericTargetsAndOwnedOpenReflectionMembersRetainTheirWholeLegacyTransport()
    {
        var (compiler, assembly) = Compile("function id<T>(x:T):T{return x;} const f:any=id; class C<T> { choose(x:T):T{return x;} } const c=new C<number>(); console.log(f(42));", true);
        Assert.True(compiler.NativeUnionArguments!.SourceUsesLegacy);
        Assert.True(compiler.NativeUnionArguments.ReflectionUsesLegacy);
        Assert.Contains(Instructions(Query(assembly, "ConvertCachedSource")), i => i.Member?.Name == "Convert");
        Assert.Contains(Instructions(Query(assembly, "ConvertCachedReflection")), i => i.Member?.Name == "Convert");
        Assert.Equal("42\n", Run(assembly));
    }

    [Theory]
    [InlineData("function id<T>(x:T):T{return x;} const f:any=id; console.log(f(42));")]
    [InlineData("class Box<T> { constructor(public value:T) {} read(){return this.value;} } console.log(new Box<number>(42).read());")]
    public void ZeroOwnedFallbackKeepsTheOriginalNoQueryPolicy(string source)
    {
        var (compiler, assembly) = Compile(source, true);
        var session = compiler.NativeUnionArguments!;
        Assert.Empty(session.Declarations); Assert.False(session.UsesLegacy);
        Assert.DoesNotContain(Instructions(Query(assembly, "Convert")), i => i.Member?.Name == "GetMethod");
        if (session.SourceUsesLegacy)
            Assert.Contains(Instructions(Query(assembly, "ConvertCachedSource")), i => i.Member?.Name == "Convert");
        if (session.ReflectionUsesLegacy)
            Assert.Contains(Instructions(Query(assembly, "ConvertCachedReflection")), i => i.Member?.Name == "Convert");
        Assert.Equal("42\n", Run(assembly));
    }

    [Fact]
    public void CompletedQueryAndMemberSnapshotsAreSameModuleImmutableAndPairedWithActualBodies()
    {
        var (compiler, _) = Compile(Producer, true);
        var methods = compiler.NativeMethods!;
        var snapshot = methods.CompletedQueries;
        Assert.Same(methods.Handles.Module, snapshot.Module);
        Assert.False(snapshot.UsesLegacy); Assert.NotEmpty(snapshot.Rows);
        Assert.All(snapshot.Rows, row =>
        {
            Assert.Same(snapshot.Module, row.Producer.Module);
            Assert.Equal(row.Producer.GetILGenerator().ILOffset, row.CompletedBodyLength);
            Assert.InRange(row.CallOffset, 0, row.CompletedBodyLength - 1);
            Assert.True(((TypeBuilder)row.Producer.DeclaringType!).IsCreated());
            Assert.Throws<NotSupportedException>(() => ((IList<MethodInfo>)row.Members).Add(typeof(object).GetMethod("ToString")!));
        });
        Assert.Throws<NotSupportedException>(() => ((IList<NativeCompletedMethodQuery>)snapshot.Rows).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<MethodInfo>)compiler.NativeUnionArguments!.ReflectionMembers).Clear());
        Assert.Throws<InvalidOperationException>(() => compiler.NativeUnionArguments!.EmitReflectionLiteral(
            methods.Handles.Select.GetILGenerator(), methods.Handles.Select));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualSymbolRegistrationsHavePairedTokensAndGenericOwnersKeepTheOriginalEndpoint(bool generic)
    {
        string owner = generic ? "C<T>" : "C";
        string argument = generic ? "T" : "number";
        string creation = generic ? "C<number>" : "C";
        var (compiler, assembly) = Compile($"const key=Symbol('key'); class {owner} {{ [key](value:{argument}){{return value;}} static [Symbol.iterator](){{return 7;}} }} const c:any=new {creation}(); console.log(c[key](42),(C as any)[Symbol.iterator]());", true);
        Assert.Equal("42 7\n", Run(assembly));
        var snapshot = compiler.NativeOwnerClosures!.CompletedSymbols;
        Assert.NotEmpty(snapshot.Rows);
        foreach (var registration in snapshot.Rows)
        {
            Assert.Same(snapshot.Module, registration.Owner.Module);
            Assert.Same(registration.Owner, registration.Method.DeclaringType);
            Assert.InRange(registration.MethodTokenOffset, registration.OwnerTokenOffset + 1, registration.CallOffset - 1);
            Assert.True(registration.CompletedBodyLength > registration.CallOffset);
            Assert.Equal(registration.Method.GetILGenerator().ILOffset, registration.RegisteredBodyLength);
        }
        Assert.Equal(generic, compiler.NativeUnionArguments!.ReflectionUsesLegacy);
        if (generic) Assert.Contains(Instructions(Query(assembly, "ConvertCachedReflection")), i => i.Member?.Name == "Convert");
        Assert.Throws<NotSupportedException>(() => ((IList<NativeCompletedSymbolRegistration>)snapshot.Rows).Clear());
    }

    [Fact]
    public void SymbolParameterProofRejectsForeignAndUnrecordedMethodTokens()
    {
        var assembly = NewAssembly(); var module = assembly.DefineDynamicModule("main");
        var proof = new NativeOwnerClosure(module, TypeProvider.Runtime);
        var owner = module.DefineType("Owner", TypeAttributes.Public);
        var method = owner.DefineMethod("M", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var body = method.GetILGenerator();
        var other = NewAssembly().DefineDynamicModule("other").DefineType("Foreign", TypeAttributes.Public);
        var foreign = other.DefineMethod("M", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        Assert.Throws<InvalidOperationException>(() => NativeOwnerClosure.EmitSymbolOwner(proof, TypeProvider.Runtime, owner, foreign, body));
        var forged = new NativeOwnerClosure.SymbolEmission(owner, method, body, 0);
        body.Emit(OpCodes.Nop);
        Assert.Throws<InvalidOperationException>(() => NativeOwnerClosure.RecordSymbolMethod(proof, body, forged, method, 0));
        Assert.Throws<InvalidOperationException>(() => proof.CompletedSymbols);
    }

    [Theory]
    [InlineData("UInt64")]
    [InlineData("UIntPtr")]
    [InlineData("Unit")]
    [InlineData("CancellationToken")]
    [InlineData("TaskContinuationOptions")]
    [InlineData("Boolean")]
    [InlineData("Double")]
    [InlineData("Int32")]
    public void FixedAsciiNegativePrefixesRemainNegativeInEveryInstalledCulture(string name)
    {
        Assert.True(NativeUnionArgumentConversion.HasCultureStableNonUnionPrefix(name));
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
            Assert.False(culture.CompareInfo.IsPrefix(name, "Union_", CompareOptions.None), culture.Name + ": " + name);
    }

    [Theory]
    [InlineData("Union")]
    [InlineData("Union_X")]
    [InlineData("union_X")]
    [InlineData("UnIoN_X")]
    [InlineData("Un\u00adion_X")]
    [InlineData("Ånion_X")]
    [InlineData("\0Union_X")]
    public void SharedOrIgnorablePrefixesDoNotProduceANegativeCertificate(string name) =>
        Assert.False(NativeUnionArgumentConversion.HasCultureStableNonUnionPrefix(name));

    [Fact]
    public void ActualCompletedUnionOperatorsKeepTheOriginalBinderAndLoopMutationOrder()
    {
        var saved = ConversionFixture(out var parameterMethod);
        var consumer = saved.GetType("Consumer")!.GetMethod(parameterMethod)!;
        var convert = saved.GetType("Conversion")!.GetMethod("Convert")!;
        var firstUnion = consumer.GetParameters()[0].ParameterType;
        object[] values = [42d, "a"];
        var error = Assert.Throws<TargetInvocationException>(() => convert.Invoke(null, [consumer.GetParameters(), values]));
        Assert.IsType<AmbiguousMatchException>(error.InnerException);
        Assert.Equal(firstUnion, values[0].GetType());
        Assert.Equal(42d, values[0].GetType().GetProperty("Value")!.GetValue(values[0]));
        Assert.Equal("a", values[1]);
        object?[] unchanged = [null, null];
        convert.Invoke(null, [consumer.GetParameters(), unchanged]); Assert.All(unchanged, Assert.Null);
        object[] existing = [values[0]]; convert.Invoke(null, [consumer.GetParameters(), existing]); Assert.Same(values[0], existing[0]);
        object[] widened = [7]; convert.Invoke(null, [consumer.GetParameters(), widened]);
        Assert.Equal(7d, widened[0].GetType().GetProperty("Value")!.GetValue(widened[0]));
    }

    [Fact]
    public void PinnedFrameworkNegativeReceiptKeepsTheActualOriginalParameterInventory()
    {
        var assembly = NewAssembly(); var module = assembly.DefineDynamicModule("main");
        var session = new NativeUnionArgumentConversion(module, TypeProvider.Runtime);
        var owner = module.DefineType("Holder", TypeAttributes.Public);
        var method = owner.DefineMethod("Load", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var body = method.GetILGenerator();
        var add = TypeProvider.Runtime.GetMethod(TypeProvider.Runtime.ListOfObject, "Add", typeof(object));
        var receipt = session.EmitListAddParameters(body, add, TypeProvider.Runtime.ListOfObject);
        var token = Assert.IsType<NativeUnionArgumentConversion.FrameworkParameterToken>(receipt.Framework);
        Assert.Same(add, token.Member); Assert.Same(TypeProvider.Runtime.ListOfObject, token.Owner);
        var parameter = Assert.Single(token.Parameters!);
        Assert.Equal(typeof(object), parameter.ParameterType); Assert.Equal(add, parameter.Member);
        Assert.Equal(add.GetParameters()[0].Attributes, parameter.Attributes);
        Assert.True(token.MethodOffset < token.OwnerOffset && token.OwnerOffset < token.ResolveOffset);
        Assert.Throws<NotSupportedException>(() => ((IList<ParameterInfo>)token.Parameters!).Clear());
        body.Emit(OpCodes.Pop);
        var clear = TypeProvider.Runtime.GetMethod(TypeProvider.Runtime.ListOfObject, "Clear");
        var unrelated = session.EmitListAddParameters(body, clear, TypeProvider.Runtime.ListOfObject);
        Assert.Null(unrelated.Framework!.Parameters);
        body.Emit(OpCodes.Pop); body.Emit(OpCodes.Ret); owner.CreateType();
    }

    [Fact]
    public void ForeignUnionShapedAndDelegatingParameterMetadataCannotAuthorizeAnOwnedQuery()
    {
        var assembly = NewAssembly(); var module = assembly.DefineDynamicModule("main");
        var session = new NativeUnionArgumentConversion(module, TypeProvider.Runtime);
        var foreign = module.DefineType("Union_Foreign", TypeAttributes.Public | TypeAttributes.SequentialLayout, typeof(ValueType));
        foreign.CreateType();
        Assert.False(session.TryCompletedParameter(foreign, [], out _));
        Assert.False(session.TryCompletedParameter(new HiddenValueType(foreign), [], out _));
        Assert.False(session.TryCompletedParameter(new TypeDelegator(typeof(string)), [], out _));
        Assert.True(session.TryCompletedParameter(typeof(string), [], out var target)); Assert.Null(target);
    }

    private sealed class HiddenValueType(Type inner) : TypeDelegator(inner)
    {
        protected override bool IsValueTypeImpl() => false;
    }

    [Fact]
    public void ConversionReceiptsRejectAnOperatorBodyChangedAfterItsActualEmission()
    {
        var assembly = NewAssembly(); var module = assembly.DefineDynamicModule("main");
        var mapper = new TypeMapper(module); var generator = new UnionTypeGenerator(mapper);
        var union = generator.GetOrCreateUnionType(new TI.Union([TI.Primitive.Number, TI.String.Shared]), module);
        var method = Assert.IsAssignableFrom<MethodBuilder>(generator.GetImplicitConversion(union, typeof(double)));
        method.GetILGenerator().Emit(OpCodes.Nop);
        generator.FinalizeAllUnionTypes();
        Assert.Throws<InvalidOperationException>(() => generator.RequireCompletedConversions(mapper, module, generator.UnionTypeInterface));
    }

    private static Assembly ConversionFixture(out string parameterMethod)
    {
        var assembly = NewAssembly(); var module = assembly.DefineDynamicModule("main");
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, new RuntimeFeatureDetector().Detect(new Parser(new Lexer("console.log(42);").ScanTokens()).ParseOrThrow()));
        var mapper = new TypeMapper(module); var generator = new UnionTypeGenerator(mapper, runtime.UnionValues.Interface);
        var first = generator.GetOrCreateUnionType(new TI.Union([TI.Primitive.Number, TI.String.Shared]), module);
        var ambiguous = generator.GetOrCreateUnionType(new TI.Union([new TI.StringLiteral("a"), new TI.StringLiteral("b")]), module);
        generator.FinalizeAllUnionTypes();
        var rows = generator.RequireCompletedConversions(mapper, module, runtime.UnionValues.Interface);
        Assert.All(rows, row => Assert.NotEmpty(row.Operators));
        var consumer = module.DefineType("Consumer", TypeAttributes.Public);
        parameterMethod = "Consume";
        var consume = consumer.DefineMethod(parameterMethod, MethodAttributes.Public | MethodAttributes.Static, typeof(void), [first, ambiguous]);
        consume.GetILGenerator().Emit(OpCodes.Ret); consumer.CreateType();
        var owner = module.DefineType("Conversion", TypeAttributes.Public);
        var convert = owner.DefineMethod("Convert", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [typeof(ParameterInfo[]), typeof(object[])]);
        UnionArgumentConversionEmission.EmitOwned(convert.GetILGenerator(), TypeProvider.Runtime, runtime.Errors, rows.Select(row => (Type)row.Owner).ToArray());
        owner.CreateType(); using var bytes = new MemoryStream(); assembly.Save(bytes); return Load(bytes.ToArray());
    }

    private static (ILCompiler, Assembly) Compile(string source, bool native)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), d => d.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = new ILCompiler("cached_union_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types); return (compiler, Load(compiler.SaveToBytes()));
    }
    private static Assembly Load(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream)); return Assembly.Load(bytes);
    }
    private static MethodInfo Query(Assembly assembly, string name) => assembly.GetType("$NativeUnionArgumentConversion")!.GetMethod(name, Static)!;
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName("cached_union_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
    private static string Run(Assembly assembly) => TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20));
}
