using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedClassInitializationRuntimeTests
{
    [Fact]
    public void CheckedDeclarationAndBodyStagesSupportForwardCallsAndFailedCompletionRepair()
    {
        var owner = new EmittedRuntime().ClassInitialization;
        Assert.Throws<InvalidOperationException>(() => owner.RunDefinition);
        Assert.Throws<ArgumentNullException>(() => owner.RunDefinition = null!);
        Assert.Throws<InvalidOperationException>(owner.MarkBodyEmitted);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        var builder = NewAssembly(); var type = builder.DefineDynamicModule("main").DefineType("Forward", TypeAttributes.Public);
        var declaration = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [typeof(Type)]);
        owner.RunDefinition = declaration;
        Assert.Same(declaration, owner.RunDefinition);
        Assert.Throws<InvalidOperationException>(() => owner.RunDefinition = declaration);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        var forward = type.DefineMethod("Consumer", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [typeof(Type)]);
        var il = forward.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, owner.RunDefinition); il.Emit(OpCodes.Ret);
        declaration.GetILGenerator().Emit(OpCodes.Ret); owner.MarkBodyEmitted();
        Assert.Throws<InvalidOperationException>(owner.MarkBodyEmitted);
        owner.CompleteEmission(); Assert.True(owner.IsComplete); Assert.Same(declaration, owner.RunDefinition);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.Throws<InvalidOperationException>(owner.MarkBodyEmitted);
        Assert.Throws<InvalidOperationException>(() => owner.RunDefinition = declaration);
        type.CreateType(); SaveVerifyLoad(builder).GetType("Forward")!.GetMethod("Consumer")!.Invoke(null, [typeof(object)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesInitializationAndOriginalExceptionIdentity(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedClassInitializationRuntime>(); var handles = new HashSet<MethodBuilder>();
        var previousCounts = new List<FieldInfo>();
        foreach (string source in new[] { "const n=1;", "Promise.resolve(1); function* g(){yield 1;} g();", "const n=2;" })
        {
            var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
            var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            var runtime = emitter.EmitAll(module, features); var owner = runtime.ClassInitialization;
            Assert.True(owner.IsComplete); Assert.True(owners.Add(owner)); Assert.True(handles.Add(owner.RunDefinition));
            Assert.Same(builder, owner.RunDefinition.Module.Assembly); Assert.Same(runtime.RuntimeClass.Type, owner.RunDefinition.DeclaringType);
            DefineInitializers(module);
            var loaded = SaveVerifyLoad(builder); var runtimeType = loaded.GetType("$Runtime")!;
            var helper = runtimeType.GetMethod("RunClassDefinition")!;
            Assert.True(helper.IsPublic && helper.IsStatic); Assert.Equal(typeof(void), helper.ReturnType);
            Assert.Equal(typeof(Type), Assert.Single(helper.GetParameters()).ParameterType);
            Assert.Equal(runtimeType.GetMethod("BuildCancellationException")!.MetadataToken + 1, helper.MetadataToken);
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
            var state = loaded.GetType("State")!; var count = state.GetField("Count")!;
            Assert.Equal(0, count.GetValue(null));
            helper.Invoke(null, [loaded.GetType("Success")!]); helper.Invoke(null, [loaded.GetType("Success")!]);
            Assert.Equal(1, count.GetValue(null));
            var original = new InvalidOperationException("original"); state.GetField("Failure")!.SetValue(null, original);
            Exception RunFailure(Type type) => Assert.Throws<TargetInvocationException>(() => helper.Invoke(null, [type])).InnerException!;
            Assert.Same(original, RunFailure(loaded.GetType("Failed")!));
            state.GetField("Failure")!.SetValue(null, new Exception("replacement"));
            Assert.Same(original, RunFailure(loaded.GetType("Failed")!)); Assert.Equal(2, count.GetValue(null));
            var nested = new TypeInitializationException("guest", original);
            state.GetField("Failure")!.SetValue(null, nested);
            Assert.Same(nested, RunFailure(loaded.GetType("Nested")!)); Assert.Equal(3, count.GetValue(null));
            // A Type supplied by an embedder can itself throw while providing TypeHandle.
            var noInner = new TypeInitializationException("no inner", null);
            Assert.Same(noInner, RunFailure(new ThrowingType(noInner)));
            Assert.Same(original, RunFailure(new ThrowingType(new TypeInitializationException("outer", original))));
            Assert.Same(original, RunFailure(new ThrowingType(original)));
            helper.Invoke(null, [typeof(int)]);
            Assert.All(previousCounts, previous => Assert.Equal(3, previous.GetValue(null))); previousCounts.Add(count);
        }
    }

    [Fact]
    public void EmissionHelperUsesOnlyItsOwnerAndCompletesAfterItsBody()
    {
        var builder = NewAssembly(); var type = builder.DefineDynamicModule("main").DefineType("Scoped", TypeAttributes.Public);
        var owner = new EmittedRuntime().ClassInitialization;
        var helper = typeof(RuntimeEmitter).GetMethod("EmitRunClassDefinition", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Equal(new[] { typeof(TypeBuilder), typeof(EmittedClassInitializationRuntime) }, helper.GetParameters().Select(p => p.ParameterType));
        helper.Invoke(new RuntimeEmitter(TypeProvider.Runtime), [type, owner]);
        Assert.False(owner.IsComplete); owner.CompleteEmission(); Assert.True(owner.IsComplete);
        type.CreateType(); SaveVerifyLoad(builder).GetType("Scoped")!.GetMethod("RunClassDefinition")!.Invoke(null, [typeof(object)]);
        Assert.NotSame(owner, new EmittedRuntime().ClassInitialization);
        Assert.Null(typeof(EmittedRuntime).GetProperty("ClassInitialization")!.SetMethod);
        Assert.Null(typeof(EmittedRuntime).GetProperty("RunClassDefinitionMethod"));
    }

    private sealed class ThrowingType(Exception failure) : TypeDelegator(typeof(object))
    {
        public override RuntimeTypeHandle TypeHandle => throw failure;
    }

    private static void DefineInitializers(ModuleBuilder module)
    {
        var state = module.DefineType("State", TypeAttributes.Public);
        var count = state.DefineField("Count", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var failure = state.DefineField("Failure", typeof(Exception), FieldAttributes.Public | FieldAttributes.Static);
        state.CreateType();
        foreach (string name in new[] { "Success", "Failed", "Nested" })
        {
            var type = module.DefineType(name, TypeAttributes.Public);
            var il = type.DefineTypeInitializer().GetILGenerator();
            il.Emit(OpCodes.Ldsfld, count); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stsfld, count);
            if (name == "Success") il.Emit(OpCodes.Ret);
            else { il.Emit(OpCodes.Ldsfld, failure); il.Emit(OpCodes.Throw); }
            type.CreateType();
        }
    }

    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"class_init_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}
