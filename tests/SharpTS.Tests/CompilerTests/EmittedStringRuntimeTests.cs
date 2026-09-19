using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedStringRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedStringRuntime).GetProperties()
        .Where(property => property.PropertyType != typeof(bool));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRepair(string missingHandle)
    {
        var strings = CreateDeclarations(missingHandle);
        var property = typeof(EmittedStringRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(strings));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(strings.CompleteEmission).Message);
        Assert.False(strings.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(strings, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(strings, property.GetValue(CreateDeclarations()));
        strings.CompleteEmission();
        AssertFrozen(strings);
    }

    [Fact]
    public void RequiredOwnerExistsBeforeEmissionAndCannotBeReplaced()
    {
        var first = new EmittedRuntime();
        var second = new EmittedRuntime();
        Assert.NotSame(first.Strings, second.Strings);
        Assert.False(first.Strings.IsComplete);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Strings))!.SetMethod);
        Assert.Throws<InvalidOperationException>(() => first.Strings.PrototypeField);
        Assert.Throws<InvalidOperationException>(() => first.Strings.PrototypePopulateMethod);
        Assert.Throws<InvalidOperationException>(() => first.Strings.CharAt);
    }

    [Fact]
    public void PrototypePopulationShellCanBeReferencedBeforeOperationAndPopulationBodies()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("string_staged"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("StagedStrings", TypeAttributes.Public);
        var strings = new EmittedStringRuntime
        {
            PrototypeField = type.DefineField("_prototype", typeof(Dictionary<string, object>), FieldAttributes.Private | FieldAttributes.Static)
        };
        var caller = type.DefineMethod("Populate", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        typeof(RuntimeEmitter).GetMethod("DefineStringPrototypePopulateShell", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(new RuntimeEmitter(TypeProvider.Runtime), [type, strings]);
        Assert.Equal(0, strings.PrototypePopulateMethod.GetILGenerator().ILOffset);
        Assert.Same(type, strings.PrototypePopulateMethod.DeclaringType);
        Assert.False(strings.IsComplete);
        Assert.Throws<InvalidOperationException>(strings.CompleteEmission);
        caller.GetILGenerator().Emit(OpCodes.Call, strings.PrototypePopulateMethod);
        caller.GetILGenerator().Emit(OpCodes.Ret);
        strings.PrototypePopulateMethod.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        bytes.Position = 0;
        Verify(bytes);
        Assembly.Load(bytes.ToArray()).GetType("StagedStrings")!.GetMethod("Populate")!.Invoke(null, null);
        Assert.False(strings.IsComplete);
    }

    [Fact]
    public void OmittedPrototypeMethodSkipsPeerReadsBeforeDeclarationsExist()
    {
        // A missing optional protocol slot must not require unrelated descriptor/function declarations.
        typeof(RuntimeEmitter).GetMethod("EmitWirePrototypeMethod", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(new RuntimeEmitter(TypeProvider.Runtime), [null, new EmittedRuntime(), null, null, null, "omitted", null, 0, true]);
    }

    [Theory]
    [InlineData("const value=1;", false)]
    [InlineData("const value=1;", true)]
    [InlineData("'abc'.slice(1);", false)]
    [InlineData("String.prototype.substring;", false)]
    [InlineData("new RegExp('value');", false)]
    [InlineData("new String('value');", false)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void CoreDeclarationsRemainRequiredWhileProtocolPeersKeepTheirFeatureGate(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        AssertFrozen(runtime.Strings);
        foreach (var property in Handles)
            Assert.Same(runtime.RuntimeClass.Type, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(runtime.Strings)).DeclaringType);
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var names = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        var features = source is null ? RuntimeFeatureSet.EmitEverything() : Detect(source);
        Assert.Equal(features.UsesRegExp, names.Contains("$RegExp"));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        Assert.Equal("__this", type.GetMethod("StringCharAt")!.GetParameters()[0].Name);
        Assert.Equal("__this", type.GetMethod("StringIterator")!.GetParameters()[0].Name);
        var prototype = Prototype(type, runtime.Strings);
        Assert.Equal(features.UsesRegExp, prototype.ContainsKey("match"));
        Assert.Equal(features.UsesRegExp, prototype.ContainsKey("replaceAll"));
        Assert.Contains("substring", prototype.Keys);
        Assert.Contains("trim", prototype.Keys);
        Assert.Equal(typeof(string), prototype["constructor"]);
    }

    [Fact]
    public void PrimitiveHelpersRetainTypedSignaturesInliningAndLocalSlowPaths()
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        Verify(bytes);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var names = new[]
        {
            "StringIndexOfPrimitive", "StringIncludesPrimitive", "StringSlicePrimitive", "StringSubstringPrimitive",
            "StringSliceFromLengthPrimitive", "StringSliceLengthPrimitive",
            "StringSubstringFromLengthPrimitive", "StringSubstringLengthPrimitive"
        };
        foreach (var name in names)
        {
            var method = type.GetMethod(name)!;
            Assert.True(method.IsPublic && method.IsStatic);
            Assert.True(method.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.AggressiveInlining));
            Assert.True(method.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.AggressiveOptimization));
            Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType == typeof(object) || parameter.ParameterType == typeof(object[]));
        }
        Assert.Equal(new[] { typeof(string), typeof(double), typeof(double), typeof(bool) },
            type.GetMethod("StringSlicePrimitive")!.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(typeof(string), type.GetMethod("StringSlicePrimitive")!.ReturnType);
        Assert.Equal(typeof(bool), type.GetMethod("StringIncludesPrimitive")!.ReturnType);
        Assert.True(type.GetMethod("StringCharCodeAt")!.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.AggressiveInlining));
        foreach (var name in new[] { "StringSliceLengthPrimitiveSlow", "StringSubstringLengthPrimitiveSlow" })
        {
            var method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.True(method.IsPrivate);
            Assert.True(method.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.NoInlining));
        }
        var integer = type.GetMethod("StringToIntegerOrInfinityPrimitive", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True(integer.IsPrivate);
        Assert.Equal(typeof(int), integer.ReturnType);
        Assert.Equal(typeof(double), Assert.Single(integer.GetParameters()).ParameterType);
    }

    [Theory]
    [InlineData(1d, 4d, "bcd", "bcd", 3d, 3d)]
    [InlineData(4d, 1d, "bcd", "", 3d, 0d)]
    [InlineData(-3d, -1d, "", "de", 0d, 2d)]
    [InlineData(double.NegativeInfinity, double.PositiveInfinity, "abcdef", "abcdef", 6d, 6d)]
    [InlineData(double.NaN, 3d, "abc", "abc", 3d, 3d)]
    [InlineData(1.9d, 4.9d, "bcd", "bcd", 3d, 3d)]
    [InlineData(99d, 100d, "", "", 0d, 0d)]
    public void SavedPrimitivePathsPreserveBoundsAndSlowPathNormalization(double start, double end,
        string substring, string slice, double substringLength, double sliceLength)
    {
        using var bytes = Save(EmitRuntime("const value=1;", false));
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        Assert.Equal(substring, Invoke(type, "StringSubstringPrimitive", "abcdef", start, end, true));
        Assert.Equal(slice, Invoke(type, "StringSlicePrimitive", "abcdef", start, end, true));
        Assert.Equal(substringLength, Invoke(type, "StringSubstringLengthPrimitive", "abcdef", start, end));
        Assert.Equal(sliceLength, Invoke(type, "StringSliceLengthPrimitive", "abcdef", start, end));
        Assert.Equal(3d, Invoke(type, "StringSliceFromLengthPrimitive", "abcdef", -3d));
        Assert.Equal(6d, Invoke(type, "StringSubstringFromLengthPrimitive", "abcdef", -3d));
        Assert.Equal(6d, Invoke(type, "StringIndexOfPrimitive", "abcdef", "", 99d));
        Assert.Equal(true, Invoke(type, "StringIncludesPrimitive", "abcdef", "", 99d));
    }

    [Fact]
    public void SavedPrototypeKeepsFunctionMetadataIdentityAndMutationAcrossRepeatedPopulation()
    {
        var runtime = EmitRuntime("String.prototype.substring;", false);
        using var bytes = Save(runtime);
        Verify(bytes);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var prototype = Prototype(type, runtime.Strings);
        var substring = prototype["substring"];
        var function = substring.GetType();
        Assert.Equal("substring", function.GetProperty("Name")!.GetValue(substring));
        Assert.Equal(2, function.GetProperty("Length")!.GetValue(substring));
        var count = prototype.Count;
        Invoke(type, runtime.Strings.PrototypePopulateMethod.Name);
        Assert.Equal(count, prototype.Count);
        Assert.Same(substring, prototype["substring"]);
        Assert.True(prototype.Remove("charAt"));
        prototype["custom"] = 7d;
        Invoke(type, runtime.Strings.PrototypePopulateMethod.Name);
        Assert.False(prototype.ContainsKey("charAt"));
        Assert.Equal(7d, prototype["custom"]);
        Assert.Same(substring, prototype["substring"]);
    }

    [Fact]
    public void ReusingEmitterKeepsDeclarationsAndMutablePrototypesIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var full = EmitRuntime(null, false, emitter);
        var first = EmitRuntime("const value=1;", false, emitter);
        var second = EmitRuntime("const value=1;", false, emitter);
        foreach (var property in Handles)
        {
            Assert.NotSame(property.GetValue(full.Strings), property.GetValue(first.Strings));
            Assert.NotSame(property.GetValue(first.Strings), property.GetValue(second.Strings));
        }
        AssertFrozen(full.Strings);
        AssertFrozen(first.Strings);
        AssertFrozen(second.Strings);
        using var firstBytes = Save(first);
        using var secondBytes = Save(second);
        Verify(secondBytes);
        var firstPrototype = Prototype(Assembly.Load(firstBytes.ToArray()).GetType("$Runtime")!, first.Strings);
        var secondPrototype = Prototype(Assembly.Load(secondBytes.ToArray()).GetType("$Runtime")!, second.Strings);
        Assert.NotSame(firstPrototype, secondPrototype);
        firstPrototype["custom"] = 7d;
        Assert.False(secondPrototype.ContainsKey("custom"));
        Assert.False(secondPrototype.ContainsKey("match"));
    }

    private static object? Invoke(Type type, string name, params object?[] args) => type.GetMethod(name)!.Invoke(null, args);

    private static Dictionary<string, object> Prototype(Type type, EmittedStringRuntime strings) =>
        Assert.IsType<Dictionary<string, object>>(type.GetField(strings.PrototypeField.Name, BindingFlags.Public | BindingFlags.Static)!.GetValue(null));

    private static EmittedStringRuntime CreateDeclarations(string? omitted = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"string_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("StringDeclarations", TypeAttributes.Public);
        var strings = new EmittedStringRuntime();
        foreach (var property in Handles)
        {
            if (property.Name == omitted) continue;
            if (property.PropertyType == typeof(FieldBuilder))
                property.SetValue(strings, type.DefineField(property.Name, typeof(object), FieldAttributes.Public));
            else
            {
                var method = type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
                method.GetILGenerator().Emit(OpCodes.Ret);
                property.SetValue(strings, method);
            }
        }
        return strings;
    }

    private static void AssertFrozen(EmittedStringRuntime strings)
    {
        Assert.True(strings.IsComplete);
        Assert.Throws<InvalidOperationException>(strings.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(strings);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(strings, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static void Verify(MemoryStream bytes)
    {
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector()
        .Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"string_core_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module, Detect(source));
    }
}
