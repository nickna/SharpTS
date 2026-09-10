using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedPromiseRuntimeTests
{
    [Theory]
    [InlineData("console.log(1);")]
    [InlineData("setTimeout(() => console.log(1), 0);")]
    public void DisabledFeatureHasNoMetadataAndReportsAccidentalUse(string source)
    {
        var runtime = EmitRuntime(source);
        Assert.Null(runtime.Promise);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequirePromise).Message);
        // The shared FIFO job queue also serves non-Promise microtasks.
        Assert.NotNull(runtime.QueuePromiseJob);
    }

    [Fact]
    public void CapabilityDeclarationSupportsCallsBeforeBodyEmission()
    {
        var runtime = new EmittedRuntime();
        runtime.BeginPromiseEmission();
        var promise = runtime.RequirePromise();
        Assert.False(promise.IsComplete);
        Assert.Contains("NewPromiseCapabilityResultMethod",
            Assert.Throws<InvalidOperationException>(() => promise.NewPromiseCapabilityResultMethod).Message);

        var assembly = new PersistedAssemblyBuilder(new AssemblyName("promise_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Runtime");
        var capability = type.DefineMethod("Capability", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [typeof(object), typeof(Task<object>)]);
        promise.NewPromiseCapabilityResultMethod = capability;
        var caller = type.DefineMethod("Caller", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [typeof(object), typeof(Task<object>)]);
        var il = caller.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, promise.NewPromiseCapabilityResultMethod);
        il.Emit(OpCodes.Ret);
        Assert.Same(capability, promise.NewPromiseCapabilityResultMethod);
        Assert.False(type.IsCreated());
        capability.GetILGenerator().Emit(OpCodes.Ldarg_0);
        capability.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);

        Assert.Throws<InvalidOperationException>(runtime.BeginPromiseEmission);
        Assert.Throws<InvalidOperationException>(promise.CompleteEmission);
        Assert.False(promise.IsComplete);
        Assert.Throws<ArgumentNullException>(() => promise.NewPromiseCapabilityResultMethod = null!);
        Assert.Same(capability, promise.NewPromiseCapabilityResultMethod);
    }

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedPromiseRuntime).GetProperties()
        .Where(property => property.Name != nameof(EmittedPromiseRuntime.IsComplete));

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void CompletionRejectsEachMissingDeclaration(string missingHandle)
    {
        var promise = new EmittedPromiseRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("promise_incomplete"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
        {
            object handle = property.PropertyType.IsAssignableFrom(typeof(TypeBuilder)) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor
                : property.PropertyType == typeof(FieldBuilder) ? field : method;
            property.SetValue(promise, handle);
        }

        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(promise.CompleteEmission).Message);
        Assert.False(promise.IsComplete);
    }

    [Theory]
    [InlineData("Promise.resolve(1);")]
    [InlineData("async function f() { return 1; }")]
    [InlineData("async function* f() { yield 1; }")]
    [InlineData("import('value');")]
    [InlineData("import { readFile } from 'fs/promises';")]
    [InlineData("import * as dns from 'dns/promises';")]
    [InlineData("import * as timers from 'timers/promises';")]
    [InlineData("import * as stream from 'stream/promises';")]
    [InlineData("import * as crypto from 'crypto';")]
    [InlineData("fetch('http://127.0.0.1/');")]
    [InlineData(null)]
    public void EnabledAndImpliedFeaturesCompleteHandlesAndRejectFurtherWrites(string? source)
        => AssertComplete(EmitRuntime(source));

    [Fact]
    public void HostedEmissionEnablesAndCompletesPromiseForSynchronousSource()
        => AssertComplete(EmitRuntime("console.log(1);", hosted: true));

    [Fact]
    public void PromiseMetadataDoesNotIntroduceGuestRuntimeDependencies()
    {
        var runtime = EmitRuntime("Promise.resolve(1);");
        Assert.Empty(runtime.RequiredSharpTSRuntimeReasons);
        Assert.Equal(SharpTSRuntimeRequirements.None, runtime.RequiredSharpTSRuntimeRequirements);
        using var stream = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeType.Assembly).Save(stream);
        stream.Position = 0;
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        Assert.DoesNotContain(reader.AssemblyReferences,
            handle => reader.GetString(reader.GetAssemblyReference(handle).Name) == "SharpTS");
    }

    private static void AssertComplete(EmittedRuntime runtime)
    {
        var promise = runtime.RequirePromise();
        Assert.True(promise.IsComplete);
        Assert.Equal("$Promise", promise.Type.Name);
        Assert.Equal("$PromiseRejectedException", promise.RejectedExceptionType.Name);
        Assert.True(Assert.IsAssignableFrom<TypeBuilder>(promise.Type).IsCreated());
        Assert.True(Assert.IsAssignableFrom<TypeBuilder>(promise.RejectedExceptionType).IsCreated());
        Assert.True(promise.CapabilityType.IsCreated());
        Assert.True(promise.ResolveCallbackType.IsCreated());
        Assert.True(promise.RejectCallbackType.IsCreated());
        Assert.Same(promise.Type, promise.Ctor.DeclaringType);
        Assert.Same(promise.CapabilityType, promise.CapabilityCtor.DeclaringType);
        Assert.Same(promise.ResolveCallbackType, promise.ResolveCallbackCtor.DeclaringType);
        Assert.Same(promise.RejectCallbackType, promise.RejectCallbackCtor.DeclaringType);
        foreach (var property in Handles)
        {
            var value = property.GetValue(promise);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var exception = Assert.Throws<TargetInvocationException>(() => property.SetValue(promise, value));
            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }
        Assert.Throws<InvalidOperationException>(promise.CompleteEmission);
        Assert.Throws<InvalidOperationException>(runtime.BeginPromiseEmission);
    }

    [Fact]
    public void CombinatorsAndCapabilitiesVerifyAndRunStandalone()
    {
        const string source = """
            async function main(): Promise<void> {
                const capability = Promise.withResolvers();
                capability.resolve(3);
                console.log(await capability.promise);
                console.log((await Promise.all([Promise.resolve(1), 2])).join(','));
                console.log(await Promise.race([Promise.resolve(4)]));
                console.log(await Promise.any([Promise.reject('skip'), Promise.resolve(5)]));
                const settled = await Promise.allSettled([Promise.resolve(6), Promise.reject('bad')]);
                console.log(settled[0].status, settled[1].status);
                console.log(await new Promise<number>((resolve) => resolve(9)));
                console.log(await Promise.reject('caught').catch(reason => reason).finally(() => console.log('finally')));
            }
            main().catch(error => console.log('unexpected', error));
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("3\n1,2\n4\n5\nfulfilled rejected\n9\nfinally\ncaught\n",
            TestHarness.RunCompiledStandalone(source));
    }

    [Fact]
    public void PrototypeHelpersVerifyAndRunStandalone()
    {
        const string source = """
            const then = Promise.prototype.then;
            then.call(Promise.resolve(7), (value: number) => console.log(value + 1));
            const catchMethod = Promise.prototype.catch;
            catchMethod.call(Promise.reject('caught'), (reason: any) => console.log(reason));
            const finallyMethod = Promise.prototype.finally;
            finallyMethod.call(Promise.resolve(9), () => console.log('finally'));
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("8\ncaught\nfinally\n", TestHarness.RunCompiledStandalone(source));
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted = false)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"promise_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null)
            return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
