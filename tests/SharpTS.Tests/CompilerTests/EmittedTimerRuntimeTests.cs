using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedTimerRuntimeTests
{
    private static readonly Type[] ComponentTypes =
        [typeof(EmittedTimerRuntime), typeof(EmittedMicrotaskRuntime), typeof(EmittedTimerPromiseRuntime)];

    private static IEnumerable<PropertyInfo> Handles(Type type) => type.GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => ComponentTypes.SelectMany(type =>
        Handles(type).Select(property => new object[] { type, property.Name }));

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(Type type, string missingHandle)
    {
        var component = CreateDeclarations(type, missingHandle);
        var property = type.GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(component));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(() => Complete(component)).Message);
        Assert.False(IsComplete(component));
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(component, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(component, property.GetValue(CreateDeclarations(type)));
        Complete(component);
        AssertFrozen(component);
    }

    [Fact]
    public void RequiredQueuesAndOptionalPromiseTimersCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Same(runtime.Timers, runtime.Timers);
        Assert.Same(runtime.Microtasks, runtime.Microtasks);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Timers))!.SetMethod);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Microtasks))!.SetMethod);
        Assert.Throws<InvalidOperationException>(() => runtime.Timers.TimeoutType);
        Assert.Throws<InvalidOperationException>(() => runtime.Microtasks.QueuePromiseJob);
        Assert.Null(runtime.TimerPromises);
        Assert.Throws<InvalidOperationException>(() => runtime.RequireTimerPromises());
        runtime.BeginTimerPromiseEmission();
        var promises = runtime.RequireTimerPromises();
        Assert.Same(runtime.TimerPromises, promises);
        Assert.Throws<InvalidOperationException>(runtime.BeginTimerPromiseEmission);
        var declarations = CreateDeclarations(typeof(EmittedTimerPromiseRuntime));
        foreach (var property in Handles(typeof(EmittedTimerPromiseRuntime)))
            property.SetValue(promises, property.GetValue(declarations));
        promises.CompleteEmission();
        AssertFrozen(promises);
        Assert.Throws<InvalidOperationException>(runtime.BeginTimerPromiseEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.TimerPromises))!.SetMethod!.IsPrivate);
    }

    [Fact]
    public void PromiseJobDeclarationCanCallTheLaterMicrotaskDrain()
    {
        var microtasks = new EmittedMicrotaskRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("microtask_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Jobs", TypeAttributes.Public);
        microtasks.QueuePromiseJob = type.DefineMethod("QueuePromiseJob", MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), [typeof(Action)]);
        var caller = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [typeof(Action)]);
        caller.GetILGenerator().Emit(OpCodes.Ldarg_0);
        caller.GetILGenerator().Emit(OpCodes.Call, microtasks.QueuePromiseJob);
        caller.GetILGenerator().Emit(OpCodes.Ret);
        // The real runtime reserves QueuePromiseJob before Promise emission, then
        // reserves ProcessMicrotasks before filling the enqueue helper's body.
        microtasks.ProcessMicrotasks = type.DefineMethod("ProcessMicrotasks", MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), [typeof(Action)]);
        microtasks.QueuePromiseJob.GetILGenerator().Emit(OpCodes.Ldarg_0);
        microtasks.QueuePromiseJob.GetILGenerator().Emit(OpCodes.Call, microtasks.ProcessMicrotasks);
        microtasks.QueuePromiseJob.GetILGenerator().Emit(OpCodes.Ret);
        Assert.False(type.IsCreated());
        Assert.False(microtasks.IsComplete);
        Assert.Throws<InvalidOperationException>(microtasks.CompleteEmission);
        microtasks.ProcessMicrotasks.GetILGenerator().Emit(OpCodes.Ldarg_0);
        microtasks.ProcessMicrotasks.GetILGenerator().Emit(OpCodes.Callvirt, typeof(Action).GetMethod("Invoke")!);
        microtasks.ProcessMicrotasks.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        int count = 0;
        Assembly.Load(stream.ToArray()).GetType("Jobs")!.GetMethod("Run")!.Invoke(null, [(Action)(() => count++)]);
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData("console.log(1);", false, false)]
    [InlineData("queueMicrotask(() => {});", false, false)]
    [InlineData("setTimeout(() => {}, 0);", false, false)]
    [InlineData("import * as timers from 'timers';", false, false)]
    [InlineData("import * as timers from 'timers/promises';", false, true)]
    [InlineData("import { setTimeout } from 'node:timers/promises';", false, true)]
    [InlineData("Promise.resolve(1);", false, true)]
    [InlineData("async function f() { return 1; } f();", false, true)]
    [InlineData("console.log(1);", true, true)]
    [InlineData("queueMicrotask(() => {});", true, true)]
    [InlineData(null, false, true)]
    [InlineData(null, true, true)]
    public void MinimalEnabledHostedAndFullEmissionKeepDeclarationsAndFeatureGates(string? source, bool hosted, bool hasPromise)
    {
        var runtime = EmitRuntime(source, hosted);
        AssertFrozen(runtime.Timers);
        AssertFrozen(runtime.Microtasks);
        Assert.Equal(hasPromise, runtime.Promise is not null);
        Assert.Equal(hasPromise, runtime.TimerPromises is not null);
        Assert.Equal("$TSTimeout", runtime.Timers.TimeoutType.Name);
        Assert.Equal("$VirtualTimer", runtime.Timers.VirtualTimerType.Name);
        Assert.True(runtime.Timers.TimeoutType.IsSealed);
        Assert.True(runtime.Timers.VirtualTimerType.IsSealed);
        Assert.True(runtime.Timers.VirtualTimerHasRef.IsPublic);
        var components = new List<object> { runtime.Timers, runtime.Microtasks };
        if (hasPromise)
        {
            var promises = runtime.RequireTimerPromises();
            AssertFrozen(promises);
            Assert.Equal("$TimerPromiseClosure", promises.TimerPromiseClosureType.Name);
            Assert.Equal("$AsyncIntervalClosure", promises.AsyncIntervalClosureType.Name);
            components.Add(promises);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => runtime.RequireTimerPromises());
        }
        foreach (var component in components)
        foreach (var property in Handles(component.GetType()))
        {
            var handle = Assert.IsAssignableFrom<MemberInfo>(property.GetValue(component));
            Assert.True(Assert.IsAssignableFrom<TypeBuilder>(handle is Type ? handle : handle.DeclaringType).IsCreated(), property.Name);
        }
        using var stream = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeType.Assembly).Save(stream);
        stream.Position = 0;
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        Assert.Contains("$TimeoutClosure", types);
        Assert.Contains("$IntervalClosure", types);
        Assert.Contains("$MicrotaskCallback", types);
        Assert.Equal(hasPromise, types.Contains("$TimerPromiseClosure"));
        Assert.Equal(hasPromise, types.Contains("$AsyncIntervalClosure"));
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        foreach (var property in Handles(typeof(EmittedTimerPromiseRuntime)).Where(p => p.PropertyType == typeof(MethodBuilder)
            && p.Name.StartsWith("Set", StringComparison.Ordinal)))
            Assert.Equal(hasPromise, methods.Contains(property.Name));
    }

    private static object CreateDeclarations(Type type, string? missingHandle = null)
    {
        var component = Activator.CreateInstance(type, nonPublic: true)!;
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"timer_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var placeholder = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = placeholder.DefineDefaultConstructor(MethodAttributes.Public);
        var method = placeholder.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        var field = placeholder.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        foreach (var property in Handles(type).Where(property => property.Name != missingHandle))
        {
            object handle = property.PropertyType == typeof(TypeBuilder) ? placeholder
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor
                : property.PropertyType == typeof(FieldBuilder) ? field : method;
            property.SetValue(component, handle);
        }
        return component;
    }

    private static bool IsComplete(object component) => component switch
    {
        EmittedTimerRuntime timers => timers.IsComplete,
        EmittedMicrotaskRuntime microtasks => microtasks.IsComplete,
        EmittedTimerPromiseRuntime promises => promises.IsComplete,
        _ => throw new ArgumentException("Unknown component", nameof(component))
    };

    private static void Complete(object component)
    {
        switch (component)
        {
            case EmittedTimerRuntime timers: timers.CompleteEmission(); break;
            case EmittedMicrotaskRuntime microtasks: microtasks.CompleteEmission(); break;
            case EmittedTimerPromiseRuntime promises: promises.CompleteEmission(); break;
            default: throw new ArgumentException("Unknown component", nameof(component));
        }
    }

    private static void AssertFrozen(object component)
    {
        Assert.True(IsComplete(component));
        Assert.Throws<InvalidOperationException>(() => Complete(component));
        foreach (var property in Handles(component.GetType()))
        {
            var value = property.GetValue(component);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(component, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"timer_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
