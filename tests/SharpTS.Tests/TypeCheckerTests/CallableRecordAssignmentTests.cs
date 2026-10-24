using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class CallableRecordAssignmentTests
{
    private static void Check(string source) =>
        new TypeChecker().Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    [Theory]
    [InlineData("{ (value: number): number; tag: string }")]
    [InlineData("{ (value: number): number }")]
    public void NoncallableRecordCannotSatisfyCallSignature(string target)
    {
        Assert.ThrowsAny<TypeCheckException>(() => Check($"const value: {target} = {{ tag: 'tag' }};"));
    }

    [Fact]
    public void MatchingCallableRecordSatisfiesSignatureAndFields()
    {
        Check("declare const source: { (value: number): number; tag: string }; const value: { (value: number): number; tag: string } = source;");
        Check("const value: { tag: string } = { tag: 'tag' };");
        Assert.ThrowsAny<TypeCheckException>(() => Check("declare const source: { (value: string): string; tag: string }; const value: { (value: number): number; tag: string } = source;"));
    }

    [Fact]
    public void CallableInterfaceAlsoRequiresSignatureAndNamedMembers()
    {
        const string declaration = "interface Callable { (value: number): number; tag: string; } ";
        Assert.ThrowsAny<TypeCheckException>(() => Check(declaration + "const value: Callable = { tag: 'tag' };"));
        Assert.ThrowsAny<TypeCheckException>(() => Check(declaration + "const value: Callable = (value: number): number => value;"));
        Check(declaration + "declare const source: Callable; const value: Callable = source;");
    }
}
