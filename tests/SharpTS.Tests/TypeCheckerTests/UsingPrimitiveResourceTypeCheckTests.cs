using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class UsingPrimitiveResourceTypeCheckTests
{
    public static IEnumerable<object[]> RejectedCases()
    {
        yield return ["using resource = 1;"];
        yield return ["using resource = 'text';"];
        yield return ["using resource = true;"];
        yield return ["using resource = 1n;"];
        yield return ["using resource = Symbol('resource');"];
        yield return ["const key = Symbol('resource'); using resource = key;"];
        yield return ["using resource: number = 1;"];
        yield return ["using resource: string = 'text';"];
        yield return ["using resource: boolean = true;"];
        yield return ["using resource: bigint = 1n;"];
        yield return ["using resource: symbol = Symbol('resource');"];
        yield return ["async function run() { await using resource = 1; }"];
        yield return ["using resource: any = 1;"];
    }

    [Theory, MemberData(nameof(RejectedCases))]
    public void ScalarResourcesAreRejectedBeforeExecution(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var result = new TypeChecker().CheckWithRecovery(statements);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2851");
        Assert.Contains(GenericInstantiationDiagnosticTests.CollectStrictModules(source),
            diagnostic => diagnostic.TsCode == "TS2851");
    }

    [Theory]
    [InlineData("using resource = null;")]
    [InlineData("using resource = undefined;")]
    [InlineData("const value: any = 1; using resource = value;")]
    [InlineData("using resource = { [Symbol.dispose]() {} };")]
    [InlineData("async function run() { await using resource = null; }")]
    public void NullableDynamicAndObjectResourcesKeepTheirExistingPolicy(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        Assert.Empty(new TypeChecker().CheckWithRecovery(statements).Diagnostics);
        Assert.Empty(GenericInstantiationDiagnosticTests.CollectStrictModules(source));
    }
}
