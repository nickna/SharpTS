using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
namespace SharpTS.Tests.TypeCheckerTests;
public class ForwardArrowFieldInferenceTests
{
    [Theory]
    [InlineData("class C { read = () => this.value; value = 1; }")]
    [InlineData("const C = class Named { read = () => this.value; value = 1; };")]
    [InlineData("class C { read = () => this.#value; #value = 1; }")]
    [InlineData("class C { read = () => this.value; value = (() => 1)(); }")]
    public void ArrowField_ResolvesLaterSiblingReturn(string declaration)
    {
        foreach (var type in new[] { "number", "string" })
        {
            var source = declaration + " const result: " + type + " = new C().read();";
            var result = new TypeChecker(strictNullChecks: true).CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            Assert.Equal(type == "string", result.Diagnostics.Any(d => d.TsCode == "TS2322"));
        }
    }
    [Fact]
    public void AnnotatedRecursiveCallback_RemainsValid()
    {
        var source = "class C { read = (): number => this.read(); }";
        var result = new TypeChecker(new TypeCheckerOptions { StrictNullChecks = true, NoImplicitAny = true })
            .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
        Assert.Empty(result.Diagnostics);
    }
    [Fact]
    public void NestedClass_DirectPendingGetterDoesNotInheritOuterCallbackContext()
    {
        var source = "const build = () => { class C { static get callback() { return C.later; } static copied = C.callback; static result = C.copied(); static later = 7; } };";
        var result = new TypeChecker().CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
        Assert.Contains(result.Diagnostics, d => d.TsCode == "TS2349");
    }
}
