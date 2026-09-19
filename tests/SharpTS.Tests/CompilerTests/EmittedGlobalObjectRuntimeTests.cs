using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedGlobalObjectRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly PropertyInfo[] Declarations = typeof(EmittedGlobalObjectRuntime).GetProperties()
        .Where(p => p.PropertyType == typeof(FieldBuilder) || p.PropertyType == typeof(MethodBuilder))
        .Where(p => p.Name != "IndirectEval").ToArray();
    public static IEnumerable<object[]> DeclarationNames => Declarations.Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(DeclarationNames))]
    public void DeclarationsRejectMissingNullAndDuplicateValues(string name)
    {
        var owner = new EmittedRuntime().GlobalObject;
        var property = Declarations.Single(p => p.Name == name);
        Fails<InvalidOperationException>(() => property.GetValue(owner));
        Fails<ArgumentNullException>(() => property.SetValue(owner, null));
        var value = Handle(property); property.SetValue(owner, value);
        Assert.Same(value, property.GetValue(owner));
        Fails<InvalidOperationException>(() => property.SetValue(owner, value));
        Assert.False(owner.IsDeclared); Assert.False(owner.IsComplete);
    }

    [Theory]
    [MemberData(nameof(DeclarationNames))]
    public void IncompleteDeclarationsCanBeRepairedThenFrozen(string missing)
    {
        var owner = new EmittedRuntime().GlobalObject;
        foreach (var property in Declarations.Where(p => p.Name != missing)) property.SetValue(owner, Handle(property));
        Fails<InvalidOperationException>(() => Stage(owner, "CompleteDeclarations"));
        Assert.False(owner.IsDeclared);
        var absent = Declarations.Single(p => p.Name == missing); absent.SetValue(owner, Handle(absent));
        Stage(owner, "CompleteDeclarations"); Assert.True(owner.IsDeclared); Assert.False(owner.IsComplete);
        Fails<InvalidOperationException>(() => Stage(owner, "CompleteDeclarations"));
        foreach (var property in Declarations)
            Fails<InvalidOperationException>(() => property.SetValue(owner, Handle(property)));
    }

    [Fact]
    public void LateHandleRequiresDeclarationsAndRejectsMissingNullAndDuplicateValues()
    {
        var owner = new EmittedRuntime().GlobalObject;
        var property = typeof(EmittedGlobalObjectRuntime).GetProperty("IndirectEval")!;
        Fails<InvalidOperationException>(() => property.GetValue(owner));
        Fails<InvalidOperationException>(() => property.SetValue(owner, Handle(property)));
        Declare(owner);
        Fails<ArgumentNullException>(() => property.SetValue(owner, null));
        var value = Handle(property); property.SetValue(owner, value);
        Assert.Same(value, owner.IndirectEval);
        Fails<InvalidOperationException>(() => property.SetValue(owner, value));
    }

    [Fact]
    public void CompletionRequiresInitializationAndTheLateHelperThenFreezesEveryStage()
    {
        var owner = new EmittedRuntime().GlobalObject;
        Fails<InvalidOperationException>(() => Stage(owner, "MarkInitializerEmitted"));
        Fails<InvalidOperationException>(() => Stage(owner, "CompleteEmission"));
        Declare(owner);
        var late = typeof(EmittedGlobalObjectRuntime).GetProperty("IndirectEval")!;
        late.SetValue(owner, Handle(late));
        Fails<InvalidOperationException>(() => Stage(owner, "CompleteEmission"));
        Assert.False(owner.IsComplete);
        Stage(owner, "MarkInitializerEmitted");
        Fails<InvalidOperationException>(() => Stage(owner, "MarkInitializerEmitted"));
        Stage(owner, "CompleteEmission"); Assert.True(owner.IsComplete);
        foreach (var name in new[] { "CompleteDeclarations", "MarkInitializerEmitted", "CompleteEmission" })
            Fails<InvalidOperationException>(() => Stage(owner, name));
        foreach (var property in Declarations.Append(late))
            Fails<InvalidOperationException>(() => property.SetValue(owner, Handle(property)));
    }

    [Fact]
    public void InitializedOwnerCannotCompleteUntilIndirectEvalIsDeclared()
    {
        var owner = new EmittedRuntime().GlobalObject; Declare(owner); Stage(owner, "MarkInitializerEmitted");
        Fails<InvalidOperationException>(() => Stage(owner, "CompleteEmission"));
        var late = typeof(EmittedGlobalObjectRuntime).GetProperty("IndirectEval")!; late.SetValue(owner, Handle(late));
        Stage(owner, "CompleteEmission"); Assert.True(owner.IsComplete);
    }

    [Fact]
    public void NullableProbePreservesIncompleteRuntimeChecksWithoutReadingRequiredHandle()
    {
        var owner = new EmittedRuntime().GlobalObject;
        var probe = typeof(EmittedGlobalObjectRuntime).GetProperty("HasSingletonField", Members)!;
        Assert.False((bool)probe.GetValue(owner)!);
        Fails<InvalidOperationException>(() => typeof(EmittedGlobalObjectRuntime).GetProperty("SingletonField")!.GetValue(owner));
        var field = Declarations.Single(p => p.Name == "SingletonField"); field.SetValue(owner, Handle(field));
        Assert.True((bool)probe.GetValue(owner)!); Assert.False(owner.IsDeclared);
    }

    [Fact]
    public void HelpersReceiveScopedInputsAndOwnerBelongsToOneCompilation()
    {
        Assert.Equal(4, Declarations.Length);
        Assert.NotSame(new EmittedRuntime().GlobalObject, new EmittedRuntime().GlobalObject);
        Assert.Null(typeof(EmittedRuntime).GetProperty("GlobalObject")!.SetMethod);
        foreach (var old in new[] { "GlobalThisSingletonField", "GlobalThisGetProperty", "GlobalThisSetProperty", "GlobalThisProperties", "EvalIndirect" })
            Assert.Null(typeof(EmittedRuntime).GetProperty(old));
        Assert.Equal(new[] { typeof(TypeBuilder), typeof(EmittedGlobalObjectRuntime), typeof(FieldInfo) },
            typeof(RuntimeEmitter).GetMethod("EmitIndirectEval", Members)!.GetParameters().Select(p => p.ParameterType));
        foreach (var name in new[] { "EmitGlobalThisGetProperty", "EmitGlobalThisSetProperty", "EmitCachedTSFunction" })
            Assert.DoesNotContain(typeof(RuntimeEmitter).GetMethod(name, Members)!.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime));
        var optional = typeof(RuntimeEmitter).GetNestedType("GlobalPropertyOptionalInputs", BindingFlags.NonPublic)!;
        Assert.Equal(new[] { "DateType", "RegExpType", "ReflectSingleton", "BufferType", "EncoderType", "DecoderType", "GetCryptoObject", "FetchInvoke" },
            optional.GetProperties().Select(p => p.Name));
        foreach (var name in new[] { "GlobalPropertyReadInputs", "GlobalPropertyWriteInputs", "GlobalPropertyOptionalInputs" })
            Assert.DoesNotContain(typeof(RuntimeEmitter).GetNestedType(name, BindingFlags.NonPublic)!.GetProperties(),
                p => p.PropertyType == typeof(EmittedRuntime) || p.PropertyType == typeof(RuntimeFeatureSet));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesGlobalIdentityForwardHandlesAndOptionalSelections(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedGlobalObjectRuntime>(); var singletons = new HashSet<object>();
        foreach (string source in new[] { "const n=1;", "new Date();new RegExp('x');Reflect.ownKeys({});Buffer.from('x');new TextEncoder();fetch('http://127.0.0.1');", "const n=2;" })
        {
            var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
            var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var features = new RuntimeFeatureDetector().Detect(statements);
            var runtime = emitter.EmitAll(module, features); var owner = runtime.GlobalObject;
            Assert.True(owners.Add(owner)); Assert.True(owner.IsDeclared); Assert.True(owner.IsComplete);
            foreach (var property in Declarations.Append(typeof(EmittedGlobalObjectRuntime).GetProperty("IndirectEval")!))
            {
                var member = (MemberInfo)property.GetValue(owner)!;
                Assert.Same(builder, member.Module.Assembly); Assert.Same(runtime.RuntimeClass.Type, member.DeclaringType);
            }
            var loaded = SaveVerifyLoad(builder); var type = loaded.GetType(runtime.RuntimeClass.Type.FullName!)!;
            var singleton = type.GetField(owner.SingletonField.Name)!.GetValue(null)!;
            Assert.True(singletons.Add(singleton));
            var get = type.GetMethod(owner.GetProperty.Name)!; var set = type.GetMethod(owner.SetProperty.Name)!;
            object? Get(string name) => get.Invoke(null, [name]);
            Assert.Same(singleton, Get("globalThis")); Assert.Same(singleton, Get("global"));
            Assert.True(get.MetadataToken < type.GetMethod(owner.IndirectEval.Name)!.MetadataToken);
            Assert.Equal(typeof(object), get.ReturnType); Assert.Equal(typeof(void), set.ReturnType);
            Assert.True(get.IsPublic && get.IsStatic && set.IsPublic && set.IsStatic);
            Assert.Equal(new[] { typeof(string) }, get.GetParameters().Select(p => p.ParameterType));
            Assert.Equal(new[] { typeof(string), typeof(object) }, set.GetParameters().Select(p => p.ParameterType));
            set.Invoke(null, ["__metadata_probe", 42.0]); Assert.Equal(42.0, Get("__metadata_probe"));
            Assert.Same(Get("parseInt"), Get("parseInt")); Assert.Same(Get("eval"), Get("eval"));
            var indirect = type.GetMethod(owner.IndirectEval.Name)!; var marker = new object();
            Assert.Same(marker, indirect.Invoke(null, [marker])); Assert.Null(indirect.Invoke(null, [null]));
            Assert.Equal(3.0, indirect.Invoke(null, ["1+2"]));
            var undefined = loaded.GetType("$Undefined")!.GetField("Instance")!.GetValue(null);
            foreach (var pair in new[] { ("Date", features.UsesDate), ("RegExp", features.UsesRegExp), ("Buffer", features.UsesBuffer), ("TextEncoder", features.UsesTextEncoding), ("TextDecoder", features.UsesTextEncoding), ("fetch", features.UsesFetch) })
                if (pair.Item2) Assert.NotSame(undefined, Get(pair.Item1)); else Assert.Same(undefined, Get(pair.Item1));
            if (features.UsesFetch) Assert.Same(Get("fetch"), Get("fetch"));
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    private static void Declare(EmittedGlobalObjectRuntime owner)
    {
        foreach (var property in Declarations) property.SetValue(owner, Handle(property));
        Stage(owner, "CompleteDeclarations");
    }
    private static object Handle(PropertyInfo property)
    {
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        return property.PropertyType == typeof(FieldBuilder)
            ? type.DefineField(property.Name, typeof(object), FieldAttributes.Public | FieldAttributes.Static)
            : type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object)]);
    }
    private static void Stage(EmittedGlobalObjectRuntime owner, string name) => typeof(EmittedGlobalObjectRuntime).GetMethod(name, Members)!.Invoke(owner, null);
    private static void Fails<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"global_object_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
}
