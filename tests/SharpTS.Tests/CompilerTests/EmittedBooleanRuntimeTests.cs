using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedBooleanRuntimeTests
{
    private static PropertyInfo[] Handles => typeof(EmittedBooleanRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType)).ToArray();

    public static IEnumerable<object[]> RequiredHandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(RequiredHandleNames))]
    public void MissingDeclarationAllowsRepairAndCompletionFreezesEveryHandle(string name)
    {
        var owner = CreateDeclarations(name);
        var property = typeof(EmittedBooleanRuntime).GetProperty(name)!;
        var missing = Assert.Throws<TargetInvocationException>(() => property.GetValue(owner));
        Assert.Contains($"'{name}'", Assert.IsType<InvalidOperationException>(missing.InnerException).Message);
        var nullWrite = Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, null));
        Assert.IsType<ArgumentNullException>(nullWrite.InnerException);
        Assert.Contains($"'{name}'", Assert.Throws<InvalidOperationException>(owner.CompleteEmission).Message);
        Assert.False(owner.IsComplete);
        property.SetValue(owner, property.GetValue(CreateDeclarations()));
        owner.CompleteEmission();
        AssertFrozen(owner);
    }

    [Fact]
    public void RequiredOwnerIsPerCompilationAndCannotBeReplaced()
    {
        var first = new EmittedRuntime().Booleans;
        var second = new EmittedRuntime().Booleans;
        Assert.NotSame(first, second);
        Assert.False(first.IsComplete);
        Assert.Equal(3, Handles.Length);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Booleans))!.SetMethod);
    }

    [Fact]
    public void TruthinessCanBeReferencedBeforeItsBodyAndPrototypeDeclarations()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("boolean_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var runtime = new EmittedRuntime();
        runtime.BigInt.BeginImplementationEmission();
        typeof(RuntimeEmitter).GetMethod("DefineRuntimeClassPhase1", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(new RuntimeEmitter(TypeProvider.Runtime), [module, runtime]);
        Assert.Same(runtime.RuntimeType, runtime.Booleans.IsTruthy.DeclaringType);
        Assert.Equal(0, runtime.Booleans.IsTruthy.GetILGenerator().ILOffset);
        Assert.Throws<InvalidOperationException>(() => runtime.Booleans.PrototypeField);
        Assert.Throws<InvalidOperationException>(() => runtime.Booleans.PrototypePopulateMethod);
        var caller = runtime.RuntimeType.DefineMethod("ForwardTruthiness", MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool), [typeof(object)]);
        var il = caller.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Booleans.IsTruthy);
        il.Emit(OpCodes.Ret);
        Assert.True(il.ILOffset > 0);
        Assert.Contains("'PrototypeField'", Assert.Throws<InvalidOperationException>(runtime.Booleans.CompleteEmission).Message);
        Assert.False(runtime.Booleans.IsComplete);
    }

    [Theory]
    [InlineData("const value=1;", false)]
    [InlineData("Boolean(false);", false)]
    [InlineData("const value=1;", true)]
    [InlineData("/x/;", false)]
    [InlineData("1n;", false)]
    [InlineData("/x/;1n;", false)]
    [InlineData("/x/;1n;", true)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void RequiredMetadataPreservesSavedSelectionHostingAndDeclarationOrder(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        AssertFrozen(runtime.Booleans);
        foreach (var property in Handles)
            Assert.Same(runtime.RuntimeType, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(runtime.Booleans)).DeclaringType);
        using var bytes = Save(runtime);
        Verify(bytes);
        using var pe = new PEReader(bytes);
        var metadata = pe.GetMetadataReader();
        var references = metadata.AssemblyReferences.Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var truthiness = type.GetMethod(runtime.Booleans.IsTruthy.Name)!;
        Assert.Equal(typeof(bool), truthiness.ReturnType);
        Assert.Equal(typeof(object), Assert.Single(truthiness.GetParameters()).ParameterType);
        var populate = type.GetMethod(runtime.Booleans.PrototypePopulateMethod.Name)!;
        Assert.True(truthiness.MetadataToken < populate.MetadataToken);
        Assert.True(populate.MetadataToken < type.GetMethod("BooleanToString")!.MetadataToken);
        Assert.True(type.GetMethod("BooleanToString")!.MetadataToken < type.GetMethod("BooleanValueOf")!.MetadataToken);
        var field = type.GetField(runtime.Booleans.PrototypeField.Name)!;
        Assert.True(field.IsPublic && field.IsStatic);
        Assert.IsType<Dictionary<string, object>>(field.GetValue(null));
        Assert.Equal(false, Call(type, runtime.Booleans.IsTruthy, 0d));
        Assert.Equal(true, Call(type, runtime.Booleans.IsTruthy, new object()));
    }

    [Fact]
    public void TruthinessKeepsPrimitiveAndObjectContractsWithoutOptionalBigIntOperations()
    {
        var runtime = EmitRuntime("const value=1;", false);
        Assert.Null(runtime.BigInt.Implementation);
        using var bytes = Save(runtime);
        var loaded = Assembly.Load(bytes.ToArray());
        var type = loaded.GetType("$Runtime")!;
        var undefined = loaded.GetType("$Undefined")!.GetField("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
        foreach (var (value, expected) in new (object?, bool)[]
        {
            (null, false), (undefined, false), (false, false), (true, true),
            (0d, false), (-0d, false), (double.NaN, false),
            (double.PositiveInfinity, true), (double.NegativeInfinity, true),
            ("", false), ("0", true), (BigInteger.Zero, false), (BigInteger.One, true),
            (new object(), true), (new List<object>(), true), (new Dictionary<string, object>(), true)
        })
            Assert.Equal(expected, Call(type, runtime.Booleans.IsTruthy, value));
        var boxedFalse = Call(type, runtime.BoxedPrimitives.ToObject, false);
        Assert.Equal(true, Call(type, runtime.Booleans.IsTruthy, boxedFalse));
        Assert.Equal(false, type.GetMethod("BooleanValueOf")!.Invoke(null, [boxedFalse]));
    }

    [Fact]
    public void PrototypePopulationKeepsIdentityBrandAndGuestMutabilityAfterMetadataFreezes()
    {
        var runtime = EmitRuntime("Boolean(false);", false);
        AssertFrozen(runtime.Booleans);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var prototype = Assert.IsType<Dictionary<string, object>>(type.GetField(runtime.Booleans.PrototypeField.Name)!.GetValue(null));
        var valueOf = prototype["valueOf"];
        var toString = prototype["toString"];
        Assert.Same(typeof(bool), prototype["constructor"]);
        prototype["extra"] = 9d;
        Call(type, runtime.Booleans.PrototypePopulateMethod);
        Assert.Same(valueOf, prototype["valueOf"]);
        Assert.Same(toString, prototype["toString"]);
        Assert.Equal(9d, prototype["extra"]);
        Assert.Equal(false, type.GetMethod("BooleanValueOf")!.Invoke(null, [prototype]));
        Assert.Equal("false", type.GetMethod("BooleanToString")!.Invoke(null, [prototype]));
        foreach (var value in new[] { false, true })
        {
            var boxed = Call(type, runtime.BoxedPrimitives.ToObject, value);
            Assert.Equal(value, type.GetMethod("BooleanValueOf")!.Invoke(null, [boxed]));
            Assert.Equal(value ? "true" : "false", type.GetMethod("BooleanToString")!.Invoke(null, [boxed]));
        }
    }

    [Fact]
    public void PrototypeHelpersRejectNonBooleanBrands()
    {
        var runtime = EmitRuntime("Boolean(false);", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var boxedNumber = Call(type, runtime.BoxedPrimitives.ToObject, 1d);
        foreach (var value in new object?[] { null, 0d, "", new object(), new Dictionary<string, object> { ["__primitiveValue"] = false }, boxedNumber })
            foreach (var name in new[] { "BooleanToString", "BooleanValueOf" })
            {
                var error = Assert.Throws<TargetInvocationException>(() => type.GetMethod(name)!.Invoke(null, [value]));
                Assert.Contains("requires a Boolean this value", error.InnerException!.Message);
            }
    }

    [Fact]
    public void ReusedEmitterKeepsTruthinessAndMutablePrototypesInTheirOwnAssemblies()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var prototypes = new List<object>();
        var owners = new List<EmittedBooleanRuntime>();
        foreach (var source in new[] { "/x/;1n;", "const value=1;", "/x/;1n;" })
        {
            var runtime = EmitRuntime(source, false, emitter);
            Assert.DoesNotContain(runtime.Booleans, owners);
            owners.Add(runtime.Booleans);
            AssertFrozen(runtime.Booleans);
            using var bytes = Save(runtime);
            Verify(bytes);
            var loaded = Assembly.Load(bytes.ToArray());
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("boolean_", StringComparison.Ordinal));
            var type = loaded.GetType("$Runtime")!;
            var prototype = Assert.IsType<Dictionary<string, object>>(type.GetField(runtime.Booleans.PrototypeField.Name)!.GetValue(null));
            Assert.DoesNotContain(prototypes, value => ReferenceEquals(value, prototype));
            Assert.False(prototype.ContainsKey("previousOutput"));
            prototype["previousOutput"] = true;
            prototypes.Add(prototype);
            Assert.Equal(false, Call(type, runtime.Booleans.IsTruthy, BigInteger.Zero));
            Assert.Equal(true, Call(type, runtime.Booleans.IsTruthy, BigInteger.One));
            Assert.Equal(false, type.GetMethod("BooleanValueOf")!.Invoke(null, [prototype]));
        }
    }

    private static EmittedBooleanRuntime CreateDeclarations(string? omission = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"boolean_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("BooleanDeclarations");
        var owner = new EmittedBooleanRuntime();
        foreach (var property in Handles)
        {
            if (property.Name == omission) continue;
            if (property.PropertyType == typeof(FieldBuilder))
                property.SetValue(owner, type.DefineField(property.Name, typeof(object), FieldAttributes.Public | FieldAttributes.Static));
            else
            {
                var method = type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
                method.GetILGenerator().Emit(OpCodes.Ret);
                property.SetValue(owner, method);
            }
        }
        return owner;
    }

    private static void AssertFrozen(EmittedBooleanRuntime owner)
    {
        Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(owner);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static object? Call(Type type, MethodInfo method, params object?[] arguments) =>
        type.GetMethod(method.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments);

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"boolean_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module,
            new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()));
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeType.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    private static void Verify(MemoryStream bytes)
    {
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        bytes.Position = 0;
    }
}
