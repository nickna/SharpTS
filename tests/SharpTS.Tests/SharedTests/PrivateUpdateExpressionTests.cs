using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrivateUpdateExpressionTests
{
    public const string OriginalSource = "class Counter{#value=1;run(){const old=this.#value++;const next=++this.#value;return old+\":\"+next+\":\"+this.#value;}}console.log(new Counter().run());";

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", OriginalSource, "1:3:3\n"];
        yield return ["four-operators", "class C{#v=1;run(){console.log(this.#v++,++this.#v,this.#v--,--this.#v,this.#v);}}new C().run();", "1 3 3 1 1\n"];
        yield return ["coerced-postfix", "class C{#v:any='5';run(){const old=this.#v++;console.log(old,typeof old,this.#v);this.#v=null;console.log(--this.#v);this.#v=undefined;console.log(this.#v++,this.#v);this.#v=true;console.log(this.#v--,this.#v);}}new C().run();", "5 number 6\n-1\nNaN NaN\n1 0\n"];
        yield return ["bigint-result-types", "class C{#v=2n;run(){const old:bigint=this.#v++;const next:bigint=--this.#v;console.log(String(old),String(next),typeof old,typeof next,String(this.#v));}}new C().run();", "2 2 bigint bigint 2\n"];
        yield return ["numeric-union", "class C{#v:number|bigint=1;run(){const old:number|bigint=this.#v++;console.log(String(old),String(this.#v));this.#v=3n;const next:number|bigint=--this.#v;console.log(String(next),typeof next);}}new C().run();", "1 2\n2 bigint\n"];
        yield return ["narrowed-private-result", "class C{#v:number|bigint=2n;#mixed:string|number='text';run(){if(typeof this.#v==='bigint'){const old:bigint=this.#v++;console.log(String(old),String(this.#v));}this.#mixed=4;if(typeof this.#mixed==='number'){const old:number=this.#mixed--;console.log(old,this.#mixed);}}}new C().run();", "2 3\n4 3\n"];
        yield return ["generic-numeric-constraint", "class C<T extends number>{#v:T;constructor(v:T){this.#v=v;}run(){const old:number=this.#v++;console.log(old,this.#v);}}new C<number>(7).run();", "7 8\n"];
        yield return ["numeric-enum", "enum E{A=1,B=2}class C{#v:E=E.A;run(){const old:E=this.#v++;console.log(old,this.#v);}}new C().run();", "1 2\n"];
        yield return ["primitive-hook-bigint", "const trace:string[]=[];class C{#v:any={[Symbol.toPrimitive](hint:string){trace.push(hint);return 10n;}};run(){const old=this.#v--;console.log(typeof old,old===(10n as any),String(this.#v),trace.join(','));}}new C().run();", "bigint true 9 number\n"];
        yield return ["throwing-symbol-conversion", "const token=Symbol('x');class C{#v:any=token;run(){try{this.#v++;}catch(e){console.log(e instanceof TypeError,this.#v===token);}}}new C().run();", "true true\n"];
        yield return ["object-to-bigint", "let calls=0;class C{#v:any={valueOf(){calls+=1;return 7n;}};run(){const old=this.#v++;console.log(String(old),typeof old,String(this.#v),calls);}}new C().run();", "7 bigint 8 1\n"];
        yield return ["raw-numeric-forwarding", "function forward(value:number):number{return value;}class C{#v:any=1n;update(){return this.#v++;}run(){const old=this.#v++;let alias:number=old;const branch=alias?alias:0;console.log(typeof alias,typeof branch,alias===(1n as any));const passed=forward(this.update());console.log(typeof passed,passed===(2n as any));}}new C().run();", "bigint bigint true\nbigint true\n"];
        yield return ["raw-annotated-field", "class C{#v:number=0;set(value:any){this.#v=value;}run(){this.set(7n);const old=this.#v++;console.log(typeof old,typeof this.#v,old===(7n as any));}}new C().run();", "bigint bigint true\n"];
        yield return ["raw-and-normal-source-callers", "function forward(value:number):number{return value;}class C{#v:any=4n;update(){return this.#v++;}}const c=new C();const normal=forward(3);const dynamic=forward(c.update());console.log(typeof normal,normal,typeof dynamic,dynamic===(4n as any));", "number 3 bigint true\n"];
        yield return ["overwritten-and-shadowed-callees", "function forward(value:number):number{return value;}class C{#v:any=5n;update(){return this.#v++;}}const c=new C();let selected:(value:number)=>number=forward;selected=(value:number)=>value;const result=selected(c.update());console.log(typeof result,result===(5n as any));function outer(){const forward=(value:number)=>0;console.log(forward(c.update()),typeof forward(1));}outer();", "bigint true\n0 number\n"];
        yield return ["raw-numeric-async-local", "class C{#v:any=3n;async run(){const old=this.#v++;await Promise.resolve(0);console.log(typeof old,old===(3n as any),String(this.#v));}}new C().run();", "bigint true 4\n"];
        yield return ["negative-zero", "class C{#v=-0;run(){const old=this.#v++;console.log(Object.is(old,-0),this.#v);}}new C().run();", "true 1\n"];
        yield return ["retained-reference-and-accessor-order", "const trace:string[]=[];let current:any;class C{#v:any;constructor(v:any){this.#v=v;}set(v:any){this.#v=v;}read(){return this.#v;}static bump(provider:any){return provider.chosen.#v++;}}const a=new C(1),b=new C(9);const sink={set mark(v:number){trace.push('setter:'+v);}};a.set({get valueOf(){trace.push('get-conversion');return function(){trace.push('convert');current=b;a.set(100);sink.mark=4;return '5';};}});current=a;const provider={get chosen(){trace.push('receiver');return current;}};const old=C.bump(provider);console.log(trace.join(','));console.log(old,a.read(),b.read(),current===b);", "receiver,get-conversion,convert,setter:4\n5 6 9 true\n"];
        yield return ["throwing-conversion-does-not-write", "let calls=0;const value={valueOf(){calls+=1;throw new Error('stop');}};class C{#v:any=value;run(){try{++this.#v;}catch(e){console.log((e as any).message);}console.log(calls,this.#v===value);}}new C().run();", "stop\n1 true\n"];
        yield return ["wrong-brand-before-conversion", "let reads=0,calls=0;class C{#v:any={valueOf(){calls+=1;return 1;}};static bump(provider:any){return provider.value.#v++;}}const provider={get value(){reads+=1;return {};}};try{C.bump(provider);}catch(e){console.log(e instanceof TypeError,reads,calls);}", "true 1 0\n"];
        yield return ["uninstalled-instance-field", "class C{#first=this.bump();#later=1;bump(){return this.#later++;}}try{new C();}catch(e){console.log(e instanceof TypeError);}", "true\n"];
        yield return ["uninstalled-static-field", "try{const Owner=class C{static #first=C.bump();static #later=1;static bump(){return ++C.#later;}};}catch(e){console.log(e instanceof TypeError);}", "true\n"];
        yield return ["generic-compound-original", "const Box=class<T>{#value=1;run(){this.#value+=2;this.#value&&=4;const before=this.#value++;this.#value??=9;return before+':'+this.#value;}};console.log(new Box<number>().run());", "4:5\n"];
        yield return ["generic-static-original", "function make(){return class Box<T>{static #value=2;#own=7;static next(){return ++Box.#value;}read(){return this.#own;}static has(other:any){return #value in other;}};}const A=make(),B=make();console.log(A.next(),B.next(),A.next(),new A<number>().read(),A.has(A),A.has(B));", "3 3 4 7 true false\n"];
        yield return ["different-evaluations-reject-update", "function make(){return class<T>{#v=1;update(other:any){return other.#v++;}read(){return this.#v;}};}const A=make(),B=make();const a=new A<number>(),b=new B<number>();try{a.update(b);}catch(e){console.log(e instanceof TypeError);}console.log(a.read(),b.read());", "true\n1 1\n"];
        yield return ["extracted-and-lexical-arrow", "class C<T>{#v=1;update(){return this.#v++;}arrow(){return()=>--this.#v;}read(){return this.#v;}}const a=new C<number>();const update=a.update;console.log(update.call(a),a.arrow()(),a.read());try{update.call({});}catch(e){console.log(e instanceof TypeError);}", "1 1 1\ntrue\n"];
        yield return ["static-inherited-brand", "class C{static #v=2;static bump(other:any){return other.#v++;}static read(){return C.#v;}}class D extends C{}console.log(C.bump(C),C.read());try{C.bump(D);}catch(e){console.log(e instanceof TypeError,C.read());}", "2 3\ntrue 3\n"];
        yield return ["async-receiver", "class C{#v=1;async run(){const before=(await Promise.resolve(this)).#v++;const after=--(await Promise.resolve(this)).#v;console.log(before,after,this.#v);}}new C().run();", "1 1 1\n"];
        yield return ["generator-receiver", "class C{#v=1;*run():Generator<any,number,C>{const before=(yield this).#v++;yield before;return this.#v;}}const c=new C();const iterator=c.run();console.log(iterator.next().value===c,iterator.next(c).value,iterator.next().value);", "true 1 2\n"];
        yield return ["async-generator-receiver", "class C{#v=2;async *run(){const before=(await Promise.resolve(this)).#v--;yield before;yield ++(await Promise.resolve(this)).#v;}}async function main(){for await(const value of new C().run()){console.log(value);}}main();", "2\n2\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
        .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PrivateUpdatesPreserveReferenceAndNumericSemantics(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory, MemberData(nameof(Cases))]
    public void NativePreparedUpdatesVerifyBeforeExecution(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var compiler = new ILCompiler("private_update_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
        var bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModulePrivateUpdatesKeepTheirActualEvaluationFrame(bool native)
    {
        const string root = "/private-updates";
        var files = new Dictionary<string, string>
        {
            [root + "/lib.ts"] = "export function make(){return class<T>{#v=1;update(other:any){return other.#v++;}read(){return this.#v;}};}",
            [root + "/main.ts"] = "import {make} from './lib';const A=make(),B=make();const a=new A<number>(),b=new B<number>();console.log(a.update(a),a.read());try{a.update(b);}catch(e){console.log(e instanceof TypeError,b.read());}"
        };
        var resolver = new SharpTS.Modules.ModuleResolver(root + "/main.ts", files);
        var entry = resolver.LoadModule(root + "/main.ts"); var modules = resolver.GetModulesInOrder(entry);
        var types = new TypeChecker().CheckModules(modules, resolver);
        var compiler = new ILCompiler("private_update_modules_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.CompileModules(modules, resolver, types);
        var bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        Assert.Equal("1 2\ntrue 1\n", TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
    }

    [Theory]
    [InlineData("class C{readonly #v=1;run(){this.#v++;}}", "TS2540")]
    [InlineData("class C{readonly #v=1;constructor(){++this.#v;}}", "TS2540")]
    [InlineData("class C{static readonly #v=1;static run(){C.#v--;}}", "TS2540")]
    [InlineData("class C{#v(){}run(){this.#v++;}}", "TS2803")]
    [InlineData("class C{#v='1';run(){--this.#v;}}", "TS2356")]
    [InlineData("class C{#v=true;run(){this.#v++;}}", "TS2356")]
    [InlineData("enum E{A='a'}class C{#v:E=E.A;run(){this.#v++;}}", "TS2356")]
    [InlineData("class C{#v:any=1;run(){const result:string=this.#v++;}}", "TS2322")]
    [InlineData("class C{#v=1n;run(){const result:number=--this.#v;}}", "TS2322")]
    public void PrivateUpdateChecksWritableNumericFields(string source, string code)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(statements));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }
}
