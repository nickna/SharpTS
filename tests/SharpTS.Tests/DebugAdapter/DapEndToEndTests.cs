using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SharpTS.Tests.DebugAdapter;

[Collection("DebugAdapterTests")]
public sealed class DapEndToEndTests
{
    [Fact]
    public async Task LaunchStepInspectEvaluateAndExitTranscript()
    {
        string directory = CreateFixtureDirectory();
        string program = Path.Combine(directory, "main.ts");
        await File.WriteAllTextAsync(program, """
            let seed = 40;
            let answer = seed + 2;
            console.log(answer);
            """);

        await using var dap = new DapProtocolHarness();
        JsonElement initialize = await dap.RequestAsync("initialize", new
        {
            adapterID = "sharpts",
            linesStartAt1 = true,
            columnsStartAt1 = true,
        });
        Assert.True(initialize.GetProperty("success").GetBoolean());
        Assert.True(initialize.GetProperty("body")
            .GetProperty("supportsConfigurationDoneRequest").GetBoolean());
        await dap.WaitForEventAsync("initialized");

        AssertSuccess(await dap.RequestAsync("launch", new
        {
            program,
            cwd = directory,
            stopOnEntry = true,
            console = "internalConsole",
        }));
        JsonElement breakpoints = await dap.RequestAsync("setBreakpoints", new
        {
            source = new { name = "main.ts", path = program },
            breakpoints = new[] { new { line = 3 } },
        });
        Assert.True(breakpoints.GetProperty("body").GetProperty("breakpoints")[0]
            .GetProperty("verified").GetBoolean());
        AssertSuccess(await dap.RequestAsync("configurationDone"));

        JsonElement entryStop = await dap.WaitForEventAsync("stopped");
        Assert.Equal("entry", entryStop.GetProperty("body").GetProperty("reason").GetString());
        JsonElement entryStack = await dap.RequestAsync("stackTrace", new { threadId = 1 });
        Assert.Equal(1, entryStack.GetProperty("body").GetProperty("stackFrames")[0]
            .GetProperty("line").GetInt32());

        AssertSuccess(await dap.RequestAsync("next", new { threadId = 1 }));
        JsonElement stepStop = await dap.WaitForEventAsync("stopped");
        Assert.Equal("step", stepStop.GetProperty("body").GetProperty("reason").GetString());
        JsonElement stack = await dap.RequestAsync("stackTrace", new { threadId = 1 });
        JsonElement topFrame = stack.GetProperty("body").GetProperty("stackFrames")[0];
        Assert.Equal(2, topFrame.GetProperty("line").GetInt32());

        int frameId = topFrame.GetProperty("id").GetInt32();
        JsonElement scopes = await dap.RequestAsync("scopes", new { frameId });
        JsonElement locals = scopes.GetProperty("body").GetProperty("scopes")
            .EnumerateArray().First(scope => scope.GetProperty("name").GetString() == "Module");
        JsonElement variables = await dap.RequestAsync("variables", new
        {
            variablesReference = locals.GetProperty("variablesReference").GetInt32(),
        });
        Assert.Contains(variables.GetProperty("body").GetProperty("variables").EnumerateArray(),
            variable => variable.GetProperty("name").GetString() == "seed"
                && variable.GetProperty("value").GetString() == "40");

        JsonElement evaluation = await dap.RequestAsync("evaluate", new
        {
            expression = "seed + 2",
            frameId,
            context = "watch",
        });
        AssertSuccess(evaluation);
        Assert.Equal("42", evaluation.GetProperty("body").GetProperty("result").GetString());

        AssertSuccess(await dap.RequestAsync("continue", new { threadId = 1 }));
        JsonElement breakpointStop = await dap.WaitForEventAsync("stopped");
        Assert.Equal("breakpoint", breakpointStop.GetProperty("body").GetProperty("reason").GetString());
        AssertSuccess(await dap.RequestAsync("continue", new { threadId = 1 }));

        JsonElement output = await dap.WaitForEventAsync("output");
        Assert.Contains("42", output.GetProperty("body").GetProperty("output").GetString());
        JsonElement exited = await dap.WaitForEventAsync("exited");
        Assert.Equal(0, exited.GetProperty("body").GetProperty("exitCode").GetInt32());
        await dap.WaitForEventAsync("terminated");
    }

    [Fact]
    public async Task InvalidOrderAndUnknownCommandReturnOneFailureResponse()
    {
        await using var dap = new DapProtocolHarness();
        JsonElement earlyLaunch = await dap.RequestAsync("launch", new { program = "missing.ts" });
        Assert.False(earlyLaunch.GetProperty("success").GetBoolean());
        Assert.Contains("state Initialized", earlyLaunch.GetProperty("message").GetString());

        AssertSuccess(await dap.RequestAsync("initialize", new { adapterID = "sharpts" }));
        await dap.WaitForEventAsync("initialized");
        JsonElement unknown = await dap.RequestAsync("sharpts/unknown");
        Assert.False(unknown.GetProperty("success").GetBoolean());
        Assert.Equal("sharpts/unknown", unknown.GetProperty("command").GetString());
    }

    [Fact]
    public async Task DuplicateRequestSequenceReturnsFailureWithoutEndingSession()
    {
        await using var dap = new DapProtocolHarness();
        AssertSuccess(await dap.RequestAsync(
            "initialize", new { adapterID = "sharpts" }, sequenceOverride: 700));
        await dap.WaitForEventAsync("initialized");

        JsonElement duplicate = await dap.RequestAsync("threads", sequenceOverride: 700);
        Assert.False(duplicate.GetProperty("success").GetBoolean());
        Assert.Contains("Duplicate", duplicate.GetProperty("message").GetString());

        AssertSuccess(await dap.RequestAsync("threads"));
    }

    [Theory]
    [InlineData("generator", "function* values() {\n  yield 1;\n  let resumed = 2;\n  yield resumed;\n}\nconst iterator = values();\niterator.next();\nconsole.log(iterator.next().value);", 3, "values")]
    [InlineData("async", "async function compute() {\n  await 0;\n  let resumed = 42;\n  console.log(resumed);\n}\ncompute();", 3, "compute")]
    public async Task GeneratorAndAsyncStopsKeepSourceLevelFrame(
        string fileStem,
        string source,
        int breakpointLine,
        string frameName)
    {
        string directory = CreateFixtureDirectory();
        string program = Path.Combine(directory, $"{fileStem}.ts");
        await File.WriteAllTextAsync(program, source);

        await using var dap = new DapProtocolHarness();
        AssertSuccess(await dap.RequestAsync("initialize", new { adapterID = "sharpts" }));
        await dap.WaitForEventAsync("initialized");
        AssertSuccess(await dap.RequestAsync("launch", new { program }));
        JsonElement breakpoints = await dap.RequestAsync("setBreakpoints", new
        {
            source = new { path = program },
            breakpoints = new[] { new { line = breakpointLine } },
        });
        Assert.True(breakpoints.GetProperty("body").GetProperty("breakpoints")[0]
            .GetProperty("verified").GetBoolean());
        AssertSuccess(await dap.RequestAsync("configurationDone"));

        await dap.WaitForEventAsync("stopped");
        JsonElement stack = await dap.RequestAsync("stackTrace", new { threadId = 1 });
        JsonElement[] frames = stack.GetProperty("body").GetProperty("stackFrames")
            .EnumerateArray().ToArray();
        Assert.Equal(frameName, frames[0].GetProperty("name").GetString());
        Assert.StartsWith("<module:", frames[^1].GetProperty("name").GetString());

        AssertSuccess(await dap.RequestAsync("continue", new { threadId = 1 }));
        await dap.WaitForEventAsync("exited");
    }

    [Fact]
    public async Task RecursiveStackIncludesEveryInvocationAndModuleCaller()
    {
        string directory = CreateFixtureDirectory();
        string program = Path.Combine(directory, "recursive.ts");
        await File.WriteAllTextAsync(program, "function recurse(n: number): number {\n  if (n === 0) {\n    let marker = n + arguments.length - 1;\n    return marker;\n  }\n  return recurse(n - 1);\n}\nconsole.log(recurse(2));");

        await using var dap = new DapProtocolHarness();
        AssertSuccess(await dap.RequestAsync("initialize", new { adapterID = "sharpts" }));
        await dap.WaitForEventAsync("initialized");
        AssertSuccess(await dap.RequestAsync("launch", new { program }));
        AssertSuccess(await dap.RequestAsync("setBreakpoints", new
        {
            source = new { path = program },
            breakpoints = new[] { new { line = 3 } },
        }));
        AssertSuccess(await dap.RequestAsync("configurationDone"));

        await dap.WaitForEventAsync("stopped");
        JsonElement stack = await dap.RequestAsync("stackTrace", new { threadId = 1 });
        string[] names = stack.GetProperty("body").GetProperty("stackFrames")
            .EnumerateArray().Select(frame => frame.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(new[] { "recurse", "recurse", "recurse", "<module: recursive.ts>" }, names);
        int topFrameId = stack.GetProperty("body").GetProperty("stackFrames")[0]
            .GetProperty("id").GetInt32();
        JsonElement scopes = await dap.RequestAsync("scopes", new { frameId = topFrameId });
        string[] scopeNames = scopes.GetProperty("body").GetProperty("scopes")
            .EnumerateArray().Select(scope => scope.GetProperty("name").GetString()!).ToArray();
        Assert.Contains("Arguments", scopeNames);
        Assert.Contains("Locals", scopeNames);
        JsonElement argumentsScope = scopes.GetProperty("body").GetProperty("scopes")
            .EnumerateArray().Single(scope => scope.GetProperty("name").GetString() == "Arguments");
        JsonElement arguments = await dap.RequestAsync("variables", new
        {
            variablesReference = argumentsScope.GetProperty("variablesReference").GetInt32(),
        });
        Assert.Contains(arguments.GetProperty("body").GetProperty("variables").EnumerateArray(),
            variable => variable.GetProperty("name").GetString() == "n"
                && variable.GetProperty("value").GetString() == "0");

        AssertSuccess(await dap.RequestAsync("continue", new { threadId = 1 }));
        await dap.WaitForEventAsync("exited");
    }

    [Fact]
    public async Task ChangedSourceIsRejectedInsteadOfBindingStaleBreakpoint()
    {
        string directory = CreateFixtureDirectory();
        string program = Path.Combine(directory, "changed.ts");
        await File.WriteAllTextAsync(program, "let value = 1;\nconsole.log(value);");

        await using var dap = new DapProtocolHarness();
        AssertSuccess(await dap.RequestAsync("initialize", new { adapterID = "sharpts" }));
        await dap.WaitForEventAsync("initialized");
        AssertSuccess(await dap.RequestAsync("launch", new { program }));
        await File.AppendAllTextAsync(program, "\n// changed after launch");

        JsonElement response = await dap.RequestAsync("setBreakpoints", new
        {
            source = new { path = program },
            breakpoints = new[] { new { line = 1 } },
        });
        JsonElement breakpoint = response.GetProperty("body").GetProperty("breakpoints")[0];
        Assert.False(breakpoint.GetProperty("verified").GetBoolean());
        Assert.Contains("changed", breakpoint.GetProperty("message").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkerOutputUsesDapEventsWithoutCorruptingProtocolStream()
    {
        string directory = CreateFixtureDirectory();
        string worker = Path.Combine(directory, "worker.ts");
        string program = Path.Combine(directory, "main.ts");
        await File.WriteAllTextAsync(worker, "console.log('worker-output');");
        await File.WriteAllTextAsync(program, "import { Worker } from 'worker_threads';\nnew Worker(__dirname + '/worker.ts');");

        await using var dap = new DapProtocolHarness();
        AssertSuccess(await dap.RequestAsync("initialize", new { adapterID = "sharpts" }));
        await dap.WaitForEventAsync("initialized");
        AssertSuccess(await dap.RequestAsync("launch", new { program }));
        AssertSuccess(await dap.RequestAsync("configurationDone"));

        JsonElement output = await dap.WaitForEventAsync("output");
        Assert.Contains("worker-output", output.GetProperty("body").GetProperty("output").GetString());
        JsonElement exited = await dap.WaitForEventAsync("exited");
        Assert.Equal(0, exited.GetProperty("body").GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task WorkerBreakpointVerifiesOnLoadAndStopsAsItsOwnThread()
    {
        string directory = CreateFixtureDirectory();
        string worker = Path.Combine(directory, "worker-debug.ts");
        string program = Path.Combine(directory, "main.ts");
        await File.WriteAllTextAsync(worker,
            "let workerValue = 41;\nlet doubled = workerValue * 2;\nconsole.log(workerValue + 1);");
        await File.WriteAllTextAsync(program,
            "import { Worker } from 'worker_threads';\nnew Worker(__dirname + '/worker-debug.ts');");

        await using var dap = new DapProtocolHarness();
        AssertSuccess(await dap.RequestAsync("initialize", new { adapterID = "sharpts" }));
        await dap.WaitForEventAsync("initialized");
        AssertSuccess(await dap.RequestAsync("launch", new { program }));
        JsonElement pending = await dap.RequestAsync("setBreakpoints", new
        {
            source = new { path = worker },
            breakpoints = new[] { new { line = 2 } },
        });
        JsonElement pendingBreakpoint = pending.GetProperty("body").GetProperty("breakpoints")[0];
        Assert.False(pendingBreakpoint.GetProperty("verified").GetBoolean());
        int breakpointId = pendingBreakpoint.GetProperty("id").GetInt32();
        AssertSuccess(await dap.RequestAsync("configurationDone"));

        JsonElement workerStarted;
        do
        {
            workerStarted = await dap.WaitForEventAsync("thread");
        }
        while (workerStarted.GetProperty("body").GetProperty("reason").GetString() != "started"
            || workerStarted.GetProperty("body").GetProperty("threadId").GetInt32() == 1);
        int workerThreadId = workerStarted.GetProperty("body").GetProperty("threadId").GetInt32();

        JsonElement changed = await dap.WaitForEventAsync("breakpoint");
        JsonElement verifiedBreakpoint = changed.GetProperty("body").GetProperty("breakpoint");
        Assert.Equal(breakpointId, verifiedBreakpoint.GetProperty("id").GetInt32());
        Assert.True(verifiedBreakpoint.GetProperty("verified").GetBoolean());

        var stoppedThreads = new HashSet<int>();
        JsonElement finalStop;
        do
        {
            finalStop = await dap.WaitForEventAsync("stopped");
            stoppedThreads.Add(finalStop.GetProperty("body").GetProperty("threadId").GetInt32());
        }
        while (!finalStop.GetProperty("body").GetProperty("allThreadsStopped").GetBoolean());

        Assert.Contains(1, stoppedThreads);
        Assert.Contains(workerThreadId, stoppedThreads);
        JsonElement threads = await dap.RequestAsync("threads");
        int[] threadIds = threads.GetProperty("body").GetProperty("threads")
            .EnumerateArray().Select(thread => thread.GetProperty("id").GetInt32()).ToArray();
        Assert.Contains(1, threadIds);
        Assert.Contains(workerThreadId, threadIds);
        JsonElement stack = await dap.RequestAsync("stackTrace", new { threadId = workerThreadId });
        JsonElement top = stack.GetProperty("body").GetProperty("stackFrames")[0];
        Assert.Equal(Path.GetFullPath(worker),
            top.GetProperty("source").GetProperty("path").GetString());
        Assert.Equal(2, top.GetProperty("line").GetInt32());
        int frameId = top.GetProperty("id").GetInt32();
        JsonElement scopes = await dap.RequestAsync("scopes", new { frameId });
        int localsReference = scopes.GetProperty("body").GetProperty("scopes")[0]
            .GetProperty("variablesReference").GetInt32();
        JsonElement variables = await dap.RequestAsync("variables", new
        {
            variablesReference = localsReference,
        });
        Assert.Contains(variables.GetProperty("body").GetProperty("variables").EnumerateArray(),
            variable => variable.GetProperty("name").GetString() == "workerValue"
                && variable.GetProperty("value").GetString() == "41");
        JsonElement evaluation = await dap.RequestAsync("evaluate", new
        {
            expression = "workerValue + 1",
            frameId,
            context = "watch",
        });
        Assert.Equal("42", evaluation.GetProperty("body").GetProperty("result").GetString());

        AssertSuccess(await dap.RequestAsync("next", new { threadId = workerThreadId }));
        JsonElement workerStep;
        do
        {
            workerStep = await dap.WaitForEventAsync("stopped");
        }
        while (workerStep.GetProperty("body").GetProperty("threadId").GetInt32() != workerThreadId
            || workerStep.GetProperty("body").GetProperty("reason").GetString() != "step");
        JsonElement steppedStack = await dap.RequestAsync("stackTrace", new { threadId = workerThreadId });
        Assert.Equal(3, steppedStack.GetProperty("body").GetProperty("stackFrames")[0]
            .GetProperty("line").GetInt32());

        JsonElement convergence = workerStep;
        while (!convergence.GetProperty("body").GetProperty("allThreadsStopped").GetBoolean())
            convergence = await dap.WaitForEventAsync("stopped");
        AssertSuccess(await dap.RequestAsync("continue", new { threadId = workerThreadId }));
        JsonElement output = await dap.WaitForEventAsync("output");
        Assert.Contains("42", output.GetProperty("body").GetProperty("output").GetString());
        await dap.WaitForEventAsync("exited");
    }

    [Fact]
    public async Task NestedWorkersEmitStableStartedAndExitedThreadEvents()
    {
        string directory = CreateFixtureDirectory();
        string inner = Path.Combine(directory, "inner.ts");
        string outer = Path.Combine(directory, "outer.ts");
        string program = Path.Combine(directory, "main.ts");
        await File.WriteAllTextAsync(inner, "console.log('nested-worker-done');");
        await File.WriteAllTextAsync(outer,
            "import { Worker } from 'worker_threads';\nnew Worker(__dirname + '/inner.ts');");
        await File.WriteAllTextAsync(program,
            "import { Worker } from 'worker_threads';\nnew Worker(__dirname + '/outer.ts');");

        await using var dap = new DapProtocolHarness();
        AssertSuccess(await dap.RequestAsync("initialize", new { adapterID = "sharpts" }));
        await dap.WaitForEventAsync("initialized");
        AssertSuccess(await dap.RequestAsync("launch", new { program }));
        AssertSuccess(await dap.RequestAsync("configurationDone"));

        var started = new HashSet<int>();
        var exited = new HashSet<int>();
        while (started.Count < 2 || exited.Count < 2)
        {
            JsonElement threadEvent = await dap.WaitForEventAsync("thread");
            JsonElement body = threadEvent.GetProperty("body");
            int threadId = body.GetProperty("threadId").GetInt32();
            if (threadId == 1)
                continue;
            if (body.GetProperty("reason").GetString() == "started")
                started.Add(threadId);
            else
                exited.Add(threadId);
        }

        Assert.Equal(2, started.Count);
        Assert.True(started.SetEquals(exited));
        Assert.DoesNotContain(1, started);
        JsonElement output = await dap.WaitForEventAsync("output");
        Assert.Contains("nested-worker-done", output.GetProperty("body").GetProperty("output").GetString());
        await dap.WaitForEventAsync("exited");
    }

    [Fact]
    public async Task WorkerExceptionInfoIsRoutedToTheWorkerThread()
    {
        string directory = CreateFixtureDirectory();
        string worker = Path.Combine(directory, "throwing-worker.ts");
        string program = Path.Combine(directory, "main.ts");
        await File.WriteAllTextAsync(worker, "let marker = 1;\nthrow new Error('worker boom');");
        await File.WriteAllTextAsync(program,
            "import { Worker } from 'worker_threads';\nnew Worker(__dirname + '/throwing-worker.ts');");

        await using var dap = new DapProtocolHarness();
        AssertSuccess(await dap.RequestAsync("initialize", new { adapterID = "sharpts" }));
        await dap.WaitForEventAsync("initialized");
        AssertSuccess(await dap.RequestAsync("launch", new { program }));
        AssertSuccess(await dap.RequestAsync("configurationDone"));

        int workerThreadId = 0;
        while (workerThreadId == 0)
        {
            JsonElement threadEvent = await dap.WaitForEventAsync("thread");
            JsonElement body = threadEvent.GetProperty("body");
            int id = body.GetProperty("threadId").GetInt32();
            if (id != 1 && body.GetProperty("reason").GetString() == "started")
                workerThreadId = id;
        }

        JsonElement exceptionStop;
        do
        {
            exceptionStop = await dap.WaitForEventAsync("stopped");
        }
        while (exceptionStop.GetProperty("body").GetProperty("threadId").GetInt32() != workerThreadId
            || exceptionStop.GetProperty("body").GetProperty("reason").GetString() != "exception");
        JsonElement info = await dap.RequestAsync("exceptionInfo", new { threadId = workerThreadId });
        AssertSuccess(info);
        Assert.Contains("worker boom", info.GetProperty("body").GetProperty("description").GetString());

        JsonElement convergence = exceptionStop;
        while (!convergence.GetProperty("body").GetProperty("allThreadsStopped").GetBoolean())
            convergence = await dap.WaitForEventAsync("stopped");
        AssertSuccess(await dap.RequestAsync("continue", new { threadId = workerThreadId }));
        await dap.WaitForEventAsync("exited");
    }

    [Fact]
    public async Task DuplicateModuleBasenamesKeepIndependentSourceIdentity()
    {
        string directory = CreateFixtureDirectory();
        string firstDirectory = Path.Combine(directory, "a");
        string secondDirectory = Path.Combine(directory, "b");
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);
        string first = Path.Combine(firstDirectory, "shared.ts");
        string second = Path.Combine(secondDirectory, "shared.ts");
        string program = Path.Combine(directory, "main.ts");
        await File.WriteAllTextAsync(first,
            "export function first(): number {\n  let onlyFirst = 1;\n  return onlyFirst;\n}");
        await File.WriteAllTextAsync(second,
            "export function second(): number {\n  let onlySecond = 2;\n  return onlySecond;\n}");
        await File.WriteAllTextAsync(program,
            "import { first } from './a/shared';\nimport { second } from './b/shared';\nconsole.log(first() + second());");

        await using var dap = new DapProtocolHarness();
        AssertSuccess(await dap.RequestAsync("initialize", new { adapterID = "sharpts" }));
        await dap.WaitForEventAsync("initialized");
        AssertSuccess(await dap.RequestAsync("launch", new { program }));
        AssertSuccess(await dap.RequestAsync("setBreakpoints", new
        {
            source = new { path = first },
            breakpoints = new[] { new { line = 2 } },
        }));

        JsonElement loaded = await dap.RequestAsync("loadedSources");
        string[] loadedPaths = loaded.GetProperty("body").GetProperty("sources")
            .EnumerateArray()
            .Where(source => source.TryGetProperty("path", out _))
            .Select(source => source.GetProperty("path").GetString()!)
            .ToArray();
        Assert.Contains(Path.GetFullPath(first), loadedPaths);
        Assert.Contains(Path.GetFullPath(second), loadedPaths);

        AssertSuccess(await dap.RequestAsync("configurationDone"));
        await dap.WaitForEventAsync("stopped");
        JsonElement stack = await dap.RequestAsync("stackTrace", new { threadId = 1 });
        string stoppedPath = stack.GetProperty("body").GetProperty("stackFrames")[0]
            .GetProperty("source").GetProperty("path").GetString()!;
        Assert.Equal(Path.GetFullPath(first), stoppedPath);

        AssertSuccess(await dap.RequestAsync("continue", new { threadId = 1 }));
        await dap.WaitForEventAsync("exited");
    }

    [Fact]
    [Trait("Category", "LoadSensitive")]
    public async Task LocalBreakpointAndVariableLatencyStayWithinV1Budget()
    {
        string directory = CreateFixtureDirectory();
        string program = Path.Combine(directory, "latency.ts");
        await File.WriteAllTextAsync(program,
            "for (let index = 0; index < 1000; index++) {\n  let marker = index;\n}\n");

        await using var dap = new DapProtocolHarness();
        AssertSuccess(await dap.RequestAsync("initialize", new { adapterID = "sharpts" }));
        await dap.WaitForEventAsync("initialized");
        AssertSuccess(await dap.RequestAsync("launch", new { program }));
        AssertSuccess(await dap.RequestAsync("setBreakpoints", new
        {
            source = new { path = program },
            breakpoints = new[] { new { line = 2 } },
        }));
        AssertSuccess(await dap.RequestAsync("configurationDone"));
        await dap.WaitForEventAsync("stopped");

        var continueLatencies = new List<double>();
        for (int index = 0; index < 21; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            AssertSuccess(await dap.RequestAsync("continue", new { threadId = 1 }));
            await dap.WaitForEventAsync("stopped");
            stopwatch.Stop();
            continueLatencies.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        JsonElement stack = await dap.RequestAsync("stackTrace", new { threadId = 1 });
        int frameId = stack.GetProperty("body").GetProperty("stackFrames")[0]
            .GetProperty("id").GetInt32();
        JsonElement scopes = await dap.RequestAsync("scopes", new { frameId });
        int variablesReference = scopes.GetProperty("body").GetProperty("scopes")[0]
            .GetProperty("variablesReference").GetInt32();
        var variableLatencies = new List<double>();
        for (int index = 0; index < 21; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            AssertSuccess(await dap.RequestAsync("variables", new { variablesReference }));
            stopwatch.Stop();
            variableLatencies.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        continueLatencies.Sort();
        variableLatencies.Sort();
        Assert.True(continueLatencies[continueLatencies.Count / 2] < 50,
            $"Median continue latency was {continueLatencies[continueLatencies.Count / 2]:F2} ms.");
        Assert.True(variableLatencies[variableLatencies.Count / 2] < 100,
            $"Median variable latency was {variableLatencies[variableLatencies.Count / 2]:F2} ms.");
    }

    [Fact]
    public async Task DisconnectWhileStoppedTerminatesOwnedProcess()
    {
        string directory = CreateFixtureDirectory();
        string program = Path.Combine(directory, "paused.ts");
        await File.WriteAllTextAsync(program, "let value = 1;\nconsole.log(value);");

        await using var dap = new DapProtocolHarness();
        AssertSuccess(await dap.RequestAsync("initialize", new { adapterID = "sharpts" }));
        await dap.WaitForEventAsync("initialized");
        AssertSuccess(await dap.RequestAsync("launch", new { program, stopOnEntry = true }));
        AssertSuccess(await dap.RequestAsync("configurationDone"));
        await dap.WaitForEventAsync("stopped");

        AssertSuccess(await dap.RequestAsync("disconnect", new { terminateDebuggee = true }));
    }

    [Theory]
    [InlineData("caught", "try { throw new Error('caught boom'); } catch (error) { console.log('handled'); }", 0)]
    [InlineData("uncaught", "throw new Error('uncaught boom');", 1)]
    [InlineData("unhandledRejection", "async function rejectLater(): Promise<void> { await Promise.resolve(); throw new Error('rejection boom'); } setTimeout(rejectLater as any, 0);", 1)]
    public async Task ExceptionFiltersStopAndReturnExceptionInfo(
        string filter,
        string source,
        int expectedExitCode)
    {
        string directory = CreateFixtureDirectory();
        string program = Path.Combine(directory, "exception.ts");
        await File.WriteAllTextAsync(program, source);

        await using var dap = new DapProtocolHarness();
        AssertSuccess(await dap.RequestAsync("initialize", new { adapterID = "sharpts" }));
        await dap.WaitForEventAsync("initialized");
        AssertSuccess(await dap.RequestAsync("launch", new { program }));
        AssertSuccess(await dap.RequestAsync("setExceptionBreakpoints", new
        {
            filters = new[] { filter },
        }));
        AssertSuccess(await dap.RequestAsync("configurationDone"));

        JsonElement stopped = await dap.WaitForEventAsync("stopped");
        Assert.Equal("exception", stopped.GetProperty("body").GetProperty("reason").GetString());
        JsonElement info = await dap.RequestAsync("exceptionInfo", new { threadId = 1 });
        AssertSuccess(info);
        Assert.Contains("boom", info.GetProperty("body").GetProperty("description").GetString());
        AssertSuccess(await dap.RequestAsync("continue", new { threadId = 1 }));
        JsonElement exited = await dap.WaitForEventAsync("exited");
        Assert.Equal(expectedExitCode, exited.GetProperty("body").GetProperty("exitCode").GetInt32());
    }

    private static string CreateFixtureDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sharpts-dap-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void AssertSuccess(JsonElement response) =>
        Assert.True(response.GetProperty("success").GetBoolean(),
            response.TryGetProperty("message", out JsonElement message) ? message.GetString() : null);
}
