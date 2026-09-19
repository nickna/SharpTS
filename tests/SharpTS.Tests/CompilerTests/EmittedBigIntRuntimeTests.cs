using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedBigIntRuntimeTests
{
    private static PropertyInfo[] Handles(Type type) => type.GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType)).ToArray();

    public static IEnumerable<object[]> RequiredHandleNames => Handles(typeof(EmittedBigIntRuntime))
        .Select(property => new object[] { property.Name });
    public static IEnumerable<object[]> ImplementationHandleNames => Handles(typeof(EmittedBigIntImplementation))
        .Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(RequiredHandleNames))]
    public void MissingRequiredDeclarationAllowsRepairWithoutFreezingImplementation(string name)
    {
        var owner = CreateDeclarations(requiredOmission: name);
        var property = typeof(EmittedBigIntRuntime).GetProperty(name)!;
        AssertMissing(owner, property);
        Assert.Contains($"'{name}'", Assert.Throws<InvalidOperationException>(owner.CompleteEmission).Message);
        Assert.False(owner.IsComplete);
        Assert.False(owner.RequireImplementation().IsComplete);
        property.SetValue(owner, property.GetValue(CreateDeclarations()));
        owner.CompleteEmission();
        AssertFrozen(owner);
    }

    [Theory]
    [MemberData(nameof(ImplementationHandleNames))]
    public void MissingImplementationDeclarationAllowsRepairWithoutFreezingParent(string name)
    {
        var owner = CreateDeclarations(implementationOmission: name);
        var implementation = owner.RequireImplementation();
        var property = typeof(EmittedBigIntImplementation).GetProperty(name)!;
        AssertMissing(implementation, property);
        Assert.Contains($"'{name}'", Assert.Throws<InvalidOperationException>(owner.CompleteEmission).Message);
        Assert.False(owner.IsComplete);
        Assert.False(implementation.IsComplete);
        property.SetValue(implementation, property.GetValue(CreateDeclarations().RequireImplementation()));
        owner.CompleteEmission();
        AssertFrozen(owner);
    }

    [Fact]
    public void RequiredOwnerHasExplicitOptionalAvailabilityAndCannotBeReplaced()
    {
        var first = new EmittedRuntime().BigInt;
        var second = new EmittedRuntime().BigInt;
        Assert.NotSame(first, second);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.BigInt))!.SetMethod);
        Assert.Null(first.Implementation);
        Assert.Throws<InvalidOperationException>(() => first.RequireImplementation());
        first.BeginImplementationEmission();
        var implementation = first.RequireImplementation();
        Assert.Throws<InvalidOperationException>(first.BeginImplementationEmission);
        Assert.Same(implementation, first.RequireImplementation());
        var absent = CreateDeclarations(enabled: false);
        absent.CompleteEmission();
        AssertFrozen(absent);
        Assert.Null(absent.Implementation);
        Assert.Throws<InvalidOperationException>(() => absent.RequireImplementation());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EarlyStrictConversionDeclarationFollowsExplicitFeatureSelection(bool enabled)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("bigint_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var runtime = new EmittedRuntime();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var features = Detect("const value=1;");
        features.UsesBigInt = enabled;
        typeof(RuntimeEmitter).GetField("_features", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(emitter, features);
        if (enabled) runtime.BigInt.BeginImplementationEmission();
        InvokeEmitter("DefineRuntimeClassPhase1", emitter, module, runtime);
        Assert.Throws<InvalidOperationException>(() => runtime.BigInt.PrototypeField);
        Assert.Throws<InvalidOperationException>(() => runtime.BigInt.ToNumber);
        if (enabled)
        {
            var implementation = runtime.BigInt.RequireImplementation();
            Assert.Equal(0, implementation.ToBigInt.GetILGenerator().ILOffset);
            Assert.Throws<InvalidOperationException>(() => implementation.Create);
            var caller = runtime.RuntimeType.DefineMethod("ForwardCaller", MethodAttributes.Public | MethodAttributes.Static,
                typeof(object), [typeof(object)]);
            var il = caller.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, implementation.ToBigInt);
            il.Emit(OpCodes.Ret);
            Assert.True(il.ILOffset > 0);
        }
        else
            Assert.Null(runtime.BigInt.Implementation);
        Assert.Throws<InvalidOperationException>(runtime.BigInt.CompleteEmission);
        Assert.False(runtime.BigInt.IsComplete);
    }

    [Theory]
    [InlineData("const value=1;", false, false)]
    [InlineData("const value=1;", true, false)]
    [InlineData("1n;", false, true)]
    [InlineData("1n;", true, true)]
    [InlineData("new DataView(new ArrayBuffer(16));", false, true)]
    [InlineData("new DataView(new ArrayBuffer(16));1n;", false, true)]
    [InlineData("new BigInt64Array(2);", false, true)]
    [InlineData(null, false, true)]
    [InlineData(null, true, true)]
    public void RequiredAndOptionalMetadataPreserveSavedFeatureAndHostingContracts(string? source, bool hosted, bool enabled)
    {
        var runtime = EmitRuntime(source, hosted);
        AssertFrozen(runtime.BigInt);
        Assert.Equal(enabled, runtime.BigInt.Implementation != null);
        Assert.Equal(3, Handles(typeof(EmittedBigIntRuntime)).Length);
        Assert.Equal(24, Handles(typeof(EmittedBigIntImplementation)).Length);
        foreach (var property in Handles(typeof(EmittedBigIntRuntime)))
            Assert.Same(runtime.RuntimeType, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(runtime.BigInt)).DeclaringType);
        if (runtime.BigInt.Implementation is { } implementation)
            foreach (var property in Handles(typeof(EmittedBigIntImplementation)))
                Assert.Same(runtime.RuntimeType, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(implementation)).DeclaringType);
        using var bytes = Save(runtime);
        Verify(bytes);
        using var pe = new PEReader(bytes);
        var metadata = pe.GetMetadataReader();
        var references = metadata.AssemblyReferences.Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        Assert.NotNull(type.GetField(runtime.BigInt.PrototypeField.Name));
        Assert.NotNull(type.GetMethod(runtime.BigInt.PrototypePopulateMethod.Name));
        Assert.Equal(enabled, type.GetMethod("CreateBigInt") != null);
        Assert.Equal(enabled, type.GetMethod("ToBigInt") != null);
        Assert.Equal(enabled, type.GetMethod("BigIntAsIntN") != null);
        Assert.Equal(1d, Call(type, runtime.BigInt.ToNumber, BigInteger.One));
        if (enabled)
        {
            Assert.True(type.GetMethod("ToBigInt")!.MetadataToken < type.GetMethod("CreateBigInt")!.MetadataToken);
            Assert.True(type.GetMethod("CreateBigInt")!.MetadataToken < type.GetMethod("BigIntToPrimitive")!.MetadataToken);
            Assert.True(type.GetMethod("BigIntToPrimitive")!.MetadataToken < type.GetMethod("BigIntAsIntN")!.MetadataToken);
            Assert.True(type.GetMethod(runtime.BigInt.PrototypePopulateMethod.Name)!.MetadataToken < type.GetMethod("CreateBigInt")!.MetadataToken);
            if (type.GetMethod("DataViewSetBigInt64") is { } setter)
                Assert.True(type.GetMethod("ToBigInt")!.MetadataToken < setter.MetadataToken);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DataViewAdaptersPreserveExplicitImplementationAvailability(bool enabled)
    {
        // Normal detection enables BigInt for DataView. An explicitly supplied feature
        // set can omit it; preserve the existing adapter declaration path in that case.
        var features = Detect("new DataView(new ArrayBuffer(16));");
        Assert.True(features.UsesBigInt);
        features.UsesBigInt = enabled;
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"bigint_dataview_{enabled}"), typeof(object).Assembly);
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(assembly.DefineDynamicModule("main"), features);
        Assert.Equal(enabled, runtime.BigInt.Implementation != null);
        AssertFrozen(runtime.BigInt);
        using var bytes = Save(runtime);
        Verify(bytes);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        Assert.NotNull(type.GetMethod("DataViewSetBigInt64"));
        Assert.Equal(enabled, type.GetMethod("ToBigInt") != null);
        Assert.Equal(1d, Call(type, runtime.BigInt.ToNumber, BigInteger.One));
    }

    [Theory]
    [InlineData("0", 0d)]
    [InlineData("1", 1d)]
    [InlineData("9007199254740993", 9007199254740992d)]
    [InlineData("9007199254740995", 9007199254740996d)]
    [InlineData("-9007199254740995", -9007199254740996d)]
    public void RequiredNumericAdapterPreservesBinary64RoundingWithoutBigIntFeature(string value, double expected)
    {
        var runtime = EmitRuntime("const value=1;", false);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        Assert.Null(runtime.BigInt.Implementation);
        var method = type.GetMethod(runtime.BigInt.ToNumber.Name)!;
        Assert.Equal(typeof(double), method.ReturnType);
        Assert.Equal(typeof(BigInteger), Assert.Single(method.GetParameters()).ParameterType);
        var actual = Assert.IsType<double>(Call(type, runtime.BigInt.ToNumber, BigInteger.Parse(value)));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
        Assert.Equal(double.PositiveInfinity, Call(type, runtime.BigInt.ToNumber, BigInteger.One << 1024));
        Assert.Equal(double.NegativeInfinity, Call(type, runtime.BigInt.ToNumber, -(BigInteger.One << 1024)));
    }

    [Theory]
    [InlineData(8d, "255", "-1", "255")]
    [InlineData(8d, "-1", "-1", "255")]
    [InlineData(0d, "123", "0", "0")]
    [InlineData(double.NaN, "123", "0", "0")]
    [InlineData(8.9d, "256", "0", "0")]
    public void FixedWidthStaticsPreserveTruncationAndStrictValueConversion(double width, string value, string signed, string unsigned)
    {
        var runtime = EmitRuntime("1n;", false);
        var bigInt = runtime.BigInt.RequireImplementation();
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        Assert.Equal(BigInteger.Parse(signed), Call(type, bigInt.AsIntN, width, BigInteger.Parse(value)));
        Assert.Equal(BigInteger.Parse(unsigned), Call(type, bigInt.AsUintN, width, BigInteger.Parse(value)));
        Assert.Contains("BigInt value is required", GuestFailure(() => Call(type, bigInt.AsIntN, 0d, 1d)));
        Assert.Contains("bit width", GuestFailure(() => Call(type, bigInt.AsUintN, -1d, BigInteger.One)));
    }

    [Fact]
    public void CallableStrictAndPrototypeConversionsKeepDistinctContracts()
    {
        var runtime = EmitRuntime("1n;", false);
        var bigInt = runtime.BigInt.RequireImplementation();
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        Assert.Equal(new BigInteger(42), Call(type, bigInt.Create, 42d));
        Assert.Equal(new BigInteger(5), Call(type, bigInt.Create, "0b101"));
        Assert.Equal(new BigInteger(15), Call(type, bigInt.Create, "0o17"));
        Assert.Equal(new BigInteger(16), Call(type, bigInt.Create, "0x10"));
        Assert.Equal(BigInteger.Zero, Call(type, bigInt.Create, "  "));
        Assert.Equal(BigInteger.One, Call(type, bigInt.ToBigInt, true));
        Assert.Contains("not an integer", GuestFailure(() => Call(type, bigInt.Create, 1.5d)));
        Assert.Contains("BigInt value is required", GuestFailure(() => Call(type, bigInt.ToBigInt, 42d)));
        Assert.Equal("ff", Call(type, bigInt.ToStringRadix, new BigInteger(255), 16d));
        var prototype = Assert.IsType<Dictionary<string, object>>(type.GetField(runtime.BigInt.PrototypeField.Name)!.GetValue(null));
        var valueOf = prototype["valueOf"];
        Call(type, runtime.BigInt.PrototypePopulateMethod);
        Assert.Same(valueOf, prototype["valueOf"]);
        var boxed = Call(type, runtime.BoxedPrimitives.ToObject, new BigInteger(42));
        Assert.Equal(new BigInteger(42), type.GetMethod("BigIntValueOf")!.Invoke(null, [boxed]));
        Assert.Contains("requires that 'this' be a BigInt", GuestFailure(() => type.GetMethod("BigIntValueOf")!.Invoke(null, [42d])));
        var symbols = Assert.IsAssignableFrom<IDictionary>(Call(type, runtime.Symbols.GetStorage, prototype));
        var tag = Assert.Single(symbols.Values.Cast<object>());
        Assert.Equal("BigInt", tag.GetType().GetProperty("Value")!.GetValue(tag));
        Assert.Equal(false, tag.GetType().GetProperty("Writable")!.GetValue(tag));
        Assert.Equal(false, tag.GetType().GetProperty("Enumerable")!.GetValue(tag));
        Assert.Equal(true, tag.GetType().GetProperty("Configurable")!.GetValue(tag));
    }

    [Fact]
    public void ReusedEmitterKeepsRequiredPrototypeAndOptionalImplementationInTheirOwnAssemblies()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var priorPrototypes = new List<object>();
        var priorOwners = new List<EmittedBigIntRuntime>();
        foreach (var source in new[] { "1n;", "const value=1;", "new DataView(new ArrayBuffer(16));1n;" })
        {
            var runtime = EmitRuntime(source, false, emitter);
            Assert.DoesNotContain(runtime.BigInt, priorOwners);
            priorOwners.Add(runtime.BigInt);
            using var bytes = Save(runtime);
            Verify(bytes);
            var loaded = Assembly.Load(bytes.ToArray());
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name!.StartsWith("bigint_", StringComparison.Ordinal));
            var type = loaded.GetType("$Runtime")!;
            var prototype = Assert.IsType<Dictionary<string, object>>(type.GetField(runtime.BigInt.PrototypeField.Name)!.GetValue(null));
            Assert.DoesNotContain(priorPrototypes, value => ReferenceEquals(value, prototype));
            Assert.False(prototype.ContainsKey("previousOutput"));
            prototype["previousOutput"] = true;
            priorPrototypes.Add(prototype);
            Assert.Equal(2d, Call(type, runtime.BigInt.ToNumber, new BigInteger(2)));
            if (runtime.BigInt.Implementation is { } implementation)
                Assert.Equal(new BigInteger(7), Call(type, implementation.Add, new BigInteger(3), new BigInteger(4)));
        }
    }

    private static void AssertMissing(object owner, PropertyInfo property)
    {
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(owner));
        Assert.Contains($"'{property.Name}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
    }

    private static EmittedBigIntRuntime CreateDeclarations(string? requiredOmission = null, string? implementationOmission = null, bool enabled = true)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"bigint_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("BigIntDeclarations");
        var owner = new EmittedBigIntRuntime();
        Populate(owner, requiredOmission);
        if (enabled)
        {
            owner.BeginImplementationEmission();
            Populate(owner.RequireImplementation(), implementationOmission);
        }
        return owner;

        void Populate(object target, string? omission)
        {
            foreach (var property in Handles(target.GetType()))
            {
                if (property.Name == omission) continue;
                if (property.PropertyType == typeof(FieldBuilder))
                    property.SetValue(target, type.DefineField(property.Name, typeof(object), FieldAttributes.Public | FieldAttributes.Static));
                else
                {
                    var method = type.DefineMethod(target.GetType().Name + property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
                    method.GetILGenerator().Emit(OpCodes.Ret);
                    property.SetValue(target, method);
                }
            }
        }
    }

    private static void AssertFrozen(EmittedBigIntRuntime owner)
    {
        Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.Throws<InvalidOperationException>(owner.BeginImplementationEmission);
        CheckWrites(owner);
        if (owner.Implementation is { } implementation)
        {
            Assert.True(implementation.IsComplete);
            Assert.Throws<InvalidOperationException>(implementation.CompleteEmission);
            CheckWrites(implementation);
        }
        static void CheckWrites(object target)
        {
            foreach (var property in Handles(target.GetType()))
            {
                var value = property.GetValue(target);
                Assert.NotNull(value);
                Assert.False(property.SetMethod!.IsPublic);
                var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(target, value));
                Assert.IsType<InvalidOperationException>(error.InnerException);
            }
        }
    }

    private static string GuestFailure(Action action) => Assert.Throws<TargetInvocationException>(action).InnerException!.Message;
    private static object? Call(Type type, MethodInfo method, params object?[] arguments) =>
        type.GetMethod(method.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments);
    private static void InvokeEmitter(string name, RuntimeEmitter emitter, params object[] arguments) =>
        typeof(RuntimeEmitter).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(emitter, arguments);
    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"bigint_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module, Detect(source));
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
