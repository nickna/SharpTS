using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericConstructorSpreadTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool explicitType in new[] { false, true })
        {
            string construction = explicitType ? "new Box<number>(...values())" : "new Box(...values())";
            yield return [explicitType ? "generic-explicit-generator" : "generic-inferred-generator", """
                class Box<T>{constructor(...rest:T[]){console.log(rest.join(','));}}
                function* values(){yield 1;yield 2;yield 3;}
                CONSTRUCTION;
                """.Replace("CONSTRUCTION", construction), "1,2,3\n"];
        }
        yield return ["generic-fixed-prefix-and-rest-generator", """
            class Box<T>{constructor(value:T,...rest:T[]){console.log(value,rest.join(','));}}
            function* values(){yield 1;yield 2;yield 3;}
            new Box<number>(0,...values());
            """, "0 1,2,3\n"];
        yield return ["generic-fixed-slot-from-checked-tuple-spread", """
            class Box<T>{constructor(public value:T){}}
            const value=new Box<number>(...[42] as const);
            console.log(value.value);
            """, "42\n"];
        yield return ["generic-array-and-empty-spreads", """
            class Box<T>{constructor(...rest:T[]){console.log(rest.length,rest.join(','));}}
            new Box<number>(...[]);
            new Box<string>(...['a','b']);
            """, "0 \n2 a,b\n"];
        yield return ["generic-static-context-keeps-new-type-arguments", """
            class Box<T>{
                constructor(...rest:T[]){console.log(rest.join(','));}
                static make(){return new Box<string>(...['text']);}
            }
            function* values(){yield 1;yield 2;}
            new Box<number>(...values());
            Box.make();
            """, "1,2\ntext\n"];
        yield return ["generic-inherited-rest-constructor", """
            class Parent<T>{constructor(...rest:T[]){console.log(rest.join(','));}}
            class Child<T> extends Parent<T>{}
            function* values(){yield 1;yield 2;yield 3;}
            new Child<number>(...values());
            """, "1,2,3\n"];
        yield return ["generic-spread-abrupt-completion-keeps-body-unreached", """
            const trace:string[]=[];
            class Box<T>{constructor(...rest:T[]){trace.push('body');}}
            function* values(){try{trace.push('start');yield 1;throw new Error('iteration');}finally{trace.push('finally');}}
            try{new Box<number>(...values());}catch(error){console.log(error.message);}
            console.log(trace.join(','));
            """, "iteration\nstart,finally\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void GenericConstructorSpreadsUseExpandedSourceArguments(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors); Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
