using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    public static IEnumerable<object[]> Issue3456Cases()
    {
        yield return ["uncaught-async", "async function main(){structuredClone(()=>{})}main();", true, "", "DataCloneError"];
        yield return ["uncaught-async-finally", "async function main(){try{structuredClone(()=>{})}finally{console.log(\"finally\")}}main();", true, "finally\n", "DataCloneError"];
        yield return ["unhandled-after-await", "async function fail(){await Promise.resolve(1);structuredClone(()=>{})}fail();", true, "", "DataCloneError"];
        yield return ["unhandled-reaction", "Promise.resolve(1).then(()=>structuredClone(()=>{}));", true, "", "DataCloneError"];
        yield return ["unhandled-string-reason", "Promise.reject(\"rejection-marker\");", true, "", "rejection-marker"];
        yield return ["caught-async", "async function fail(){structuredClone(()=>{})}fail().catch(()=>console.log(\"caught\"));", false, "caught\n", ""];
        yield return ["caught-reaction", "Promise.resolve(1).then(()=>structuredClone(()=>{})).catch(()=>console.log(\"caught\"));", false, "caught\n", ""];
        yield return ["rejection-listener", "process.on(\"unhandledRejection\",()=>console.log(\"handled\"));async function fail(){structuredClone(()=>{})}fail();", false, "handled\n", ""];
        yield return ["listener-receives-string", "process.on(\"unhandledRejection\",(reason:any)=>console.log(typeof reason,reason));Promise.reject(\"DataCloneError: guest\");", false, "string DataCloneError: guest\n", ""];
        yield return ["successful-async", "async function ok(){await Promise.resolve(1);console.log(\"done\")}ok();", false, "done\n", ""];
        yield return ["pending-unsettled", "new Promise(()=>{});console.log(\"done\");", false, "done\n", ""];
        yield return ["microtask-catch", "async function fail(){structuredClone(()=>{})}const p=fail();queueMicrotask(()=>p.catch(()=>console.log(\"caught\")));", false, "caught\n", ""];
    }

    [Theory, MemberData(nameof(Issue3456Cases))]
    public void Isolated_Issue3456UnhandledRejectionsMatchProcessOutcomes(
        string name, string source, bool nonzero, string expectedStdout, string diagnostic)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var output = directory.GetPath(name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --lib es2022,dom --compile \"{path}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        var result = CaptureBoundedProcess(output, hidden: true);
        Assert.Equal(expectedStdout, result.Stdout);
        if (nonzero)
        {
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(diagnostic, result.Stderr);
        }
        else
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Stderr);
        }
    }
}
