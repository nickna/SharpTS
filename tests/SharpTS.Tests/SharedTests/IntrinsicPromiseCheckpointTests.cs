using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class IntrinsicPromiseCheckpointTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["getter-before-job", "const log:string[]=[];const value:any={get then(){log.push(\"get\");return function(resolve:any){log.push(\"then\");resolve(7);log.push(\"after\");};}};async function run(){const p=Promise.resolve(value);log.push(\"made\");const result=await p;log.push(\"await:\"+result);}const pending=run();log.push(\"tail\");queueMicrotask(()=>log.push(\"queued\"));setTimeout(()=>console.log(log.join(\",\")),0);", "get,made,tail,then,after,queued,await:7\n"];
        yield return ["rejection-before-timer", "const log:string[]=[],marker={};const value:any={then(resolve:any,reject:any){log.push(\"then\");reject(marker);log.push(\"after\");}};async function run(){try{await Promise.resolve(value);}catch(error){log.push(\"caught:\"+(error===marker));}finally{log.push(\"finally\");}}const pending=run();queueMicrotask(()=>log.push(\"queued\"));setTimeout(()=>console.log(log.join(\",\")),0);", "then,after,queued,caught:true,finally\n"];
        yield return ["nested-jobs", "const log:string[]=[];const inner:any={then(resolve:any){log.push(\"inner\");resolve(9);log.push(\"inner-after\");}},outer:any={then(resolve:any){log.push(\"outer\");resolve(inner);log.push(\"outer-after\");}};async function run(){const result=await Promise.resolve(outer);log.push(\"await:\"+result);}const pending=run();queueMicrotask(()=>log.push(\"queued\"));setTimeout(()=>console.log(log.join(\",\")),0);", "outer,outer-after,queued,inner,inner-after,await:9\n"];
        yield return ["then-remains-a-job", "const log:string[]=[];const value:any={then(resolve:any){log.push(\"then\");resolve(7);log.push(\"after\");}};Promise.resolve(value).then(result=>log.push(\"reaction:\"+result));queueMicrotask(()=>log.push(\"queued\"));setTimeout(()=>console.log(log.join(\",\")),0);", "then,after,queued,reaction:7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ResolvePublishesReactionsBeforeTimers(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Fact]
    public void PendingIntrinsicResolvePublishesItsTaskObserversInTheCheckpoint()
    {
        var parsed = new Parser(new Lexer("Promise.resolve({then(resolve:any){resolve(7);}});").ScanTokens()).Parse();
        Assert.True(parsed.IsSuccess);
        using var interpreter = new Interpreter(new StringWriter(), new StringWriter());
        new VariableResolver(interpreter).Resolve(parsed.Statements);
        var promise = Assert.IsType<SharpTSPromise>(interpreter.InterpretScriptCompletion(parsed.Statements));
        Assert.False(promise.Task.IsCompleted);
        var scheduler = new HeldScheduler();
        var observed = promise.Task.ContinueWith(task => Assert.Equal(7d, task.Result),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, scheduler);

        interpreter.ProcessMicrotasks();

        Assert.True(promise.Task.IsCompletedSuccessfully);
        Assert.True(observed.IsCompletedSuccessfully);
        Assert.Equal(0, scheduler.QueuedCount);
    }

    private sealed class HeldScheduler : TaskScheduler
    {
        private readonly List<Task> _queued = [];
        public int QueuedCount => _queued.Count;
        protected override IEnumerable<Task> GetScheduledTasks() => _queued;
        protected override void QueueTask(Task task) => _queued.Add(task);
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
            => TryExecuteTask(task);
    }
}
