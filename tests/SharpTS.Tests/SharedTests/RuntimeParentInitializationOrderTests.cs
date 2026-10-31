using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class RuntimeParentInitializationOrderTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", """
            const events: string[] = [];
            function note(value: string) { events.push(value); return value; }
            class Base {
                field = note('field');
                constructor(value = note('default')) { events.push('body'); }
            }
            const Parent: any = Base;
            class Child extends Parent {
                constructor() { events.push('before'); super(); events.push('after'); }
            }
            new Child();
            console.log(events.join(','));
            """, "before,field,default,body,after\n"];
        yield return ["provided-argument", """
            const events:string[]=[];function note(value:string){events.push(value);return value;}
            class Base{field=note('field');constructor(value=note('default')){events.push('body:'+value);}}
            const Parent:any=Base;class Child extends Parent{constructor(){events.push('before');super(note('argument'));events.push('after');}}
            new Child();console.log(events.join(','));
            """, "before,argument,field,body:argument,after\n"];
        yield return ["bound-this-and-outer-field-scope", """
            const events:string[]=[];const value='outer';
            class Base{field=(events.push('field:'+value),7);constructor(value=(events.push('default:'+this.field),'parameter')){events.push('body:'+value+':'+this.field);}}
            const Parent:any=Base;class Child extends Parent{}new Child();console.log(events.join(','));
            """, "field:outer,default:7,body:parameter:7\n"];
        yield return ["abrupt-field", """
            const events:string[]=[];function stop(){events.push('field');throw new Error('stop');}
            class Base{field=stop();constructor(value=(events.push('default'),1)){events.push('body');}}
            const Parent:any=Base;class Child extends Parent{constructor(){events.push('before');super();events.push('after');}}
            try{new Child();}catch(error){events.push((error as any).message);}console.log(events.join(','));
            """, "before,field,stop\n"];
        yield return ["abrupt-default", """
            const events:string[]=[];function stop(){events.push('default');throw new Error('stop');}
            class Base{field=(events.push('field'),7);constructor(value=stop()){events.push('body');}}
            const Parent:any=Base;class Child extends Parent{constructor(){events.push('before');super();events.push('after');}}
            try{new Child();}catch(error){events.push((error as any).message);}console.log(events.join(','));
            """, "before,field,default,stop\n"];
        yield return ["replacement-rest-and-derived-fields", """
            const events:string[]=[];function note(value:string){events.push(value);return value;}
            class Base{field=note('field');constructor(value=note('default'),...tail:any[]){events.push('body:'+tail.length);return {replacement:true} as any;}}
            const Parent:any=Base;class Child extends Parent{own=note('own');constructor(...args:any[]){events.push('before');super(...args);events.push('after');}}
            const value:any=new Child(undefined,1,2);console.log(events.join(','));console.log(value.replacement,value.field===undefined,value.own);
            """, "before,field,default,body:2,own,after\ntrue true own\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
        .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void BaseAndDerivedElementsKeepTheirJavaScriptOrder(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory, MemberData(nameof(Cases))]
    public void NativePreparedOutputVerifiesBeforeExecution(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(); var types = checker.Check(statements);
        var compiler = new ILCompiler("parent_order_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
        byte[] bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
    }
}
