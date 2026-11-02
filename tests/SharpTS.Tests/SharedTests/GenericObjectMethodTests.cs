using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericObjectMethodTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["direct-original", "const value={id<T>(x:T){return x}};console.log(value.id<number>(7));", "7\n"];
        yield return ["string-key", "const value={\"id\"<T>(x:T){return x}};console.log(value.id<string>(\"value\"));", "value\n"];
        yield return ["numeric-key", "const value={7<T>(x:T){return x}};console.log(value[7]<number>(7));", "7\n"];
        yield return ["computed-key", "const key=\"id\";const value={[key]<T>(x:T){return x}};console.log(value[key]<number>(7));", "7\n"];
        yield return ["receiver", "const value={n:5,add<T extends number>(x:T){return this.n+x}};console.log(value.add(2));", "7\n"];
        yield return ["default-type", "const value={id<T=number>(x:T):T{return x}};console.log(value.id(7));", "7\n"];
        yield return ["keyword-get", "const value={get<T>(x:T){return x}};console.log(value.get<number>(7));", "7\n"];
        yield return ["keyword-async", "const value={async<T>(x:T){return x}};console.log(value.async<number>(7));", "7\n"];
        yield return ["async-method", "const value={async id<T>(x:T){return x}};value.id(7).then(x=>console.log(x));", "7\n"];
        yield return ["generator-method", "const value={*id<T>(x:T){yield x}};console.log(value.id(7).next().value);", "7\n"];
        yield return ["async-generator-method", "const value={async *id<T>(x:T){yield x}};async function run(){const result=await value.id(7).next();console.log(result.value)}run();", "7\n"];
        yield return ["nongeneric-control", "const async=3,get=4;const value={async,get,id(x:number){return x}};console.log(value.async,value.get,value.id(7));", "3 4 7\n"];
        yield return ["optional-member-original", "function run(v:{id<T>(x:T):T}|undefined){console.log(v?.id<number>(7))}run(undefined);run({id<T>(x:T){return x}});", "undefined\n7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void GenericObjectMethodsPreserveTypeParametersAndRuntimeCalls(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
