using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeNumericRestBindingTests
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    [Fact]
    public void NativeProducerUsesExactMethodTokensAndPreservesUnknownAndCapturedFallbacks()
    {
        const string source = """
            function add(...v: number[]): number { return v[0] + v[1] + v[2] + v[3]; }
            function plain(a: number, b: number, c: number, d: number): number { return a + b + c + d; }
            function invoke(fn: (...v: number[]) => number): number { return fn(1, 2, 3, 4); }
            const alias = add;
            console.log(invoke(alias), alias === add, invoke(plain as any), invoke(add.bind(null, 10)));
            """;
        var assembly = Compile(source);
        Assert.Equal("10 true 10 16\n", TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20)));
        var original = assembly.GetType("$Program")!.GetMethod("add")!;
        var unknown = assembly.GetType("$Program")!.GetMethod("plain")!;
        var binder = assembly.GetType("$NativeNumericRest4Binding")!.GetMethod("Bind", PrivateStatic)!;
        var bound = Assert.IsType<Func<double, double, double, double, double>>(binder.Invoke(null, [original]));
        Assert.Equal(10d, bound(1d, 2d, 3d, 4d));
        Assert.Same(original, bound.Target);
        Assert.True(bound.Method.IsAssembly);
        Assert.Null(binder.Invoke(null, [unknown]));
        Assert.Null(binder.Invoke(null, [typeof(NativeNumericRestBindingTests).GetMethod(nameof(Foreign))]));
        Assert.Null(binder.Invoke(null, [new ForeignMethodInfo()]));
        Assert.Null(binder.Invoke(null, [null]));
        Assert.Contains(Instructions(binder), instruction => instruction.Code == OpCodes.Ldtoken && Equals(instruction.Member, original));
        Assert.Contains(Instructions(binder), instruction => instruction.Code == OpCodes.Ldftn && Equals(instruction.Member, bound.Method));
        Assert.DoesNotContain(Instructions(binder), instruction => instruction.Member is MethodInfo method &&
            method.Name is "GetMethod" or "CreateDelegate" or "GetCustomAttributes");

        var function = assembly.GetType("$TSFunction")!;
        var field = function.GetField("_numericRest4", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var ordinary = Activator.CreateInstance(function, [null, original])!;
        Assert.NotNull(field.GetValue(ordinary));
        var captured = Activator.CreateInstance(function, [new object(), original])!;
        Assert.Null(field.GetValue(captured));
        var unknownWrapper = Activator.CreateInstance(function, [null, unknown])!;
        Assert.Null(field.GetValue(unknownWrapper));
        Assert.Equal(10d, function.GetMethod("Invoke")!.Invoke(unknownWrapper, [new object[] { 1d, 2d, 3d, 4d }]));
        foreach (var constructor in function.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            Assert.Contains(Instructions(constructor), instruction => Equals(instruction.Member, binder));
    }

    [Fact]
    public void LateBindingIsPerCompilationAndCompletesOnceAfterAllProducers()
    {
        var fixture = Fixture();
        Assert.False(fixture.Binding.IsComplete);
        Assert.Equal(0, fixture.Binding.ProducerCount);
        fixture.Binding.RegisterProducer(fixture.Original, fixture.Entry, fixture.Marker);
        Assert.Equal(1, fixture.Binding.ProducerCount);
        Assert.Throws<InvalidOperationException>(() => fixture.Binding.RegisterProducer(fixture.Original, fixture.Entry, fixture.Marker));
        fixture.Binding.CompleteEmission();
        Assert.True(fixture.Binding.IsComplete);
        Assert.Throws<InvalidOperationException>(() => fixture.Binding.CompleteEmission());
        Assert.Throws<InvalidOperationException>(() => fixture.Binding.RegisterProducer(fixture.Original, fixture.Entry, fixture.Marker));
        fixture.Source.CreateType();
        using var bytes = new MemoryStream(); fixture.Assembly.Save(bytes);
        var saved = Verify(bytes.ToArray());
        var method = saved.GetType("Source")!.GetMethod("Original")!;
        var bind = saved.GetType("$NativeNumericRest4Binding")!.GetMethod("Bind", PrivateStatic)!;
        var bound = Assert.IsType<Func<double, double, double, double, double>>(bind.Invoke(null, [method]));
        Assert.Equal(10d, bound(1d, 2d, 3d, 4d));
        var another = Compile("function add(...v: number[]): number { return v[0] + v[1] + v[2] + v[3]; } console.log(add(1, 2, 3, 4));");
        Assert.Null(another.GetType("$NativeNumericRest4Binding")!.GetMethod("Bind", PrivateStatic)!.Invoke(null, [method]));
    }

    [Theory]
    [InlineData("foreign module")]
    [InlineData("different owner")]
    [InlineData("generic source")]
    [InlineData("generic entry")]
    [InlineData("instance entry")]
    [InlineData("private entry")]
    [InlineData("entry parameters")]
    [InlineData("entry return")]
    public void InvalidProducerDoesNotPublishAMarkerOrPartialBinding(string invalid)
    {
        var fixture = Fixture();
        var original = fixture.Original;
        var owner = invalid == "different owner" ? fixture.Module.DefineType("Other") : fixture.Source;
        if (invalid == "foreign module")
            owner = NewAssembly().DefineDynamicModule("other").DefineType("Foreign");
        var entry = owner.DefineMethod("Invalid", (invalid == "private entry" ? MethodAttributes.Private : MethodAttributes.Assembly)
            | (invalid == "instance entry" ? 0 : MethodAttributes.Static),
            invalid == "entry return" ? typeof(bool) : typeof(double),
            invalid == "entry parameters" ? [typeof(double)] : [typeof(object), typeof(double), typeof(double), typeof(double), typeof(double)]);
        if (invalid == "generic source") original.DefineGenericParameters("T");
        if (invalid == "generic entry") entry.DefineGenericParameters("T");
        Assert.Throws<InvalidOperationException>(() => fixture.Binding.RegisterProducer(original, entry, fixture.Marker));
        Assert.Equal(0, fixture.Binding.ProducerCount);
    }

    public static double Foreign(List<object> values) => 0d;

    // Unknown MethodInfo implementations must not choose a source entry by
    // overriding equality. The finite binder owns the comparison's left side.
    private sealed class ForeignMethodInfo : MethodInfo
    {
        public override bool Equals(object? obj) => true;
        public override int GetHashCode() => 0;
        public override string Name => "Foreign";
        public override Type DeclaringType => typeof(NativeNumericRestBindingTests);
        public override Type ReflectedType => DeclaringType;
        public override Type ReturnType => typeof(double);
        public override ICustomAttributeProvider ReturnTypeCustomAttributes => typeof(double);
        public override MethodAttributes Attributes => MethodAttributes.Public | MethodAttributes.Static;
        public override RuntimeMethodHandle MethodHandle => default;
        public override MethodInfo GetBaseDefinition() => this;
        public override MethodImplAttributes GetMethodImplementationFlags() => MethodImplAttributes.IL;
        public override ParameterInfo[] GetParameters() => [];
        public override object[] GetCustomAttributes(bool inherit) => [];
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => [];
        public override bool IsDefined(Type attributeType, bool inherit) => false;
        public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters,
            System.Globalization.CultureInfo? culture) => null;
    }

    private static (PersistedAssemblyBuilder Assembly, ModuleBuilder Module, TypeBuilder Source,
        NativeNumericRest4Binding Binding, MethodBuilder Original, MethodBuilder Entry, ConstructorBuilder Marker) Fixture()
    {
        var assembly = NewAssembly(); var module = assembly.DefineDynamicModule("main");
        var marker = module.DefineType("$NumericRest4", TypeAttributes.Public | TypeAttributes.Sealed, typeof(Attribute));
        var constructor = marker.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(string)]);
        var il = constructor.GetILGenerator(); il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Attribute).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!);
        il.Emit(OpCodes.Ret); marker.CreateType();
        var source = module.DefineType("Source", TypeAttributes.Public);
        var original = source.DefineMethod("Original", MethodAttributes.Public | MethodAttributes.Static, typeof(double), [typeof(List<object>)]);
        il = original.GetILGenerator(); il.Emit(OpCodes.Ldc_R8, 10d); il.Emit(OpCodes.Ret);
        var entry = source.DefineMethod("Entry", MethodAttributes.Assembly | MethodAttributes.Static, typeof(double),
            [typeof(object), typeof(double), typeof(double), typeof(double), typeof(double)]);
        il = entry.GetILGenerator(); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ldarg, 4); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ret);
        return (assembly, module, source, new NativeNumericRest4Binding(module, TypeProvider.Runtime), original, entry, constructor);
    }

    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName("native_rest_binding_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var compiler = new ILCompiler("native_rest_binding_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
        return Verify(compiler.SaveToBytes());
    }
    private static Assembly Verify(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream));
        return Assembly.Load(bytes);
    }
}
