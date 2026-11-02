using SharpTS.Runtime.BuiltIns;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

// Compiled-only mirror regression; the original dual-mode VmPromiseCompletionTests remain unchanged.
[Collection("ProcessLifecycleTests")]
public sealed class VmPromiseRejectionHandlingTests : IDisposable
{
    public VmPromiseRejectionHandlingTests() => ProcessBuiltIns.ResetProcessState();
    public void Dispose() => ProcessBuiltIns.ResetProcessState();

    public static IEnumerable<object[]> Cases()
    {
        yield return ["vm-caught-completion", "import * as vm from \"node:vm\";const value:any=vm.runInNewContext(\"Promise.reject(7)\");value.catch(()=>{});console.log(typeof value,typeof value.then);", "object function\n"];
        yield return ["vm-late-handled-identity", "import * as vm from \"node:vm\";const p:any=vm.runInNewContext(`Promise.reject(\"vm-late\")`);process.on(\"unhandledRejection\",(e:any,q:any)=>{console.log(\"unhandled\",e,q===p);setTimeout(()=>p.catch(()=>console.log(\"caught\")),0)});process.on(\"rejectionHandled\",(q:any)=>console.log(\"handled\",q===p));", "unhandled vm-late true\ncaught\nhandled true\n"];
        yield return ["vm-catch-read-does-not-handle", "import * as vm from \"node:vm\";const p:any=vm.runInNewContext(`Promise.reject(\"vm-read\")`);const method=p.catch;process.on(\"unhandledRejection\",(e:any,q:any)=>console.log(e,q===p));", "vm-read true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ActualVmHandlingRetainsCompletionAndIdentity(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(
            new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", ExecutionMode.Compiled));
    }
}
