using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedInspectionRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedInspectionRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var inspection = CreateDeclarations(missingHandle);
        var property = typeof(EmittedInspectionRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(inspection));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(inspection.CompleteEmission).Message);
        Assert.False(inspection.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(inspection, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(inspection, property.GetValue(CreateDeclarations()));
        inspection.CompleteEmission();
        AssertFrozen(inspection);
    }

    [Fact]
    public void RequiredComponentIsImmediatelyAvailableAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.NotNull(runtime.Inspection);
        Assert.False(runtime.Inspection.IsComplete);
        Assert.Same(runtime.Inspection, runtime.Inspection);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Inspection))!.SetMethod);
        Assert.Throws<InvalidOperationException>(() => runtime.Inspection.InspectValue);
    }

    [Fact]
    public void ForwardCallsUseDeclarationsBeforeTheirBodiesExist()
    {
        var inspection = new EmittedInspectionRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("inspection_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Helpers");
        inspection.InspectValue = type.DefineMethod("Value", MethodAttributes.Public | MethodAttributes.Static,
            typeof(string), Type.EmptyTypes);
        inspection.InspectArray = type.DefineMethod("Array", MethodAttributes.Public | MethodAttributes.Static,
            typeof(string), Type.EmptyTypes);
        inspection.InspectObject = type.DefineMethod("Object", MethodAttributes.Public | MethodAttributes.Static,
            typeof(string), Type.EmptyTypes);
        inspection.InspectValue.GetILGenerator().Emit(OpCodes.Call, inspection.InspectArray);
        inspection.InspectValue.GetILGenerator().Emit(OpCodes.Ret);
        inspection.InspectArray.GetILGenerator().Emit(OpCodes.Call, inspection.InspectObject);
        inspection.InspectArray.GetILGenerator().Emit(OpCodes.Ret);
        Assert.False(inspection.IsComplete);
        inspection.InspectObject.GetILGenerator().Emit(OpCodes.Ldstr, "ready");
        inspection.InspectObject.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        inspection.CompleteEmission();
        AssertFrozen(inspection);
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        Assert.Equal("ready", Assembly.Load(bytes.ToArray()).GetType("Helpers")!.GetMethod("Value")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("const value = 1;", false)]
    [InlineData("console.dir({a: 1});", false)]
    [InlineData("import * as os from 'os';", false)]
    [InlineData("new Uint8Array(2);", false)]
    [InlineData("const value = 1;", true)]
    [InlineData("console.dir({a: 1});", true)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void MinimalFeatureRichAndHostedEmissionCompleteEveryDeclaration(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        AssertFrozen(runtime.Inspection);
        Assert.Equal(3, Handles.Count());
        foreach (var property in Handles)
        {
            var method = Assert.IsAssignableFrom<MethodBuilder>(property.GetValue(runtime.Inspection));
            Assert.Same(runtime.RuntimeClass.Type, method.DeclaringType);
            Assert.True(method.IsPublic && method.IsStatic);
        }
        Assert.True(runtime.RuntimeClass.Type.IsCreated());
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToList();
        Assert.True(methods.IndexOf("UtilInspectValue") < methods.IndexOf("ConsoleDir"));
        Assert.True(methods.IndexOf("UtilInspectArray") < methods.IndexOf("ConsoleDir"));
        Assert.True(methods.IndexOf("UtilInspectObject") < methods.IndexOf("ConsoleDir"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedHelpersVerifyAndPreservePrimitiveRecursiveAndDepthBehavior(bool hosted)
    {
        using var bytes = Save(EmitRuntime("const value = 1;", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var runtime = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var inspect = runtime.GetMethod("UtilInspectValue")!;
        string Inspect(object? value, int depth = 2, int current = 0) =>
            Assert.IsType<string>(inspect.Invoke(null, [value, depth, current]));
        Assert.Equal("null", Inspect(null));
        Assert.Equal("'text'", Inspect("text"));
        Assert.Equal("42.5", Inspect(42.5));
        Assert.Equal("true", Inspect(true));
        Assert.Equal("false", Inspect(false));
        Assert.Equal("[Function]", Inspect((Action)(() => { })));
        Assert.Equal("undefined", Inspect(new NullStringValue()));
        Assert.Equal("[  ]", Inspect(new List<object>()));
        Assert.Equal("{  }", Inspect(new Dictionary<string, object>()));
        Assert.Equal("[Array]", Inspect(new List<object>(), depth: 0));
        Assert.Equal("[Object]", Inspect(new Dictionary<string, object>(), depth: 0));
        Assert.Equal("[Object]", Inspect("text", depth: 0, current: 1));
        var nested = new Dictionary<string, object>
        {
            ["values"] = new List<object> { 1d, new Dictionary<string, object> { ["ok"] = true } }
        };
        Assert.Equal("{ values: [ 1, [Object] ] }", Inspect(nested));
        Assert.Equal("{ values: [ 1, { ok: true } ] }", Inspect(nested, depth: 3));
        var cycle = new Dictionary<string, object>();
        cycle["self"] = cycle;
        Assert.Equal("{ self: { self: [Object] } }", Inspect(cycle));
        var arrayCycle = new List<object>();
        arrayCycle.Add(arrayCycle);
        Assert.Equal("[ [ [Array] ] ]", Inspect(arrayCycle));
    }

    [Fact]
    public void ReusingEmitterKeepsEachCompilationMetadataIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("const value = 1;", false, emitter).Inspection;
        var second = EmitRuntime("console.dir(2);", false, emitter).Inspection;
        Assert.NotSame(first, second);
        foreach (var property in Handles) Assert.NotSame(property.GetValue(first), property.GetValue(second));
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private sealed class NullStringValue
    {
        public override string? ToString() => null;
    }

    private static EmittedInspectionRuntime CreateDeclarations(string? missingHandle = null)
    {
        var inspection = new EmittedInspectionRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"inspection_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        foreach (var property in Handles.Where(property => property.Name != missingHandle)) property.SetValue(inspection, method);
        return inspection;
    }

    private static void AssertFrozen(EmittedInspectionRuntime inspection)
    {
        Assert.True(inspection.IsComplete);
        Assert.Throws<InvalidOperationException>(inspection.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(inspection);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(inspection, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"inspection_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
