using SharpTS.Runtime.BuiltIns;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("ProcessLifecycleTests")]
public sealed class EventEmitterListenerReceiverTests : IDisposable
{
    public EventEmitterListenerReceiverTests() => ProcessBuiltIns.ResetProcessState();

    public void Dispose() => ProcessBuiltIns.ResetProcessState();

    public static IEnumerable<object[]> Cases()
    {
        yield return ["process-original", "process.on(\"x\",function(this:any){console.log(this===process)});process.emit(\"x\");", "true\n"];
        yield return ["process-arguments", "process.on(\"x\",function(this:any,a:any,b:any){console.log(this===process,a,b)});process.emit(\"x\",7,\"v\");", "true 7 v\n"];
        yield return ["process-once", "let n=0;process.once(\"x\",function(this:any){console.log(this===process,++n)});process.emit(\"x\");process.emit(\"x\");", "true 1\n"];
        yield return ["process-prepend", "process.on(\"x\",function(this:any){console.log(\"last\",this===process)});process.prependListener(\"x\",function(this:any){console.log(\"first\",this===process)});process.emit(\"x\");", "first true\nlast true\n"];
        yield return ["process-bound-control", "const target={n:7};function listener(this:any,a:any,b:any){console.log(this===target,this.n,a,b)}process.on(\"x\",listener.bind(target,\"prefix\"));process.emit(\"x\",\"event\");", "true 7 prefix event\n"];
        yield return ["process-arrow-control", "const target={n:7};function outer(this:any){process.on(\"x\",()=>console.log(this===target,this.n))}outer.call(target);process.emit(\"x\");", "true 7\n"];
        yield return ["emitter-original", "import {EventEmitter} from \"events\";const emitter=new EventEmitter();emitter.on(\"x\",function(this:any){console.log(this===emitter)});emitter.emit(\"x\");", "true\n"];
        yield return ["emitter-once", "import {EventEmitter} from \"events\";const emitter=new EventEmitter();let n=0;emitter.once(\"x\",function(this:any){console.log(this===emitter,++n)});emitter.emit(\"x\");emitter.emit(\"x\");", "true 1\n"];
        yield return ["emitter-shared-listener", "import {EventEmitter} from \"events\";const a=new EventEmitter(),b=new EventEmitter();function listener(this:any){console.log(this===a,this===b)}a.on(\"x\",listener);b.on(\"x\",listener);a.emit(\"x\");b.emit(\"x\");", "true false\nfalse true\n"];
        yield return ["emitter-bound-control", "import {EventEmitter} from \"events\";const emitter=new EventEmitter(),target={n:9};function listener(this:any,a:any,b:any){console.log(this===target,this.n,a,b)}emitter.on(\"x\",listener.bind(target,\"prefix\"));emitter.emit(\"x\",\"event\");", "true 9 prefix event\n"];
        yield return ["emitter-arrow-control", "import {EventEmitter} from \"events\";const emitter=new EventEmitter(),target={n:9};function outer(this:any){emitter.on(\"x\",()=>console.log(this===target,this.n))}outer.call(target);emitter.emit(\"x\");", "true 9\n"];
        yield return ["emitter-prepend-once", "import {EventEmitter} from \"events\";const emitter=new EventEmitter();emitter.on(\"x\",function(this:any){console.log(\"last\",this===emitter)});emitter.prependOnceListener(\"x\",function(this:any){console.log(\"first\",this===emitter)});emitter.emit(\"x\");emitter.emit(\"x\");", "first true\nlast true\nlast true\n"];
        yield return ["emitter-error-monitor", "import {EventEmitter,errorMonitor} from \"events\";const emitter=new EventEmitter();emitter.on(errorMonitor,function(this:any,e:any){console.log(\"monitor\",this===emitter,e)});emitter.on(\"error\",function(this:any,e:any){console.log(\"error\",this===emitter,e)});emitter.emit(\"error\",\"marker\");", "monitor true marker\nerror true marker\n"];
        yield return ["emitter-removed-during-call", "import {EventEmitter} from \"events\";const emitter=new EventEmitter();function second(this:any){console.log(\"second\",this===emitter)}emitter.on(\"x\",function(this:any){console.log(\"first\",this===emitter);emitter.off(\"x\",second)});emitter.on(\"x\",second);emitter.emit(\"x\");emitter.emit(\"x\");", "first true\nsecond true\nfirst true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void OrdinaryListenersReceiveTheEmitter(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        var actual = source.StartsWith("import ", StringComparison.Ordinal)
            ? TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode)
            : TestHarness.Run(source, mode);
        Assert.Equal(expected, actual);
    }
}
