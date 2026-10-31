using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UsingForwardLexicalBindingTests
{
    public enum Pipeline { Dynamic, Resolved, Compiled }

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
        yield return ["fresh-call-activation", "/// <reference lib=\"esnext.disposable\" />\nlet disposed=0;function make(id:number){const read=()=>resource;using resource={id,[Symbol.dispose](){disposed++}};return read}const a=make(1),b=make(2);console.log(a().id,b().id,a()!==b(),disposed);", "1 2 true 2\n"];
        yield return ["sibling-escaped-resources", "/// <reference lib=\"esnext.disposable\" />\nconst readers:(()=>any)[]=[];{const read=()=>resource;using resource={id:1,[Symbol.dispose](){}};readers.push(read)}{const read=()=>resource;using resource={id:2,[Symbol.dispose](){}};readers.push(read)}console.log(readers[0]().id,readers[1]().id,readers[0]()!==readers[1]());", "1 2 true\n"];
        yield return ["loop-fresh-resource-cell", "/// <reference lib=\"esnext.disposable\" />\nconst readers:(()=>any)[]=[];let disposed=0;for(let i=0;i<2;i++){const read=()=>resource;using resource={id:i,[Symbol.dispose](){disposed++}};readers.push(read)}console.log(readers[0]().id,readers[1]().id,readers[0]()!==readers[1](),disposed);", "0 1 true 2\n"];
        yield return ["acquisition-observes-tdz", "/// <reference lib=\"esnext.disposable\" />\n{const read=()=>resource;const value:any={get[Symbol.dispose](){try{read();console.log('missing')}catch(e){console.log(e instanceof ReferenceError)}return()=>console.log('dispose')}};using resource=value;console.log(read()===value)}", "true\ntrue\ndispose\n"];
        yield return ["failed-acquisition-retains-tdz", "/// <reference lib=\"esnext.disposable\" />\nlet read:()=>any=()=>null;const marker={};try{{read=()=>resource;const value:any={get[Symbol.dispose](){throw marker}};using resource=value;}}catch(e){console.log(e===marker)}try{read();console.log('missing')}catch(e){console.log(e instanceof ReferenceError)}", "true\ntrue\n"];
        yield return ["initializer-observes-tdz", "/// <reference lib=\"esnext.disposable\" />\nlet acquired=0;const value:any={get[Symbol.dispose](){acquired++;return()=>{}}};try{const read:()=>any=()=>resource;using resource:any=(read(),value);}catch(e){console.log(e instanceof ReferenceError,acquired)}", "true 0\n"];
        yield return ["try-list-owner", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];let saved:()=>any=()=>null;try{const read=()=>resource;using resource={id:1,[Symbol.dispose](){log.push('dispose')}};saved=read;console.log(read().id)}finally{log.push('finally')}console.log(saved().id,log.join(','));", "1\n1 dispose,finally\n"];
        yield return ["catch-list-owner", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];try{throw 7}catch(error){const read=()=>resource;using resource={id:2,[Symbol.dispose](){log.push('dispose')}};console.log(read().id,error)}console.log(log.join(','));", "2 7\ndispose\n"];
        yield return ["finally-list-owner", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];try{log.push('try')}finally{const read=()=>resource;using resource={id:3,[Symbol.dispose](){log.push('dispose')}};console.log(read().id)}console.log(log.join(','));", "3\ntry,dispose\n"];
        yield return ["handler-repeat-restoration", "/// <reference lib=\"esnext.disposable\" />\nconst resource='outer',readers:(()=>any)[]=[],log:string[]=[];var shared=-1;for(let i=0;i<2;i++){try{var shared=i;const read=()=>resource;using resource={id:i,[Symbol.dispose](){log.push('d'+i)}};readers.push(read);if(i===0)continue;log.push('body')}finally{log.push('f'+i)}}console.log(readers[0]().id,readers[1]().id,readers[0]()!==readers[1](),shared,resource,log.join(','));", "0 1 true 1 outer d0,f0,body,d1,f1\n"];
    }

    public static IEnumerable<object[]> Pipelines() => Cases().SelectMany(row =>
        Enum.GetValues<Pipeline>().Select(pipeline =>
            new object[] { row[0], row[1], row[2], pipeline }));

    [Theory, MemberData(nameof(Pipelines))]
    public void ForwardUsingReadsRetainTheirLexicalOwner(string name, string source, string expected, Pipeline pipeline)
    {
        Assert.NotEmpty(name);
        if (pipeline == Pipeline.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
            return;
        }
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        using var stdout = new StringWriter();
        using var interpreter = new Interpreter(stdout: stdout, stderr: TextWriter.Null);
        if (pipeline == Pipeline.Resolved) new VariableResolver(interpreter).Resolve(statements);
        interpreter.Interpret(statements, typeMap);
        Assert.Null(interpreter.LastUncaughtError);
        Assert.Equal(expected, stdout.ToString().Replace("\r\n", "\n"));
    }
}
