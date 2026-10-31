using SharpTS.Tests.Infrastructure;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ArrayNamedIndexedMemberTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["bound-array-push", "const value:number[]=[1];console.log(value['push'](2),value.join(','));", "2 1,2\n"];
        yield return ["length", "const value:number[]=[7];console.log(value['length']);", "1\n"];
        yield return ["literal-variable-key", "const method='push';const value:number[]=[];console.log(value[method](7),value[0]);", "1 7\n"];
        yield return ["readonly-slice", "const value:readonly number[]=[1,2];console.log(value['slice'](1).join(','));", "2\n"];
        yield return ["mixed-index-and-member-key", "function read(value:number[],key:'0'|'length'){return value[key]};console.log(read([7],'0'),read([7],'length'));", "7 1\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void KnownStringKeysKeepTheirDeclaredMemberTypes(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("const value:readonly number[]=[1];value['push'](1);")]
    [InlineData("const value:readonly number[]=[1];value['reverse']();")]
    [InlineData("const value:number[]=[];value['missing']();")]
    [InlineData("const value:number[]=[];value['push']('wrong');")]
    public void UnknownKeysReadonlyMutatorsAndWrongElementTypesStayRejected(string source)
        => Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));

    [Theory]
    [InlineData("declare const value:number[];const result:number=value['0'];", 1)]
    [InlineData("declare const value:number[];const result:number=value['length'];", 0)]
    [InlineData("declare const value:number[] | string[];const result:number=value['length'];", 0)]
    [InlineData("declare const value:number[];declare const key:'0'|'length';const result:number=value[key];", 1)]
    public void UncheckedIndexedAccessAffectsElementsButNotKnownMembers(string source, int expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict with { NoUncheckedIndexedAccess = true });
        var result = checker.CheckWithRecovery(statements);
        Assert.Equal(expected, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("TS2322", diagnostic.TsCode));
    }

}
