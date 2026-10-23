using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class StaticBlockLexicalReservationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["nominal-static-block-lexical-tdz", """
            var value = 99;
            class Example {
                static {
                    const read = () => value;
                    try { read(); } catch (error: any) { console.log("before", error instanceof ReferenceError); }
                    let value = 7;
                    console.log("after", read());
                }
            }
            console.log("outer", value);
            """ + "\n", "before true\nafter 7\nouter 99\n"];
        yield return ["nominal-static-block-tdz-and-live-captures", """
            var laterLet = 99;
            var laterConst = 'outer';
            var nestedValue = 88;
            class LexicalTdzControls {
                static {
                    let ready = 3;
                    const readReady = () => ready;
                    console.log('ready', readReady());
                }
                static {
                    const readLet = () => laterLet;
                    try { readLet(); } catch (error: any) { console.log('let-before', error instanceof ReferenceError); }
                    let laterLet = 7;
                    console.log('let-after', readLet());
                    laterLet = 8;
                    console.log('let-live', readLet());
                }
                static {
                    const readConst = () => laterConst;
                    try { readConst(); } catch (error: any) { console.log('const-before', error instanceof ReferenceError); }
                    const laterConst = 'inner';
                    console.log('const-after', readConst());
                }
                static {
                    {
                        const readNested = () => nestedValue;
                        try { readNested(); } catch (error: any) { console.log('nested-before', error instanceof ReferenceError); }
                        let nestedValue = 11;
                        console.log('nested-after', readNested());
                    }
                }
            }
            console.log('outer', laterLet, laterConst, nestedValue);
            """ + "\n", "ready 3\nlet-before true\nlet-after 7\nlet-live 8\nconst-before true\nconst-after inner\nnested-before true\nnested-after 11\nouter 99 outer 88\n"];
    }

    public static IEnumerable<object[]> DirectCases() => Cases().SelectMany(row =>
        new[] { false, true }
            .Select(resolve => new object[] { row[0], row[1], row[2], resolve }));

    [Theory, MemberData(nameof(DirectCases))]
    public void DirectInterpreterReservesEachStaticBlockLexicalBinding(
        string name, string source, string expected, bool resolve)
    {
        Assert.NotEmpty(name);
        var statements = TestHarness.ParseOrThrow(source);
        var check = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements);
        Assert.Empty(check.Diagnostics);
        using var output = new StringWriter();
        using var errors = new StringWriter();
        using var interpreter = new Interpreter(stdout: output, stderr: errors);
        if (resolve)
            new VariableResolver(interpreter).Resolve(statements);

        interpreter.Interpret(statements, check.TypeMap);

        Assert.Null(interpreter.LastUncaughtError);
        Assert.Empty(errors.ToString());
        Assert.Equal(expected, output.ToString().Replace("\r\n", "\n"));
    }

    [Theory, MemberData(nameof(Cases))]
    public void PublicInterpreterReservesEachStaticBlockLexicalBinding(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        var result = SourceExecutionService.Interpret(source);
        Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
        Assert.Empty(result.Errors);
        Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
    }
}
