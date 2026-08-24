#pragma warning disable SHARPTS_HOSTING001
#pragma warning disable xUnit1031

using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.Hosting;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.References;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.Hosting;

[Collection("ProcessLifecycleTests")]
public sealed class HostedInterpreterRuntimeTests
{
    [Fact]
    public void CompiledHostedAssembly_IsAValidFrameworkReference()
    {
        SharpTSProgram program = CreateProgram("export const value = 42;");
        var compiler = new ILCompiler($"hosted_reference_{Guid.NewGuid():N}");
        compiler.EnableHostedOutput();
        compiler.CompileModules(
            program.RuntimeModules.ToList(),
            program.Resolver,
            program.TypeMap);

        System.Reflection.Assembly assembly =
            System.Reflection.Assembly.Load(compiler.SaveToBytes());
        string[] references = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToArray();

        Assert.Contains("System.Runtime", references);
        Assert.DoesNotContain("System.Private.CoreLib", references);
        Assert.NotNull(assembly.GetType("SharpTSHostedProgramFactory", throwOnError: false));
        string[] suppressions = assembly
            .GetCustomAttributes(
                typeof(System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute),
                inherit: false)
            .Cast<System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute>()
            .Select(attribute => attribute.CheckId)
            .ToArray();
        Assert.Equal(
            ["IL2026", "IL2055", "IL2059", "IL2067", "IL2070", "IL2072", "IL2075", "IL3050"],
            suppressions);
    }

    [Fact]
    public void Initialization_IsAsynchronousAndPreservesHostSynchronizationContext()
    {
        var dispatcher = new DeterministicHostDispatcher();
        var lifetime = new RecordingLifetime();
        var errors = new RecordingErrorSink();
        using var output = new StringWriter();
        using var runtime = CreateRuntime(
            "console.log('initialized'); export {};",
            dispatcher, lifetime, errors, output);

        Task initialization = runtime.InitializeAsync();
        Assert.False(initialization.IsCompleted);
        dispatcher.RunUntil(() => initialization.IsCompleted);
        initialization.GetAwaiter().GetResult();

        Assert.Equal(SharpTSHostedRuntimeState.Running, runtime.State);
        Assert.Equal(dispatcher.OwnerThreadId, runtime.OwnerThreadId);
        Assert.Contains("initialized", output.ToString());
        Assert.Empty(errors.Errors);
        Assert.Empty(lifetime.Exits);
    }

    [Fact]
    public void EsmTopLevelAwait_SuspendsAndResumesThroughHostDeadline()
    {
        const string source = """
            console.log('module-start');
            await new Promise<void>(resolve => setTimeout(resolve, 10));
            console.log('module-resume');
            export {};
            """;
        var dispatcher = new DeterministicHostDispatcher();
        using var output = new StringWriter();
        using var runtime = CreateRuntime(
            source, dispatcher, new RecordingLifetime(), new RecordingErrorSink(), output);

        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => initialization.IsCompleted);
        initialization.GetAwaiter().GetResult();

        string text = output.ToString();
        Assert.True(text.IndexOf("module-start", StringComparison.Ordinal) <
                    text.IndexOf("module-resume", StringComparison.Ordinal));
        Assert.Contains(dispatcher.Trace, item => item.StartsWith("schedule:", StringComparison.Ordinal));
    }

    [Fact]
    public void AwaitedDependency_CompletesBeforeImporterAndCycleDoesNotDeadlock()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { value } from './dependency';
                import './cycle-a';
                console.log(`main-${value}`);
                """,
            ["dependency.ts"] = """
                console.log('dependency-start');
                await new Promise<void>(resolve => setTimeout(resolve, 2));
                console.log('dependency-resume');
                export const value = 42;
                """,
            ["cycle-a.ts"] = """
                import './cycle-b';
                console.log('cycle-a');
                export const a = 1;
                """,
            ["cycle-b.ts"] = """
                import './cycle-a';
                await Promise.resolve(1);
                console.log('cycle-b');
                export const b = 2;
                """,
        };
        var dispatcher = new DeterministicHostDispatcher();
        using var output = new StringWriter();
        using var runtime = new HostedInterpreterRuntime(
            dispatcher,
            new RecordingLifetime(),
            new RecordingErrorSink(),
            CreateProgram(files, "main.ts"),
            output,
            output);

        RunInitialization(runtime, dispatcher);

        string[] lines = Lines(output);
        Assert.True(Array.IndexOf(lines, "dependency-start") < Array.IndexOf(lines, "dependency-resume"));
        Assert.True(Array.IndexOf(lines, "dependency-resume") < Array.FindIndex(lines, line => line.StartsWith("main-", StringComparison.Ordinal)));
        Assert.Contains("cycle-a", lines);
        Assert.Contains("cycle-b", lines);
    }

    [Fact]
    public void EsmTopLevelAwait_SupportsCompoundConditionalAndLoopShapes()
    {
        const string source = """
            const compound = 2 + await new Promise<number>(
                resolve => setTimeout(() => resolve(3), 1));
            const conditional = compound === 5
                ? await Promise.resolve(7)
                : await Promise.resolve(0);
            let loop = 0;
            for (let index = 1; index <= 3; index++) {
                loop += await Promise.resolve(index);
            }
            if (await Promise.resolve(true)) {
                console.log(`shapes-${compound}-${conditional}-${loop}`);
            }
            export {};
            """;
        var dispatcher = new DeterministicHostDispatcher();
        using var output = new StringWriter();
        using var runtime = CreateRuntime(
            source, dispatcher, new RecordingLifetime(), new RecordingErrorSink(), output);

        RunInitialization(runtime, dispatcher);

        Assert.Equal(["shapes-5-7-6"], Lines(output));
    }

    [Fact]
    public void CompiledHostedTopLevelAwait_SupportsCompoundConditionalLoopAndCatchShapes()
    {
        const string source = """
            const compound = 2 + await new Promise<number>(
                resolve => setTimeout(() => resolve(3), 1));
            const conditional = compound === 5
                ? await Promise.resolve(7)
                : await Promise.resolve(0);
            let loop = 0;
            for (let index = 1; index <= 3; index++) {
                loop += await Promise.resolve(index);
            }
            try {
                await Promise.reject(new Error('caught-shape'));
                console.log('unexpected');
            } catch (error) {
                console.log('caught');
            }
            if (await Promise.resolve(true)) {
                console.log(`shapes-${compound}-${conditional}-${loop}`);
            }
            export {};
            """;
        SharpTSProgram program = CreateProgram(source);
        var compiler = new ILCompiler($"hosted_tla_shapes_{Guid.NewGuid():N}");
        compiler.EnableHostedOutput();
        compiler.CompileModules(
            program.RuntimeModules.ToList(),
            program.Resolver,
            program.TypeMap);

        var dispatcher = new DeterministicHostDispatcher();
        using var output = Infrastructure.AsyncLocalConsoleRedirector.Capture();
        using ISharpTSHostedRuntime runtime = SharpTSHostedAssembly.CreateRuntime(
            System.Reflection.Assembly.Load(compiler.SaveToBytes()),
            dispatcher,
            new RecordingLifetime(),
            new RecordingErrorSink());
        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => initialization.IsCompleted);
        initialization.GetAwaiter().GetResult();

        Assert.Equal(
            ["caught", "shapes-5-7-6"],
            output.GetOutput().Split(
                [Environment.NewLine],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    [Fact]
    public void HostedDynamicImport_AwaitsDependencyGraphAndCheckpointsEachModuleJob()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                console.log('main-start');
                const specifier = await Promise.resolve('./lazy');
                const loaded = await import(await Promise.resolve(specifier));
                console.log(`main-${loaded.value}`);
                export {};
                """,
            ["lazy.ts"] = """
                import { prefix } from './lazy-dependency';
                console.log('lazy-start');
                export const value = prefix + await new Promise<number>(
                    resolve => setTimeout(() => resolve(2), 2));
                console.log('lazy-end');
                queueMicrotask(() => console.log('lazy-microtask'));
                """,
            ["lazy-dependency.ts"] = """
                console.log('dependency-start');
                export const prefix = await Promise.resolve(40);
                queueMicrotask(() => console.log('dependency-microtask'));
                """,
        };
        var dispatcher = new DeterministicHostDispatcher();
        using var output = new StringWriter();
        using var runtime = new HostedInterpreterRuntime(
            dispatcher,
            new RecordingLifetime(),
            new RecordingErrorSink(),
            CreateProgram(files, "main.ts"),
            output,
            output);

        RunInitialization(runtime, dispatcher);

        Assert.Equal(
            [
                "main-start", "dependency-start", "dependency-microtask",
                "lazy-start", "lazy-end", "lazy-microtask", "main-42"
            ],
            Lines(output));
    }

    [Fact]
    public void CompiledHostedDynamicImport_InitializesDiscoveredModuleOnDemand()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                console.log('main-start');
                const loaded = await import('./sub/lazy');
                console.log(`main-${loaded.value}`);
                export {};
                """,
            ["sub/lazy.ts"] = """
                console.log('lazy-start');
                const dependency = await import('../dependency');
                export const value = dependency.value;
                console.log('lazy-end');
                """,
            ["dependency.ts"] = """
                console.log('dependency-start');
                export const value = 40 + await Promise.resolve(2);
                """,
        };
        SharpTSProgram program = CreateProgramWithDynamicImports(files, "main.ts");
        Assert.Contains(program.RuntimeModules, module => module.IsDynamicImportOnly);
        var compiler = new ILCompiler($"hosted_dynamic_{Guid.NewGuid():N}");
        compiler.EnableHostedOutput();
        compiler.CompileModules(
            program.RuntimeModules.ToList(),
            program.Resolver,
            program.TypeMap);

        var dispatcher = new DeterministicHostDispatcher();
        using var output = Infrastructure.AsyncLocalConsoleRedirector.Capture();
        using ISharpTSHostedRuntime runtime = SharpTSHostedAssembly.CreateRuntime(
            System.Reflection.Assembly.Load(compiler.SaveToBytes()),
            dispatcher,
            new RecordingLifetime(),
            new RecordingErrorSink());
        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => initialization.IsCompleted);
        initialization.GetAwaiter().GetResult();

        Assert.Equal(
            ["main-start", "lazy-start", "dependency-start", "lazy-end", "main-42"],
            output.GetOutput().Split(
                [Environment.NewLine],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    [Fact]
    public void CompiledHostedDynamicImport_RejectsMissingFailedAndSelfImportsInGuestCode()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                try { await import('./missing'); }
                catch (error) { console.log('missing-rejected'); }
                try { await import('./rejected'); }
                catch (error) { console.log('module-rejected'); }
                try { await import('./main'); }
                catch (error) { console.log('self-rejected'); }
                export {};
                """,
            ["rejected.ts"] = """
                await Promise.reject(new Error('dynamic-compiled-boom'));
                export {};
                """,
        };
        SharpTSProgram program = CreateProgramWithDynamicImports(files, "main.ts");
        var compiler = new ILCompiler($"hosted_dynamic_reject_{Guid.NewGuid():N}");
        compiler.EnableHostedOutput();
        compiler.CompileModules(
            program.RuntimeModules.ToList(),
            program.Resolver,
            program.TypeMap);

        var dispatcher = new DeterministicHostDispatcher();
        var errors = new RecordingErrorSink();
        using var output = Infrastructure.AsyncLocalConsoleRedirector.Capture();
        using ISharpTSHostedRuntime runtime = SharpTSHostedAssembly.CreateRuntime(
            System.Reflection.Assembly.Load(compiler.SaveToBytes()),
            dispatcher,
            new RecordingLifetime(),
            errors);
        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => initialization.IsCompleted);
        initialization.GetAwaiter().GetResult();

        Assert.Equal(
            ["missing-rejected", "module-rejected", "self-rejected"],
            output.GetOutput().Split(
                [Environment.NewLine],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.Empty(errors.Errors);
    }

    [Fact]
    public void HostedDynamicImport_RejectsMissingRejectedAndCyclicModulesInGuestCode()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                try {
                    await import('./missing');
                } catch (error) {
                    console.log('missing-rejected');
                }
                try {
                    await import('./rejected');
                } catch (error) {
                    console.log('module-rejected');
                }
                try {
                    await import('./main');
                } catch (error) {
                    console.log('cycle-rejected');
                }
                export {};
                """,
            ["rejected.ts"] = """
                await Promise.reject(new Error('dynamic-boom'));
                export const unreachable = true;
                """,
        };
        var dispatcher = new DeterministicHostDispatcher();
        var errors = new RecordingErrorSink();
        using var output = new StringWriter();
        using var runtime = new HostedInterpreterRuntime(
            dispatcher,
            new RecordingLifetime(),
            errors,
            CreateProgram(files, "main.ts"),
            output,
            output);

        RunInitialization(runtime, dispatcher);

        Assert.Equal(
            ["missing-rejected", "module-rejected", "cycle-rejected"],
            Lines(output));
        Assert.Empty(errors.Errors);
    }

    [Fact]
    public void Shutdown_CancelsSuspendedTopLevelAwaitAndLateTimerWork()
    {
        const string source = """
            console.log('suspended');
            await new Promise<void>(resolve => setTimeout(resolve, 60_000));
            console.log('unexpected-resume');
            export {};
            """;
        var dispatcher = new DeterministicHostDispatcher();
        var errors = new RecordingErrorSink();
        using var output = new StringWriter();
        using var runtime = CreateRuntime(
            source, dispatcher, new RecordingLifetime(), errors, output);

        Task initialization = runtime.InitializeAsync();
        Assert.True(dispatcher.RunNext());
        Task shutdown = runtime.ShutdownAsync();
        dispatcher.RunUntil(() => shutdown.IsCompleted);
        shutdown.GetAwaiter().GetResult();
        dispatcher.AdvanceBy(TimeSpan.FromMinutes(2));
        dispatcher.RunUntilIdle();

        Assert.True(initialization.IsFaulted);
        Assert.Equal(SharpTSHostedRuntimeState.Stopped, runtime.State);
        Assert.Equal(["suspended"], Lines(output));
        Assert.Empty(errors.Errors);
    }

    [Fact]
    public void CompiledShutdown_CancelsSuspendedTopLevelAwaitAndLateTimerWork()
    {
        SharpTSProgram program = CreateProgram("""
            console.log('compiled-suspended');
            await new Promise<void>(resolve => setTimeout(resolve, 60_000));
            console.log('unexpected-compiled-resume');
            export {};
            """);
        var compiler = new ILCompiler($"hosted_tla_cancel_{Guid.NewGuid():N}");
        compiler.EnableHostedOutput();
        compiler.CompileModules(
            program.RuntimeModules.ToList(),
            program.Resolver,
            program.TypeMap);

        var dispatcher = new DeterministicHostDispatcher();
        var errors = new RecordingErrorSink();
        using var output = Infrastructure.AsyncLocalConsoleRedirector.Capture();
        using ISharpTSHostedRuntime runtime = SharpTSHostedAssembly.CreateRuntime(
            System.Reflection.Assembly.Load(compiler.SaveToBytes()),
            dispatcher,
            new RecordingLifetime(),
            errors);
        Task initialization = runtime.InitializeAsync();
        Assert.True(dispatcher.RunNext());
        Task shutdown = runtime.ShutdownAsync();
        dispatcher.RunUntil(() => shutdown.IsCompleted);
        shutdown.GetAwaiter().GetResult();
        dispatcher.AdvanceBy(TimeSpan.FromMinutes(2));
        dispatcher.RunUntilIdle();

        Assert.True(initialization.IsFaulted);
        Assert.Equal(SharpTSHostedRuntimeState.Stopped, runtime.State);
        Assert.Equal(
            ["compiled-suspended"],
            output.GetOutput().Split(
                [Environment.NewLine],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.Empty(errors.Errors);
    }

    [Fact]
    public void RejectedTopLevelAwaitShapes_ReportInitializationErrorAndModulePath()
    {
        (string Shape, string Source)[] cases =
        [
            ("compound", "const value = 1 + await Promise.reject(new Error('compound-boom')); export {};"),
            ("conditional", "const value = true ? await Promise.reject(new Error('conditional-boom')) : 0; export {};"),
            ("loop", "for (let i = 0; i < 1; i++) { await Promise.reject(new Error('loop-boom')); } export {};"),
        ];

        foreach ((string shape, string source) in cases)
        {
            var dispatcher = new DeterministicHostDispatcher();
            var errors = new RecordingErrorSink();
            using var runtime = CreateRuntime(
                source,
                dispatcher,
                new RecordingLifetime(),
                errors,
                new StringWriter());

            Task initialization = runtime.InitializeAsync();
            dispatcher.RunUntil(() => initialization.IsCompleted);

            SharpTSHostedError error = Assert.Single(errors.Errors);
            Assert.True(initialization.IsFaulted);
            Assert.Equal(SharpTSHostedErrorPhase.Initialization, error.Phase);
            Assert.Contains($"{shape}-boom", error.Exception.Message, StringComparison.Ordinal);
            Assert.Contains("main.ts", error.Exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CompiledRejectedTopLevelAwait_ReportsInitializationErrorAndModulePath()
    {
        SharpTSProgram program = CreateProgram(
            "const value = 1 + await Promise.reject(new Error('compiled-boom')); export {};");
        var compiler = new ILCompiler($"hosted_tla_rejected_{Guid.NewGuid():N}");
        compiler.EnableHostedOutput();
        compiler.CompileModules(
            program.RuntimeModules.ToList(),
            program.Resolver,
            program.TypeMap);

        var dispatcher = new DeterministicHostDispatcher();
        var errors = new RecordingErrorSink();
        using ISharpTSHostedRuntime runtime = SharpTSHostedAssembly.CreateRuntime(
            System.Reflection.Assembly.Load(compiler.SaveToBytes()),
            dispatcher,
            new RecordingLifetime(),
            errors);
        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => initialization.IsCompleted);

        SharpTSHostedError error = Assert.Single(errors.Errors);
        Assert.True(initialization.IsFaulted);
        Assert.Equal(SharpTSHostedErrorPhase.Initialization, error.Phase);
        Assert.Contains("compiled-boom", error.Exception.Message, StringComparison.Ordinal);
        Assert.Contains("main.ts", error.Exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectedHostedDynamicImport_AttributesTheDiscoveredModule()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = "await import('./rejected'); export {};",
            ["rejected.ts"] = """
                await Promise.reject(new Error('dynamic-attribution'));
                export {};
                """,
        };
        var dispatcher = new DeterministicHostDispatcher();
        var errors = new RecordingErrorSink();
        using var runtime = new HostedInterpreterRuntime(
            dispatcher,
            new RecordingLifetime(),
            errors,
            CreateProgram(files, "main.ts"),
            new StringWriter(),
            new StringWriter());

        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => initialization.IsCompleted);

        SharpTSHostedError error = Assert.Single(errors.Errors);
        Assert.True(initialization.IsFaulted);
        Assert.Contains("dynamic-attribution", error.Exception.Message, StringComparison.Ordinal);
        Assert.Contains("rejected.ts", error.Exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GuestMacrotasks_AreFifoWithFullMicrotaskCheckpointAndHostFairness()
    {
        const string source = """
            setTimeout(() => {
                console.log('macro-1');
                queueMicrotask(() => console.log('micro-1'));
                Promise.resolve(1).then(() => console.log('promise-1'));
            }, 0);
            setTimeout(() => console.log('macro-2'), 0);
            export {};
            """;
        var dispatcher = new DeterministicHostDispatcher();
        var order = new List<string>();
        using var output = new CallbackTextWriter(line => order.Add(line));
        using var runtime = CreateRuntime(
            source, dispatcher, new RecordingLifetime(), new RecordingErrorSink(), output);
        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => initialization.IsCompleted);
        initialization.GetAwaiter().GetResult();

        dispatcher.Post(() => order.Add("sentinel"));
        dispatcher.RunUntil(() => order.Contains("macro-2"));

        Assert.Equal(["macro-1", "micro-1", "promise-1", "sentinel", "macro-2"], order);
    }

    [Fact]
    public void PromiseReactions_RunAfterHostedInterpreterModuleJob()
    {
        const string source = """
            const order: string[] = ["start"];
            Promise.resolve(1).then((): void => { order.push("then"); });
            order.push("after");
            queueMicrotask((): void => console.log(order.join(":")));
            export {};
            """;
        var dispatcher = new DeterministicHostDispatcher();
        using var output = new StringWriter();
        using var runtime = CreateRuntime(
            source, dispatcher, new RecordingLifetime(), new RecordingErrorSink(), output);

        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => initialization.IsCompleted);
        initialization.GetAwaiter().GetResult();

        Assert.Equal(["start:after:then"], Lines(output));
    }

    [Fact]
    public void PromiseReactions_RunAfterHostedCompiledModuleJob()
    {
        SharpTSProgram program = CreateProgram("""
            const order: string[] = ["start"];
            Promise.resolve(1).then((): void => { order.push("then"); });
            order.push("after");
            queueMicrotask((): void => console.log(order.join(":")));
            export {};
            """);
        var compiler = new ILCompiler($"hosted_promise_jobs_{Guid.NewGuid():N}");
        compiler.EnableHostedOutput();
        compiler.CompileModules(
            program.RuntimeModules.ToList(), program.Resolver, program.TypeMap);

        var dispatcher = new DeterministicHostDispatcher();
        using var output = Infrastructure.AsyncLocalConsoleRedirector.Capture();
        using ISharpTSHostedRuntime runtime = SharpTSHostedAssembly.CreateRuntime(
            System.Reflection.Assembly.Load(compiler.SaveToBytes()),
            dispatcher,
            new RecordingLifetime(),
            new RecordingErrorSink());

        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => initialization.IsCompleted);
        initialization.GetAwaiter().GetResult();

        Assert.Equal("start:after:then\n", output.GetOutput().Replace("\r\n", "\n"));
    }

    [Fact]
    public void OffThreadNotifications_CoalesceWakeAndRunOnePerTurn()
    {
        var dispatcher = new DeterministicHostDispatcher();
        using var runtime = CreateRunningRuntime(dispatcher);
        var order = new List<(string Name, int Thread)>();
        int postsBefore = dispatcher.PostCount;

        Task.Run(() =>
        {
            runtime.Notify(() => order.Add(("callback-1", Environment.CurrentManagedThreadId)));
            runtime.Notify(() => order.Add(("callback-2", Environment.CurrentManagedThreadId)));
            runtime.Notify(() => order.Add(("callback-3", Environment.CurrentManagedThreadId)));
        }).GetAwaiter().GetResult();

        Assert.Equal(postsBefore + 1, dispatcher.PostCount);
        dispatcher.Post(() => order.Add(("sentinel", Environment.CurrentManagedThreadId)));
        dispatcher.RunUntil(() => order.Count == 4);
        Assert.Equal(
            ["callback-1", "sentinel", "callback-2", "callback-3"],
            order.Select(item => item.Name));
        Assert.All(order, item => Assert.Equal(dispatcher.OwnerThreadId, item.Thread));
    }

    [Fact]
    public void OwnerNotifications_ArePostedAndOffThreadReturnValuesAreRejected()
    {
        var dispatcher = new DeterministicHostDispatcher();
        using var runtime = CreateRunningRuntime(dispatcher);
        var order = new List<string>();

        runtime.Invoke(() =>
        {
            order.Add("outer-start");
            runtime.Notify(() => order.Add("inner"));
            order.Add("outer-end");
        });
        Assert.Equal(["outer-start", "outer-end"], order);

        Assert.True(dispatcher.RunNext());
        Exception? exception = Task.Run(() => Record.Exception(() => runtime.Invoke(() => 42)))
            .GetAwaiter().GetResult();

        Assert.Equal(["outer-start", "outer-end", "inner"], order);
        Assert.Contains("return-valued", Assert.IsType<InvalidOperationException>(exception).Message);
    }

    [Fact]
    public void NativePredicate_ReturnsSynchronouslyButDefersItsMicrotaskCheckpoint()
    {
        var dispatcher = new DeterministicHostDispatcher();
        using var runtime = CreateRunningRuntime(dispatcher);
        var order = new List<string>();

        object? result = runtime.InvokeNativeCallback(() =>
        {
            order.Add("predicate");
            runtime.EnqueueMicrotask(() => order.Add("microtask"));
            return true;
        });

        Assert.Equal(true, result);
        Assert.Equal(["predicate"], order);

        Assert.True(dispatcher.RunNext());
        Assert.Equal(["predicate", "microtask"], order);
    }

    [Fact]
    public void HostMicrotasks_CoalesceAtBoundaryAndFailuresUseOrderedErrorShutdown()
    {
        var dispatcher = new DeterministicHostDispatcher();
        var lifetime = new RecordingLifetime();
        var errors = new RecordingErrorSink();
        using var runtime = CreateRuntime(
            "export const ready = true;",
            dispatcher,
            lifetime,
            errors,
            new StringWriter());
        RunInitialization(runtime, dispatcher);
        var order = new List<string>();

        runtime.Invoke(() =>
        {
            runtime.EnqueueMicrotask(() => order.Add("microtask-1"));
            runtime.EnqueueMicrotask(() => order.Add("microtask-2"));
            order.Add("boundary");
            Assert.Equal(["boundary"], order);
        });

        Assert.Equal(["boundary", "microtask-1", "microtask-2"], order);

        runtime.Notify(() => runtime.EnqueueMicrotask(
            () => throw new InvalidOperationException("rerender failed")));
        Assert.Empty(errors.Errors);
        Assert.True(dispatcher.RunNext());
        Assert.Single(errors.Errors);
        Assert.Equal(SharpTSHostedErrorPhase.Running, errors.Errors[0].Phase);
        Assert.Equal(SharpTSHostedShutdownReason.UncaughtError, runtime.ShutdownReason);
        dispatcher.RunUntil(() => runtime.State == SharpTSHostedRuntimeState.Stopped);
        Assert.Single(lifetime.Exits);
    }

    [Fact]
    public void IntervalsPreserveOrderAndCancellation()
    {
        const string source = """
            let count = 0;
            const interval = setInterval(() => {
                count++;
                console.log(`interval-${count}`);
                if (count === 2) clearInterval(interval);
            }, 5);
            interval.unref();
            setTimeout(() => console.log('timeout'), 5);
            export {};
            """;
        var dispatcher = new DeterministicHostDispatcher();
        using var output = new StringWriter();
        using var runtime = CreateRuntime(
            source, dispatcher, new RecordingLifetime(), new RecordingErrorSink(), output);
        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => initialization.IsCompleted);
        initialization.GetAwaiter().GetResult();
        dispatcher.RunUntil(() =>
            output.ToString().Contains("interval-2", StringComparison.Ordinal) &&
            output.ToString().Contains("timeout", StringComparison.Ordinal));

        string[] lines = Lines(output);
        Assert.Equal(2, lines.Count(line => line.StartsWith("interval-", StringComparison.Ordinal)));
        Assert.True(Array.IndexOf(lines, "interval-1") < Array.IndexOf(lines, "interval-2"));
        Assert.Contains("timeout", lines);
    }

    [Fact]
    public void GracefulShutdown_EmitsLifecycleAndRunsCleanupInReverseOrder()
    {
        const string source = """
            process.on('beforeExit', () => {
                console.log('beforeExit');
                queueMicrotask(() => console.log('beforeExit-microtask'));
            });
            process.on('exit', () => {
                console.log('exit');
                process.removeAllListeners('beforeExit');
                process.removeAllListeners('exit');
            });
            export {};
            """;
        var dispatcher = new DeterministicHostDispatcher();
        using var output = new StringWriter();
        using var runtime = CreateRuntime(
            source, dispatcher, new RecordingLifetime(), new RecordingErrorSink(), output);
        var cleanup = new List<int>();
        runtime.RegisterCleanup(() => cleanup.Add(1));
        runtime.RegisterCleanup(() => cleanup.Add(2));
        RunInitialization(runtime, dispatcher);

        Task shutdown = runtime.ShutdownAsync();
        dispatcher.RunUntil(() => shutdown.IsCompleted);
        shutdown.GetAwaiter().GetResult();
        runtime.Notify(() => cleanup.Add(99));

        Assert.Equal(SharpTSHostedRuntimeState.Stopped, runtime.State);
        Assert.Equal([2, 1], cleanup);
        Assert.Equal(["beforeExit", "beforeExit-microtask", "exit"], Lines(output));
    }

    [Fact]
    public void ProcessExit_SkipsBeforeExitAndRequestsSuppliedCode()
    {
        const string source = """
            process.on('beforeExit', () => console.log('beforeExit'));
            process.on('exit', code => {
                console.log(`exit-${code}-${process.exitCode}`);
                process.removeAllListeners('beforeExit');
                process.removeAllListeners('exit');
            });
            process.exit(7);
            console.log('unreachable');
            export {};
            """;
        var dispatcher = new DeterministicHostDispatcher();
        var lifetime = new RecordingLifetime();
        using var output = new StringWriter();
        using var runtime = CreateRuntime(
            source, dispatcher, lifetime, new RecordingErrorSink(), output);

        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => runtime.State == SharpTSHostedRuntimeState.Stopped);

        Assert.True(initialization.IsFaulted);
        Assert.Equal([(7, dispatcher.OwnerThreadId)], lifetime.Exits);
        Assert.Equal(["exit-7-7"], Lines(output));
    }

    [Fact]
    public void PostStartUncaughtError_FlowsToSinkAndInitiatesShutdown()
    {
        const string source = """
            setTimeout(() => { throw new Error('boom'); }, 0);
            export {};
            """;
        var dispatcher = new DeterministicHostDispatcher();
        var lifetime = new RecordingLifetime();
        var errors = new RecordingErrorSink();
        using var runtime = CreateRuntime(source, dispatcher, lifetime, errors, new StringWriter());
        RunInitialization(runtime, dispatcher);

        dispatcher.RunUntil(() => runtime.State == SharpTSHostedRuntimeState.Stopped);

        Assert.Single(errors.Errors);
        Assert.Equal(SharpTSHostedErrorPhase.Running, errors.Errors[0].Phase);
        Assert.Equal(1, lifetime.Exits.Single().ExitCode);
    }

    [Fact]
    public void StartupFailure_FaultsInitializationAndRequestsExitOne()
    {
        const string source = "throw new Error('startup'); export {};";
        var dispatcher = new DeterministicHostDispatcher();
        var lifetime = new RecordingLifetime();
        var errors = new RecordingErrorSink();
        using var runtime = CreateRuntime(source, dispatcher, lifetime, errors, new StringWriter());

        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => initialization.IsCompleted);

        Assert.Equal(SharpTSHostedRuntimeState.Faulted, runtime.State);
        Assert.True(initialization.IsFaulted);
        Assert.Equal(SharpTSHostedErrorPhase.Initialization, Assert.Single(errors.Errors).Phase);
        Assert.Equal(1, Assert.Single(lifetime.Exits).ExitCode);
    }

    [Fact]
    public void ProgramLoader_ReusesTsconfigDeclarationsAndReferenceSetup()
    {
        string root = Path.Combine(Path.GetTempPath(), $"sharpts-program-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string entry = Path.Combine(root, "main.ts");
            string config = Path.Combine(root, "tsconfig.json");
            File.WriteAllText(entry, "export const answer: number = 42;");
            File.WriteAllText(config, """{"compilerOptions":{"strict":true},"files":["main.ts"]}""");

            SharpTSProgram program = SharpTSProgramLoader.Load(entry, new SharpTSProgramLoadOptions
            {
                TsConfigPath = config,
            });

            Assert.Equal(Path.GetFullPath(entry), program.EntryPath);
            Assert.Equal(Path.GetFullPath(config), program.Configuration!.ConfigPath);
            Assert.NotEmpty(program.RuntimeModules);
            Assert.DoesNotContain(
                program.Diagnostics,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProgramLoader_IncludesLiteralDynamicImportModulesAsOnDemandRoots()
    {
        string root = Path.Combine(Path.GetTempPath(), $"sharpts-program-dynamic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string entry = Path.Combine(root, "main.ts");
            File.WriteAllText(entry, """
                const path = await Promise.resolve('./lazy');
                await import(await Promise.resolve(path));
                export {};
                """);
            File.WriteAllText(
                Path.Combine(root, "lazy.ts"),
                "export const value = 42;");

            SharpTSProgram program = SharpTSProgramLoader.Load(entry, new SharpTSProgramLoadOptions
            {
                DiscoverTsConfig = false,
            });

            ParsedModule dynamicModule = Assert.Single(
                program.RuntimeModules, module => module.IsDynamicImportOnly);
            Assert.EndsWith("lazy.ts", dynamicModule.Path, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Path.GetFullPath(entry), program.RuntimeModules[^1].Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CompiledHostedTopLevelAwait_InitializesThroughVersionedAbi()
    {
        SharpTSProgram program = CreateProgram(
            "await new Promise<void>(resolve => setTimeout(resolve, 1)); export {};");
        var compiler = new ILCompiler($"hosted_tla_{Guid.NewGuid():N}");
        compiler.EnableHostedOutput();
        compiler.CompileModules(
            program.RuntimeModules.ToList(),
            program.Resolver,
            program.TypeMap);

        System.Reflection.Assembly assembly =
            System.Reflection.Assembly.Load(compiler.SaveToBytes());
        SharpTSHostedProgramAttribute marker = Assert.Single(
            assembly.GetCustomAttributes(typeof(SharpTSHostedProgramAttribute), inherit: false)
                .Cast<SharpTSHostedProgramAttribute>());
        Assert.Equal(SharpTSHostedAbi.CurrentVersion, marker.AbiVersion);
        Assert.Null(assembly.GetType("$Program")!.GetMethod("InitializeHosted"));
        Assert.Null(assembly.GetType("$Program")!.GetMethod("PumpHostedOnce"));

        var dispatcher = new DeterministicHostDispatcher();
        ISharpTSHostedRuntime runtime = SharpTSHostedAssembly.CreateRuntime(
            assembly,
            dispatcher,
            new RecordingLifetime(),
            new RecordingErrorSink());
        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => initialization.IsCompleted);
        initialization.GetAwaiter().GetResult();

        Assert.Equal(SharpTSHostedRuntimeState.Running, runtime.State);
        Assert.Equal(dispatcher.OwnerThreadId, runtime.OwnerThreadId);
        Assert.Contains(dispatcher.Trace, item => item.StartsWith("schedule:", StringComparison.Ordinal));
        runtime.Dispose();
    }

    [Fact]
    public void CompiledHostedTopLevelAwait_ResumesExportBeforeDependentModule()
    {
        SharpTSProgram program = CreateProgram(
            new Dictionary<string, string>
            {
                ["dependency.ts"] = """
                    export const answer: number = await new Promise<number>(
                        resolve => setTimeout(() => resolve(13), 1));
                    """,
                ["main.ts"] = """
                    import { answer } from './dependency';
                    function main(): number { return answer; }
                    export {};
                    """,
            },
            "main.ts");
        var compiler = new ILCompiler($"hosted_tla_export_{Guid.NewGuid():N}");
        compiler.EnableHostedOutput();
        compiler.CompileModules(
            program.RuntimeModules.ToList(),
            program.Resolver,
            program.TypeMap);

        var dispatcher = new DeterministicHostDispatcher();
        var lifetime = new RecordingLifetime();
        ISharpTSHostedRuntime runtime = SharpTSHostedAssembly.CreateRuntime(
            System.Reflection.Assembly.Load(compiler.SaveToBytes()),
            dispatcher,
            lifetime,
            new RecordingErrorSink());
        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => runtime.State == SharpTSHostedRuntimeState.Stopped);
        initialization.GetAwaiter().GetResult();

        Assert.Equal(SharpTSHostedShutdownReason.ProgramCompleted, runtime.ShutdownReason);
        Assert.Equal([(13, dispatcher.OwnerThreadId)], lifetime.Exits);
        runtime.Dispose();
    }

    [Fact]
    public void CompiledHostedTopLevelAwait_PopulatesDefaultAndFunctionExports()
    {
        SharpTSProgram program = CreateProgram(
            new Dictionary<string, string>
            {
                ["dependency.ts"] = """
                    export default 10 + await Promise.resolve(2);
                    export function increment(value: number): number { return value + 1; }
                    """,
                ["main.ts"] = """
                    import answer, { increment } from './dependency';
                    function main(): number { return increment(answer); }
                    export {};
                    """,
            },
            "main.ts");
        var compiler = new ILCompiler($"hosted_tla_default_export_{Guid.NewGuid():N}");
        compiler.EnableHostedOutput();
        compiler.CompileModules(
            program.RuntimeModules.ToList(),
            program.Resolver,
            program.TypeMap);

        var dispatcher = new DeterministicHostDispatcher();
        var lifetime = new RecordingLifetime();
        using ISharpTSHostedRuntime runtime = SharpTSHostedAssembly.CreateRuntime(
            System.Reflection.Assembly.Load(compiler.SaveToBytes()),
            dispatcher,
            lifetime,
            new RecordingErrorSink());
        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => runtime.State == SharpTSHostedRuntimeState.Stopped);
        initialization.GetAwaiter().GetResult();

        Assert.Equal([(13, dispatcher.OwnerThreadId)], lifetime.Exits);
    }

    [Fact]
    public void CompiledHostedProcessExit_IsForcedAndRequestsSuppliedCode()
    {
        SharpTSProgram program = CreateProgram(
            "process.exit(9); throw new Error('unreachable'); export {};");
        var compiler = new ILCompiler($"hosted_exit_{Guid.NewGuid():N}");
        compiler.EnableHostedOutput();
        compiler.CompileModules(
            program.RuntimeModules.ToList(),
            program.Resolver,
            program.TypeMap);

        var dispatcher = new DeterministicHostDispatcher();
        var lifetime = new RecordingLifetime();
        var errors = new RecordingErrorSink();
        ISharpTSHostedRuntime runtime = SharpTSHostedAssembly.CreateRuntime(
            System.Reflection.Assembly.Load(compiler.SaveToBytes()),
            dispatcher,
            lifetime,
            errors);
        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => runtime.State == SharpTSHostedRuntimeState.Stopped);

        Assert.True(initialization.IsFaulted);
        Assert.Equal(SharpTSHostedShutdownReason.ProcessExit, runtime.ShutdownReason);
        Assert.Equal([(9, dispatcher.OwnerThreadId)], lifetime.Exits);
        Assert.Empty(errors.Errors);
        runtime.Dispose();
    }

    [Fact]
    public void HostedAssemblyLoader_RejectsMissingAndMismatchedAbiBeforeActivation()
    {
        var dispatcher = new DeterministicHostDispatcher();
        var lifetime = new RecordingLifetime();
        var errors = new RecordingErrorSink();

        SharpTSHostedAbiException missing = Assert.Throws<SharpTSHostedAbiException>(() =>
            SharpTSHostedAssembly.CreateRuntime(
                typeof(HostedInterpreterRuntimeTests).Assembly,
                dispatcher,
                lifetime,
                errors));
        Assert.Contains("exactly one", missing.Message);

        var name = new System.Reflection.AssemblyName($"hosted_badabi_{Guid.NewGuid():N}");
        System.Reflection.Emit.AssemblyBuilder badAssembly =
            System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                name, System.Reflection.Emit.AssemblyBuilderAccess.Run);
        var marker = new System.Reflection.Emit.CustomAttributeBuilder(
            typeof(SharpTSHostedProgramAttribute).GetConstructor([typeof(int), typeof(Type)])!,
            [SharpTSHostedAbi.CurrentVersion + 1, typeof(HostedInterpreterRuntimeTests)]);
        badAssembly.SetCustomAttribute(marker);

        SharpTSHostedAbiException mismatch = Assert.Throws<SharpTSHostedAbiException>(() =>
            SharpTSHostedAssembly.CreateRuntime(
                badAssembly,
                dispatcher,
                lifetime,
                errors));
        Assert.Contains($"ABI {SharpTSHostedAbi.CurrentVersion + 1}", mismatch.Message);
        Assert.Empty(lifetime.Exits);
    }

    [Fact]
    public void CompiledHostedNumericMain_CompletesGracefullyThroughHostLifetime()
    {
        SharpTSProgram program = CreateProgram(
            "function main(): number { return 7; } export {};");
        var compiler = new ILCompiler($"hosted_main_{Guid.NewGuid():N}");
        compiler.EnableHostedOutput();
        compiler.CompileModules(
            program.RuntimeModules.ToList(),
            program.Resolver,
            program.TypeMap);

        var dispatcher = new DeterministicHostDispatcher();
        var lifetime = new RecordingLifetime();
        ISharpTSHostedRuntime runtime = SharpTSHostedAssembly.CreateRuntime(
            System.Reflection.Assembly.Load(compiler.SaveToBytes()),
            dispatcher,
            lifetime,
            new RecordingErrorSink());
        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => runtime.State == SharpTSHostedRuntimeState.Stopped);
        initialization.GetAwaiter().GetResult();

        Assert.Equal(SharpTSHostedShutdownReason.ProgramCompleted, runtime.ShutdownReason);
        Assert.Equal([(7, dispatcher.OwnerThreadId)], lifetime.Exits);
        runtime.Dispose();
    }

    [Fact]
    public void OrdinaryTypeChecking_StillRejectsTopLevelAwait()
    {
        string root = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(), $"sharpts_non_hosted_{Guid.NewGuid():N}"));
        string entryPath = Path.Combine(root, "main.ts");
        var resolver = new ModuleResolver(
            entryPath,
            new Dictionary<string, string> { [entryPath] = "await Promise.resolve(1); export {};" });
        ParsedModule entry = resolver.LoadProgram(entryPath);
        var checker = new TypeChecker();

        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);

        Assert.Contains(
            checker.GetDiagnostics(),
            diagnostic => diagnostic.TsCode == "TS1308" &&
                diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static HostedInterpreterRuntime CreateRunningRuntime(DeterministicHostDispatcher dispatcher)
    {
        var runtime = CreateRuntime(
            "export const ready = true;",
            dispatcher,
            new RecordingLifetime(),
            new RecordingErrorSink(),
            new StringWriter());
        RunInitialization(runtime, dispatcher);
        return runtime;
    }

    private static void RunInitialization(
        HostedInterpreterRuntime runtime,
        DeterministicHostDispatcher dispatcher)
    {
        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => initialization.IsCompleted);
        initialization.GetAwaiter().GetResult();
    }

    private static HostedInterpreterRuntime CreateRuntime(
        string source,
        DeterministicHostDispatcher dispatcher,
        RecordingLifetime lifetime,
        RecordingErrorSink errors,
        TextWriter output) =>
        new(dispatcher, lifetime, errors, CreateProgram(source), output, output);

    private static SharpTSProgram CreateProgram(string source)
        => CreateProgram(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts");

    private static SharpTSProgram CreateProgram(
        IReadOnlyDictionary<string, string> sources,
        string entryFile)
    {
        string root = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(), $"sharpts_hosted_{Guid.NewGuid():N}"));
        string entryPath = Path.Combine(root, entryFile);
        var files = sources.ToDictionary(
            pair => Path.Combine(root, pair.Key),
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        var resolver = new ModuleResolver(entryPath, files);
        ParsedModule entry = resolver.LoadProgram(entryPath);
        List<ParsedModule> runtimeModules = resolver.GetRuntimeModulesInOrder(entry);
        List<ParsedModule> typeModules = resolver.GetModulesInOrder(entry);
        var checker = new TypeChecker();
        checker.EnableHostedTopLevelAwait();
        TypeMap typeMap = checker.CheckModules(typeModules, resolver);
        Diagnostic[] errors = checker.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length != 0)
            throw new Xunit.Sdk.XunitException(string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
        return new SharpTSProgram(
            entryPath,
            configuration: null,
            DecoratorMode.Stage3,
            ReferenceSet.Empty,
            resolver,
            runtimeModules,
            typeModules,
            typeMap,
            checker.GetDiagnostics().ToArray());
    }

    private static SharpTSProgram CreateProgramWithDynamicImports(
        IReadOnlyDictionary<string, string> sources,
        string entryFile)
    {
        string root = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(), $"sharpts_hosted_dynamic_{Guid.NewGuid():N}"));
        string entryPath = Path.Combine(root, entryFile);
        var files = sources.ToDictionary(
            pair => Path.Combine(root, pair.Key),
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        var resolver = new ModuleResolver(entryPath, files);
        ParsedModule entry = resolver.LoadProgram(entryPath);
        var checker = new TypeChecker();
        checker.EnableHostedTopLevelAwait();
        List<ParsedModule> initialTypes = resolver.GetModulesInOrder(entry);
        var dynamicModules = new List<ParsedModule>();
        var dynamicPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processed = new HashSet<(string Specifier, string ImportingModulePath)>();
        TypeMap typeMap = checker.CheckModules(initialTypes, resolver);
        while (true)
        {
            var pending = checker.DynamicImportReferences
                .Where(reference => processed.Add(reference))
                .ToArray();
            if (pending.Length == 0) break;
            List<ParsedModule> discovered = pending
                .SelectMany(reference => resolver.LoadDynamicImportModules(
                    [reference.Specifier],
                    reference.ImportingModulePath,
                    DecoratorMode.Stage3))
                .Where(module => dynamicPaths.Add(module.Path))
                .ToList();
            if (discovered.Count == 0) continue;
            dynamicModules.AddRange(discovered);
            typeMap = checker.CheckModules(
                resolver.GetModulesInOrder(dynamicModules.Append(entry)), resolver);
        }
        List<ParsedModule> runtimeModules = resolver.GetRuntimeModulesInOrder(
            dynamicModules.Append(entry));
        List<ParsedModule> typeModules = resolver.GetModulesInOrder(
            dynamicModules.Append(entry));
        typeMap = checker.CheckModules(typeModules, resolver);
        Diagnostic[] errors = checker.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length != 0)
            throw new Xunit.Sdk.XunitException(string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
        return new SharpTSProgram(
            entryPath,
            configuration: null,
            DecoratorMode.Stage3,
            ReferenceSet.Empty,
            resolver,
            runtimeModules,
            typeModules,
            typeMap,
            checker.GetDiagnostics().ToArray());
    }

    private static string[] Lines(StringWriter output) => output.ToString()
        .Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private sealed class CallbackTextWriter(Action<string> onLine) : StringWriter
    {
        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            if (value != null)
                onLine(value);
        }
    }
}
