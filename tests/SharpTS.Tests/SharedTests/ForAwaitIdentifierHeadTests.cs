using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ForAwaitIdentifierHeadTests
{
    [Theory]
    [InlineData("key in { x: 1 }")]
    [InlineData("let key in { x: 1 }")]
    [InlineData("const key in { x: 1 }")]
    [InlineData("var key in { x: 1 }")]
    public void AsyncForInIsRejectedBeforeExecution(string head)
    {
        var source = $"async function run() {{ let key: any; for await ({head}) console.log(key); }} run();";
        var parser = new Parser(new Lexer(source).ScanTokens());
        Assert.False(parser.Parse().IsSuccess);
    }

    [Theory, ModeData]
    public void ExistingIdentifierSupportsSyncForInAndAsyncForOf(ExecutionMode mode)
    {
        const string source = """
            async function run() {
                let key: any;
                for (key in { x: 1 }) console.log(key);
                console.log(key);
                for await (key of [Promise.resolve("first"), "last"]) console.log(key);
                console.log(key);
            }
            run();
            """;
        Assert.Equal("x\nx\nfirst\nlast\nlast\n", TestHarness.Run(source, mode));
    }

    public static IEnumerable<object[]> BindingCases()
    {
        yield return ["sync", """
            function run() {
                let key: any = "outer";
                const shared: any[] = [], distinct: any[] = [];
                for (key in { a: 1, b: 2 }) shared.push(() => key);
                console.log(key, shared[0](), shared[1]());
                for (key of [1, 2]) { const local = key; distinct.push(() => local); }
                console.log(key, distinct[0](), distinct[1]());
                for (let key of [3, 4]) distinct.push(() => key);
                console.log(key, distinct[2](), distinct[3]());
            }
            run();
            """, "b b b\n2 1 2\n2 3 4\n"];
        yield return ["async", """
            async function* values() { yield "one"; yield "two"; }
            async function run() {
                let key: any = "outer";
                const shared: any[] = [], distinct: any[] = [];
                for await (key of values()) { shared.push(() => key); await Promise.resolve(0); }
                console.log(key, shared[0](), shared[1]());
                for await (let key of ["three", "four"]) distinct.push(() => key);
                console.log(key, distinct[0](), distinct[1]());
                for (key in { a: 1 }) { let key = "body"; console.log(key); }
                console.log(key);
                for (key of [5, 6]) {}
                console.log(key);
            }
            run();
            """, "two two two\ntwo three four\nbody\na\n6\n"];
    }

    public static IEnumerable<object[]> ModeCases() => BindingCases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void IdentifierHeadsAssignWhileDeclarationsRetainIterationBindings(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory, MemberData(nameof(BindingCases))]
    public void ResolvedBodiesRetainTheSameScopeDepth(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new SharpTS.TypeSystem.TypeChecker().Check(statements);
        using var output = new StringWriter();
        using var interpreter = new SharpTS.Execution.Interpreter(stdout: output, stderr: TextWriter.Null);
        new SharpTS.Execution.VariableResolver(interpreter).Resolve(statements);
        interpreter.Interpret(statements, typeMap);
        Assert.Null(interpreter.LastUncaughtError);
        Assert.Equal(expected, output.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public void FailedAssignmentHeadClosesTheAsyncIterator()
    {
        // Runtime-only forward lexical access; compiled TDZ assignment is #1761.
        const string source = """
            const state = { closed: false };
            async function* values() { try { yield 1; } finally { state.closed = true; } }
            async function run() {
                try {
                    for await (value of values()) {}
                    let value: any;
                } catch (error: any) { console.log(error.name, state.closed); }
            }
            run();
            """;
        Assert.Equal("ReferenceError true\n", TestHarness.RunUnchecked(source, ExecutionMode.Interpreted));
    }
}
