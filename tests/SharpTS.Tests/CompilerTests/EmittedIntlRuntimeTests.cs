using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedIntlRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedIntlRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    private static readonly string[] Factories =
    [
        "NumberFormat", "DateTimeFormat", "Collator", "PluralRules", "RelativeTimeFormat",
        "ListFormat", "DisplayNames", "Segmenter"
    ];

    private static readonly string[] NamespaceMembers =
    [
        "NumberFormat", "DateTimeFormat", "Collator", "PluralRules", "RelativeTimeFormat",
        "ListFormat", "Segmenter", "DisplayNames"
    ];

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var intl = CreateDeclarations(missingHandle);
        var property = typeof(EmittedIntlRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(intl));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(intl.CompleteEmission).Message);
        Assert.False(intl.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(intl, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(intl, property.GetValue(CreateDeclarations()));
        intl.CompleteEmission();
        AssertFrozen(intl);
    }

    [Fact]
    public void OptionalComponentHasExplicitAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.Intl);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireIntl).Message);
        runtime.BeginIntlEmission();
        var intl = runtime.RequireIntl();
        Assert.Same(runtime.Intl, intl);
        Assert.Throws<InvalidOperationException>(runtime.BeginIntlEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Intl))!.SetMethod!.IsPrivate);
        var declarations = CreateDeclarations();
        foreach (var property in Handles) property.SetValue(intl, property.GetValue(declarations));
        intl.CompleteEmission();
        AssertFrozen(intl);
        Assert.Throws<InvalidOperationException>(runtime.BeginIntlEmission);
    }

    [Fact]
    public void EarlyNamespaceFieldAndFactoriesRemainReadableBeforeLatePopulation()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("intl_staged"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Runtime", TypeAttributes.Public);
        var getOrCreate = type.DefineMethod("GetOrCreate", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [typeof(MethodInfo), typeof(string), typeof(int)]);
        getOrCreate.GetILGenerator().Emit(OpCodes.Ldnull);
        getOrCreate.GetILGenerator().Emit(OpCodes.Ret);
        var intl = new EmittedIntlRuntime();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        void Emit(string name, params object?[] args) => typeof(RuntimeEmitter)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(emitter, args);

        Emit("DefineNamespaceSingletonFields", type, null, intl);
        Assert.Equal("_IntlNamespace", intl.NamespaceField.Name);
        Assert.Throws<InvalidOperationException>(() => intl.CreateNumberFormat);
        Assert.Throws<InvalidOperationException>(() => intl.NamespacePopulate);
        Emit("EmitIntlMethods", type, intl);
        foreach (var name in Factories)
        {
            var method = (MethodBuilder)typeof(EmittedIntlRuntime).GetProperty("Create" + name)!.GetValue(intl)!;
            Assert.Equal("CreateIntl" + name, method.Name);
            Assert.True(method.GetILGenerator().ILOffset > 0);
        }
        Assert.Throws<InvalidOperationException>(intl.CompleteEmission);
        Assert.False(intl.IsComplete);
        Emit("EmitNamespaceSingletons", type, null, intl, getOrCreate);
        Assert.True(intl.NamespacePopulate.GetILGenerator().ILOffset > 0);
        Assert.False(type.IsCreated());
        Assert.False(intl.IsComplete);
        type.CreateType();
        intl.CompleteEmission();
        AssertFrozen(intl);
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
    }

    [Theory]
    [InlineData("const value=1;", false, false)]
    [InlineData("Buffer.from('only');", false, false)]
    [InlineData("Math.abs(-1);", false, false)]
    [InlineData("new AbortController();", false, false)]
    [InlineData("Intl;", true, false)]
    [InlineData("const I=Intl;", true, false)]
    [InlineData("new Intl.NumberFormat('en-US');", true, false)]
    [InlineData("new Intl.DisplayNames('en',{type:'script'});", true, false)]
    [InlineData("new Intl.Segmenter('en');", true, false)]
    [InlineData("globalThis.Intl;", true, false)]
    [InlineData("globalThis.Math;", true, false)]
    [InlineData("const value=1;", false, true)]
    [InlineData("Intl;", true, true)]
    [InlineData(null, true, false)]
    [InlineData(null, true, true)]
    public void FeatureGatesPreserveOptionalDeclarationsAndRuntimeRequirement(string? source, bool enabled, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Equal(enabled, runtime.Intl is not null);
        Assert.Equal(enabled, runtime.Deployment.Reasons.Contains("Intl"));
        if (enabled)
        {
            var intl = runtime.RequireIntl();
            AssertFrozen(intl);
            Assert.Equal(10, Handles.Count());
            Assert.True(runtime.Deployment.Requirements.HasFlag(SharpTSRuntimeRequirements.RuntimeAssembly));
        }
        else Assert.Throws<InvalidOperationException>(runtime.RequireIntl);
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        var fields = reader.FieldDefinitions.Select(handle => reader.GetString(reader.GetFieldDefinition(handle).Name)).ToArray();
        Assert.Equal(enabled, fields.Contains("_IntlNamespace"));
        Assert.Equal(enabled, methods.Contains("_IntlNamespacePopulate"));
        foreach (var name in Factories) Assert.Equal(enabled, methods.Contains("CreateIntl" + name));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedFactoriesAndNamespaceVerifyAndPreserveWrapperIdentity(bool hosted)
    {
        using var bytes = Save(EmitRuntime("Intl;", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$Runtime")!;
        var field = type.GetField("_IntlNamespace")!;
        var populate = type.GetMethod("_IntlNamespacePopulate")!;
        Assert.Null(field.GetValue(null));
        populate.Invoke(null, null);
        var value = Assert.IsType<Dictionary<string, object>>(field.GetValue(null));
        Assert.Equal(NamespaceMembers, value.Keys);
        var wrappers = NamespaceMembers.Select(name => value[name]).ToArray();
        populate.Invoke(null, null);
        Assert.Same(value, field.GetValue(null));
        for (var i = 0; i < NamespaceMembers.Length; i++)
        {
            var wrapper = value[NamespaceMembers[i]];
            Assert.Same(wrappers[i], wrapper);
            Assert.Equal("$TSFunction", wrapper.GetType().Name);
            Assert.Equal(2, wrapper.GetType().GetProperty("Length")!.GetValue(wrapper));
        }
        foreach (var name in Factories)
        {
            object? options = name switch
            {
                "DisplayNames" => new Dictionary<string, object?> { ["type"] = "script" },
                "DateTimeFormat" => new Dictionary<string, object?> { ["timeZone"] = "UTC" },
                _ => null
            };
            var instance = type.GetMethod("CreateIntl" + name)!.Invoke(null, ["en-US", options]);
            Assert.NotNull(instance);
            Assert.Equal("SharpTSIntl" + name, instance.GetType().Name);
        }
    }

    [Fact]
    public void ReusingEmitterKeepsEachCompilationMetadataIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("Intl;", false, emitter).RequireIntl();
        var minimal = EmitRuntime("const value=1;", false, emitter);
        var second = EmitRuntime("Intl;", false, emitter).RequireIntl();
        Assert.Null(minimal.Intl);
        Assert.NotSame(first, second);
        foreach (var property in Handles) Assert.NotSame(property.GetValue(first), property.GetValue(second));
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private static EmittedIntlRuntime CreateDeclarations(string? missingHandle = null)
    {
        var intl = new EmittedIntlRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"intl_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
            property.SetValue(intl, property.PropertyType == typeof(FieldBuilder) ? (object)field : method);
        return intl;
    }

    private static void AssertFrozen(EmittedIntlRuntime intl)
    {
        Assert.True(intl.IsComplete);
        Assert.Throws<InvalidOperationException>(intl.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(intl);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(intl, value));
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"intl_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
