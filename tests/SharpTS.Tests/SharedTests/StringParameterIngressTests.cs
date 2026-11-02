using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class StringParameterIngressTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-object-identity", "function identity(value:string){return value}const raw:any={marker:7};console.log(identity(raw)===raw);", "true\n"];
        yield return ["number-value", "function identity(value:string){return value}const raw:any=7;console.log(identity(raw)===raw);", "true\n"];
        yield return ["boolean-value", "function identity(value:string){return value}const raw:any=true;console.log(identity(raw)===raw);", "true\n"];
        yield return ["null-value", "function identity(value:string){return value}const raw:any=null;console.log(identity(raw)===raw);", "true\n"];
        yield return ["undefined-value", "function identity(value:string){return value}const raw:any=undefined;console.log(identity(raw)===raw);", "true\n"];
        yield return ["error-identity", "function identity(value:string){return value}const raw:any=new Error(\"marker\");console.log(identity(raw)===raw);", "true\n"];
        yield return ["ordinary-string", "function identity(value:string){return value}console.log(identity(\"value\"));", "value\n"];
        yield return ["const-alias", "function identity(value:string){return value}const fn=identity;const raw:any={marker:7};console.log(fn(raw)===raw);", "true\n"];
        yield return ["forwarded-value", "function identity(value:string){return value}function forward(value:any){return identity(value)}const raw:any={marker:7};console.log(forward(raw)===raw);", "true\n"];
        yield return ["arrow-parameter", "const identity=(value:string)=>value;const raw:any={marker:7};console.log(identity(raw)===raw);", "true\n"];
        yield return ["function-expression", "const identity=function(value:string){return value};const raw:any={marker:7};console.log(identity(raw)===raw);", "true\n"];
        yield return ["method-parameter", "class Reader{identity(value:string){return value}}const raw:any={marker:7};console.log(new Reader().identity(raw)===raw);", "true\n"];
        yield return ["constructor-parameter", "const raw:any={marker:7};class Reader{constructor(value:string){console.log(value===raw)}}new Reader(raw);", "true\n"];
        yield return ["lifted-function-parameter", "async function run(){const raw:any={marker:7};function identity(value:string){return value}await Promise.resolve(1);console.log(identity(raw)===raw)}run();", "true\n"];
        yield return ["same-named-shadow-control", "function identity(value:string){return value}function inner(){const identity=(value:number)=>value+1;console.log(identity(7))}inner();console.log(identity(\"safe\"));", "8\nsafe\n"];
        yield return ["string-hot-control", "function hot(value:string):number{let total=0;for(let i=0;i<100;i++)total+=value.length;return total}console.log(hot(\"abc\"));", "300\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void StringAnnotationPreservesTheActualArgument(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
