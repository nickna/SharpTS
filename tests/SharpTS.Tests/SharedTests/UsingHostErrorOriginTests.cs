using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UsingHostErrorOriginTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["sync-repeat", "/// <reference lib=\"esnext.disposable\" />\ntry{using resource=null;\"x\".repeat(-1)}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["sync-no-using-control", "try{\"x\".repeat(-1)}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["sync-array-control", "/// <reference lib=\"esnext.disposable\" />\ntry{using resource=null;new Array(-1)}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["guest-error-string", "/// <reference lib=\"esnext.disposable\" />\nconst marker=\"RangeError: guest\";try{using resource=null;throw marker}catch(error){console.log(error===marker,typeof error)}", "true string\n"];
        yield return ["guest-object", "/// <reference lib=\"esnext.disposable\" />\nconst marker={};try{using resource=null;throw marker}catch(error){console.log(error===marker,typeof error)}", "true object\n"];
        yield return ["guest-undefined", "/// <reference lib=\"esnext.disposable\" />\ntry{using resource=null;throw undefined}catch(error){console.log(error===undefined,typeof error)}", "true undefined\n"];
        yield return ["async-nested-using-host-error", "/// <reference lib=\"esnext.disposable\" />\nasync function run(){try{{using resource=null;\"x\".repeat(-1)}}catch(error){console.log(error instanceof RangeError,typeof error)}}run();", "true object\n"];
        yield return ["host-error-rethrow", "/// <reference lib=\"esnext.disposable\" />\ntry{try{using resource=null;\"x\".repeat(-1)}catch(error){throw error}}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["sync-arraybuffer-host", "/// <reference lib=\"esnext.disposable\" />\ntry{using resource=null;new ArrayBuffer(-1)}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["arraybuffer-no-using", "try{new ArrayBuffer(-1)}catch(error){console.log(error instanceof RangeError,typeof error)}", "true object\n"];
        yield return ["arraybuffer-disposal-before-catch", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];try{using resource={[Symbol.dispose](){log.push(\"dispose\")}};new ArrayBuffer(-1)}catch(error){console.log(error instanceof RangeError,typeof error,log.join(\",\"))}", "true object dispose\n"];
        yield return ["async-arraybuffer-direct", "/// <reference lib=\"esnext.disposable\" />\nasync function run(){try{using resource=null;new ArrayBuffer(-1)}catch(error){console.log(error instanceof RangeError,typeof error)}}run();", "true object\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void CatchBindingPreservesActualErrorOrigin(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
