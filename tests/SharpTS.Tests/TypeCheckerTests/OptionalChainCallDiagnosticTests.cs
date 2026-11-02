using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class OptionalChainCallDiagnosticTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["nullable-function", "function run(fn:(()=>number)|undefined){fn()}", "TS2722"];
        yield return ["grouped-chain", "function run(v:string|undefined){(v?.includes)(\"x\")}", "TS2722"];
        yield return ["optional-method-not-called-optionally", "function run(v:{fn?:()=>number}|undefined){v?.fn()}", "TS2722"];
        yield return ["nullable-method", "function run(v:{fn:(()=>number)|null}|undefined){v?.fn()}", "TS2721"];
        yield return ["wrong-argument", "function run(v:string|undefined){v?.includes(7)}", "TS2345"];
        yield return ["missing-argument", "function run(v:string|undefined){v?.includes()}", "TS2554"];
        yield return ["missing-member", "function run(v:string|undefined){v?.missing(\"x\")}", "TS2339"];
        yield return ["result-remains-undefined", "function run(v:string|undefined){const result:boolean=v?.includes(\"x\");}", "TS2322"];
        yield return ["optional-call-result-remains-undefined", "function run(fn:(()=>number)|undefined){const result:number=fn?.();}", "TS2322"];
        yield return ["unknown-receiver", "function run(v:unknown){v?.fn()}", "TS18046"];
        yield return ["noncallable-member", "function run(v:{fn:number}|undefined){v?.fn()}", "TS2349"];
        yield return ["grouped-method-value", "function run(v:{fn():number}|undefined){(v?.fn)()}", "TS2722"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ChainDoesNotPermitAnUnsafeInvocationOrEraseUndefined(
        string name, string source, string code)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var result = new TypeChecker().CheckWithRecovery(statements);
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == code);
    }
}
