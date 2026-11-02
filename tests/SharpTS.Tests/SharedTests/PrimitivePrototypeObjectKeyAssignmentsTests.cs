using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitivePrototypeObjectKeyAssignmentsTests
{
    public const string Template = """
        const p:any=$TARGET,log:string[]=[];let stored:any=$VALUE;
        const key:any={[Symbol.toPrimitive](hint:string){log.push('key:'+hint);return 'probe'}};
        Object.defineProperty(p,'probe',{configurable:true,get(){log.push('get');return stored},set(v){log.push('set:'+v);stored=v}});
        const rhs=()=>{log.push('rhs');return 2};const result=$OP;
        console.log(result,stored,log.join(','));
        """;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var target in new[] { "String.prototype", "Number.prototype", "Boolean.prototype" })
        foreach (var row in new[]
        {
            new[] { "compound", "1", "p[key]+=rhs()", "3 3 key:string,get,rhs,key:string,set:3\n" },
            new[] { "logical-or", "0", "p[key]||=rhs()", "2 2 key:string,get,rhs,key:string,set:2\n" },
            new[] { "logical-and", "1", "p[key]&&=rhs()", "2 2 key:string,get,rhs,key:string,set:2\n" },
            new[] { "logical-nullish", "null", "p[key]??=rhs()", "2 2 key:string,get,rhs,key:string,set:2\n" },
            new[] { "short-or", "1", "p[key]||=rhs()", "1 1 key:string,get\n" },
            new[] { "short-and", "0", "p[key]&&=rhs()", "0 0 key:string,get\n" },
            new[] { "short-nullish", "0", "p[key]??=rhs()", "0 0 key:string,get\n" },
        })
        {
            var source = Template.Replace("$TARGET", target).Replace("$VALUE", row[1]).Replace("$OP", row[2]);
            yield return [target + "-" + row[0], source, row[3]];
        }
    }

    public static IEnumerable<object[]> DualCases()
    {
        foreach (var row in Cases())
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [.. row, mode];
    }

    [Theory, MemberData(nameof(DualCases))]
    public void RawObjectKeysUseSeparateGetAndPutConversions(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
