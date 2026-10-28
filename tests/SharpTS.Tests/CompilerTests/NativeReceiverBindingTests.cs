using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeReceiverBindingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForeignPublicFieldsKeepLegacyBehaviorOnlyInOrdinaryOutput(bool native)
    {
        var assembly = Compile("console.log(42);", native);
        object marker = new();
        var target = new ForeignReceiver();
        if (native)
        {
            var error = Assert.Throws<TargetInvocationException>(() => Bind(assembly, target, marker)).InnerException!;
            var guest = assembly.GetType("$Runtime")!.GetMethod("WrapException")!.Invoke(null, [error])!;
            Assert.Equal("$TypeError", guest.GetType().Name);
            Assert.Equal(NativeReceiverBinding.ForeignMessage, guest.GetType().GetProperty("Message")!.GetValue(guest));
            Assert.Null(target.@this);
        }
        else { Bind(assembly, target, marker); Assert.Same(marker, target.@this); }
        Bind(assembly, null, marker);
    }

    [Fact]
    public void ExactFrameworkValuesUseLiteralLookupsAndForeignSubclassesRefuse()
    {
        var assembly = Compile("console.log(42);", true);
        object[] known = [new object(), Array.Empty<object>(), new List<object>(), new Dictionary<string, object>(),
            new Dictionary<object, object>(), new HashSet<object>(), (Func<object[], object>)(_ => new object()), "x", true,
            1d, new System.Numerics.BigInteger(1), typeof(string), new Exception("x"), new System.Net.Sockets.SocketException(10048),
            new ConditionalWeakTable<object, object>(), new WeakReference<object>(new object())];
        foreach (var target in known) Bind(assembly, target, new object());
        foreach (var target in new object[] { new ForeignException(), new ForeignList(), new ForeignDictionary() })
            Assert.Throws<TargetInvocationException>(() => Bind(assembly, target, new object()));
        var body = Instructions(assembly.GetType("$TSFunction")!.GetMethod("BindThis")!);
        Assert.DoesNotContain(body, instruction => instruction.Member?.Name == "GetField");
        Assert.Contains(body, instruction => instruction.Member?.DeclaringType?.Name == "$NativeReceiverBinding");
        var helper = assembly.GetType("$NativeReceiverBinding")!.GetMethod("Bind", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(28, Instructions(helper).Count(instruction => instruction.Member?.Name == "GetField"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedLexicalReceiverFieldsAndSuspendedFunctionsKeepTheirBehavior(bool native)
    {
        const string source = """
            class Box<T> {
              constructor(public value:T) {}
              make(){ return () => this.value; }
              async later(){ await Promise.resolve(1); const f=()=>this.value; return f(); }
              *values(){ yield 0; const f=()=>this.value; yield f(); }
            }
            const a=new Box(7);const b=new Box('text');
            console.log(a.make()(),b.make()());
            console.log(a.values().next().value);
            console.log([...b.values()].join(','));
            a.later().then((x:any)=>console.log(x));
            """;
        var assembly = Compile(source, native);
        Assert.Equal("7 text\n0\n0,text\n7\n", TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20)));
        var fields = assembly.GetTypes().SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(field => field.Name == "this").ToArray();
        Assert.NotEmpty(fields);
        if (native)
            foreach (var field in fields)
            {
                var owner = field.DeclaringType!;
                if (owner.ContainsGenericParameters) owner = owner.MakeGenericType(owner.GetGenericArguments().Select(_ => typeof(object)).ToArray());
                var instance = Activator.CreateInstance(owner)!; object marker = new();
                Bind(assembly, instance, marker); Assert.Same(marker, owner.GetField("this")!.GetValue(instance));
            }
    }

    [Theory]
    [InlineData("bytes", false)]
    [InlineData("artifacts", false)]
    [InlineData("file", false)]
    [InlineData("artifacts", true)]
    public void UnregisteredFinalFieldsRefuseEverySaveBoundaryBeforeOutput(string api, bool symbols)
    {
        var compiler = NewCompiler("console.log(42);", true);
        compiler.EmitDebugSymbols = symbols;
        var module = (ModuleBuilder)typeof(ILCompiler).GetField("_moduleBuilder", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(compiler)!;
        var extra = module.DefineType("UnregisteredReceiver");
        extra.DefineField("this", typeof(object), FieldAttributes.Public);
        extra.CreateType();
        string path = Path.Combine(Path.GetTempPath(), "receiver_certificate_" + Guid.NewGuid().ToString("N") + ".dll");
        var error = Assert.Throws<CompileException>(() =>
        {
            if (api == "bytes") compiler.SaveToBytes();
            else if (api == "artifacts") compiler.SaveArtifacts();
            else compiler.Save(path);
        });
        Assert.Equal(DiagnosticCode.NativeGuestReceiverCertificate, error.Diagnostic.Code);
        Assert.False(File.Exists(path)); Assert.False(File.Exists(Path.ChangeExtension(path, ".pdb")));
    }

    [Fact]
    public void ForeignBaseAndLateOrUnusualProducersCannotReceiveAnAbsenceCertificate()
    {
        var compiler = NewCompiler("console.log(42);", true);
        var module = (ModuleBuilder)typeof(ILCompiler).GetField("_moduleBuilder", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(compiler)!;
        var extra = module.DefineType("ForeignBaseReceiver", TypeAttributes.Abstract, typeof(Stream)); extra.CreateType();
        Assert.Throws<CompileException>(() => compiler.SaveToBytes());
        var session = new NativeReceiverBinding(NewAssembly().DefineDynamicModule("main"), TypeProvider.Runtime);
        var own = session.Bind.Module as ModuleBuilder;
        var invalid = own!.DefineType("ReadonlyReceiver").DefineField("this", typeof(object), FieldAttributes.Public | FieldAttributes.InitOnly);
        Assert.Throws<InvalidOperationException>(() => session.Register(invalid));
        var wrongModule = NewAssembly().DefineDynamicModule("other").DefineType("Foreign").DefineField("this", typeof(object), FieldAttributes.Public);
        Assert.Throws<InvalidOperationException>(() => session.Register(wrongModule));
        var wrongType = own.DefineType("StringReceiver").DefineField("this", typeof(string), FieldAttributes.Public);
        Assert.Throws<InvalidOperationException>(() => session.Register(wrongType));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DictionaryAcquisitionPreservesCapturesReceiverIdentityAndArgumentRules(bool native)
    {
        const string source = """
            let count=1;
            const fn=function(x:any=1,...rest:any[]){ return this.value+x+rest.length+count; };
            const first:any={value:5,fn};const second:any={value:9,fn};
            console.log(first.fn(undefined,2),second.fn(2,3,4),first.fn===fn);
            count=2;
            console.log(first.fn.call({value:12},2),second.fn===first.fn);
            const nested:any={method:first.hasOwnProperty};
            console.log(nested.method('method'));
            """;
        var saved = Compile(source, native);
        Assert.Equal("8 14 true\n16 true\ntrue\n", TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void ForeignGenericBaseDependingOnOwnedParametersDoesNotGetAClosedRootCertificate()
    {
        var compiler = NewCompiler("console.log(42);", true);
        var module = (ModuleBuilder)typeof(ILCompiler).GetField("_moduleBuilder", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(compiler)!;
        var extra = module.DefineType("DependentForeignBase`1", TypeAttributes.Abstract);
        var parameter = extra.DefineGenericParameters("T")[0];
        extra.SetParent(typeof(Dictionary<,>).MakeGenericType(typeof(string), parameter)); extra.CreateType();
        var error = Assert.Throws<CompileException>(() => compiler.SaveToBytes());
        Assert.Equal(DiagnosticCode.NativeGuestReceiverCertificate, error.Diagnostic.Code);
    }

    [Fact]
    public void PrivateRuntimeTypeScopeCannotBeRedirectedByAForeignLookalike()
    {
        var foreign = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ForeignRuntimeType"), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("main").DefineType("System.RuntimeType", TypeAttributes.Public).CreateType()!;
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var owner = module.DefineType("ForeignToken"); var method = owner.DefineMethod("Read", MethodAttributes.Static, typeof(Type), Type.EmptyTypes);
        var il = method.GetILGenerator(); il.Emit(OpCodes.Ldtoken, foreign); il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!); il.Emit(OpCodes.Ret); owner.CreateType();
        using var stream = new MemoryStream(); builder.Save(stream);
        Assert.Throws<InvalidOperationException>(() => new NativeRuntimeTypeReferenceIndex(PEPacker.EmbeddedReferenceAssemblyIndex.Default,
            typeof(object).GetType(), stream.ToArray()));
        Assert.Throws<ArgumentException>(() => new NativeRuntimeTypeReferenceIndex(PEPacker.EmbeddedReferenceAssemblyIndex.Default,
            foreign, stream.ToArray()));
    }

    [Fact]
    public void ExactPrivateRuntimeTypeRemainsScopedToCoreLibAndDefaultEmitterStaysLegacy()
    {
        var assembly = Compile("console.log(42);", true);
        Assert.NotNull(assembly.GetType("$NativeReceiverBinding"));
        var builder = NewAssembly();
        new RuntimeEmitter(TypeProvider.Runtime, false, true).EmitAll(builder.DefineDynamicModule("main"), RuntimeFeatureSet.EmitEverything());
        using var stream = new MemoryStream(); builder.Save(stream); var legacy = VerifyLoad(stream.ToArray());
        Assert.Null(legacy.GetType("$NativeReceiverBinding"));
        Assert.Contains(Instructions(legacy.GetType("$TSFunction")!.GetMethod("BindThis")!), instruction => instruction.Member?.Name == "GetField");
        var target = new ForeignReceiver(); object marker = new(); Bind(legacy, target, marker); Assert.Same(marker, target.@this);
    }

    [Fact]
    public void ImportedClosureProducerIsCompletedAndCertifiedWithTheWholeOutput()
    {
        string directory = Path.Combine(Path.GetTempPath(), "receiver_modules_" + Guid.NewGuid().ToString("N"));
        string entry = Path.Combine(directory, "main.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(directory, "lib.ts")] = "export class Box { value=17; make(){ return ()=>this.value; } }",
            [entry] = "import { Box } from './lib'; const box=new Box(); console.log(box.make()());"
        };
        var resolver = new ModuleResolver(entry, files); var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry));
        var checker = new TypeChecker(); var types = checker.CheckModules(modules, resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var compiler = new ILCompiler("receiver_modules_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); compiler.CompileModules(modules, resolver, types);
        var saved = VerifyLoad(compiler.SaveArtifacts().Assembly);
        Assert.Equal("17\n", TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void GenericDirectSetterAndFinalImageMutationAreCheckedWithoutOpenTypeTokens()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var session = new NativeReceiverBinding(module, TypeProvider.Runtime);
        var runtime = new RuntimeEmitter(TypeProvider.Runtime, false, true).EmitAll(module,
            new RuntimeFeatureDetector().Detect(new Parser(new Lexer("console.log(42);").ScanTokens()).ParseOrThrow()),
            FunctionEmissionPolicy.Full with { NativeReceiverBinder = session.Bind });
        var owner = module.DefineType("GenericReceiver`1", TypeAttributes.Public);
        owner.DefineGenericParameters("T");
        var field = owner.DefineField("this", typeof(object), FieldAttributes.Public);
        session.Register(field); owner.DefineDefaultConstructor(MethodAttributes.Public); owner.CreateType();
        session.CompleteEmission(runtime.Errors);
        Assert.Throws<InvalidOperationException>(() => session.Register(field));
        Assert.Throws<InvalidOperationException>(() => session.CompleteEmission(runtime.Errors));
        using var stream = new MemoryStream(); builder.Save(stream); byte[] image = stream.ToArray();
        session.ValidateImage(image); var saved = VerifyLoad(image);
        foreach (var argument in new[] { typeof(double), typeof(string) })
        {
            var closed = saved.GetType("GenericReceiver`1")!.MakeGenericType(argument);
            var instance = Activator.CreateInstance(closed)!; object marker = new(); Bind(saved, instance, marker);
            Assert.Same(marker, closed.GetField("this")!.GetValue(instance));
        }
        using var pe = new PEReader(new MemoryStream(image)); var metadata = pe.GetMetadataReader();
        int offset = pe.PEHeaders.MetadataStartOffset + metadata.GetTableMetadataOffset(TableIndex.Field) +
            (MetadataTokens.GetRowNumber(MetadataTokens.FieldDefinitionHandle(field.MetadataToken)) - 1) * metadata.GetTableRowSize(TableIndex.Field);
        image[offset] |= (byte)FieldAttributes.InitOnly;
        Assert.Throws<CompileException>(() => session.ValidateImage(image));
    }

    [Fact]
    public void SuccessfulSymbolArtifactsAreCertifiedAfterFinalDebugInjection()
    {
        var compiler = NewCompiler("class Box{value=5;make(){return ()=>this.value;}} console.log(new Box().make()());", true);
        compiler.EmitDebugSymbols = true;
        var artifacts = compiler.SaveArtifacts("receiver-debug.pdb");
        Assert.NotEmpty(artifacts.Pdb!);
        var assembly = VerifyLoad(artifacts.Assembly);
        Assert.Equal("5\n", TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20)));
    }

    private static void Bind(Assembly assembly, object? target, object marker)
    {
        var type = assembly.GetType("$TSFunction")!;
        var function = type.GetConstructor([typeof(object), typeof(MethodInfo)])!.Invoke([target, null]);
        type.GetMethod("BindThis")!.Invoke(function, [marker]);
    }
    private static ILCompiler NewCompiler(string source, bool native)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var map = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var compiler = new ILCompiler("receiver_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, map); return compiler;
    }
    private static Assembly Compile(string source, bool native) => VerifyLoad(NewCompiler(source, native).SaveToBytes());
    private static Assembly VerifyLoad(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream)); return Assembly.Load(bytes);
    }
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName("receiver_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
    public sealed class ForeignReceiver { public object? @this; }
    public sealed class ForeignException : Exception { public object? @this; }
    public sealed class ForeignList : List<object> { public object? @this; }
    public sealed class ForeignDictionary : Dictionary<string, object> { public object? @this; }
}
