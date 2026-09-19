using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedReceiverGuardRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly PropertyInfo Handle = typeof(EmittedReceiverGuardRuntime).GetProperty("RequireObjectCoercibleThis")!;

    [Fact]
    public void DeclarationRejectsMissingNullAndDuplicateValues()
    {
        var owner = new EmittedRuntime().ReceiverGuard;
        Assert.Throws<InvalidOperationException>(() => owner.RequireObjectCoercibleThis);
        Expect<ArgumentNullException>(() => Handle.SetValue(owner, null));
        var value = NewAssembly().DefineDynamicModule("main").DefineType("Handles")
            .DefineMethod("Guard", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object)]);
        Handle.SetValue(owner, value);
        Assert.Same(value, owner.RequireObjectCoercibleThis);
        Expect<InvalidOperationException>(() => Handle.SetValue(owner, value));
        Assert.False(owner.IsComplete);
    }

    [Fact]
    public void MissingDeclarationCanBeSuppliedAfterFailedCompletionThenMetadataFreezes()
    {
        var owner = new EmittedRuntime().ReceiverGuard;
        Expect<InvalidOperationException>(() => Complete(owner));
        Assert.False(owner.IsComplete);
        var value = NewAssembly().DefineDynamicModule("main").DefineType("Handles")
            .DefineMethod("Guard", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object)]);
        Handle.SetValue(owner, value); Complete(owner);
        Assert.True(owner.IsComplete);
        Expect<InvalidOperationException>(() => Complete(owner));
        Expect<InvalidOperationException>(() => Handle.SetValue(owner, value));
    }

    [Fact]
    public void ScopedHelperUsesExactTypesErrorConstructorAndWrapperWithoutOtherOwners()
    {
        Assert.NotSame(new EmittedRuntime().ReceiverGuard, new EmittedRuntime().ReceiverGuard);
        Assert.Null(typeof(EmittedRuntime).GetProperty("ReceiverGuard")!.SetMethod);
        Assert.Null(typeof(EmittedRuntime).GetProperty("RequireObjectCoercibleThis"));
        var helper = typeof(RuntimeEmitter).GetMethod("EmitRequireObjectCoercibleThis", Members)!;
        Assert.Equal(new[] { typeof(TypeBuilder), typeof(EmittedReceiverGuardRuntime), typeof(Type), typeof(Type), typeof(MethodInfo), typeof(ConstructorInfo) },
            helper.GetParameters().Select(p => p.ParameterType));
        var builder = NewAssembly(); var type = builder.DefineDynamicModule("main").DefineType("Probe", TypeAttributes.Public);
        var calls = type.DefineField("Calls", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var wrapper = type.DefineMethod("Wrap", MethodAttributes.Public | MethodAttributes.Static, typeof(Exception), [typeof(object)]);
        var il = wrapper.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, calls); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stsfld, calls);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, typeof(Exception)); il.Emit(OpCodes.Ret);
        var owner = new EmittedRuntime().ReceiverGuard;
        helper.Invoke(new RuntimeEmitter(TypeProvider.Runtime),
            [type, owner, typeof(string), typeof(Uri), wrapper, typeof(ArgumentException).GetConstructor([typeof(string)])!]);
        Assert.Same(type, owner.RequireObjectCoercibleThis.DeclaringType); Assert.False(owner.IsComplete);
        type.CreateType(); Complete(owner);
        var loaded = SaveVerifyLoad(builder).GetType("Probe")!; var guard = loaded.GetMethod("RequireObjectCoercibleThis")!;
        var accepted = new object(); Assert.Same(accepted, guard.Invoke(null, [accepted]));
        foreach (object? input in new object?[] { null, "selected undefined type", new Uri("https://example.test") })
        {
            var error = Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(() => guard.Invoke(null, [input])).InnerException);
            Assert.Equal(input is Uri ? "Cannot convert a Symbol value to a string" : "Cannot convert undefined or null to object", error.Message);
        }
        Assert.Equal(3, loaded.GetField("Calls")!.GetValue(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesOwnReflectionCacheReceiverIdentityAndGuestErrors(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedReceiverGuardRuntime>(); var handles = new HashSet<MethodBuilder>();
        foreach (string source in new[] { "const n=1;", "new Map();Buffer.from('x');", "const n=2;" })
        {
            var builder = NewAssembly(); var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), new RuntimeFeatureDetector().Detect(statements));
            var owner = runtime.ReceiverGuard;
            Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete); Assert.True(handles.Add(owner.RequireObjectCoercibleThis));
            Assert.Same(builder, owner.RequireObjectCoercibleThis.Module.Assembly);
            Assert.Same(runtime.RuntimeClass.Type, owner.RequireObjectCoercibleThis.DeclaringType);
            var loaded = SaveVerifyLoad(builder); var type = loaded.GetType("$Runtime")!; var guard = type.GetMethod("RequireObjectCoercibleThis")!;
            Assert.True(guard.IsPublic && guard.IsStatic); Assert.Equal(typeof(object), guard.ReturnType);
            Assert.Equal(new[] { typeof(object) }, guard.GetParameters().Select(p => p.ParameterType));
            Assert.True(guard.MetadataToken < type.GetMethod("ArrayJoin")!.MetadataToken);
            object? ReadField(FieldInfo field) => loaded.GetType(field.DeclaringType!.FullName!)!
                .GetField(field.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            var undefined = ReadField(runtime.Sentinels.UndefinedInstance)!; var symbol = ReadField(runtime.Symbols.Iterator)!;
            foreach (object value in new object[] { "abc", "", 123.0, true, new object(), new System.Numerics.BigInteger(7) })
                Assert.Same(value, guard.Invoke(null, [value]));
            foreach (object? value in new object?[] { null, undefined, symbol })
                AssertGuestError(() => guard.Invoke(null, [value]), ReferenceEquals(value, symbol));

            var function = loaded.GetType("$TSFunction")!;
            var cache = function.GetField("_requireObjectCoercibleThisCache", BindingFlags.NonPublic | BindingFlags.Static)!;
            var coerce = function.GetMethod("CoercePrimitiveArgs", BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Null(cache.GetValue(null));
            var parameters = typeof(EmittedReceiverGuardRuntimeTests).GetMethod(nameof(ReceiverEcho))!.GetParameters();
            object?[] numberArgs = [123.0]; coerce.Invoke(null, [parameters, numberArgs]);
            Assert.Equal("123", Assert.IsType<string>(numberArgs[0]));
            var resolved = Assert.IsAssignableFrom<MethodInfo>(cache.GetValue(null));
            Assert.Same(type, resolved.DeclaringType); Assert.Equal(guard.MetadataToken, resolved.MetadataToken);
            foreach (object? value in new object?[] { null, undefined, symbol })
            {
                object?[] receiverArgs = [value];
                AssertGuestError(() => coerce.Invoke(null, [parameters, receiverArgs]), ReferenceEquals(value, symbol));
            }
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    public static string ReceiverEcho(string __this) => __this;
    private static void AssertGuestError(Action action, bool symbol)
    {
        var error = Assert.Throws<TargetInvocationException>(action).GetBaseException();
        Assert.Equal("$ThrownValueException", error.GetType().Name);
        Assert.Equal("$TypeError", error.GetType().GetProperty("Value")!.GetValue(error)!.GetType().Name);
        Assert.Contains(symbol ? "Cannot convert a Symbol value to a string" : "Cannot convert undefined or null to object", error.Message);
    }
    private static void Expect<T>(Action action) where T : Exception =>
        Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static void Complete(EmittedReceiverGuardRuntime owner) =>
        typeof(EmittedReceiverGuardRuntime).GetMethod("CompleteEmission", Members)!.Invoke(owner, null);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"receiver_guard_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
}
