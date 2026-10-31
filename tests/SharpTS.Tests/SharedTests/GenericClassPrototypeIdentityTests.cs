using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class GenericClassPrototypeIdentityTests
{
    [Fact]
    public void ForeignClosedTypeKeepsTheOriginalVirtualMetadataProtocol()
    {
        var saved = BuildSaved("class C {}", false);
        var query = saved.GetType("$Runtime")!.GetMethod("GetClassPrototype")!;
        var foreign = new ForeignClosedType();
        query.Invoke(null, [foreign]);
        Assert.Equal(0, foreign.NewQueries);
    }

    private static Assembly BuildSaved(string source, bool native)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var compiler = new ILCompiler("generic_prototype_" + Guid.NewGuid().ToString("N"), false, true,
            null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, types);
        byte[] bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        return Assembly.Load(bytes);
    }

    private sealed class ForeignClosedType() : TypeDelegator(typeof(List<int>))
    {
        internal int NewQueries;
        public override bool ContainsGenericParameters => false;
        public override RuntimeTypeHandle TypeHandle => typeof(List<int>).TypeHandle;
        public override bool IsGenericType { get { NewQueries++; throw new InvalidOperationException("foreign generic metadata"); } }
        public override Module Module { get { NewQueries++; throw new InvalidOperationException("foreign module metadata"); } }
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return ["inherited-assigned-data", "class P<T>{} class C extends P<number>{} const c:any=new C(); (P.prototype as any).value=4; console.log(c.value,c.absent);", "4 undefined\n"];
        yield return ["closed-owner-identities", """
            class P<T>{} class N extends P<number>{} class S extends P<string>{}
            const number=new P<number>(),text=new P<string>();
            console.log(Object.getPrototypeOf(N.prototype)===P.prototype,
                Object.getPrototypeOf(S.prototype)===P.prototype,
                Object.getPrototypeOf(number)===P.prototype,
                Object.getPrototypeOf(text)===P.prototype);
            """, "true true true true\n"];
        yield return ["late-construction-retains-properties", """
            class P<T>{} (P.prototype as any).extra=4;
            class N extends P<number>{} const n:any=new N();
            class S extends P<string>{} const s:any=new S();
            console.log(n.extra,s.extra,(new P<boolean>() as any).extra);
            delete (P.prototype as any).extra;
            console.log(n.extra,s.extra,(new P<object>() as any).extra);
            """, "4 4 4\nundefined undefined undefined\n"];
        yield return ["descriptor-receivers", """
            class P<T>{constructor(public value:T){}}
            Object.defineProperty(P.prototype,'read',{get:function(this:any){return this.value;}});
            class N extends P<number>{} class S extends P<string>{}
            console.log((new N(7) as any).read,(new S('text') as any).read);
            """, "7 text\n"];
        yield return ["method-receivers-and-super", """
            class P<T>{constructor(public value:T){} read(){return this.value;}}
            class N extends P<number>{via(){return super.read();}}
            class S extends P<string>{via(){return super.read();}}
            const n=new N(7),s=new S('text'),read:any=P.prototype.read;
            console.log(read.call(n),read.call(s)); console.log(n.via(),s.via());
            """, "7 text\n7 text\n"];
        yield return ["late-owner-does-not-reinstall-deleted-method", """
            class P<T>{read(){return 1;}}
            console.log(delete (P.prototype as any).read);
            class N extends P<number>{} const n:any=new N();
            console.log(typeof n.read,Object.hasOwn(P.prototype,'read'));
            """, "true\nundefined false\n"];
        yield return ["late-owner-retains-accessor-and-constructor-replacements", """
            class P<T>{get value(){return 1;}}
            Object.defineProperty(P.prototype,'value',{value:9,configurable:true});
            (P.prototype as any).constructor='changed';
            class N extends P<number>{} class S extends P<string>{}
            const n:any=new N(),s:any=new S();
            const direct:any=new P<boolean>();
            console.log(n.value,s.value,(P.prototype as any).constructor,direct.constructor);
            """, "9 9 changed changed\n"];
        yield return ["late-owner-retains-auto-accessor-replacement", """
            class P<T>{accessor value=1;}
            Object.defineProperty(P.prototype,'value',{value:9,configurable:true});
            class N extends P<number>{} class S extends P<string>{}
            console.log(new N().value,new S().value);
            """, "9 9\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Cases))]
    public void NativePreparedOutputPreservesTheSameSourcePrototype(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var saved = BuildSaved(source, true);
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void ClosedClrStorageSharesTheSourcePrototype(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
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
