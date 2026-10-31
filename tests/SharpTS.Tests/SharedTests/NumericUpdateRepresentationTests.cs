using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NumericUpdateRepresentationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["local-original", "function run(){let value:any=1n;const old=value++;console.log(typeof old,typeof value,old===(1n as any));}run();", "bigint bigint true\n"];
        yield return ["top-level-control", "let value:any=1n;const old=value++;console.log(typeof old,typeof value,old===(1n as any));", "bigint bigint true\n"];
        yield return ["member-local", "function run(){const box:any={value:2n};const old=box.value++;console.log(typeof old,old===(2n as any),String(box.value));}run();", "bigint true 3\n"];
        yield return ["index-local", "function run(){const box:any=[3n];const old=box[0]--;console.log(typeof old,old===(3n as any),String(box[0]));}run();", "bigint true 2\n"];
        yield return ["direct-source-forward", "function forward(value:number):number{return value;}class C{value:any=1n;update(){return this.value++;}run(){const old=this.value++;let alias:number=old;const branch=alias?alias:0;console.log(typeof alias,typeof branch,alias===(1n as any));const passed=forward(this.update());console.log(typeof passed,passed===(2n as any));}}new C().run();", "bigint bigint true\nbigint true\n"];
        yield return ["normal-and-raw", "function forward(value:number):number{return value;}class C{value:any=4n;update(){return this.value++;}}const c=new C();const normal=forward(3);const dynamic=forward(c.update());console.log(typeof normal,normal,typeof dynamic,dynamic===(4n as any));", "number 3 bigint true\n"];
        yield return ["overwritten-and-shadowed", "function forward(value:number):number{return value;}class C{value:any=5n;update(){return this.value++;}}const c=new C();let selected:(value:number)=>number=forward;selected=(value:number)=>value;const result=selected(c.update());console.log(typeof result,result===(5n as any));function outer(){const forward=(value:number)=>0;console.log(forward(c.update()),typeof forward(1));}outer();", "bigint true\n0 number\n"];
        yield return ["const-callable", "function run(){let value:any=6n;const pass=(x:number)=>x;const result=pass(value++);console.log(typeof result,result===(6n as any));}run();", "bigint true\n"];
        yield return ["member-callable", "function run(){let value:any=7n;const object={pass:(x:number)=>x};const result=object.pass(value++);console.log(typeof result,result===(7n as any));}run();", "bigint true\n"];
        yield return ["index-callable", "function run(){let value:any=8n;const object=[(x:number)=>x];const result=object[0](value++);console.log(typeof result,result===(8n as any));}run();", "bigint true\n"];
        yield return ["closure-parameter", "function apply(fn:(x:number)=>number,x:number){return fn(x);}function make(){return(x:number)=>x;}function run(){let value:any=9n;const result=apply(make(),value++);console.log(typeof result,result===(9n as any));}run();", "bigint true\n"];
        yield return ["async-parameter", "async function pass(x:number){await Promise.resolve(0);return x;}async function run(){let value:any=10n;const old=value++;await Promise.resolve(0);const result=await pass(old);console.log(typeof old,typeof result,result===(10n as any));}run();", "bigint bigint true\n"];
        yield return ["generic-parameter", "function pass<T extends number>(x:T):T{return x;}function run(){let value:any=11n;const result=pass<number>(value++);console.log(typeof result,result===(11n as any));}run();", "bigint true\n"];
        yield return ["constructor-property", "class C{constructor(public value:number){}}function run(){let value:any=12n;const result=new C(value++);console.log(typeof result.value,result.value===(12n as any));}run();", "bigint true\n"];
        yield return ["virtual-method", "class C{pass(value:number){return value;}}class D extends C{override pass(value:number){return value;}}function run(){let value:any=13n;const target:C=new D();const result=target.pass(value++);console.log(typeof result,result===(13n as any));}run();", "bigint true\n"];
        yield return ["numeric-rest-direct", "function first(...values:number[]):number{return values[0];}function run(){let value:any=14n;const result=first(value++,2,3,4);console.log(typeof result,result===(14n as any));}run();", "bigint true\n"];
        yield return ["numeric-rest-held", "function first(...values:number[]):number{return values[0];}function run(){let value:any=15n;const fn:any=first;const result=fn(value++,2,3,4);console.log(typeof result,result===(15n as any));}run();", "bigint true\n"];
        yield return ["numeric-rest-spread", "function first(...values:number[]):number{return values[0];}function run(){let value:any=16n;const args:number[]=[value++,2,3,4];const result=first(...args);console.log(typeof result,result===(16n as any));}run();", "bigint true\n"];
        yield return ["raw-non-numeric-annotation", "function pass(value:string):string{return value;}function run(){let value:any=25n;const result=pass((value++) as any);console.log(typeof result,result===(25n as any));}run();", "bigint true\n"];
        yield return ["destructured-array", "function pass([value]:[number]){return value;}function run(){let value:any=27n;const result=pass([value++]);console.log(typeof result,result===(27n as any));}run();", "bigint true\n"];
        yield return ["destructured-object", "function pass({value}:{value:number}){return value;}function run(){let value:any=28n;const result=pass({value:value++});console.log(typeof result,result===(28n as any));}run();", "bigint true\n"];
        yield return ["constructor-property-alias", "class C{constructor(public value:number){}}function run(){let value:any=29n;const instance=new C(value++);const result=instance.value;console.log(typeof result,result===(29n as any));}run();", "bigint true\n"];
        yield return ["record-field-alias", "function run(){let value:any=30n;const object={value:value++};const result=object.value;console.log(typeof result,result===(30n as any));}run();", "bigint true\n"];
        yield return ["tagged-template", "function tag(parts:TemplateStringsArray,x:number){return x;}function run(){let value:any=31n;const result=tag`${value++}`;console.log(typeof result,result===(31n as any));}run();", "bigint true\n"];
        yield return ["setter-ingress", "class C{set p(value:number){console.log(typeof value,value===(32n as any));}}let value:any=32n;new C().p=value++;", "bigint true\n"];
        yield return ["setter-forwarding", "function pass(value:number){return value;}class C{set p(value:number){const result=pass(value);console.log(typeof result,result===(33n as any));}}let value:any=33n;new C().p=value++;", "bigint true\n"];
        yield return ["update-setter-forwarding", "function pass(value:number){return value;}let current:any=34n;class C{get p():any{return current;}set p(value:number){const result=pass(value);console.log(typeof result,result===(35n as any));current=value;}}new C().p++;", "bigint true\n"];
        yield return ["numeric-consumer-kind", "function run(){let value:any=1n;const old=value++;console.log(old===1,old===(1n as any));try{console.log(old+1);}catch(e){console.log(e instanceof TypeError);}}run();", "false true\ntrue\n"];
        yield return ["numeric-result-forwarding", "function double(value:number):number{return value+value;}function multiply(value:number):number{return value*value;}function run(){let value:any=3n;const a=double(value++);const b=multiply(value++);console.log(typeof a,a===(6n as any),typeof b,b===(16n as any));console.log(double(2),multiply(2));}run();", "bigint true bigint true\n4 4\n"];
        yield return ["unary-kind-after-update", "function run(){let value:any=3n;const old=value++;const minus=-old;const not=~old;console.log(typeof minus,minus===(-3n as any),typeof not,not===(-4n as any));try{console.log(+old);}catch(e){console.log(e instanceof TypeError);}}run();", "bigint true bigint true\ntrue\n"];
        yield return ["async-unary-kind-after-update", "async function run(){let value:any=3n;const old=value++;await Promise.resolve(0);const minus=-old;console.log(typeof minus,minus===(-3n as any));try{console.log(+old);}catch(e){console.log(e instanceof TypeError);}}run();", "bigint true\ntrue\n"];

        yield return ["unary-kind-after-update-runtime-bigint-control", "function run(){let value:any=3n;const old=value++;const minus=-old;const not=~old;console.log(typeof minus,minus===(BigInt(\"-3\") as any),typeof not,not===(BigInt(\"-4\") as any));try{console.log(+old);}catch(e){console.log(e instanceof TypeError);}}run();", "bigint true bigint true\ntrue\n"];
        yield return ["async-unary-kind-after-update-runtime-bigint-control", "async function run(){let value:any=3n;const old=value++;await Promise.resolve(0);const minus=-old;console.log(typeof minus,minus===(BigInt(\"-3\") as any));try{console.log(+old);}catch(e){console.log(e instanceof TypeError);}}run();", "bigint true\ntrue\n"];
        yield return ["super-setter-forwarding", "function pass(x:number):number{return x;}class Base{set p(x:number){const y=pass(x);console.log(typeof y,String(y));}}class Child extends Base{run(){let v:any=33n;super.p=v++;}}new Child().run();", "bigint 33\n"];
        yield return ["logical-setter-forwarding", "function pass(x:number):number{return x;}class C{get p():number{return 0;}set p(x:number){const y=pass(x);console.log(typeof y,String(y));}}let v:any=35n;new C().p ||= v++;", "bigint 35\n"];
        yield return ["normally-numeric", "function step(value:number):number{return ++value;}function run(){let i=0,total=0;for(i=0;i<4;i++){total+=step(i);}console.log(total,typeof total);}run();", "10 number\n"];
        yield return ["captured-local", "function run(){let value:any=17n;const old=value++;const read=()=>old;console.log(typeof read(),read()===(17n as any));}run();", "bigint true\n"];
        yield return ["call-and-apply", "function pass(value:number){return value;}function run(){let value:any=18n;const fn:any=pass;const a=fn.call(null,value++);const b=fn.apply(null,[value++]);console.log(typeof a,a===(18n as any),typeof b,b===(19n as any));}run();", "bigint true bigint true\n"];
        yield return ["reflect-apply", "function pass(value:number){return value;}function run(){let value:any=20n;const result=Reflect.apply(pass,null,[value++]);console.log(typeof result,result===(20n as any));}run();", "bigint true\n"];
        yield return ["generic-class-method", "class C<T>{pass(value:T){return value;}}function run(){let value:any=21n;const result=new C<number>().pass(value++);console.log(typeof result,result===(21n as any));}run();", "bigint true\n"];
        yield return ["async-generator-entry", "async function* pass(value:number){await Promise.resolve(0);yield value;}async function run(){let value:any=22n;for await(const result of pass(value++)){console.log(typeof result,result===(22n as any));}}run();", "bigint true\n"];
        yield return ["source-public-accessor-order", "const trace:string[]=[];let value:any=23n;class C{get current():any{trace.push('get');return value;}set current(next:any){trace.push('set');value=next;}run(){const old=this.current++;console.log(typeof old,old===(23n as any),String(value),trace.join(','));}}new C().run();", "bigint true 24 get,set\n"];
        yield return ["foreign-signature", "@DotNetType(\"System.Math\") declare class ClrMath{static Abs(value:number):number;}function pass(value:number){return value;}function run(){let value:any=24n;const result=pass(value++);console.log(typeof result,result===(24n as any),ClrMath.Abs(-7));}run();", "bigint true 7\n"];
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyProvedConstProducerSetsNarrowTheNegativeParameterCohort(bool immutable)
    {
        string declaration = immutable ? "const" : "let";
        string source = "function unrelated(value:number):number{return value;}function run(){let value:any=1n;"
            + declaration + " pass=(input:number)=>input;const result=pass(value++);console.log(typeof result);}run();";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var map = new TypeChecker().Check(statements);
        var unrelated = statements.OfType<Stmt.Function>().Single(function => function.Name.Lexeme == "unrelated");
        var run = statements.OfType<Stmt.Function>().Single(function => function.Name.Lexeme == "run");
        var binding = run.Body!.Single(statement => statement switch
        {
            Stmt.Const constant => constant.Name.Lexeme == "pass",
            Stmt.Var variable => variable.Name.Lexeme == "pass",
            _ => false
        });
        var arrow = Assert.IsType<Expr.ArrowFunction>(binding is Stmt.Const constantBinding
            ? constantBinding.Initializer : ((Stmt.Var)binding).Initializer);
        var compiler = new ILCompiler("negative_parameter_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.Compile(statements, map);
        Assert.True(map.IsRawNumericStorage(arrow.Parameters[0].Name));
        Assert.False(map.IsRawCallStorage(arrow.Parameters[0].Name));
        Assert.Equal(!immutable, map.IsRawNumericStorage(unrelated.Parameters[0].Name));
        var bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        var assembly = Assembly.Load(bytes);
        var method = assembly.GetType("$Program")!.GetMethod("unrelated", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        Assert.Equal(immutable ? typeof(double) : typeof(object), method.GetParameters()[0].ParameterType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeCohortDoesNotRewriteMappedOrForeignInterfaceSignatures(bool native)
    {
        string source = ((string)Cases().Single(row => (string)row[0] == "foreign-signature")[1])
            .Replace("declare class ClrMath{static Abs(value:number):number;}",
                "class ClrMath{static Abs(value:number):number{return value;}}", StringComparison.Ordinal);
        var statements = new Parser(new Lexer(source).ScanTokens(), DecoratorMode.Legacy).ParseOrThrow();
        var checker = new TypeChecker(); checker.SetDecoratorMode(DecoratorMode.Legacy);
        var map = checker.Check(statements);
        var mapped = Assert.Single(statements.OfType<Stmt.Class>());
        var parameter = Assert.Single(Assert.Single(mapped.Methods).Parameters);
        var compiler = new ILCompiler("foreign_parameter_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, map);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(compiler.SaveToBytes())));
        Assert.False(map.IsRawNumericStorage(parameter.Name));
        Assert.False(map.IsUndefinedReachableNumericParam(parameter));
        using var loader = new AssemblyReferenceLoader([Path.Combine(AppContext.BaseDirectory, "ExternalSignatureFixture.dll")]);
        var method = loader.TryResolve("ExternalSignatureFixture.IReadOnlyInput")!.GetMethod("Read")!;
        var originalParameter = Assert.Single(method.GetParameters());
        Assert.True(originalParameter.ParameterType.IsByRef);
        var view = loader.ForEmission(method);
        var emittedParameter = Assert.Single(view.GetParameters());
        Assert.True(emittedParameter.ParameterType.IsByRef);
        Assert.Same(typeof(int), emittedParameter.ParameterType.GetElementType());
        Assert.Equal(originalParameter.GetRequiredCustomModifiers().Select(type => type.FullName),
            emittedParameter.GetRequiredCustomModifiers().Select(type => type.FullName));
        Assert.Equal(method.MetadataToken, view.MetadataToken);
        Assert.Same(method.Module, view.Module);
        Assert.Same(originalParameter.ParameterType, Assert.Single(method.GetParameters()).ParameterType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceModuleParametersUseTheSameRawAbi(bool native)
    {
        const string root = "/numeric-updates";
        var files = new Dictionary<string, string>
        {
            [root + "/lib.ts"] = "export function forward(value:number):number{return value;}",
            [root + "/main.ts"] = "import {forward as pass} from './lib';function run(){let value:any=26n;const result=pass(value++);console.log(typeof result,result===(26n as any));}run();"
        };
        var resolver = new SharpTS.Modules.ModuleResolver(root + "/main.ts", files);
        var entry = resolver.LoadModule(root + "/main.ts"); var modules = resolver.GetModulesInOrder(entry);
        var map = new TypeChecker().CheckModules(modules, resolver);
        var compiler = new ILCompiler("numeric_update_modules_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.CompileModules(modules, resolver, map);
        var bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        Assert.Equal("bigint true\n", TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
    }

    [Theory, MemberData(nameof(Cases))]
    public void InterpreterRemainsTheNumericReference(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source, name == "foreign-signature" ? DecoratorMode.Legacy : DecoratorMode.None));
    }

    public static IEnumerable<object[]> Compilations() => Cases().SelectMany(row => new[] { false, true }
        .Select(native => new object[] { row[0], row[1], row[2], native }));

    [Theory, MemberData(nameof(Compilations))]
    public void SavedUpdatesRetainNumericKindAcrossSourceStorage(string name, string source, string expected, bool native)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens(), name == "foreign-signature" ? DecoratorMode.Legacy : DecoratorMode.None).ParseOrThrow();
        var checker = new TypeChecker();
        checker.SetDecoratorMode(name == "foreign-signature" ? DecoratorMode.Legacy : DecoratorMode.None);
        var types = checker.Check(statements);
        var compiler = new ILCompiler("numeric_update_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, types);
        var bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
    }
}
