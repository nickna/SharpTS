using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DynamicClassConstructorSpreadTests
{
    public const string EvaluationSource = """
        const trace: string[] = [];
        class First {
            value: any;
            constructor(value: any = "default", ...rest: any[]) {
                trace.push("body"); this.value = value + ":" + rest.join(",");
            }
        }
        class Second {}
        let current: any = First;
        const holder: any = { get C() { trace.push("callee"); return current; } };
        function argument() { trace.push("argument"); current = Second; return undefined; }
        function* spread() { trace.push("spread"); yield 2; yield 3; trace.push("done"); }
        const result: any = new holder.C(argument(), ...spread());
        console.log(result instanceof First, result.value, trace.join(","));
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["evaluated-owner-and-ordered-generator", EvaluationSource,
            "true default:2,3 callee,argument,spread,done,body\n"];
        foreach (string callee in new[] { "Box", "Held", "holder.Box", "holder['Box']" })
        {
            yield return ["generator-" + callee, """
                class Box { constructor(value:any=7,...rest:any[]) { console.log(value,rest.join(','),arguments.length); } }
                const Held:any=Box; const holder:any={Box};
                function* values() { yield undefined; yield 2; yield 3; }
                new CALLEE(...values(),4);
                """.Replace("CALLEE", callee), "7 2,3,4 4\n"];
            yield return ["array-and-empty-" + callee, """
                class Box { constructor(value:any=7,...rest:any[]) { console.log(value,rest.join(','),arguments.length); } }
                const Held:any=Box; const holder:any={Box};
                new CALLEE(...[],...[undefined,2],3,...[]);
                new CALLEE(...[]);
                """.Replace("CALLEE", callee), "7 2,3 3\n7  0\n"];
        }
        yield return ["surplus-arguments-survive-source-factory", """
            class Box { constructor(first:any) {console.log(first,arguments.length,arguments[2]);} }
            const holder:any={Box};
            function* values(){yield 43;yield 44;yield 45;}
            new holder.Box(...values());
            """, "43 3 45\n"];
        yield return ["bound-and-reflect-controls", """
            class Box { constructor(value:any=7,...rest:any[]) {console.log(value,rest.join(','),arguments.length);} }
            const C:any=Box; const Bound:any=C.bind(null,'bound');
            function* values(){yield 2;yield 3;}
            new Bound(...values(),4);
            Reflect.construct(C,[undefined,...values(),4]);
            """, "bound 2,3,4 4\n7 2,3,4 4\n"];
        yield return ["abrupt-iterator-prevents-later-arguments-and-body", """
            const trace:string[]=[];
            class Box {constructor(...items:any[]){trace.push('body');}}
            const holder:any={get Box(){trace.push('callee');return Box;}};
            function* values(){try{trace.push('start');yield 1;trace.push('throw');throw new Error('iteration');}finally{trace.push('finally');}}
            function later(){trace.push('later');return 2;}
            try{new holder.Box(...values(),later());}catch(error){console.log(error.message);}
            console.log(trace.join(','));
            """, "iteration\ncallee,start,throw,finally\n"];
        yield return ["callee-failure-prevents-argument-evaluation", """
            const trace:string[]=[];
            const holder:any={get Box(){trace.push('callee');throw new Error('callee');}};
            function* values(){trace.push('spread');yield 1;}
            try{new holder.Box(...values());}catch(error){console.log(error.message);}
            console.log(trace.join(','));
            """, "callee\ncallee\n"];
        yield return ["multiple-generators-interleave-with-scalar-effects", """
            const trace:string[]=[];
            class Box {constructor(...items:any[]){trace.push('body');console.log(items.join(','));}}
            const C:any=Box;
            function scalar(n:number){trace.push('s'+n);return n;}
            function* values(n:number){trace.push('g'+n);yield n;trace.push('d'+n);}
            new C(scalar(1),...values(2),scalar(3),...values(4),scalar(5));
            console.log(trace.join(','));
            """, "1,2,3,4,5\ns1,g2,d2,s3,g4,d4,s5,body\n"];
        yield return ["aliased-string-regexp-and-error-read-expanded-slots", """
            const S:any=String, R:any=RegExp, E:any=Error;
            function* words(){yield 'hello';yield 'ignored';}
            function* pattern(){yield 'a+';yield 'i';}
            function* empty(){}
            console.log(new S(...words()).valueOf(),new S(...empty()).valueOf());
            console.log(new R(...pattern()).test('A'),new E(...words()).message);
            """, "hello \ntrue hello\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ConstructorSpreadsKeepTheFullIteratorAndCallerProtocol(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, actual) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors); Assert.Equal(expected, actual);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
