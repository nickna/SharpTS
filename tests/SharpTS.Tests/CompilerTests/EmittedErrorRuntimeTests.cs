using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedErrorRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static PropertyInfo[] Handles => typeof(EmittedErrorRuntime).GetProperties()
        .Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).ToArray();
    private static readonly string[] Stages = ["CreateExceptionBody", "CreateFromTypeBody", "PrototypeBody", "NativePrototypeBodies"];
    public static IEnumerable<object[]> Declarations => Handles.Select(p => new object[] { p.Name });
    public static IEnumerable<object[]> BodyStages => Stages.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingDeclarationIsRepairableAndCompletionFreezesMetadata(string missing)
    {
        var owner = new EmittedErrorRuntime(); var property = typeof(EmittedErrorRuntime).GetProperty(missing)!;
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        Fill(owner, missing); MarkStages(owner);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        var handle = Handle(property.PropertyType); property.SetValue(owner, handle);
        Assert.Same(handle, property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, Handle(property.PropertyType)));
        Assert.Same(handle, property.GetValue(owner)); owner.CompleteEmission(); AssertFrozen(owner);
    }

    [Theory]
    [MemberData(nameof(BodyStages))]
    public void ForwardDeclarationsRemainAvailableUntilEveryBodyStageCompletes(string missing)
    {
        var owner = new EmittedErrorRuntime(); Fill(owner); MarkStages(owner, missing);
        var create = owner.CreateException; var factory = owner.CreateErrorFromTypeOrNull;
        var prototype = owner.PrototypePopulate; var nativePrototype = owner.TypeErrorPrototypePopulate;
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        Assert.Same(create, owner.CreateException); Assert.Same(factory, owner.CreateErrorFromTypeOrNull);
        Assert.Same(prototype, owner.PrototypePopulate); Assert.Same(nativePrototype, owner.TypeErrorPrototypePopulate);
        Mark(owner, missing); Expect<InvalidOperationException>(() => Mark(owner, missing));
        owner.CompleteEmission(); AssertFrozen(owner); Expect<InvalidOperationException>(() => Mark(owner, missing));
    }

    [Fact]
    public void RequiredOwnerBelongsToOneCompilation()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.Errors, second.Errors); Assert.False(first.Errors.IsComplete);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Errors))!.SetMethod);
        Assert.Equal(68, Handles.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesErrorIdentityAndIsolatesPrototypes(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new List<EmittedErrorRuntime>(); var prototypes = new List<object>();
        foreach (bool promise in new[] { false, true, false, true })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(promise));
            var owner = runtime.Errors; Assert.DoesNotContain(owner, owners); owners.Add(owner); AssertFrozen(owner);
            foreach (var property in Handles)
                Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(owner)).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var rt = loaded.GetType("$Runtime")!;
            var references = loaded.GetReferencedAssemblies().Select(r => r.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references); Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, n => n!.StartsWith("error_runtime_", StringComparison.Ordinal));
            Assert.Equal(typeof(object), loaded.GetType("$Error")!.BaseType);
            Assert.Equal(typeof(Exception), loaded.GetType("$ThrownValueException")!.BaseType);
            foreach (var property in Handles.Where(p => p.PropertyType == typeof(FieldBuilder)))
            {
                var field = (FieldBuilder)property.GetValue(owner)!; var prototype = rt.GetField(field.Name, Members)!.GetValue(null)!;
                Assert.DoesNotContain(prototypes, previous => ReferenceEquals(previous, prototype)); prototypes.Add(prototype);
            }
            foreach (string name in new[] { "Error", "TypeError", "RangeError", "ReferenceError", "SyntaxError", "URIError", "EvalError" })
            {
                var type = loaded.GetType("$" + name)!; var error = Activator.CreateInstance(type, ["value"]);
                Assert.Equal(true, Call(rt, "ErrorIsError", error)); Assert.Equal(name, Call(rt, "ErrorGetName", error));
                var carrier = Assert.IsAssignableFrom<Exception>(Call(rt, "CreateException", error));
                Assert.Same(error, Call(rt, "WrapException", carrier));
                Assert.Same(error, Call(rt, "WrapException", new TargetInvocationException(new TargetInvocationException(carrier))));
                var dynamicError = Call(rt, "CreateErrorFromTypeOrNull", type, new object[] { "dynamic" });
                Assert.Equal(type, dynamicError!.GetType()); Assert.Equal("dynamic", Call(rt, "ErrorGetMessage", dynamicError));
            }
            Assert.Equal("TypeError: plain", Call(rt, "WrapException", Call(rt, "CreateException", "TypeError: plain")));
            var strict = Assert.Throws<TargetInvocationException>(() => Call(rt, "ThrowStrictSyntaxError", "strict"));
            Assert.Equal("SyntaxError: strict", Assert.IsType<Exception>(strict.InnerException).Message);
            var undefined = Assert.Throws<TargetInvocationException>(() => Call(rt, "ThrowUndefinedVariable", "missing"));
            Assert.Equal(loaded.GetType("$ReferenceError"), Call(rt, "WrapException", undefined.InnerException!)!.GetType());
            if (promise)
            {
                var reason = new object(); var rejected = Activator.CreateInstance(loaded.GetType("$PromiseRejectedException")!, [reason]);
                Assert.Same(reason, Call(rt, "WrapException", rejected));
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void WrapExceptionFollowsSuppliedPromiseAvailability(bool selected, bool supplied)
    {
        var builder = NewAssembly(); var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(selected));
        var probe = runtime.RuntimeType.DefineNestedType("WrapProbe", TypeAttributes.NestedPublic);
        var (rejection, constructor, getter) = DefineRejection(probe);
        var promise = supplied ? new EmittedPromiseRuntime { RejectedExceptionType = rejection, RejectedExceptionReasonGetter = getter } : null;
        var owner = Copy(runtime.Errors, nameof(EmittedErrorRuntime.WrapException));
        var inputs = Inputs("WrapExceptionInputs", promise, runtime.StructuredClone);
        typeof(RuntimeEmitter).GetMethod("EmitWrapException", Members)!.Invoke(emitter, [probe, owner, inputs]);
        MarkStages(owner); owner.CompleteEmission(); probe.CreateType();
        var loaded = SaveVerifyLoad(builder); var loadedProbe = loaded.GetType(probe.FullName!)!;
        var marker = new object(); var error = Activator.CreateInstance(loaded.GetType(rejection.FullName!)!, [marker]);
        var value = Call(loadedProbe, "WrapException", error);
        if (supplied) Assert.Same(marker, value);
        else
        {
            Assert.Equal(loaded.GetType("$Error"), value!.GetType());
            Assert.Equal("probe rejection", Call(loaded.GetType("$Runtime")!, "ErrorGetMessage", value));
        }
    }

    [Fact]
    public void ForwardExceptionFactoryUsesItsSuppliedCarrierConstructor()
    {
        var builder = NewAssembly(); var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(false));
        var probe = runtime.RuntimeType.DefineNestedType("FactoryProbe", TypeAttributes.NestedPublic);
        var (rejection, constructor, _) = DefineRejection(probe);
        var owner = Copy(runtime.Errors, nameof(EmittedErrorRuntime.CreateException), nameof(EmittedErrorRuntime.ThrownValueConstructor));
        owner.CreateException = probe.DefineMethod("CreateException", MethodAttributes.Public | MethodAttributes.Static, typeof(Exception), [typeof(object)]);
        owner.ThrownValueConstructor = constructor; var forward = owner.CreateException;
        typeof(RuntimeEmitter).GetMethod("EmitCreateException", Members)!.Invoke(emitter, [owner]);
        Assert.Same(forward, owner.CreateException); MarkStages(owner, "CreateExceptionBody"); owner.CompleteEmission(); probe.CreateType();
        var loaded = SaveVerifyLoad(builder); var value = new object();
        var result = Call(loaded.GetType(probe.FullName!)!, "CreateException", value)!;
        Assert.Equal(loaded.GetType(rejection.FullName!), result.GetType());
        Assert.Same(value, result.GetType().GetMethod("GetReason")!.Invoke(result, null));
    }

    [Fact]
    public void ScopedHelpersAndConstructionStateHaveOneMetadataOwner()
    {
        foreach (var name in new[]
        {
            "EmitTSErrorClasses",
            "EmitTSErrorBaseClass",
            "EmitTSErrorCtorMessage",
            "EmitTSErrorCtorNameMessage",
            "EmitTSErrorNameProperty",
            "EmitTSErrorMessageProperty",
            "EmitTSErrorStackProperty",
            "EmitTSErrorCapturedStackSetter",
            "EmitTSErrorCauseProperty",
            "EmitTSErrorCodeProperty",
            "EmitTSErrorSyscallProperty",
            "EmitTSErrorToStringMethod",
            "EmitSimpleErrorSubclass",
            "EmitTSTypeErrorClass",
            "EmitTSRangeErrorClass",
            "EmitTSReferenceErrorClass",
            "EmitTSSyntaxErrorClass",
            "EmitTSURIErrorClass",
            "EmitTSEvalErrorClass",
            "EmitTSAggregateErrorClass",
            "EmitErrorIsError",
            "EmitErrorMethods",
            "EmitCreateErrorFromTypeOrNull",
            "EmitErrorDefineMessageProperty",
            "EmitCreateError",
            "EmitDefineErrorDataPropertyIfPresent",
            "EmitApplyErrorOptions",
            "EmitErrorGetters",
            "EmitErrorPropertyGetter",
            "EmitErrorSetters",
            "EmitErrorPropertySetter",
            "EmitErrorGetCause",
            "EmitErrorSetCause",
            "EmitAggregateErrorGetErrors",
            "DefineErrorPrototypePopulateShell",
            "EmitErrorPrototypePopulate",
            "EmitErrorToStringSpecHelper",
            "DefineNativeErrorPrototypePopulateShells",
            "EmitNativeErrorPrototypePopulates",
            "EmitOneNativeErrorPopulate",
            "EmitThrownValueExceptionType",
            "EmitCreateException",
            "EmitWrapException",
            "EmitThrowUndefinedVariable",
            "EmitThrowStrictSyntaxError"
        })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            foreach (var parameter in method.GetParameters().Where(p => p.ParameterType.IsNestedPrivate))
                Assert.DoesNotContain(parameter.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        }
        foreach (var name in new[]
        {
            "ErrorPrototypeField",
            "ErrorPrototypePopulateMethod",
            "TypeErrorPrototypeField",
            "TypeErrorPrototypePopulateMethod",
            "RangeErrorPrototypeField",
            "RangeErrorPrototypePopulateMethod",
            "ReferenceErrorPrototypeField",
            "ReferenceErrorPrototypePopulateMethod",
            "SyntaxErrorPrototypeField",
            "SyntaxErrorPrototypePopulateMethod",
            "URIErrorPrototypeField",
            "URIErrorPrototypePopulateMethod",
            "EvalErrorPrototypeField",
            "EvalErrorPrototypePopulateMethod",
            "AggregateErrorPrototypeField",
            "AggregateErrorPrototypePopulateMethod",
            "ThrowStrictSyntaxError",
            "CreateException",
            "WrapException",
            "ThrowUndefinedVariable",
            "TSErrorType",
            "TSErrorCtorMessage",
            "TSErrorCtorNameMessage",
            "TSErrorNameGetter",
            "TSErrorNameSetter",
            "TSErrorMessageGetter",
            "TSErrorMessageSetter",
            "TSErrorStackGetter",
            "TSErrorStackSetter",
            "TSErrorCapturedStackSetter",
            "TSErrorCauseGetter",
            "TSErrorCauseSetter",
            "TSErrorHasCauseGetter",
            "TSErrorCodeGetter",
            "TSErrorCodeSetter",
            "TSErrorSyscallGetter",
            "TSErrorSyscallSetter",
            "TSTypeErrorType",
            "TSTypeErrorCtor",
            "TSRangeErrorType",
            "TSRangeErrorCtor",
            "TSReferenceErrorType",
            "TSReferenceErrorCtor",
            "TSSyntaxErrorType",
            "TSSyntaxErrorCtor",
            "TSURIErrorType",
            "TSURIErrorCtor",
            "TSEvalErrorType",
            "TSEvalErrorCtor",
            "TSAggregateErrorType",
            "TSAggregateErrorCtor",
            "TSAggregateErrorErrorsGetter",
            "CreateError",
            "CreateErrorFromTypeOrNull",
            "ErrorGetName",
            "ErrorGetMessage",
            "ErrorGetStack",
            "ErrorSetName",
            "ErrorSetMessage",
            "ErrorDefineMessageProperty",
            "ErrorSetStack",
            "ErrorGetCause",
            "ErrorSetCause",
            "ErrorIsError",
            "AggregateErrorGetErrors",
            "ThrownValueExceptionType",
            "ThrownValueExceptionCtor",
            "ThrownValueExceptionValueGetter"
        }) Assert.Null(typeof(EmittedRuntime).GetProperty(name));
        foreach (var name in new[]
        {
            "_tsErrorNameField",
            "_tsErrorMessageField",
            "_tsErrorStackField",
            "_tsErrorCapturedStackField",
            "_tsErrorCauseField",
            "_tsErrorHasCauseField",
            "_tsErrorCodeField",
            "_tsErrorSyscallField",
            "_tsAggregateErrorErrorsField"
        }) Assert.Null(typeof(RuntimeEmitter).GetField(name, Members));
    }

    private static (TypeBuilder Type, ConstructorBuilder Constructor, MethodBuilder Getter) DefineRejection(TypeBuilder probe)
    {
        var type = probe.DefineNestedType("SuppliedRejection", TypeAttributes.NestedPublic, typeof(Exception));
        var field = type.DefineField("_reason", typeof(object), FieldAttributes.Private | FieldAttributes.InitOnly);
        var ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(object)]);
        var il = ctor.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, "probe rejection");
        il.Emit(OpCodes.Call, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, field); il.Emit(OpCodes.Ret);
        var getter = type.DefineMethod("GetReason", MethodAttributes.Public, typeof(object), Type.EmptyTypes);
        il = getter.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
        type.CreateType(); return (type, ctor, getter);
    }

    private static object Inputs(string name, params object?[] values) => Activator.CreateInstance(
        typeof(RuntimeEmitter).GetNestedType(name, BindingFlags.NonPublic)!, Members, null, values, null)!;
    private static EmittedErrorRuntime Copy(EmittedErrorRuntime original, params string[] omitted)
    {
        var owner = new EmittedErrorRuntime();
        foreach (var property in Handles.Where(p => !omitted.Contains(p.Name))) property.SetValue(owner, property.GetValue(original));
        return owner;
    }
    private static void Fill(EmittedErrorRuntime owner, string? omitted = null)
    {
        foreach (var property in Handles.Where(p => p.Name != omitted)) property.SetValue(owner, Handle(property.PropertyType));
    }
    private static object Handle(Type kind)
    {
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Declaration");
        if (kind == typeof(Type)) return type;
        if (kind == typeof(ConstructorBuilder)) return type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        if (kind == typeof(FieldBuilder)) return type.DefineField("Value", typeof(object), FieldAttributes.Public);
        return type.DefineMethod("Invoke", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
    }
    private static void Mark(EmittedErrorRuntime owner, string stage) => typeof(EmittedErrorRuntime).GetMethod("Mark" + stage + "Emitted", Members)!.Invoke(owner, null);
    private static void MarkStages(EmittedErrorRuntime owner, string? omitted = null)
    {
        foreach (string stage in Stages.Where(s => s != omitted)) Mark(owner, stage);
    }
    private static void AssertFrozen(EmittedErrorRuntime owner)
    {
        Assert.True(owner.IsComplete); Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        foreach (var property in Handles)
        {
            Assert.False(property.SetMethod!.IsPublic); Expect<InvalidOperationException>(() => property.SetValue(owner, property.GetValue(owner)));
        }
    }
    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"error_runtime_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static RuntimeFeatureSet Detect(bool promise) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(promise ? "Promise.resolve(1);" : "const n = 1;").ScanTokens()).ParseOrThrow());
    private static object? Call(Type type, string name, params object?[] values) => type.GetMethod(name, Members)!.Invoke(null, values);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); var errors = verifier.Verify(bytes);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors)); return Assembly.Load(bytes.ToArray());
    }
}
