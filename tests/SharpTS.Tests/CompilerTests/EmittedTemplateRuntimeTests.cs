using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedTemplateRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedTemplateRuntime).GetProperties()
        .Where(property => property.PropertyType != typeof(bool));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRepair(string missingHandle)
    {
        var templates = CreateDeclarations(missingHandle);
        var property = typeof(EmittedTemplateRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(templates));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(templates.CompleteEmission).Message);
        Assert.False(templates.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(templates, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(templates, property.GetValue(CreateDeclarations()));
        templates.CompleteEmission();
        AssertFrozen(templates);
    }

    [Fact]
    public void RequiredOwnerExistsBeforeEmissionAndCannotBeReplaced()
    {
        var first = new EmittedRuntime();
        var second = new EmittedRuntime();
        Assert.NotSame(first.Templates, second.Templates);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Templates))!.SetMethod);
        Assert.False(first.Templates.IsComplete);
        Assert.Throws<InvalidOperationException>(() => first.Templates.StringsListType);
        Assert.Throws<InvalidOperationException>(() => first.Templates.Invoke);
    }

    [Fact]
    public void CreatedStringsListPrecedesRuntimeHelpersAndPreservesRawAndCookedValues()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("template_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var templates = new EmittedRuntime().Templates;
        new RuntimeEmitter(TypeProvider.Runtime).EmitTemplateStringsListClass(module, templates);
        Assert.Equal("$TemplateStringsList", templates.StringsListType.Name);
        Assert.Same(templates.StringsListType, templates.StringsListCtor.DeclaringType);
        Assert.Same(templates.StringsListType, templates.RawGetter.DeclaringType);
        Assert.False(templates.IsComplete);
        Assert.Throws<InvalidOperationException>(() => templates.Concat);
        Assert.Throws<InvalidOperationException>(templates.CompleteEmission);
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        bytes.Position = 0;
        Verify(bytes);
        var listType = Assembly.Load(bytes.ToArray()).GetType("$TemplateStringsList")!;
        var list = Assert.IsAssignableFrom<List<object>>(Activator.CreateInstance(listType,
            [new object?[] { null, "cooked\n" }, new[] { "raw", "cooked\\n" }]));
        Assert.Equal(new object[] { "undefined", "cooked\n" }, list);
        var raw = Assert.IsType<List<object>>(listType.GetProperty("raw")!.GetValue(list));
        Assert.Equal(new object[] { "raw", "cooked\\n" }, raw);
        Assert.NotSame(list, raw);
        Assert.Null(listType.GetProperty("raw")!.SetMethod);
        Assert.True(listType.GetField("_rawStrings", BindingFlags.NonPublic | BindingFlags.Instance)!.IsInitOnly);
    }

    [Theory]
    [InlineData("const value=1;", false)]
    [InlineData("const value=1;", true)]
    [InlineData("String.raw`value`;", false)]
    [InlineData("function tag(s:any){return s.raw;}tag`value`;", false)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void TemplatesRemainRequiredForMinimalFullAndHostedEmission(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        AssertFrozen(runtime.Templates);
        using var bytes = Save(runtime);
        Verify(bytes);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        var assembly = Assembly.Load(bytes.ToArray());
        Assert.NotNull(assembly.GetType("$TemplateStringsList"));
        var type = assembly.GetType("$Runtime")!;
        Assert.Equal("a7b", Invoke(type, runtime.Templates.Raw.Name, new[] { "a", "b" }, new List<object> { 7d }));
        Assert.Equal("a7false", Invoke(type, runtime.Templates.Concat.Name, (object)new object[] { "a", 7d, false }));
    }

    [Fact]
    public void SavedHelpersKeepInvocationAndRestParameterSignatures()
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        Assert.Equal(new[] { typeof(object), typeof(List<object>) },
            type.GetMethod(runtime.Templates.Raw.Name)!.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(typeof(string), type.GetMethod(runtime.Templates.Raw.Name)!.ReturnType);
        Assert.Equal(new[] { typeof(object[]) },
            type.GetMethod(runtime.Templates.Concat.Name)!.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(new[] { typeof(object), typeof(object[]), typeof(string[]), typeof(object[]) },
            type.GetMethod(runtime.Templates.Invoke.Name)!.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(new[] { typeof(object), typeof(object), typeof(object[]), typeof(string[]), typeof(object[]) },
            type.GetMethod(runtime.Templates.InvokeWithThis.Name)!.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "a")]
    [InlineData(3, "a7bc")]
    public void RawArrayPathRetainsMissingAndUnusedSubstitutionBehavior(int length, string expected)
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        Assert.Equal(expected, Invoke(type, runtime.Templates.Raw.Name, new[] { "a", "b", "c" }.Take(length).ToArray(), new List<object> { 7d }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingTagRaisesGuestTypeErrorAfterTemplateConstruction(bool withThis)
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var args = withThis
            ? new object?[] { null, null, new object[] { "value" }, new[] { "value" }, Array.Empty<object>() }
            : new object?[] { null, new object[] { "value" }, new[] { "value" }, Array.Empty<object>() };
        var method = withThis ? runtime.Templates.InvokeWithThis : runtime.Templates.Invoke;
        var error = Assert.Throws<TargetInvocationException>(() => Invoke(type, method.Name, args));
        Assert.Equal("$ThrownValueException", error.InnerException!.GetType().Name);
        var value = error.InnerException.GetType().GetProperty("Value")!.GetValue(error.InnerException);
        Assert.Equal("$TypeError", value!.GetType().Name);
        Assert.Contains("Tagged template tag must be a function.", error.InnerException.Message);
    }

    [Fact]
    public void ReusedEmitterKeepsTemplateTypesAndHandlesIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var full = EmitRuntime(null, false, emitter);
        var minimal = EmitRuntime("const value=1;", false, emitter);
        AssertFrozen(full.Templates);
        AssertFrozen(minimal.Templates);
        foreach (var property in Handles)
            Assert.NotSame(property.GetValue(full.Templates), property.GetValue(minimal.Templates));
        using var bytes = Save(minimal);
        Verify(bytes);
    }

    private static object? Invoke(Type type, string name, params object?[] args) => type.GetMethod(name)!.Invoke(null, args);

    private static EmittedTemplateRuntime CreateDeclarations(string? omitted = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"template_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("TemplateDeclarations", TypeAttributes.Public);
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var templates = new EmittedTemplateRuntime();
        foreach (var property in Handles)
        {
            if (property.Name == omitted) continue;
            if (property.PropertyType == typeof(Type)) property.SetValue(templates, type);
            else if (property.PropertyType == typeof(ConstructorInfo)) property.SetValue(templates, ctor);
            else
            {
                var method = type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
                method.GetILGenerator().Emit(OpCodes.Ret);
                property.SetValue(templates, method);
            }
        }
        return templates;
    }

    private static void AssertFrozen(EmittedTemplateRuntime templates)
    {
        Assert.True(templates.IsComplete);
        Assert.Throws<InvalidOperationException>(templates.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(templates);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(templates, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static void Verify(MemoryStream bytes)
    {
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        bytes.Position = 0;
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"template_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module,
            new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()));
    }
}
