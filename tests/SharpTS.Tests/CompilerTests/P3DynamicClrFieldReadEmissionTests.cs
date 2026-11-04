using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class P3DynamicClrFieldReadEmissionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ManagedFieldBranchUsesSuppliedSelection(bool selected, bool supplied)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName("field_inputs_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("main");
        var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer("console.log(1);").ScanTokens()).ParseOrThrow());
        features.UsesExternalFieldReads = selected;
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(module, features);
        var probe = runtime.RuntimeClass.Type.DefineNestedType("SuppliedFieldReads", TypeAttributes.NestedPublic);
        var helper = typeof(RuntimeEmitter).GetMethod("EmitGetFieldsProperty", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var inputs = ObjectReadTestSupport.MakeInputs(helper.GetParameters()[2].ParameterType, runtime,
            new Dictionary<string, object?> { ["ExternalFieldReads"] = supplied, ["NativePropertyGetter"] = null });
        helper.Invoke(emitter, [probe, new EmittedObjectReadRuntime(), inputs]);
        probe.CreateType();
        using var image = new MemoryStream(); builder.Save(image);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(image.ToArray())));
        var saved = Assembly.Load(image.ToArray());
        var read = saved.GetType(probe.FullName!)!.GetMethod("GetFieldsProperty")!;
        var receiver = new ForeignFields();
        object? value = read.Invoke(null, [receiver, "x"]);
        if (supplied) Assert.Equal(1d, value);
        else Assert.Same(saved.GetType("$Undefined")!.GetField("Instance")!.GetValue(null), value);
        var undefined = saved.GetType("$Undefined")!.GetField("Instance")!.GetValue(null);
        Assert.Same(undefined, read.Invoke(null, [receiver, "secret"]));
        Assert.Same(undefined, read.Invoke(null, [receiver, "shared"]));
        Assert.Equal(4d, read.Invoke(null, [receiver, "property"]));
    }

    [Theory]
    [InlineData("console.log(1);", false)]
    [InlineData("class Guest { value=1; }", false)]
    [InlineData("@DotNetType('System.Numerics.Vector2') declare class Vector {}", true)]
    [InlineData("import { Vector2 } from 'dotnet:System.Numerics.Vector2';", true)]
    [InlineData("import type { Vector2 } from 'dotnet:System.Numerics.Vector2';", false)]
    [InlineData("const evaluator:any=Function;", true)]
    public void FieldReflectionFollowsExternalValueIngress(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens(), DecoratorMode.Legacy).ParseOrThrow();
        Assert.Equal(expected, new RuntimeFeatureDetector().Detect(statements).UsesExternalFieldReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeOutputKeepsFinitePropertyOwnerGuardInsteadOfManagedFieldReflection(bool native)
    {
        const string source = """
            @DotNetType("System.Numerics.Vector2") declare class Vector {}
            console.log(1);
            """;
        var statements = new Parser(new Lexer(source).ScanTokens(), DecoratorMode.Legacy).ParseOrThrow();
        var checker = new TypeChecker(); checker.SetDecoratorMode(DecoratorMode.Legacy);
        var map = checker.Check(statements);
        var compiler = new ILCompiler("external_fields_" + Guid.NewGuid().ToString("N"), false, true,
            null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, map);
        byte[] bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        var saved = Assembly.Load(bytes);
        var read = saved.GetType("$Runtime")!.GetMethod("GetFieldsProperty")!;
        Assert.Equal(!native, Instructions(read).Any(instruction => instruction.Member is MethodInfo
            { Name: "GetField", DeclaringType: { } owner } && owner == typeof(Type)));
        if (native)
        {
            var error = Assert.Throws<TargetInvocationException>(() => read.Invoke(null,
                [new System.Numerics.Vector2(1, 2), "x"]));
            Assert.Contains(NativePropertyLookupCatalog.ForeignOwnerMessage, error.InnerException!.Message);
        }
    }

    public sealed class ForeignFields
    {
        public float X = 1;
        public static int Shared = 3;
        private int secret = 2;
        public int Property => secret + 2;
    }
}
