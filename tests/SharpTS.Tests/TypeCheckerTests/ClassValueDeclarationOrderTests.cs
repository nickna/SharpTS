using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ClassValueDeclarationOrderTests
{
    [Theory]
    [InlineData("Later;")]
    [InlineData("new Later();")]
    [InlineData("class Earlier extends Later {}")]
    public void SeparatelyParsedDocumentlessInputsDoNotCompareOffsets(string use)
    {
        var declaration = TestHarness.ParseOrThrow(new string(' ', 200) + "class Later {}");
        var receiver = TestHarness.ParseOrThrow(use);

        var result = new TypeChecker().CheckWithRecovery([.. declaration, .. receiver]);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ExplicitDocumentStillOwnsSeparatelyLexedFragments()
    {
        const string use = "new Later();";
        string declaration = new string(' ', use.Length) + "class Later {}";
        var fragments = TestHarness.ParseOrThrow(use);
        fragments.AddRange(TestHarness.ParseOrThrow(declaration));
        var document = new SourceDocument("same.ts", use + "class Later {}", isVirtual: true);

        var result = new TypeChecker().CheckWithRecovery(fragments, document);

        Assert.Single(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2449");
    }

    [Fact]
    public void JsxSuffixRelexKeepsDeclarationOrderInTheSameSource()
    {
        const string source = "Later; const view = <div>don't panic</div>; class Later {}";
        var tokens = new Lexer(source) { JsxTolerant = true }.ScanTokens();
        var statements = new Parser(tokens).WithJsx(source, JsxParseOptions.Default).ParseOrThrow();

        var result = new TypeChecker().CheckWithRecovery(statements);

        Assert.Single(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2449");
    }

    [Theory]
    [InlineData("function read() { return Later; class Later {} }")]
    [InlineData("class C { read() { return Later; class Later {} } }")]
    [InlineData("function read() { Later; class Later {} }")]
    [InlineData("function read() { new Later(); class Later {} }")]
    [InlineData("function read() { typeof Later; class Later {} }")]
    [InlineData("function read() { if (true) { Later; } class Later {} }")]
    [InlineData("class C { static value = Later; } class Later {}")]
    [InlineData("class C { static { Later; } } class Later {}")]
    [InlineData("function read() { (() => Later)(); class Later {} }")]
    [InlineData("function read() { (() => { return Later; })(); class Later {} }")]
    [InlineData("function read() { (function() { return Later; })(); class Later {} }")]
    [InlineData("function read() { (((() => Later)))(); class Later {} }")]
    [InlineData("function read() { (((function() { return Later; })))(); class Later {} }")]
    [InlineData("function read() { (() => (() => Later)())(); class Later {} }")]
    [InlineData("function read() { ((value = Later) => value)(); class Later {} }")]
    [InlineData("function read() { ((value: any = Later) => 0)(1); class Later {} }")]
    [InlineData("function read() { (({ value = Later } = {}) => value)(); class Later {} }")]
    [InlineData("function read() { (function({ value = Later } = {}) { return value; })(); class Later {} }")]
    [InlineData("function read() { { (function*() { yield Later; })(); class Later {} } }")]
    [InlineData("function read() { (function*() { yield Later; })(); class Later {} }")]
    [InlineData("function read() { (((function*() { yield Later; })))(); class Later {} }")]
    [InlineData("function read() { (async () => { await 0; return Later; })(); class Later {} }")]
    [InlineData("function read() { (async function() { return Later; })(); class Later {} }")]
    public void EagerClassValueReadsBeforeDeclarationAreRejected(string source)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2449", error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("function read() { return Later; class Later {} }")]
    [InlineData("class C { read() { return Later; class Later {} } }")]
    public void RecoveryReportsEagerClassValueReads(string source)
    {
        var result = new TypeChecker().CheckWithRecovery(TestHarness.ParseOrThrow(source));
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2449");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ForwardHeritageReportsOrderAndContinuesCheckingMembers(bool sameLine, bool grouped)
    {
        string baseReference = grouped ? "(Later)" : "Later";
        string separator = sameLine ? " " : "\n";
        string source = $"class Earlier extends {baseReference} {{ " +
            "read(): number { return \"bad\"; } }" + separator +
            "class Later {}";
        var result = new TypeChecker().CheckWithRecovery(TestHarness.ParseOrThrow(source));
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2449");
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
        Assert.Equal(2, result.Diagnostics.Count);
    }

    [Theory]
    [InlineData("function read() { function later() { return Later; } class Later {} return later(); }")]
    [InlineData("class C { read() { function later() { return Later; } class Later {} return later(); } }")]
    [InlineData("function read() { const later = () => Later; class Later {} return later(); }")]
    [InlineData("class C { read() { const later = () => Later; class Later {} return later(); } }")]
    [InlineData("class C { value = Later; read() { return Later; } } class Later {}")]
    [InlineData("class C { static read() { return Later; } } class Later {}")]
    [InlineData("class C { static later = () => Later; } class Later {}")]
    [InlineData("function read(value = Later) {} class Later {}")]
    [InlineData("const read = (value = Later) => value; class Later {}")]
    [InlineData("class C { read(value = Later) {} } class Later {}")]
    [InlineData("function read() { let value: Later | null = null; class Later {} return value; }")]
    [InlineData("function read() { let value: typeof Later | null = null; class Later {} return value; }")]
    [InlineData("function read() { const later = (() => () => Later)(); class Later {} return later(); }")]
    [InlineData("function read() { const later = (function() { return function() { return Later; }; })(); class Later {} return later(); }")]
    [InlineData("function read(value = (() => Later)()) {} class Later {}")]
    [InlineData("function read({ value = Later } = {}) {} class Later {}")]
    [InlineData("function read() { const later = function*() { yield Later; }; class Later {} return later; }")]
    [InlineData("function make() { class Earlier extends Later {} return Earlier; } class Later {}")]
    [InlineData("function make() { class Earlier extends Later {} return Earlier; }\nclass Later {}")]
    public void DeferredAndTypeOnlyForwardReferencesRemainAccepted(string source)
        => new TypeChecker().Check(TestHarness.ParseOrThrow(source));
}
