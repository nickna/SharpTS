using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeStaticMemberPresenceTests
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceStaticsKeepOwnInheritanceDescriptorsDeletionAndComputedMethods(bool native)
    {
        const string source = """
            const key = 'computed';
            class Parent { static inherited = 1; static inheritedMethod() { return 1; } }
            class Owner extends Parent {
                static field = 2;
                static method() { return 3; }
                static get label() { return 'tag'; }
                static [key]() { return 4; }
            }
            const owner: any = Owner;
            for (const name of ['field','method','label','computed','inherited','inheritedMethod','absent'])
                console.log(name, Object.hasOwn(owner, name));
            delete owner.field;
            console.log(Object.hasOwn(owner, 'field'));
            Object.defineProperty(owner, 'field', {value: 9, configurable: true});
            console.log(Object.hasOwn(owner, 'field'), owner.field);
            """;
        var saved = Compile(source, native, out var compiler);
        Assert.Equal("field true\nmethod true\nlabel true\ncomputed true\ninherited false\ninheritedMethod false\nabsent false\nfalse\ntrue 9\n", Run(saved));
        if (native) Assert.False(compiler.NativeStaticMembers!.UsesLegacy);
        else { Assert.Null(compiler.NativeStaticMembers); Assert.Null(saved.GetType("$NativeStaticMemberPresence")); }
    }

    [Fact]
    public void CompletedPresenceRowsMatchSavedMetadataAndAnExistingFieldDoesNotRunItsInitializer()
    {
        var saved = Compile("class State { static count=0; } class Owner { static field=++State.count; static method() {} }", true, out var compiler);
        var has = Helper(saved);
        var count = saved.GetType("State")!.GetField("count")!;
        Assert.Equal(0d, count.GetValue(null));
        Assert.Equal(true, has.Invoke(null, [saved.GetType("Owner"), "field"]));
        Assert.Equal(0d, count.GetValue(null));
        Type contract = saved.GetType("$IHasFields")!;
        foreach (var original in compiler.NativeRuntimeInitializers!.Owners.Where(type => type.Assembly == compiler.NativeRuntimeInitializers.Initialize.DeclaringType!.Assembly))
        {
            var owner = saved.GetType(original.FullName!)!;
            Assert.NotNull(owner);
            var fields = owner.GetFields(Flags).Select(field => field.Name);
            var properties = owner.GetProperties(Flags).Select(property => property.Name);
            var methods = contract.IsAssignableFrom(owner) ? owner.GetMethods(Flags).Select(method => method.Name) : [];
            var expected = fields.Concat(properties).Concat(methods).ToHashSet(StringComparer.Ordinal);
            foreach (var name in expected.Append("<not-a-member>"))
                Assert.Equal(expected.Contains(name), has.Invoke(null, [owner, name]));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyTheNongenericNativeProfileRejectsAnUnprovedForeignOwner(bool generic)
    {
        var saved = Compile(generic ? "class Owner<T> { static field=7; }" : "console.log(42);", true, out var compiler);
        Assert.Equal(generic, compiler.NativeStaticMembers!.UsesLegacy);
        var has = Helper(saved);
        if (generic) Assert.Equal(true, has.Invoke(null, [typeof(Foreign), nameof(Foreign.Field)]));
        else
        {
            var exception = Assert.Throws<TargetInvocationException>(() => has.Invoke(null, [typeof(Foreign), nameof(Foreign.Field)]));
            Assert.Contains(NativeStaticMemberPresence.UnsupportedOwnerMessage, exception.InnerException!.Message);
        }
    }

    [Fact]
    public void LiteralBclQueriesKeepTheirActualFieldsPropertiesAndNullNameException()
    {
        var saved = Compile("console.log(42);", true, out _);
        var has = Helper(saved);
        foreach (var owner in new[] { typeof(double), typeof(string), typeof(object), typeof(Task<object>), typeof(System.Numerics.BigInteger) })
        foreach (var name in owner.GetFields(Flags).Select(field => field.Name).Concat(owner.GetProperties(Flags).Select(property => property.Name)).Append("<missing>"))
            Assert.Equal(owner.GetField(name, Flags) != null || owner.GetProperty(name, Flags) != null, has.Invoke(null, [owner, name]));
        var exception = Assert.Throws<TargetInvocationException>(() => has.Invoke(null, [typeof(double), null]));
        Assert.Equal("name", Assert.IsType<ArgumentNullException>(exception.InnerException).ParamName);
    }

    [Fact]
    public void MixedVisibilityBuilderPropertiesRetainTheLoadedRuntimeLookup()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("native_mixed_presence_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var catalog = new NativeSourceOwnerCatalog(module, TypeProvider.Runtime);
        var runtime = new RuntimeEmitter(TypeProvider.Runtime, false, true).EmitAll(module,
            RuntimeFeatureSet.EmitEverything(), FunctionEmissionPolicy.Full, catalog);
        var owner = module.DefineType("Mixed", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, runtime.ObjectStorage.Type);
        var property = owner.DefineProperty("Value", PropertyAttributes.None, typeof(double), Type.EmptyTypes);
        var getter = owner.DefineMethod("get_Value", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.SpecialName, typeof(double), Type.EmptyTypes);
        getter.GetILGenerator().Emit(OpCodes.Ldc_R8, 7d); getter.GetILGenerator().Emit(OpCodes.Ret);
        var setter = owner.DefineMethod("set_Value", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName, typeof(void), [typeof(double)]);
        setter.GetILGenerator().Emit(OpCodes.Ret); property.SetGetMethod(getter); property.SetSetMethod(setter);
        NativeSourceOwnerCatalog.Implement(owner, runtime.ObjectFields.Interface, catalog); owner.CreateType();
        var parameters = new ClassGenericParameterRegistry(); parameters.Declare(owner, []); parameters.CompleteEmission([owner]); catalog.CompleteEmission(parameters, [owner]);
        var initializers = new NativeRuntimeClassInitialization(module, TypeProvider.Runtime); initializers.CompleteEmission(catalog, runtime);
        var presence = new NativeStaticMemberPresence(module, TypeProvider.Runtime); presence.CompleteEmission(initializers, catalog, runtime);
        Assert.True(presence.UsesLegacy);
        using var bytes = new MemoryStream(); assembly.Save(bytes);
        using var check = new MemoryStream(bytes.ToArray()); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(check));
        var saved = Assembly.Load(bytes.ToArray());
        Assert.NotNull(saved.GetType("Mixed")!.GetProperty("Value", Flags));
        Assert.Equal(true, Helper(saved).Invoke(null, [saved.GetType("Mixed"), "Value"]));
    }

    public sealed class Foreign { public static int Field = 7; }
    private static MethodInfo Helper(Assembly saved) => saved.GetType("$NativeStaticMemberPresence")!.GetMethod("HasMember", Methods)!;
    private static Assembly Compile(string source, bool native, out ILCompiler compiler)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        compiler = new ILCompiler("native_presence_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
        byte[] bytes = compiler.SaveToBytes(); using var stream = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); Assert.Empty(verifier.Verify(stream)); return Assembly.Load(bytes);
    }
    private static string Run(Assembly saved) => TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20));
}
