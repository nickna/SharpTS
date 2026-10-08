using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Services;
using SharpTS.Parsing;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

public sealed class SemanticSignatureHelpServiceTests
{
    [Theory]
    [InlineData("function f(value: number): number { return value; } f(/*cursor*/1);", "number")]
    [InlineData("const f = (value: string): string => value; f(/*cursor*/'s');", "string")]
    [InlineData("interface F { (value: number): string; } declare const f: F; f(/*cursor*/1);", "string")]
    [InlineData("declare const f: { (value: string): number }; f(/*cursor*/'s');", "number")]
    public async Task FunctionsAndCallableValuesUseActualCapturedSignatures(string source, string expected)
    {
        using var project = new SignatureProject(source);
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        Assert.Contains(expected, Assert.Single(help.Signatures).Label);
        Assert.Equal(0, help.ActiveSignature);
        Assert.Equal(0, help.ActiveParameter);
    }

    [Fact]
    public async Task PublicOverloadsKeepOrderAndExactWinnerWithoutImplementation()
    {
        using var project = new SignatureProject("""
            function choose(value: number): boolean;
            function choose(value: string): boolean;
            function choose(value: any): boolean { return true; }
            choose(/*cursor*/'text');
            """);
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        var signatures = help.Signatures.ToArray();
        Assert.Equal(2, signatures.Length);
        Assert.Contains("number", signatures[0].Label);
        Assert.Contains("string", signatures[1].Label);
        Assert.DoesNotContain(signatures, signature => signature.Label.Contains("any", StringComparison.Ordinal));
        Assert.Equal(1, help.ActiveSignature);
    }

    [Theory]
    [InlineData("f(/*cursor*/)")]
    [InlineData("f(/*cursor*/'bad')")]
    [InlineData("f(/*cursor*/missing)")]
    public async Task ArgumentFailuresKeepCandidatesWithoutSelectingOne(string call)
    {
        using var project = new SignatureProject("function f(value: number): number { return value; } " + call + ";");
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        Assert.Contains("number", Assert.Single(help.Signatures).Label);
        Assert.Null(help.ActiveSignature);
    }

    [Theory]
    [InlineData("identity(/*cursor*/1)", "1")]
    [InlineData("identity<number>(/*cursor*/1)", "number")]
    public async Task GenericSuccessUsesActualInstantiatedParameterTypes(string call, string expected)
    {
        using var project = new SignatureProject("function identity<T>(value: T): T { return value; } " + call + ";");
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        var parameter = Assert.Single(Assert.Single(help.Signatures).Parameters!);
        Assert.Equal("value: " + expected, parameter.Label.Label);
        Assert.Equal(0, help.ActiveSignature);
    }

    [Fact]
    public async Task GenericConstraintFailureKeepsTheDeclaredPublicSignature()
    {
        using var project = new SignatureProject("function f<T extends number>(value: T): T { return value; } f<string>(/*cursor*/'bad');");
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        Assert.Contains("T", Assert.Single(help.Signatures).Label);
        Assert.Null(help.ActiveSignature);
    }

    [Theory]
    [InlineData("class C { method(value: number): string { return 's'; } } new C().method(/*cursor*/1);", "string")]
    [InlineData("class Base<T> { method(value: T): T { return value; } } class C extends Base<number> {} new C().method(/*cursor*/1);", "number")]
    [InlineData("class C { static method(value: string): number { return 1; } } C.method(/*cursor*/'s');", "number")]
    [InlineData("class C { #method(value: number): number { return value; } inspect(): void { this.#method(/*cursor*/1); } }", "number")]
    public async Task MethodsUseCheckedStaticInheritedGenericAndPrivateCandidates(string source, string expected)
    {
        using var project = new SignatureProject(source);
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        Assert.Contains(expected, Assert.Single(help.Signatures).Label);
        Assert.Equal(0, help.ActiveSignature);
    }

    [Theory]
    [InlineData("class C { constructor(value: number) {} } new C(/*cursor*/1);", "number")]
    [InlineData("class Base<T> { constructor(value: T) {} } class C extends Base<number> {} new C(/*cursor*/1);", "number")]
    public async Task ConstructorsUseActualSubstitutedParametersAndConstructedResult(string source, string expected)
    {
        using var project = new SignatureProject(source);
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        var signature = Assert.Single(help.Signatures);
        Assert.Contains(expected, signature.Label);
        Assert.Contains("C", signature.Label);
        Assert.Equal(0, help.ActiveSignature);
    }

    [Fact]
    public async Task AmbientConstructorOverloadsKeepActualSelection()
    {
        using var project = new SignatureProject("declare class C { constructor(value: number); constructor(value: string); } new C(/*cursor*/'s');");
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        Assert.Equal(2, help.Signatures.Count());
        Assert.Equal(1, help.ActiveSignature);
    }

    [Fact]
    public async Task RuntimeConstructorOverloadsDoNotPretendTheFlattenedImplementationSelectedAPublicOverload()
    {
        using var project = new SignatureProject("class C { constructor(value: number); constructor(value: string); constructor(value: any) {} } new C(/*cursor*/'s');");
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        Assert.Equal(2, help.Signatures.Count());
        Assert.DoesNotContain(help.Signatures, signature => signature.Label.Contains("any", StringComparison.Ordinal));
        Assert.Null(help.ActiveSignature);
    }

    [Fact]
    public async Task ImplicitZeroArgumentConstructorHasNoActiveParameter()
    {
        using var project = new SignatureProject("class C {} new C(/*cursor*/);");
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        Assert.Empty(Assert.Single(help.Signatures).Parameters!);
        Assert.Null(help.ActiveParameter);
        Assert.Equal(0, help.ActiveSignature);
    }

    [Theory]
    [InlineData("function f(value: number): void {} f(/*cursor*/")]
    [InlineData("function f(first: number, second: string): void {} f(1, /*cursor*/")]
    [InlineData("function f(first: number, second: string): void {} f(1, /*cursor*/)")]
    [InlineData("class C { constructor(value: number) {} } new C(/*cursor*/")]
    [InlineData("class C { constructor(first: number, second: string) {} } new C(1, /*cursor*/")]
    [InlineData("function inspect(): void { function f(value: number): void {} f(/*cursor*/ }")]
    public async Task UnfinishedCallsAndConstructorsUseFreshCheckedCursorCandidates(string source)
    {
        using var project = new SignatureProject(source);
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        Assert.Single(help.Signatures);
        Assert.Null(help.ActiveSignature);
        Assert.Equal(source.Contains("1,", StringComparison.Ordinal) ? 1 : 0, help.ActiveParameter);
        long checks = project.Analysis.Statistics.Checks;
        Assert.NotNull(await project.HelpAsync());
        Assert.Equal(checks, project.Analysis.Statistics.Checks);
    }

    [Theory]
    [InlineData("outer(inner(/*cursor*/1), 's');", "innerValue", 0)]
    [InlineData("outer(inner(1), /*cursor*/'s');", "outerValue", 1)]
    public async Task InnermostInvocationAndOnlyItsTopLevelCommasDetermineTheParameter(string call, string parameter, int active)
    {
        using var project = new SignatureProject("function inner(innerValue: number): number { return innerValue; } function outer(outerValue: number, text: string): void {} " + call);
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        Assert.Contains(parameter, Assert.Single(help.Signatures).Label);
        Assert.Equal(active, help.ActiveParameter);
    }

    [Theory]
    [InlineData("f([1, 2], /*cursor*/'s');")]
    [InlineData("f({ a: 1, b: 2 }, /*cursor*/'s');")]
    [InlineData("f(`one,two`, /*cursor*/'s');")]
    [InlineData("f(identity<{ a: number, b: number }>({ a: 1, b: 2 }), /*cursor*/'s');")]
    [InlineData("f(1 /* comma , */, /*cursor*/'s');")]
    public async Task NestedTemplateTypeArgumentAndCommentCommasAreNotArgumentSeparators(string call)
    {
        using var project = new SignatureProject("function f(first: any, second: string): void {} function identity<T>(value: T): T { return value; } " + call);
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        Assert.Equal(1, help.ActiveParameter);
    }

    [Theory]
    [InlineData("function f(first: number, second?: string): void {} f(1, /*cursor*/'s');", 1)]
    [InlineData("function f(first: number, ...rest: string[]): void {} f(1, 's', /*cursor*/'t');", 1)]
    [InlineData("function f(first: number, second: string): void {} f(1, 's', /*cursor*/'t');", null)]
    public async Task OptionalRestAndOverflowParameterIndicesAreMappedPerSignature(string source, int? expected)
    {
        using var project = new SignatureProject(source);
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync(activeParameterSupport: true));
        Assert.Equal(expected, help.ActiveParameter);
        Assert.Equal(expected, Assert.Single(help.Signatures).ActiveParameter);
    }

    [Fact]
    public async Task DifferentOverloadRestShapesGetIndependentParameterIndices()
    {
        using var project = new SignatureProject("function f(first: number): void; function f(first: string, ...rest: string[]): void; function f(first: any, ...rest: any[]): void {} f('s', 't', /*cursor*/'u');");
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync(activeParameterSupport: true));
        var signatures = help.Signatures.ToArray();
        Assert.Equal(2, signatures.Length);
        Assert.Null(signatures[0].ActiveParameter);
        Assert.Equal(1, signatures[1].ActiveParameter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParameterLabelsHonorUtf16OffsetCapabilityAndOptionalRestText(bool offsets)
    {
        using var project = new SignatureProject("function f(first: '😀', second?: number, ...rest: string[]): void {} f('😀', /*cursor*/1);");
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync(offsets));
        var signature = Assert.Single(help.Signatures);
        var labels = signature.Parameters!.Select(parameter => offsets
            ? signature.Label[parameter.Label.Range.Item1..parameter.Label.Range.Item2]
            : parameter.Label.Label).ToArray();
        Assert.Contains("😀", labels[0]);
        Assert.Contains("second?", labels[1]);
        Assert.Contains("...rest", labels[2]);
        Assert.All(signature.Parameters!, parameter => Assert.Equal(offsets, parameter.Label.IsRange));
        Assert.Null(signature.ActiveParameter);
    }

    [Theory]
    [InlineData("\n", "main.ts")]
    [InlineData("\r\n", "main.ts")]
    [InlineData("\n", "main.tsx")]
    [InlineData("\r\n", "main.tsx")]
    public async Task UnicodeLineEndingsAndTsxKeepExactInvocationContext(string newline, string fileName)
    {
        using var project = new SignatureProject("// 😀" + newline + "function résumé(value: number): void {}" + newline + "résumé(/*cursor*/1);", fileName);
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        Assert.Contains("number", Assert.Single(help.Signatures).Label);
        Assert.Equal(0, help.ActiveSignature);
    }

    [Theory]
    [InlineData("function f(value: string): void {} f('te/*cursor*/xt');")]
    [InlineData("function f(value: string): void {} f(`te/*cursor*/xt`);")]
    public async Task CompletedLiteralArgumentsUseTheirExactEnclosingInvocation(string source)
    {
        using var project = new SignatureProject(source);
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        Assert.Equal(0, help.ActiveParameter);
        Assert.Contains("value", Assert.Single(help.Signatures).Label);
    }

    [Theory]
    [InlineData("function f(value: number): void {} // f(/*cursor*/")]
    [InlineData("function f(value: number): void {} /* f(/*cursor*/ */")]
    [InlineData("function f(value: number): void {} const text = 'f(/*cursor*/';")]
    [InlineData("function f(value: number): void {} f(/* text /*cursor*/ */1);")]
    [InlineData("function f(value: number): void {} f('unfinished/*cursor*/")]
    [InlineData("function f(value: number): void {} f((1/*cursor*/")]
    [InlineData("function f(/*cursor*/value: number): void {}")]
    [InlineData("function f(value: number): void { /*cursor*/ }")]
    [InlineData("function outer(callback: () => void): void {} outer(() => { /*cursor*/ });")]
    [InlineData("function outer(callback: (value: number) => void): void {} outer((/*cursor*/value: number) => {});")]
    [InlineData("const element = <div>f(/*cursor*/</div>;")]
    [InlineData("function f(value: number): void {} f(1); /*cursor*/")]
    public async Task CommentsForeignBodiesLiteralFauxCallsAndNoninvocationPositionsAreRefused(string source)
    {
        using var project = new SignatureProject(source, source.Contains("<div>", StringComparison.Ordinal) ? "main.tsx" : "main.ts");
        Assert.Null(await project.HelpAsync());
    }

    [Theory]
    [InlineData("declare const loose: any; loose(/*cursor*/1);")]
    [InlineData("missing(/*cursor*/1);")]
    [InlineData("function f(value: Missing): void {} f(/*cursor*/1);")]
    [InlineData("function f(value: number): Missing { return 1; } f(/*cursor*/1);")]
    public async Task UnresolvedCandidateTypesCannotBePresentedAsRealAny(string source)
    {
        using var project = new SignatureProject(source);
        Assert.Null(await project.HelpAsync());
    }

    [Fact]
    public async Task UnprovenExplicitTypeArgumentKeepsOnlyTheDeclaredFormalCandidate()
    {
        using var project = new SignatureProject("function identity<T>(value: T): T { return value; } identity<Missing>(/*cursor*/1);");
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        var signature = Assert.Single(help.Signatures);
        Assert.Contains("value: T", signature.Label);
        Assert.DoesNotContain("any", signature.Label, StringComparison.Ordinal);
        Assert.Null(help.ActiveSignature);
    }

    [Fact]
    public async Task ImportedAliasUsesCheckedDependencySignature()
    {
        using var project = new SignatureProject("import { original as local } from './dependency'; local(/*cursor*/1);");
        project.AddFile("dependency.ts", "export function original(value: number): string { return 's'; }");
        var help = Assert.IsType<SignatureHelp>(await project.HelpAsync());
        Assert.Contains("number", Assert.Single(help.Signatures).Label);
        Assert.Contains("string", Assert.Single(help.Signatures).Label);
        Assert.Equal(0, help.ActiveSignature);
    }

    private sealed class SignatureProject : IDisposable
    {
        private readonly TempTestDirectory _directory = CliTestHelper.CreateTempDirectory();
        private readonly DocumentStore _store = new();
        private readonly SemanticSignatureHelpService _service;
        private readonly DocumentUri _uri;
        private readonly string _source;
        private readonly int _offset;
        public SemanticAnalysisService Analysis { get; } = new();

        public SignatureProject(string source, string fileName = "main.ts")
        {
            const string marker = "/*cursor*/";
            _offset = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(_offset >= 0);
            _source = source.Remove(_offset, marker.Length);
            _directory.CreateFile("tsconfig.json", """{"compilerOptions":{"noLib":true,"types":[]},"include":["*.ts","*.tsx"]}""");
            _uri = DocumentUri.FromFileSystemPath(_directory.CreateFile(fileName, _source));
            _store.Set(_uri.ToString(), _source);
            _service = new(Analysis);
        }

        public void AddFile(string name, string source)
        {
            var uri = DocumentUri.FromFileSystemPath(_directory.CreateFile(name, source));
            _store.Set(uri.ToString(), source);
        }

        public async Task<SignatureHelp?> HelpAsync(bool labelOffsetSupport = false, bool activeParameterSupport = false)
        {
            Assert.True(_store.TryCapture(_uri.ToString(), out var capture));
            var (line, column) = new LineIndex(_source).ToPosition(_offset);
            var result = await _service.SignatureHelpAsync(capture!, new Position(line - 1, column - 1),
                labelOffsetSupport, activeParameterSupport, CancellationToken.None);
            Assert.True(result.IsCurrent());
            return result.Help;
        }

        public void Dispose()
        {
            Analysis.Dispose();
            _directory.Dispose();
        }
    }
}
