using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedInvocationRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static PropertyInfo[] Properties => typeof(EmittedInvocationRuntime).GetProperties().Where(p => p.PropertyType == typeof(MethodBuilder)).ToArray();
    public static IEnumerable<object[]> Declarations => Properties.Select(p => new object[] { p.Name });
    public static IEnumerable<object[]> FeatureSelections =>
        from method in new[] { "EmitInvokeValue", "EmitInvokeMethodValue" }
        from feature in new[] { "UsesTextEncoding", "UsesNodeStreams", "UsesPromise", "HasAnyTypedArray" }
        from selected in new[] { false, true }
        select new object[] { method, feature, selected };

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingDeclarationCanBeRepairedAndCompletionFreezesMetadata(string missing)
    {
        var owner = new EmittedInvocationRuntime(); var handles = NewHandles();
        var property = Properties.Single(p => p.Name == missing);
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        foreach (var other in Properties.Where(p => p.Name != missing)) other.SetValue(owner, handles[other.Name]);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        property.SetValue(owner, handles[missing]); Assert.Same(handles[missing], property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, handles[missing]));
        owner.MarkMethodBodyEmitted(); owner.CompleteEmission(); Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.Throws<InvalidOperationException>(owner.MarkMethodBodyEmitted);
        foreach (var item in Properties) Expect<InvalidOperationException>(() => item.SetValue(owner, handles[item.Name]));
    }

    [Fact]
    public void ForwardMethodDeclarationCannotCompleteBeforeItsBody()
    {
        var owner = new EmittedInvocationRuntime();
        Assert.Throws<InvalidOperationException>(owner.MarkMethodBodyEmitted);
        var handles = NewHandles();
        foreach (var item in Properties) item.SetValue(owner, handles[item.Name]);
        Assert.Same(handles["Method"], owner.Method);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        owner.MarkMethodBodyEmitted(); Assert.Throws<InvalidOperationException>(owner.MarkMethodBodyEmitted);
        owner.CompleteEmission(); Assert.True(owner.IsComplete);
    }

    [Theory]
    [MemberData(nameof(FeatureSelections))]
    public void FeatureSelectionIsIndependentOfOptionalMetadata(string helperName, string feature, bool selected)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        // The emitter's most recent feature set has every tested optional feature disabled.
        // An explicit true selection must still request that feature's metadata.
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(module, Detect("const n=1;"));
        var owner = new EmittedInvocationRuntime(); var probe = module.DefineType("Probe");
        owner.Method = probe.DefineMethod("InvokeMethodValue", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object), typeof(object), typeof(object[])]);
        if (helperName == "EmitInvokeMethodValue")
        {
            owner.Value = probe.DefineMethod("InvokeValue", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object), typeof(object[])]);
            owner.Value.GetILGenerator().Emit(OpCodes.Ldnull); owner.Value.GetILGenerator().Emit(OpCodes.Ret);
        }
        else
        {
            owner.Method.GetILGenerator().Emit(OpCodes.Ldnull); owner.Method.GetILGenerator().Emit(OpCodes.Ret);
        }
        string key = feature switch { "UsesTextEncoding" => "TextEncoding", "UsesNodeStreams" => "NodeStreams", "UsesPromise" => "Promise", _ => "TypedArrays" };
        var typedArrays = new EmittedTypedArrayRuntime();
        if (!selected) typedArrays.BeginImplementationEmission();
        object? Read(ParameterInfo p)
        {
            if (p.ParameterType == typeof(bool)) return p.Name == feature && selected;
            if (p.Name == "CheckCancellationMethod") return null;
            if (p.Name == "TypedArrays") return typedArrays;
            if (p.Name == key) return selected ? null : key == "NodeStreams" ? new EmittedNodeStreamRuntime(hasAbortSignal: false) : Activator.CreateInstance(p.ParameterType, nonPublic: true);
            return (p.Name! switch { "UndefinedType" => runtime.Sentinels.UndefinedType, "UndefinedInstance" => runtime.Sentinels.UndefinedInstance, _ => typeof(EmittedRuntime).GetProperty(p.Name!)!.GetValue(runtime) });
        }
        var helper = typeof(RuntimeEmitter).GetMethod(helperName, Members)!;
        var constructor = helper.GetParameters().Last().ParameterType.GetConstructors(Members).Single();
        var inputs = constructor.Invoke(constructor.GetParameters().Select(Read).ToArray());
        void Emit() => helper.Invoke(emitter, [probe, owner, inputs]);
        if (selected)
        {
            var error = Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(Emit).InnerException);
            Assert.Equal(key switch
            {
                "TextEncoding" => "Text-encoding runtime was not enabled for this compilation.",
                "NodeStreams" => "Node streams were not enabled for this compilation.",
                "Promise" => "Promise runtime was not enabled for this compilation.",
                _ => "TypedArray implementation was not enabled for this compilation."
            }, error.Message);
        }
        else
        {
            // Available but undeclared optional metadata must be ignored when deselected.
            Emit(); probe.CreateType(); var loaded = SaveVerifyLoad(builder);
            var emitted = loaded.GetType("Probe")!.GetMethod(helperName[4..])!;
            Func<object[], object> callee = args => args[0];
            object?[] args = helperName == "EmitInvokeValue" ? [callee, new object[] { "ok" }] : [new object(), callee, new object[] { "ok" }];
            Assert.Equal("ok", emitted.Invoke(null, args));
            Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterKeepsInvocationOwnersAndReceiverDispatchIndependent(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted); var owners = new HashSet<object>();
        foreach (string code in new[] { "const n=1;", "function f(a:number){return a;} f.bind(null,1)(); new Map(); new Set();", "const n=2;" })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(code));
            var owner = runtime.Invocation; Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete);
            foreach (var property in Properties) Assert.Same(builder, ((MethodInfo)property.GetValue(owner)!).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var type = loaded.GetType("$Runtime")!;
            var function = loaded.GetType(runtime.FunctionValues.Type.Name)!;
            var wrapper = Activator.CreateInstance(function, [null, typeof(EmittedInvocationRuntimeTests).GetMethod(nameof(ReadThis))!])!;
            var receiver = new object();
            Assert.Same(receiver, type.GetMethod(owner.Method.Name)!.Invoke(null, [receiver, wrapper, Array.Empty<object>()]));
            Assert.Same(receiver, type.GetMethod(owner.Method0.Name)!.Invoke(null, [receiver, wrapper]));
            Assert.Equal(runtime.Sentinels.UndefinedType.Name, type.GetMethod(owner.Value.Name)!.Invoke(null, [wrapper, Array.Empty<object>()])!.GetType().Name);
            Func<object[], object> count = args => args.Length;
            Assert.Equal(2, type.GetMethod(owner.Value.Name)!.Invoke(null, [count, new object[] { 1, 2 }]));
            Assert.Equal(0, type.GetMethod(owner.Method0.Name)!.Invoke(null, [receiver, count]));
            var error = new InvalidOperationException("same exception"); Func<object[], object> fail = _ => throw error;
            Assert.Same(error, Assert.Throws<TargetInvocationException>(() => type.GetMethod(owner.Method.Name)!.Invoke(null, [receiver, fail, Array.Empty<object>()])).InnerException);
            foreach (var name in new[] { owner.Value.Name, owner.Method.Name, owner.Method0.Name })
            {
                object?[] args = name == owner.Value.Name ? [null, Array.Empty<object>()] : name == owner.Method.Name ? [receiver, null, Array.Empty<object>()] : [receiver, null];
                var exception = Assert.Throws<TargetInvocationException>(() => type.GetMethod(name)!.Invoke(null, args));
                Assert.Contains("not a function", exception.InnerException!.Message);
            }
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Fact]
    public void OwnerAndHelpersUseExplicitDependencies()
    {
        Assert.Equal(3, Properties.Length);
        Assert.NotSame(new EmittedRuntime().Invocation, new EmittedRuntime().Invocation);
        Assert.Null(typeof(EmittedRuntime).GetProperty("Invocation")!.SetMethod);
        foreach (string old in new[] { "InvokeValue", "InvokeMethodValue", "InvokeMethodValue0" }) Assert.Null(typeof(EmittedRuntime).GetProperty(old));
        foreach (string name in new[] { "EmitInvokeValue", "EmitInvokeMethodValue", "EmitInvokeMethodValue0", "EmitStackGuard", "EmitThrowNotAFunction", "EmitProxyInvokeCheck" })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            foreach (var parameter in method.GetParameters().Where(p => p.ParameterType.IsNested))
                Assert.DoesNotContain(parameter.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        }
    }

    public static object ReadThis(object __this) => __this;
    private static Dictionary<string, MethodBuilder> NewHandles()
    {
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        return new[] { "Value", "Method", "Method0" }.ToDictionary(n => n, n => type.DefineMethod(n, MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes));
    }
    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static RuntimeFeatureSet Detect(string code) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(code).ScanTokens()).ParseOrThrow());
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"invocation_dispatch_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}
