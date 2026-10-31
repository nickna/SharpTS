using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AmbientVariableErasureTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-const", "declare const global:any;console.log(global===globalThis);", "true\n"];
        yield return ["let", "declare let global:any;console.log(global===globalThis);", "true\n"];
        yield return ["var", "declare var global:any;console.log(global===globalThis);", "true\n"];
        yield return ["comma", "declare const global:any,missing:number;console.log(global===globalThis,typeof missing);", "true undefined\n"];
        yield return ["function", "declare const global:any;function read(){return global===globalThis;}console.log(read());", "true\n"];
        yield return ["class", "declare const global:any;class Reader{read(){return global===globalThis;}}console.log(new Reader().read());", "true\n"];
        yield return ["absent", "declare const missing:number;console.log(typeof missing,Object.prototype.hasOwnProperty.call(globalThis,'missing'));", "undefined false\n"];
        yield return ["namespace", "namespace N{export declare const missing:number;export const present=7;}console.log(Object.prototype.hasOwnProperty.call(N,'missing'),N.present);", "false 7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void AmbientVariablesDoNotCreateRuntimeBindings(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
