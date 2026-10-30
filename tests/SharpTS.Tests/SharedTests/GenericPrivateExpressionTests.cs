using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericPrivateExpressionTests
{
    public const string OriginalSource = "class C<T>{#x:T;constructor(x:T){this.#x=x;}read(){return this.#x;}static make(){return class C<U>{#y:U;constructor(y:U){this.#y=y;}read(){return this.#y;}};}}const Inner=C.make();console.log(new C<number>(7).read(),new Inner<string>(\"x\").read());";
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-nested-same-name", OriginalSource, "7 x\n"];
        yield return ["same-frame-cross-instantiation", "const Box=class<T>{#value:T;constructor(value:T){this.#value=value;}read(other:any){return other.#value;}};const a=new Box<number>(7);const b=new Box<string>('word');console.log(a.read(b),b.read(a));", "word 7\n"];
        yield return ["distinct-evaluation-brands", "function make(){return class<T>{#value:T;constructor(value:T){this.#value=value;}read(other:any){return other.#value;}has(other:any){return #value in other;}};}const A=make(),B=make();const a=new A<number>(7),b=new B<number>(9);console.log(a.has(a),a.has(b),b.has(a),b.has(b));try{a.read(b);}catch(e){console.log(e instanceof TypeError);}try{b.read(a);}catch(e){console.log(e instanceof TypeError);}", "true false false true\ntrue\ntrue\n"];
        yield return ["extracted-method-arrow-and-in", "const Box=class<T>{#value=7;#read(){return this.#value;}method(){return this.#read;}arrow(){return()=>this.#read();}has(other:any){return #read in other;}};const box=new Box<number>();const fn=box.method();console.log(fn.call(box),box.arrow()(),box.has(box),box.has({}));try{fn.call({});}catch(e){console.log(e instanceof TypeError);}", "7 7 true false\ntrue\n"];
        yield return ["compound-logical-and-sequential-write", "const Box=class<T>{#value=1;run(){this.#value+=2;this.#value&&=4;const before=this.#value;this.#value+=1;this.#value??=9;return before+':'+this.#value;}};console.log(new Box<number>().run());", "4:5\n"];
        yield return ["static-and-instance-evaluation", "function make(){return class Box<T>{static #value=2;#own=7;static next(){Box.#value+=1;return Box.#value;}read(){return this.#own;}static has(other:any){return #value in other;}};}const A=make(),B=make();console.log(A.next(),B.next(),A.next(),new A<number>().read(),A.has(A),A.has(B));", "3 3 4 7 true false\n"];
        yield return ["nested-declaration-owner-frame", "const Outer=class<T>{#value=7;make(){const self=this;class Inner{read(other:any){return other.#value;}arrow(){return()=>self.#value;}has(other:any){return #value in other;}}return new Inner();}};const outer=new Outer<number>();const inner=outer.make();console.log(inner.read(outer),inner.arrow()(),inner.has(outer),inner.has(inner));", "7 7 true false\n"];
        yield return ["runtime-declaration-frame", "function make(){class Box<T>{#value:T;constructor(value:T){this.#value=value;}read(){return this.#value;}has(other:any){return #value in other;}}return Box;}const A=make(),B=make();const a=new A<number>(7),b=new B<number>(9);console.log(a.read(),b.read(),a.has(a),a.has(b));", "7 9 true false\n"];
        yield return ["abrupt-installation", "let calls=0;const Box=class<T>{#first=(calls++,1);#second=(()=>{throw new Error('stop');})();read(){return this.#first;}};try{new Box<number>();}catch(e){console.log((e as any).message,calls);}", "stop 1\n"];
    }
    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
        .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ActualEvaluationFramesPreservePrivateSemantics(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory, MemberData(nameof(Cases))]
    public void NativePreparedFramesVerifyBeforeExecution(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var bytes = Compile(source, true);
        AssertUniqueTypes(bytes);
        Assert.Equal(expected, Execute(bytes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameNameExpressionDoesNotAddADeclaredClassBridge(bool native)
    {
        var bytes = Compile(OriginalSource, native); AssertUniqueTypes(bytes);
        var assembly = LoadVerified(bytes);
        var bridges = assembly.GetTypes().Where(type => type.IsInterface && type.Name.StartsWith("<>Private_", StringComparison.Ordinal)).ToArray();
        var bridge = Assert.Single(bridges);
        var owners = assembly.GetTypes().Where(type => type.IsGenericTypeDefinition && type.Name == "C").ToArray();
        Assert.Equal(2, owners.Length);
        Assert.Single(owners, type => type.GetInterfaces().Contains(bridge));
        Assert.Single(owners, type => !type.GetInterfaces().Contains(bridge));
        Assert.Equal("7 x\n", TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModuleFactoriesKeepTheActualExpressionFrame(bool native)
    {
        const string root = "/generic-private-expression";
        var files = new Dictionary<string, string>
        {
            [root + "/lib.ts"] = "export function make(){return class Box<T>{#value:T;constructor(value:T){this.#value=value;}read(other:any){return other.#value;}has(other:any){return #value in other;}};}",
            [root + "/main.ts"] = "import {make} from './lib';const A=make(),B=make();const a=new A<number>(7),b=new B<number>(9);console.log(a.read(a),a.has(a),a.has(b));try{a.read(b);}catch(e){console.log(e instanceof TypeError);}"
        };
        var resolver = new SharpTS.Modules.ModuleResolver(root + "/main.ts", files);
        var entry = resolver.LoadModule(root + "/main.ts"); var modules = resolver.GetModulesInOrder(entry);
        var types = new TypeChecker().CheckModules(modules, resolver);
        var compiler = new ILCompiler("private_expression_modules_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.CompileModules(modules, resolver, types);
        var bytes = compiler.SaveToBytes(); AssertUniqueTypes(bytes);
        Assert.Equal("7 true false\ntrue\n", Execute(bytes));
    }

    private static byte[] Compile(string source, bool native)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var compiler = new ILCompiler("generic_private_expression_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, types); return compiler.SaveToBytes();
    }
    private static Assembly LoadVerified(byte[] bytes)
    {
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        return Assembly.Load(bytes);
    }
    private static string Execute(byte[] bytes) => TestHarness.RunCompiledWithTimeout(() => LoadVerified(bytes), TimeSpan.FromSeconds(20));
    private static void AssertUniqueTypes(byte[] bytes)
    {
        using var pe = new PEReader(new MemoryStream(bytes)); var metadata = pe.GetMetadataReader();
        var names = metadata.TypeDefinitions.Select(handle =>
        {
            var type = metadata.GetTypeDefinition(handle);
            return (type.GetDeclaringType(), Namespace: metadata.GetString(type.Namespace), Name: metadata.GetString(type.Name));
        }).ToArray();
        Assert.Equal(names.Length, names.Distinct().Count());
    }
}
