using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ScopedFeatureGateTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadlineHelpersFollowRequiredMetadataRegardlessOfGlobalSelection(bool globalFlag)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var receiver = module.DefineType("Readline", TypeAttributes.Public);
        var owner = new EmittedReadlineRuntime
        {
            InterfaceType = receiver,
            InterfaceCtor = receiver.DefineDefaultConstructor(MethodAttributes.Public),
            PromptField = receiver.DefineField("Prompt", typeof(string), FieldAttributes.Public)
        };
        var helper = module.DefineType("Helpers", TypeAttributes.Public);
        var emitter = Emitter(new RuntimeFeatureSet { UsesReadline = globalFlag });
        Emit(emitter, "EmitReadlineMethods", helper, owner);
        Assert.Same(helper, owner.QuestionSync.DeclaringType);
        Assert.Same(helper, owner.CreateInterface.DeclaringType);
        receiver.CreateType(); helper.CreateType(); var loaded = SaveVerifyLoad(builder);
        var methods = loaded.GetType("Helpers")!;
        Assert.NotNull(methods.GetMethod("ReadlineQuestionSync"));
        var created = methods.GetMethod("ReadlineCreateInterface")!.Invoke(null, [new Dictionary<string, object?> { ["prompt"] = "supplied> " }])!;
        Assert.Equal("supplied> ", created.GetType().GetField("Prompt")!.GetValue(created));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadlineMissingRequiredDeclarationsFailRegardlessOfGlobalSelection(bool globalFlag)
    {
        var helper = NewAssembly().DefineDynamicModule("main").DefineType("Helpers");
        var emitter = Emitter(new RuntimeFeatureSet { UsesReadline = globalFlag });
        var error = Assert.Throws<TargetInvocationException>(() => Emit(emitter, "EmitReadlineMethods", helper, new EmittedReadlineRuntime()));
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void AbortAnyDependencyFollowsExplicitSelection(bool controllerFlag, bool anyFlag, bool selected)
    {
        var builder = NewAssembly(); var helper = builder.DefineDynamicModule("main").DefineType("Helpers", TypeAttributes.Public);
        var owner = new EmittedAbortRuntime(); var reasons = new List<string>();
        Action<string> requireRuntime = reason =>
        {
            Assert.Throws<InvalidOperationException>(() => owner.SignalAny);
            reasons.Add(reason);
        };
        var emitter = Emitter(new RuntimeFeatureSet { UsesAbortController = controllerFlag, UsesAbortSignalAny = anyFlag });
        Emit(emitter, "EmitAbortSignalStaticAny", helper, owner, requireRuntime, selected);
        Assert.Equal(selected ? new[] { "AbortSignal.any" } : Array.Empty<string>(), reasons);
        Assert.Equal("AbortSignalAny", owner.SignalAny.Name);
        helper.CreateType(); var loaded = SaveVerifyLoad(builder);
        Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
        Assert.NotNull(loaded.GetType("Helpers")!.GetMethod("AbortSignalAny"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedNormalEmissionPreservesAvailabilityStateAndDeployment(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<object>();
        foreach (var (readline, abort, any) in new[] { (false, false, false), (true, false, false), (false, true, false), (false, true, true), (false, false, false), (true, true, true) })
        {
            var source = "const value=1;" + (readline ? "import * as readline from 'readline';" : "") + (abort ? "new AbortController();" : "") + (any ? "AbortSignal.any([]);" : "");
            var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), features);
            Assert.Equal(readline, runtime.Readline is not null); Assert.Equal(abort, runtime.Abort is not null);
            Assert.Equal(any, runtime.Deployment.Reasons.Contains("AbortSignal.any"));
            foreach (var owner in new object?[] { runtime.Readline, runtime.Abort }.OfType<object>())
            {
                Assert.True(owners.Add(owner));
                Assert.Equal(true, owner.GetType().GetProperty("IsComplete")!.GetValue(owner));
                foreach (var property in owner.GetType().GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)))
                    Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(owner)).Module.Assembly);
            }
            var loaded = SaveVerifyLoad(builder); var references = loaded.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references); Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("scoped_gates_", StringComparison.Ordinal));
            var type = loaded.GetType("$Runtime")!;
            Assert.Equal(readline, type.GetMethod("ReadlineCreateInterface") is not null);
            Assert.Equal(abort, type.GetMethod("AbortSignalAny") is not null);
            if (readline)
            {
                var factory = type.GetMethod("ReadlineCreateInterface")!;
                var first = factory.Invoke(null, [new Dictionary<string, object?> { ["prompt"] = "first> " }])!;
                var second = factory.Invoke(null, [new Dictionary<string, object?> { ["prompt"] = "second> " }])!;
                first.GetType().GetMethod("SetPrompt")!.Invoke(first, ["changed> "]);
                Assert.Equal("changed> ", first.GetType().GetMethod("GetPrompt")!.Invoke(first, null));
                Assert.Equal("second> ", second.GetType().GetMethod("GetPrompt")!.Invoke(second, null));
            }
            if (abort)
            {
                object? Call(string name, params object?[] args) => type.GetMethod(name)!.Invoke(null, args);
                var controller = Assert.IsType<Dictionary<string, object?>>(Call("CreateAbortController"));
                using var cancellation = Assert.IsType<CancellationTokenSource>(controller["_cts"]);
                var signal = Call("AbortControllerGetSignal", controller);
                Assert.Equal(false, Call("AbortSignalGetAborted", signal));
                Call("AbortControllerAbort", controller, "selected");
                Assert.True(cancellation.IsCancellationRequested);
                Assert.Equal(true, Call("AbortSignalGetAborted", signal));
                Assert.Equal("selected", Call("AbortSignalGetReason", signal));
                if (any)
                {
                    var combined = Assert.IsType<Dictionary<string, object?>>(Call("AbortSignalAny", new List<object?> { signal }));
                    using var combinedCancellation = Assert.IsType<CancellationTokenSource>(combined["_cts"]);
                    Assert.Equal(true, Call("AbortSignalGetAborted", combined));
                    Assert.Equal("selected", Call("AbortSignalGetReason", combined));
                }
            }
        }
    }

    [Fact]
    public void ScopedHelpersDoNotAcceptWholeFeatureOrRuntimeHolders()
    {
        foreach (var name in new[] { "EmitReadlineMethods", "EmitAbortControllerMethods", "EmitAbortSignalStaticAny" })
            Assert.DoesNotContain(typeof(RuntimeEmitter).GetMethod(name, Members)!.GetParameters(), p => p.ParameterType == typeof(RuntimeFeatureSet) || p.ParameterType == typeof(EmittedRuntime));
    }

    private static RuntimeEmitter Emitter(RuntimeFeatureSet features)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        typeof(RuntimeEmitter).GetField("_features", Members)!.SetValue(emitter, features);
        return emitter;
    }
    private static void Emit(RuntimeEmitter emitter, string name, params object?[] args) => typeof(RuntimeEmitter).GetMethod(name, Members)!.Invoke(emitter, args);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"scoped_gates_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}
