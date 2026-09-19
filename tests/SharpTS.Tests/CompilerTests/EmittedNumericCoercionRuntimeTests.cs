using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedNumericCoercionRuntimeTests
{
    private static PropertyInfo[] Handles => typeof(EmittedNumericCoercionRuntime).GetProperties()
        .Where(property => property.PropertyType == typeof(MethodBuilder)).ToArray();

    public static IEnumerable<object[]> RequiredHandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(RequiredHandleNames))]
    public void MissingDeclarationAllowsRepairAndCompletionFreezesEveryHandle(string name)
    {
        var owner = CreateDeclarations(name);
        var property = typeof(EmittedNumericCoercionRuntime).GetProperty(name)!;
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
        var first = new EmittedRuntime().NumericCoercion;
        Assert.NotSame(first, new EmittedRuntime().NumericCoercion);
        Assert.False(first.IsComplete);
        Assert.Equal(5, Handles.Length);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.NumericCoercion))!.SetMethod);
    }

    [Fact]
    public void PhaseOneDeclarationsSupportForwardCallsBeforeLaterNumericDeclarations()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("numeric_staged"), typeof(object).Assembly);
        var runtime = new EmittedRuntime();
        runtime.BigInt.BeginImplementationEmission();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        typeof(RuntimeEmitter).GetMethod("DefineRuntimeClassPhase1", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(emitter, [assembly.DefineDynamicModule("main"), runtime]);
        var owner = runtime.NumericCoercion;
        foreach (var method in new[] { owner.ToNumber, owner.JsToInt32 })
        {
            Assert.Same(runtime.RuntimeClass.Type, method.DeclaringType);
            Assert.Equal(0, method.GetILGenerator().ILOffset);
            var caller = runtime.RuntimeClass.Type.DefineMethod("Forward" + method.Name,
                MethodAttributes.Public | MethodAttributes.Static, method.ReturnType, [typeof(object)]);
            var il = caller.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, method);
            il.Emit(OpCodes.Ret);
            Assert.True(il.ILOffset > 0);
        }
        Assert.Throws<InvalidOperationException>(() => owner.ConvertToNumber);
        Assert.Throws<InvalidOperationException>(() => owner.JsNumberToInt32);
        Assert.Throws<InvalidOperationException>(() => owner.ToIntegerOrInfinity);
        emitter.DeclareConvertToNumber(runtime.RuntimeClass.Type, owner);
        Assert.Equal(0, owner.ConvertToNumber.GetILGenerator().ILOffset);
        Assert.Contains("'JsNumberToInt32'", Assert.Throws<InvalidOperationException>(owner.CompleteEmission).Message);
        Assert.False(owner.IsComplete);
    }

    [Theory]
    [InlineData("const value=1;", false)]
    [InlineData("Number('2');", false)]
    [InlineData("const value=1;", true)]
    [InlineData("/x/;", false)]
    [InlineData("1n;", false)]
    [InlineData("/x/;1n;", false)]
    [InlineData("/x/;1n;", true)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void RequiredMetadataPreservesFeatureSelectionHostingSignaturesAndOrder(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        var owner = runtime.NumericCoercion;
        AssertFrozen(owner);
        foreach (var property in Handles)
            Assert.Same(runtime.RuntimeClass.Type, Assert.IsAssignableFrom<MethodInfo>(property.GetValue(owner)).DeclaringType);
        using var bytes = Save(runtime);
        Verify(bytes);
        var assembly = Assembly.Load(bytes.ToArray());
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        var type = assembly.GetType("$Runtime")!;
        foreach (var (name, result, parameters) in new (string, Type, Type[])[]
        {
            ("ToNumber", typeof(double), [typeof(object)]),
            ("ConvertToNumber", typeof(double), [typeof(object)]),
            ("JsNumberToInt32", typeof(int), [typeof(double)]),
            ("JsToInt32", typeof(int), [typeof(object)]),
            ("ToIntegerOrInfinity", typeof(int), [typeof(object), typeof(int)])
        })
        {
            var method = type.GetMethod(name)!;
            Assert.True(method.IsPublic && method.IsStatic);
            Assert.Equal(result, method.ReturnType);
            Assert.Equal(parameters, method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        }
        Assert.True(type.GetMethod("JsNumberToInt32")!.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.AggressiveInlining));
        var order = new[] { "ToNumber", "JsToInt32", "ConvertToNumber", "JsNumberToInt32", "ToIntegerOrInfinity" }
            .Select(name => type.GetMethod(name)!.MetadataToken).ToArray();
        Assert.Equal(order.OrderBy(token => token).ToArray(), order);
        Assert.Equal(2d, Call(type, owner.ToNumber, "2"));
        Assert.Equal(1d, Call(type, owner.ConvertToNumber, BigInteger.One));
    }

    [Fact]
    public void PrimitiveConversionsKeepSignedZeroAndExplicitBigIntDistinctionWithoutOptionalImplementation()
    {
        var runtime = EmitRuntime("const value=1;");
        Assert.Null(runtime.BigInt.Implementation);
        using var bytes = Save(runtime);
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$Runtime")!;
        var undefined = Undefined(assembly);
        foreach (var method in new[] { runtime.NumericCoercion.ToNumber, runtime.NumericCoercion.ConvertToNumber })
        {
            foreach (var (value, expected) in new (object?, double)[]
            {
                (null, 0d), (undefined, double.NaN), (false, 0d), (true, 1d),
                (0d, 0d), (-0d, -0d), (double.NaN, double.NaN),
                (double.PositiveInfinity, double.PositiveInfinity), (double.NegativeInfinity, double.NegativeInfinity),
                ("", 0d), ("  ", 0d), ("-0", -0d), ("0xff", 255d), ("0b101", 5d),
                ("0o17", 15d), ("1e2", 100d), ("bad", double.NaN),
                (new List<object>(), 0d), (new List<object> { 4d }, 4d),
                (new List<object> { 1d, 2d }, double.NaN)
            })
            {
                var actual = Assert.IsType<double>(Call(type, method, value));
                if (double.IsNaN(expected)) Assert.True(double.IsNaN(actual));
                else Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
            }
            Assert.Equal(3d, Call(type, method, Call(type, runtime.BoxedPrimitives.ToObject, 3d)));
        }
        Assert.Equal(1d, Call(type, runtime.NumericCoercion.ConvertToNumber, BigInteger.One));
        var error = Assert.Throws<TargetInvocationException>(() => Call(type, runtime.NumericCoercion.ToNumber, BigInteger.One));
        Assert.Contains("BigInt", error.InnerException!.Message);
    }

    [Fact]
    public void NativeAndDynamicInt32ConversionsAgreeAtWrapTruncationAndNonFiniteBoundaries()
    {
        var runtime = EmitRuntime("const value=1;");
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        foreach (var (value, expected) in new (double, int)[]
        {
            (0d, 0), (-0d, 0), (double.NaN, 0), (double.PositiveInfinity, 0), (double.NegativeInfinity, 0),
            (1.9, 1), (-1.9, -1), (2147483647, int.MaxValue), (2147483648, int.MinValue),
            (2147483649, int.MinValue + 1), (-2147483648, int.MinValue), (-2147483649, int.MaxValue),
            (4294967295, -1), (4294967296, 0), (4294967297, 1),
            (-4294967295, 1), (-4294967296, 0), (-4294967297, -1)
        })
        {
            Assert.Equal(expected, Call(type, runtime.NumericCoercion.JsNumberToInt32, value));
            Assert.Equal(expected, Call(type, runtime.NumericCoercion.JsToInt32, value));
        }
        Assert.Equal(-1, Call(type, runtime.NumericCoercion.JsToInt32, "4294967295"));
    }

    [Fact]
    public void IntegerOrInfinityDistinguishesUndefinedDefaultFromNullAndTruncatesFiniteValues()
    {
        var runtime = EmitRuntime("const value=1;");
        using var bytes = Save(runtime);
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$Runtime")!;
        foreach (var (value, expected) in new (object?, int)[]
        {
            (Undefined(assembly), 17), (null, 0), (double.NaN, 0), (-0d, 0),
            (double.PositiveInfinity, int.MaxValue), (double.NegativeInfinity, int.MinValue),
            (1.9, 1), (-1.9, -1), ("2.9", 2), (true, 1)
        })
            Assert.Equal(expected, Call(type, runtime.NumericCoercion.ToIntegerOrInfinity, value, 17));
    }

    [Fact]
    public void ReusedEmitterKeepsIndirectNumberLookupAndDeclarationsInTheirOwnSavedAssemblies()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var owners = new List<EmittedNumericCoercionRuntime>();
        var resolvedMethods = new List<MethodInfo>();
        foreach (var source in new[] { "/x/;1n;", "const value=1;", "/x/;1n;" })
        {
            var runtime = EmitRuntime(source, emitter: emitter);
            Assert.DoesNotContain(runtime.NumericCoercion, owners);
            owners.Add(runtime.NumericCoercion);
            AssertFrozen(runtime.NumericCoercion);
            using var bytes = Save(runtime);
            Verify(bytes);
            var assembly = Assembly.Load(bytes.ToArray());
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("numeric_", StringComparison.Ordinal));
            var type = assembly.GetType("$Runtime")!;
            var wrapper = assembly.GetType("$TSFunction")!;
            var cache = wrapper.GetField("_toNumberCache", BindingFlags.NonPublic | BindingFlags.Static)!;
            var coerce = wrapper.GetMethod("CoercePrimitiveArgs", BindingFlags.NonPublic | BindingFlags.Static)!;
            var parameters = type.GetMethod("JsNumberToInt32")!.GetParameters();
            object[] arguments = ["7"];
            coerce.Invoke(null, [parameters, arguments]);
            Assert.Equal(7d, arguments[0]);
            var resolved = Assert.IsAssignableFrom<MethodInfo>(cache.GetValue(null));
            Assert.Same(assembly, resolved.DeclaringType!.Assembly);
            Assert.Equal(type.GetMethod("ToNumber"), resolved);
            Assert.DoesNotContain(resolved, resolvedMethods);
            resolvedMethods.Add(resolved);
            arguments[0] = "8";
            coerce.Invoke(null, [parameters, arguments]);
            Assert.Equal(8d, arguments[0]);
            Assert.Same(resolved, cache.GetValue(null));
            Assert.Equal(1d, Call(type, runtime.NumericCoercion.ConvertToNumber, BigInteger.One));
        }
    }

    private static object? Undefined(Assembly assembly) =>
        assembly.GetType("$Undefined")!.GetField("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);

    private static EmittedNumericCoercionRuntime CreateDeclarations(string? omission = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"numeric_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("NumericDeclarations");
        var owner = new EmittedNumericCoercionRuntime();
        foreach (var property in Handles)
        {
            if (property.Name == omission) continue;
            var method = type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
            method.GetILGenerator().Emit(OpCodes.Ret);
            property.SetValue(owner, method);
        }
        return owner;
    }

    private static void AssertFrozen(EmittedNumericCoercionRuntime owner)
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

    private static EmittedRuntime EmitRuntime(string? source, bool hosted = false, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"numeric_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module,
            new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()));
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(bytes);
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
