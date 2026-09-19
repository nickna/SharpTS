using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedRuntimeClassTests
{
    [Fact]
    public void DeclarationSupportsForwardReferencesAndCompletionRequiresACreatedType()
    {
        var root = new EmittedRuntime(); var owner = root.RuntimeClass;
        Assert.Throws<InvalidOperationException>(() => owner.Type);
        Assert.Throws<ArgumentNullException>(() => owner.Type = null!);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var type = module.DefineType("Forward", TypeAttributes.Public);
        owner.Type = type; Assert.Same(type, owner.Type);
        Assert.Throws<InvalidOperationException>(() => owner.Type = type);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        var helper = type.DefineMethod("Value", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        var caller = module.DefineType("Consumer", TypeAttributes.Public);
        var il = caller.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes).GetILGenerator();
        il.Emit(OpCodes.Call, helper); il.Emit(OpCodes.Ret);
        il = caller.DefineMethod("Owner", MethodAttributes.Public | MethodAttributes.Static, typeof(Type), Type.EmptyTypes).GetILGenerator();
        il.Emit(OpCodes.Ldtoken, owner.Type); il.Emit(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!); il.Emit(OpCodes.Ret);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        il = helper.GetILGenerator(); il.Emit(OpCodes.Ldc_I4, 7); il.Emit(OpCodes.Ret);
        RuntimeEmitter.EmitRuntimeClassFinalize(owner); caller.CreateType();
        Assert.True(owner.IsComplete); Assert.True(type.IsCreated()); Assert.Same(type, owner.Type);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.Throws<InvalidOperationException>(() => owner.Type = type);
        Assert.Throws<InvalidOperationException>(() => RuntimeEmitter.EmitRuntimeClassFinalize(owner));
        var loaded = SaveVerifyLoad(builder); var consumer = loaded.GetType("Consumer")!;
        Assert.Equal(7, consumer.GetMethod("Call")!.Invoke(null, null));
        Assert.Same(loaded.GetType("Forward"), consumer.GetMethod("Owner")!.Invoke(null, null));
    }

    [Fact]
    public void FinalizerUsesTheSuppliedOwnerAndRejectsMissingDeclarations()
    {
        var first = new EmittedRuntime().RuntimeClass; var second = new EmittedRuntime().RuntimeClass;
        Assert.NotSame(first, second);
        Assert.Throws<InvalidOperationException>(() => RuntimeEmitter.EmitRuntimeClassFinalize(first));
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        first.Type = module.DefineType("First", TypeAttributes.Public);
        second.Type = module.DefineType("Second", TypeAttributes.Public);
        var method = first.Type.DefineMethod("Later", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        Assert.Throws<InvalidOperationException>(first.CompleteEmission);
        Assert.False(first.IsComplete); Assert.False(second.IsComplete);
        method.GetILGenerator().Emit(OpCodes.Ret);
        RuntimeEmitter.EmitRuntimeClassFinalize(second); Assert.True(second.IsComplete); Assert.False(first.IsComplete);
        RuntimeEmitter.EmitRuntimeClassFinalize(first); Assert.True(first.IsComplete);
        SaveVerifyLoad(builder).GetType("First")!.GetMethod("Later")!.Invoke(null, null);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.RuntimeClass))!.SetMethod);
        Assert.Null(typeof(EmittedRuntime).GetProperty("RuntimeType"));
        Assert.Null(typeof(RuntimeEmitter).GetField("_runtimeTypeBuilder", BindingFlags.NonPublic | BindingFlags.Instance));
        var finalizer = typeof(RuntimeEmitter).GetMethod("EmitRuntimeClassFinalize", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(typeof(EmittedRuntimeClass), Assert.Single(finalizer.GetParameters()).ParameterType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PhaseOneDeclaresExactlyOneRuntimeTypeAndLeavesItOpenForDeferredBodies(bool hosted)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var runtime = new EmittedRuntime();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        typeof(RuntimeEmitter).GetField("_features", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(emitter, new RuntimeFeatureDetector().Detect(new Parser(new Lexer("const n=1;").ScanTokens()).ParseOrThrow()));
        typeof(RuntimeEmitter).GetMethod("DefineRuntimeClassPhase1", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(emitter, [module, runtime]);
        var owner = runtime.RuntimeClass; Assert.False(owner.IsComplete); Assert.False(owner.Type.IsCreated());
        Assert.Equal("$Runtime", owner.Type.Name);
        Assert.Same(owner.Type, runtime.StringCoercion.Stringify.DeclaringType);
        Assert.Same(owner.Type, runtime.Errors.CreateException.DeclaringType);
        Assert.Same(owner.Type, runtime.ObjectRead.Property.DeclaringType);
        Assert.Equal(0, runtime.StringCoercion.Stringify.GetILGenerator().ILOffset);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterFinalizesFreshOwnersAcrossOptionalAndDeferredFamilies(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedRuntimeClass>(); var types = new HashSet<TypeBuilder>();
        foreach (string? source in new string?[] {
            "const n=1;",
            "async function* values(){yield /a/.test(\"a\");}async function run(){for await(const x of values()){} }run();",
            "import * as tls from 'tls'; import * as http from 'http'; import * as fs from 'fs';",
            "const bytes=new Uint8Array(2);const stream=new ReadableStream();",
            null,
            "const n=2;" })
        {
            var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
            var features = source is null ? null : new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            var runtime = features is null ? emitter.EmitAll(module) : emitter.EmitAll(module, features);
            var owner = runtime.RuntimeClass;
            Assert.True(owners.Add(owner)); Assert.True(types.Add(owner.Type));
            Assert.True(owner.IsComplete); Assert.True(owner.Type.IsCreated());
            Assert.Same(builder, owner.Type.Assembly);
            Assert.Equal(TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, owner.Type.Attributes);
            Assert.Equal(typeof(object), owner.Type.BaseType);
            Assert.Same(owner.Type, runtime.DynamicConstruction.Function.DeclaringType);
            Assert.Same(owner.Type, runtime.Workers.ReceiveMessageOnPort.DeclaringType);
            var loaded = SaveVerifyLoad(builder); var saved = loaded.GetType("$Runtime")!;
            Assert.Equal(new[] { "Stringify", "FormatNumber", "CreateException" }, saved.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly).OrderBy(m => m.MetadataToken).Take(3).Select(m => m.Name));
            Assert.Equal("7", saved.GetMethod("Stringify")!.Invoke(null, [7d]));
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
            Assert.All(owners, previous => { Assert.True(previous.IsComplete); Assert.True(previous.Type.IsCreated()); });
        }
    }

    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"runtime_owner_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}
