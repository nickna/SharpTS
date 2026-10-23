using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassDeclarationStaticBlockCaptureTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["earlier-binding-control", """
            class Example {
                static {
                    let value = 9;
                    const read = () => value;
                    console.log(read());
                }
            }
            """ + "\n", "9\n"];
        yield return ["initialized-static-block-lexical-captures", """
            const shadow = 99;
            class InitializedCaptures {
                static {
                    let numberValue: number = 11;
                    const labelValue: string = 'first';
                    const readPair = () => labelValue + ':' + numberValue;
                    const readClass = () => this;
                    console.log('pair', readPair(), readClass() === InitializedCaptures);
                    numberValue = 12;
                    console.log('updated', readPair());
                }
                static {
                    const shadow: number = 21;
                    const readShadow = () => shadow;
                    console.log('shadow', readShadow());
                }
                static {
                    const flagValue: boolean = true;
                    const readFlag = () => flagValue;
                    console.log('flag', readFlag());
                }
            }
            console.log('outer', shadow);
            """ + "\n", "pair first:11 true\nupdated first:12\nshadow 21\nflag true\nouter 99\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void StaticBlockArrowsRetainInitializedLexicalBindings(
        string name, string source, string expected, ExecutionMode mode)
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
