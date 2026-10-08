using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class PrivateRenameDomainTests
{
    private sealed record Checked(SourceDocument Document, IReadOnlyList<Token> Tokens,
        ParseDiagnosticResult Parse, TypeCheckDiagnosticResult Check, TypeChecker Checker,
        FrozenMemberIndex Members, FrozenEditorSemanticIndex Facts);

    private static Checked Check(string source, int maxErrors = 10, bool metadata = true,
        bool isVirtual = false)
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "private-domain.ts"));
        var document = new SourceDocument(path, source, isVirtual);
        List<Token> tokens = new Lexer(source).ScanTokens();
        var parsed = new Parser(tokens).WithSourceDocument(document).WithEditorSyntax().Parse();
        var checker = new TypeChecker(new TypeCheckerOptions { MaxErrors = maxErrors }).WithEditorMetadata(metadata);
        if (!metadata) checker.WithMemberProvenance();
        var result = checker.CheckWithRecovery(parsed.Statements, document);
        return new(document, tokens, parsed, result, checker, checker.Members.Freeze(), checker.EditorFacts.Freeze());
    }

    private static int Offset(string source, string marker)
    {
        string comment = "/*" + marker + "*/";
        int start = source.IndexOf(comment, StringComparison.Ordinal);
        Assert.True(start >= 0, "Missing marker " + marker);
        return start + comment.Length;
    }

    private static PrivateRenameDomainResult Query(Checked value, string marker) =>
        value.Members.GetPrivateRenameDomain(value.Document, Offset(value.Document.Text, marker),
            value.Tokens, value.Facts, !value.Parse.IsSuccess || value.Parse.HitErrorLimit);

    private static PrivateRenameDomain Domain(Checked value, string marker)
    {
        var result = Query(value, marker);
        Assert.True(result.IsAvailable, result.Status + "; " + string.Join("; ", value.Check.Errors));
        return Assert.IsType<PrivateRenameDomain>(result.Domain);
    }

    private static void Clean(Checked value)
    {
        Assert.True(value.Parse.IsSuccess, string.Join("; ", value.Parse.Errors));
        Assert.True(value.Check.IsSuccess, string.Join("; ", value.Check.Errors));
    }

    [Fact]
    public void FieldsMethodsClosuresStaticUsesAndBrandChecksHaveExactOneOwnerDomains()
    {
        var value = Check("""
            class Box {
                /*field*/#value: number = 1;
                static /*staticField*/#total: number = 5;
                /*method*/#add(input: number): number { return this./*methodRead*/#value + input; }
                static /*staticMethod*/#read(): number { return Box./*staticRead*/#total; }
                inspect(candidate: object): number {
                    const read = (): number => this./*closure*/#value;
                    this./*write*/#value = read() + 1;
                    const result = this./*call*/#add(3);
                    const has = /*brand*/#value in candidate;
                    return result + Box./*staticCall*/#read();
                }
            }
            """);
        Clean(value);
        PrivateRenameDomain field = Domain(value, "field");
        Assert.Equal(new[] { "field", "methodRead", "closure", "write", "brand" }
            .Select(marker => Offset(value.Document.Text, marker)).Order(), field.Tokens.Select(token => token.Start));
        Assert.All(field.Tokens, token => Assert.Equal("#value", token.Lexeme));
        Assert.Same(field.SelectedToken, value.Tokens.Single(token => token.Start == Offset(value.Document.Text, "field")));
        Assert.False(field.Symbol.CanRename);
        Assert.Same(field.Symbol, Domain(value, "closure").Symbol);
        Assert.Equal(4, field.PrivateMembers.Count);
        Assert.Equal(MemberFacet.PrivateStatic, Domain(value, "staticField").Symbol.Facet);
        Assert.Equal(2, Domain(value, "staticField").Tokens.Count);
        Assert.Equal(2, Domain(value, "method").Tokens.Count);
        Assert.Equal(2, Domain(value, "staticMethod").Tokens.Count);
    }

    [Theory]
    [InlineData("class Box { /*declaration*/#value: number = 1; read(): number { return this./*use*/#value; } }")]
    [InlineData("const Box = class { /*declaration*/#value: number = 1; read(): number { return this./*use*/#value; } };")]
    [InlineData("const Box = class Named<T> { /*declaration*/#value: number = 1; read(): number { return this./*use*/#value; } };")]
    [InlineData("function outer(): number { class Box { /*declaration*/#value: number = 1; read(): number { return this./*use*/#value; } } return new Box().read(); }")]
    public void SourceClassFormsAndOrdinaryFunctionEnclosureRemainSupported(string source)
    {
        var value = Check(source);
        Clean(value);
        var domain = Domain(value, "use");
        Assert.Equal(2, domain.Tokens.Count);
        Assert.False(domain.Owner.IsNestedPrivateEnvironment);
        Assert.False(domain.Owner.ContainsNestedClass);
        Assert.Same(domain.Symbol, Domain(value, "declaration").Symbol);
    }

    [Theory]
    [InlineData("#café")]
    [InlineData("#变量")]
    [InlineData("#class")]
    public void WrittenPrivateIdentifierNamesKeepExactTokensAndRebind(string name)
    {
        string source = "class Box { /*declaration*/" + name + ": number = 1; " +
            "read(): number { return this./*use*/" + name + "; } }";
        var value = Check(source);
        Clean(value);
        var domain = Domain(value, "use");
        Assert.Equal(name, domain.Symbol.Name);
        Assert.Same(domain.Symbol, Domain(value, "declaration").Symbol);
        Assert.Equal(new[] { Offset(source, "declaration"), Offset(source, "use") },
            domain.Tokens.Select(token => token.Start));
        Assert.All(domain.Tokens, token =>
        {
            Assert.Equal(name, token.Lexeme);
            Assert.Equal(name, source[token.Start..token.End]);
        });

        const string replacement = "#résumé";
        var rebound = Check(Apply(source, domain, replacement));
        Clean(rebound);
        var renamed = Domain(rebound, "use");
        Assert.Equal(replacement, renamed.Symbol.Name);
        Assert.Equal(domain.Tokens.Count, renamed.Tokens.Count);
        Assert.All(renamed.Tokens, token => Assert.Equal(replacement, token.Lexeme));
    }

    [Fact]
    public void SiblingPrivateClassesAndPrivateLookingTextRemainOutsideTheEditDomain()
    {
        const string source = "// 😀 #value\r\nclass One { /*one*/#value: number = 1; read(): number { return this.#value; } }\r\n" +
            "class Two { /*two*/#value: number = 2; read(): number { return this.#value; } }\r\n" +
            "const text: string = '#value';";
        var value = Check(source);
        Clean(value);
        var domain = Domain(value, "one");
        string changed = Apply(source, domain, "#renamed");
        Assert.Contains("class Two { /*two*/#value: number = 2; read(): number { return this.#value; } }", changed);
        Assert.Contains("// 😀 #value\r\n", changed);
        Assert.Contains("const text: string = '#value';", changed);
        var rebound = Check(changed);
        Clean(rebound);
        Assert.Equal("#renamed", Domain(rebound, "one").Symbol.Name);
        Assert.Equal("#value", Domain(rebound, "two").Symbol.Name);
        Assert.Equal(2, Domain(rebound, "one").Tokens.Count);
    }

    [Theory]
    [InlineData("class Outer { /*selected*/#value: number = 1; method(): void { class Inner {} } }")]
    [InlineData("class Outer { method(): void { class Inner { /*selected*/#value: number = 1; } } }")]
    [InlineData("class Outer { /*selected*/#value: number = 1; create(): void { const Inner = class {}; } }")]
    [InlineData("class Outer { create(): void { const Inner = class { /*selected*/#value: number = 1; }; } }")]
    public void EitherSideOfNestedClassPrivateEnvironmentsRefusesTheWholeDomain(string source)
    {
        var value = Check(source);
        Assert.False(Query(value, "selected").IsAvailable);
    }

    [Theory]
    [InlineData("this.#missing;")]
    [InlineData("candidate.#value;")]
    [InlineData("#missing in candidate;")]
    public void AnyUnprovedPrivateOccurrenceRefusesEvenAProvedSelectedDeclaration(string use)
    {
        var value = Check("class Box { /*selected*/#value: number = 1; inspect(candidate: any): void { " + use + " } }");
        Assert.True(value.Members.FindResolution(value.Document, Offset(value.Document.Text, "selected")).IsResolved);
        Assert.False(Query(value, "selected").IsAvailable);
    }

    [Theory]
    [InlineData("this.#value += 1;")]
    [InlineData("this.#value ||= 1;")]
    [InlineData("this.#value &&= 1;")]
    [InlineData("this.#value ??= 1;")]
    [InlineData("this.#value++;")]
    [InlineData("++this.#value;")]
    [InlineData("this.#value--;")]
    [InlineData("--this.#value;")]
    public void ExistingUnsupportedPrivateCompoundAndUpdateSyntaxRefusesTheWholeDomain(string use)
    {
        var value = Check("class Box { /*selected*/#value: number = 1; inspect(): void { " + use + " } }");
        Assert.False(value.Parse.IsSuccess);
        Assert.Equal(PrivateRenameDomainStatus.IncompleteSyntax, Query(value, "selected").Status);
    }

    [Theory]
    [InlineData("class Box { /*selected*/#value: number = 1; #value: number = 2; }")]
    [InlineData("class Box { /*selected*/#value: number = 1; static #value: number = 2; }")]
    [InlineData("class Box { /*selected*/#value: number = 1; #value(): number { return 2; } }")]
    [InlineData("class Box { /*selected*/#constructor: number = 1; }")]
    [InlineData("class Box { get /*selected*/#value(): number { return 1; } }")]
    [InlineData("class Box { accessor /*selected*/#value: number = 1; }")]
    public void InvalidCollisionsReservedPrivateConstructorAndUnsupportedAccessorShapesRefuse(string source)
    {
        Assert.False(Query(Check(source), "selected").IsAvailable);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("private")]
    [InlineData("protected")]
    public void StringKeyedClassMembersNeverEnterThePrivateOperation(string access)
    {
        var value = Check("class Box { " + access + " /*selected*/value: number = 1; #brand: number = 2; }");
        Assert.False(Query(value, "selected").IsAvailable);
    }

    [Fact]
    public void UnrelatedSemanticErrorsDoNotRequireWorkspaceOrGlobalSuccess()
    {
        var value = Check("const broken: number = 'wrong'; class Box { /*selected*/#value: number = 1; read(): number { return this.#value; } }");
        Assert.False(value.Check.IsSuccess);
        Assert.Equal(2, Domain(value, "selected").Tokens.Count);
    }

    [Fact]
    public void ParseErrorElsewhereInTheTargetDocumentRefusesACompleteSelectedClass()
    {
        var value = Check("class Box { /*selected*/#value: number = 1; read(): number { return this.#value; } } " +
            "const broken = ;");
        Assert.False(value.Parse.IsSuccess);
        Assert.True(value.Members.FindResolution(value.Document, Offset(value.Document.Text, "selected")).IsResolved);
        Assert.Equal(PrivateRenameDomainStatus.IncompleteSyntax, Query(value, "selected").Status);
    }

    [Fact]
    public void APreparatoryClassBodyCannotAuthorizeAnUnvisitedPrivateDomain()
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "private-domain-module.ts"));
        const string source = "function outer(): void { missing; class Box { /*selected*/#value: number = 1; read(): number { return this.#value; } } }";
        var resolver = new ModuleResolver(path, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { [path] = source }, TypeScriptProgramOptions.Disabled) { CaptureEditorSyntax = true };
        var module = resolver.LoadModule(path);
        var checker = new TypeChecker(new TypeCheckerOptions { MaxErrors = 1 }).WithEditorMetadata();
        checker.CheckModules(resolver.GetModulesInOrder(module), resolver);
        var members = checker.Members.Freeze();
        Assert.Contains(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var result = members.GetPrivateRenameDomain(module.Document!, Offset(source, "selected"), module.Tokens,
            checker.EditorFacts.Freeze(), hasParseErrors: false);
        Assert.False(result.IsAvailable);
    }

    [Fact]
    public void UnvisitedCatchAndFailedPrivateUseCannotReusePreparatoryFacts()
    {
        var value = Check("class Box { /*selected*/#value: number = 1; read(): void { try {} catch { this.#value; } } }");
        Clean(value);
        Assert.False(Query(value, "selected").IsAvailable);
    }

    [Fact]
    public void DomainRequiresTheExactDocumentCompleteTokenStreamAndCapturedBodyFacts()
    {
        const string source = "class Box { /*selected*/#value: number = 1; read(): number { return this.#value; } }";
        var value = Check(source);
        var domain = Domain(value, "selected");
        var copy = new SourceDocument(value.Document.Path, source);
        Assert.False(value.Members.GetPrivateRenameDomain(copy, domain.SelectedToken.Start, value.Tokens,
            value.Facts, false).IsAvailable);
        Assert.False(Query(Check(source, isVirtual: true), "selected").IsAvailable);
        Assert.False(Query(Check(source, metadata: false), "selected").IsAvailable);
        Token[] missingUse = value.Tokens.Where(token => !ReferenceEquals(token, domain.Tokens[^1])).ToArray();
        Assert.False(value.Members.GetPrivateRenameDomain(value.Document, domain.SelectedToken.Start,
            missingUse, value.Facts, false).IsAvailable);
        Token[] cloned = value.Tokens.Select(token => new Token(token.Type, token.Lexeme, token.Literal, token.Line, token.Start)).ToArray();
        Assert.False(value.Members.GetPrivateRenameDomain(value.Document, domain.SelectedToken.Start,
            cloned, value.Facts, false).IsAvailable);
        value.Checker.WithEditorMetadata(false);
        Assert.Equal(domain.Tokens.Select(token => token.Start), Domain(value, "selected").Tokens.Select(token => token.Start));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => value.Members.GetPrivateRenameDomain(value.Document,
            domain.SelectedToken.Start, value.Tokens, value.Facts, false, cancellation.Token));
    }

    [Theory, ModeData]
    public void AppliedPrivateFieldMethodAndStaticEditsReparseRebindAndPreserveRuntime(ExecutionMode mode)
    {
        string source = """
            class Box {
                /*field*/#value: number = 1;
                static /*staticField*/#total: number = 5;
                /*method*/#add(input: number): number { return this.#value + input; }
                run(candidate: object): number {
                    const read = (): number => this.#value;
                    this.#value = read() + 1;
                    return this.#add(3) + (#value in candidate ? Box.#total : 0);
                }
            }
            const box = new Box();
            console.log(box.run(box));
            """;
        string before = TestHarness.Run(source, mode);
        Assert.Equal("10\n", before);
        foreach (var rename in new[] { ("field", "#renamedValue"), ("method", "#renamedMethod"), ("staticField", "#renamedTotal") })
        {
            var checkedBefore = Check(source);
            Clean(checkedBefore);
            var oldDomain = Domain(checkedBefore, rename.Item1);
            source = Apply(source, oldDomain, rename.Item2);
            var checkedAfter = Check(source);
            Clean(checkedAfter);
            var newDomain = Domain(checkedAfter, rename.Item1);
            Assert.Equal(rename.Item2, newDomain.Symbol.Name);
            Assert.Equal(oldDomain.Tokens.Count, newDomain.Tokens.Count);
            Assert.All(newDomain.Tokens, token => Assert.Equal(rename.Item2, token.Lexeme));
        }
        Assert.Equal(before, TestHarness.Run(source, mode));
    }

    private static string Apply(string source, PrivateRenameDomain domain, string replacement)
    {
        foreach (Token token in domain.Tokens.OrderByDescending(token => token.Start))
            source = source[..token.Start] + replacement + source[token.End..];
        return source;
    }
}
