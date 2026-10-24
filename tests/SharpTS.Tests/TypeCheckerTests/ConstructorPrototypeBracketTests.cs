using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
namespace SharpTS.Tests.TypeCheckerTests;
public class ConstructorPrototypeBracketTests
{
    [Fact]
    public void LiteralPrototype_IsSameMemberAsNamedPrototype()
    {
        var source = "class C {} const prototype: C = C['prototype']; const same: boolean = C['prototype'] === C.prototype;";
        Assert.Empty(new TypeChecker().CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()).Diagnostics);
    }
    [Fact]
    public void MissingConstructorMember_IsStillRejected()
    {
        var source = "class C {} C['missing'];";
        Assert.NotEmpty(new TypeChecker().CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()).Diagnostics);
    }
}
