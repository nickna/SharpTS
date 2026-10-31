using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class LexicalCommaForwardReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["const-closure", "const read=()=>later,later=7;console.log(read());", "7\n"];
        yield return ["let-closure", "let read=()=>later,later=8;console.log(read());", "8\n"];
        yield return ["block-shadow", "const value='outer';{const read=()=>value,value='inner';console.log(read());}console.log(value);", "inner\nouter\n"];
        yield return ["function-local", "function run(){const read=()=>later.value,later={value:9};return read();}console.log(run());", "9\n"];
        yield return ["object-method", "const obj={read(){return later;}},later=10;console.log(obj.read());", "10\n"];
        yield return ["exported-sequence", "export const read=()=>later,later=11;console.log(read());", "11\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void SameScopeSequenceBindingsAreVisibleInDeferredBodies(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Fact]
    public void NestedBlockDeclarationsDoNotBecomeOuterBindings()
    {
        const string source = "function read(){return hidden;} {const first=1,hidden=2;}";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var error = Assert.Throws<TypeCheckException>(() => new TypeChecker().Check(statements));
        Assert.Contains("Undefined variable 'hidden'", error.Message);
    }
}
