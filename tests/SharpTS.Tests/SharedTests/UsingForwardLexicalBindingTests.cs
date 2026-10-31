using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UsingForwardLexicalBindingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "/// <reference lib=\"esnext.disposable\" />\nconst run=async()=>{const n=100;{function f(){return n}using n=null;await Promise.resolve(0);console.log(f()===null,n===null)}console.log(n)};run();", "true true\n100\n"];
        yield return ["sync-block-owner", "/// <reference lib=\"esnext.disposable\" />\nconst n=100;{function f(){return n}using n=null;console.log(f()===null,n===null)}console.log(n);", "true true\n100\n"];
        yield return ["arrow-owner", "/// <reference lib=\"esnext.disposable\" />\nconst n=100;{const f=()=>n;using n=null;console.log(f()===null)}console.log(n);", "true\n100\n"];
        yield return ["function-owner", "/// <reference lib=\"esnext.disposable\" />\nfunction run(){const n=100;{function f(){return n}using n=null;console.log(f()===null)}return n}console.log(run());", "true\n100\n"];
        yield return ["multiple-bindings", "/// <reference lib=\"esnext.disposable\" />\nconst first=100,n=200;{function f(){return [first,n]}using first=null,n=null;const values=f();console.log(values[0]===null,values[1]===null)}console.log(first,n);", "true true\n100 200\n"];
        yield return ["sibling-owners", "/// <reference lib=\"esnext.disposable\" />\nconst n=100,values:any[]=[];{function f(){return n}using n=null;values.push(f())}{function g(){return n}using n=null;values.push(g())}console.log(values[0]===null,values[1]===null,n);", "true true 100\n"];
        yield return ["disposal-preserved", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[],resource={[Symbol.dispose](){log.push('dispose')}};const held:any='outer';{function read(){return held}using held=resource;console.log(read()===resource);log.push('body')}console.log(log.join(','));", "true\nbody,dispose\n"];
        yield return ["tdz-before-initialization", "/// <reference lib=\"esnext.disposable\" />\nconst value=100;{const read=()=>value;try{read();console.log('missing')}catch(e){console.log(e instanceof ReferenceError)}using value=null;console.log(read()===null)}", "true\ntrue\n"];
        yield return ["await-using-owner", "/// <reference lib=\"esnext.disposable\" />\nconst run=async()=>{const n=100;{function f(){return n}await using n=null;await Promise.resolve(0);console.log(f()===null)}console.log(n)};run();", "true\n100\n"];
        yield return ["plain-let-control", "const n=100;{function f(){return n}let n=3;console.log(f(),n)}console.log(n);", "3 3\n100\n"];
        yield return ["outer-owner-control", "/// <reference lib=\"esnext.disposable\" />\nconst n=100;{function f(){return n}using resource=null;console.log(f())}", "100\n"];
    }

    public static IEnumerable<object[]> Pipelines() => Cases().SelectMany(row =>
        new[] { false, true }.Select(resolved =>
            new object[] { row[0], row[1], row[2], resolved }));

    [Theory, MemberData(nameof(Pipelines))]
    public void ForwardUsingReadsRetainTheirLexicalOwner(string name, string source, string expected, bool resolved)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        using var stdout = new StringWriter();
        using var interpreter = new Interpreter(stdout: stdout, stderr: TextWriter.Null);
        if (resolved) new VariableResolver(interpreter).Resolve(statements);
        interpreter.Interpret(statements, typeMap);
        Assert.Null(interpreter.LastUncaughtError);
        Assert.Equal(expected, stdout.ToString().Replace("\r\n", "\n"));
    }
}
