using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncObjectLiteralDataDefinitionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-async-data", "async function run(){const value={answer:7};console.log(value.answer);}run();", "7\n"];
        yield return ["async-before-await", "async function run(){const value={answer:7,flag:false};console.log(value.answer,value.flag);await Promise.resolve();console.log(value.answer,value.flag);}run();", "7 false\n7 false\n"];
        yield return ["async-after-await", "async function run(){await Promise.resolve();const value={answer:7,flag:true,text:'ok'};console.log(value.answer,value.flag,value.text);}run();", "7 true ok\n"];
        yield return ["await-in-value", "async function run(){const value={a:1,b:await Promise.resolve(2),c:3};console.log(value.a,value.b,value.c);}run();", "1 2 3\n"];
        yield return ["async-shorthand", "const answer=7;async function run(){const value={answer};console.log(value.answer);}run();", "7\n"];
        yield return ["async-method-capture", "async function run(){const state={called:false};const value={run(){state.called=true;return 47;}};console.log(state.called,value.run(),state.called);}run();", "false 47 true\n"];
        yield return ["async-thenable-existing", "async function run(){const state={called:false};const value:any={then(resolve:any){state.called=true;resolve(47);}};const pending=Promise.resolve(value);for(let index=0;index<1;index++){}console.log('sync',state.called);const observed=pending.then(result=>console.log('resolved',result));}const pending=run();console.log('tail');", "sync false\ntail\nresolved 47\n"];
        yield return ["generator-data", "function* values(){const value={answer:7,flag:false};yield value.answer;yield value.flag;}const iterator=values();console.log(iterator.next().value,iterator.next().value);", "7 false\n"];
        yield return ["generator-yield-value", "function* values():Generator<number,number,number>{const value={a:1,answer:yield 2};return value.a+value.answer;}const iterator=values();console.log(iterator.next().value);console.log(iterator.next(41).value);", "2\n42\n"];
        yield return ["async-generator-data", "async function* values(){const value={answer:7};yield value.answer;}async function run(){for await(const value of values())console.log(value);}run();", "7\n"];
        yield return ["ordinary-omission-control", "const value={answer:7,flag:false};console.log(value.answer,value.flag);", "7 false\n"];
        yield return ["selected-descriptor-control", "async function run(){const inherited:any={};Object.defineProperty(inherited,'answer',{set(value:any){console.log('miss');}});const key:string='answer';const value:any={__proto__:inherited,[key]:7};console.log(value.answer,Object.keys(value).join(','));}run();", "7 answer\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void FreshDataLiteralsRetainTheirFields(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
