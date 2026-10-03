using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class InheritedSuperMethodSignatureTests
{
    [Theory]
    [InlineData("argument", "class A {value(x:number){return x;}}class B extends A {}class C extends B {read(){return super.value('bad');}}", "TS2345")]
    [InlineData("generic-argument", "class A<T> {value(x:T):T{return x;}}class B extends A<number> {}class C extends B {read(){return super.value('bad');}}", "TS2345")]
    [InlineData("generic-chain-argument", "class A<T> {value(x:T):T{return x;}}class B<U> extends A<U> {}class C extends B<number> {read(){return super.value('bad');}}", "TS2345")]
    [InlineData("missing", "class A {value(){return 1;}}class B extends A {}class C extends B {read(){return super.missing();}}", "TS2339")]
    [InlineData("private", "class A {private value(){return 1;}}class B extends A {}class C extends B {read(){return super.value();}}", "TS2341")]
    public void InheritedSuperLookupPreservesSignaturesAndAccess(string name, string source, string code)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var diagnostics = new TypeChecker(maxErrors: 50).CheckWithRecovery(statements).Diagnostics;
        Assert.True(diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.TsCode == code),
            $"{name}: expected {code}; received {string.Join(';', diagnostics.Select(d => d.TsCode))}");
    }

    [Fact]
    public void GenericSuperclassChainComposesTheInheritedReturnAndParameterTypes()
    {
        const string source = "class A<T> {value(x:T):T{return x;}}class B<U> extends A<U> {}class C extends B<number> {read():number{return super.value(3);}}";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var diagnostics = new TypeChecker(maxErrors: 50).CheckWithRecovery(statements).Diagnostics;
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
