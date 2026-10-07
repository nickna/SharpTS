using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Services;
using SharpTS.Parsing;
using SharpTS.Tests.IntegrationTests;
using Xunit;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.Tests.LanguageServer;

/// <summary>Verifies final LSP locations from the frozen checker's member identities.</summary>
public sealed class MemberDefinitionServiceTests
{
    [Fact]
    public async Task FieldsMethodsAndStaticFacetsReachOnlyTheirSelectedDeclaration()
    {
        using var project = new MemberProject("""
            class Box {
                /*field*/value: number = 1;
                static /*staticField*/value: number = 2;
                /*method*/read(): number { return this./*this*/value; }
                static /*staticMethod*/read(): number { return this./*staticThis*/value; }
            }
            class Other { value: number = 3; }
            const box = new Box();
            box./*get*/value;
            box./*set*/value = 4;
            ++box./*update*/value;
            box./*call*/read();
            Box./*staticGet*/value;
            Box./*staticCall*/read();
            """);

        foreach (string marker in new[] { "field", "this", "get", "set", "update" })
            await project.AssertTargetsAsync(marker, ("field", "value"));
        foreach (string marker in new[] { "staticField", "staticThis", "staticGet" })
            await project.AssertTargetsAsync(marker, ("staticField", "value"));
        await project.AssertTargetsAsync("call", ("method", "read"));
        await project.AssertTargetsAsync("staticCall", ("staticMethod", "read"));
    }

    [Fact]
    public async Task AccessorAndOverloadGroupsReturnAllExactNamesInSourceOrder()
    {
        using var project = new MemberProject("""
            class Box {
                get /*getter*/size(): number { return 1; }
                set /*setter*/size(value: number) {}
                /*overload1*/map(value: string): string;
                /*overload2*/map(value: number): number;
                /*implementation*/map(value: any): any { return value; }
            }
            const box = new Box();
            box./*read*/size;
            box./*write*/size = 2;
            box./*call*/map(1);
            """);

        foreach (string marker in new[] { "getter", "setter", "read", "write" })
            await project.AssertTargetsAsync(marker, ("getter", "size"), ("setter", "size"));
        foreach (string marker in new[] { "overload1", "overload2", "implementation", "call" })
            await project.AssertTargetsAsync(marker, ("overload1", "map"), ("overload2", "map"),
                ("implementation", "map"));
    }

    [Fact]
    public async Task AutoAccessorAndParameterPropertyRetainTheirWrittenNamesAndLexicalFacet()
    {
        using var project = new MemberProject("""
            class Box {
                accessor /*auto*/count: number = 0;
                constructor(public readonly /*parameter*/value: number) { /*local*/value; }
            }
            const box = new Box(1);
            box./*autoRead*/count;
            box./*autoWrite*/count = 2;
            box./*property*/value;
            """);

        await project.AssertTargetsAsync("auto", ("auto", "count"));
        await project.AssertTargetsAsync("autoRead", ("auto", "count"));
        await project.AssertTargetsAsync("autoWrite", ("auto", "count"));
        foreach (string marker in new[] { "parameter", "local", "property" })
            await project.AssertTargetsAsync(marker, ("parameter", "value"));
    }

    [Fact]
    public async Task InheritedGenericMembersSuperAndOverridesKeepTheirDeclaringOrigin()
    {
        using var project = new MemberProject("""
            class Base<T> {
                /*field*/value: T;
                /*baseMethod*/read(): number { return 1; }
            }
            class Derived extends Base<number> {
                inspect(): number { return super./*super*/read(); }
            }
            class Override extends Base<number> { /*override*/read(): number { return 2; } }
            const numberBox = new Base<number>();
            const stringBox = new Base<string>();
            const derived = new Derived();
            numberBox./*number*/value;
            stringBox./*string*/value;
            derived?./*optional*/value;
            derived./*inherited*/read();
            new Override()./*overridden*/read();
            """);

        foreach (string marker in new[] { "number", "string", "optional" })
            await project.AssertTargetsAsync(marker, ("field", "value"));
        await project.AssertTargetsAsync("super", ("baseMethod", "read"));
        await project.AssertTargetsAsync("inherited", ("baseMethod", "read"));
        await project.AssertTargetsAsync("overridden", ("override", "read"));
    }

    [Fact]
    public async Task NamedAndAnonymousClassExpressionsDoNotShareMemberDefinitions()
    {
        using var project = new MemberProject("""
            const Named = class Local<T> { /*named*/value: T; };
            const Anonymous = class { /*anonymous*/value: number = 1; };
            new Named<number>()./*namedUse*/value;
            new Anonymous()./*anonymousUse*/value;
            """);

        await project.AssertTargetsAsync("namedUse", ("named", "value"));
        await project.AssertTargetsAsync("anonymousUse", ("anonymous", "value"));
    }

    [Fact]
    public async Task PrivateReadsWritesCallsStaticAccessAndBrandChecksReachExactPrivateNames()
    {
        using var project = new MemberProject("""
            class Box {
                /*field*/#value: number = 1;
                /*method*/#read(): number { return this.#value; }
                static /*static*/#staticValue: number = 2;
                inspect(other: Box, candidate: object): boolean {
                    other./*read*/#value;
                    other./*write*/#value = 3;
                    other./*call*/#read();
                    return /*presence*/#value in candidate;
                }
                static inspect(): number { return Box./*staticUse*/#staticValue; }
            }
            """);

        foreach (string marker in new[] { "field", "read", "write", "presence" })
            await project.AssertTargetsAsync(marker, ("field", "#value"));
        await project.AssertTargetsAsync("call", ("method", "#read"));
        await project.AssertTargetsAsync("staticUse", ("static", "#staticValue"));
    }

    [Fact]
    public async Task NestedPrivateEnvironmentsUseTheInnerSourceOwner()
    {
        using var project = new MemberProject("""
            class Outer {
                /*outer*/#value: number = 1;
                inspect(): number {
                    class Inner {
                        /*inner*/#value: number = 2;
                        read(): number { return this./*use*/#value; }
                    }
                    return new Inner().read();
                }
            }
            """);

        await project.AssertTargetsAsync("use", ("inner", "#value"));
    }

    [Fact]
    public async Task UnionRequiresOneCanonicalOriginAcrossEveryConstituent()
    {
        using var project = new MemberProject("""
            class Box<T> { /*origin*/value: T; }
            class A { value: number = 1; }
            class B { value: number = 2; }
            function same(box: Box<number> | Box<string>) { return box./*same*/value; }
            function distinct(box: A | B) { return box./*distinct*/value; }
            function mixed(box: A | { value: number }) { return box./*mixed*/value; }
            function intersection(box: A & B) { return box./*intersection*/value; }
            function nullable(box: A | null) { return box?./*nullable*/value; }
            """);

        await project.AssertTargetsAsync("same", ("origin", "value"));
        foreach (string marker in new[] { "distinct", "mixed", "intersection", "nullable" })
            Assert.Empty(await project.QueryAsync(marker));
    }

    [Fact]
    public async Task UnsupportedDomainsNeverGuessFromAnUnrelatedMatchingClass()
    {
        using var project = new MemberProject("""
            class Box { value: number = 1; }
            function dynamic(box: any) { return box./*any*/value; }
            function unknownReceiver(box: unknown) { return box./*unknown*/value; }
            function structural(box: { value: number }) { return box./*structural*/value; }
            interface Shape { value: number; }
            function interfaceReceiver(box: Shape) { return box./*interface*/value; }
            type Mapped = { [K in 'value']: number };
            function mapped(box: Mapped) { return box./*mapped*/value; }
            function indexed(box: { [key: string]: number }) { return box./*index*/value; }
            const box = new Box();
            box[/*literal*/'value'];
            const /*key*/key = 'value';
            box[/*computed*/key];
            box./*compound*/value += 1;
            box./*logical*/value ||= 1;
            """);

        foreach (string marker in new[] { "any", "unknown", "structural", "interface", "mapped", "index",
            "literal", "compound", "logical" })
            Assert.Empty(await project.QueryAsync(marker));
        // The expression inside a dynamic index remains a normal lexical binding.
        await project.AssertTargetsAsync("computed", ("key", "key"));
    }

    [Fact]
    public async Task WrongFacetsInaccessibleAndMissingMembersHaveNoTarget()
    {
        using var project = new MemberProject("""
            class Box {
                instance: number = 1;
                static staticValue: number = 2;
                private hidden: number = 3;
                protected protectedValue: number = 4;
            }
            const absent = 5;
            Box./*instance*/instance;
            new Box()./*static*/staticValue;
            new Box()./*private*/hidden;
            new Box()./*protected*/protectedValue;
            new Box()./*missing*/absent;
            """);

        foreach (string marker in new[] { "instance", "static", "private", "protected", "missing" })
            Assert.Empty(await project.QueryAsync(marker));
    }

    [Fact]
    public async Task AnyUnrelatedAndWrongStaticPrivateReceiversHaveNoTarget()
    {
        using var project = new MemberProject("""
            class Other { #value: number = 1; }
            class Box {
                #value: number = 2;
                static #static: number = 3;
                inspect(loose: any, other: Other): void {
                    loose./*any*/#value;
                    other./*unrelated*/#value;
                    Box./*wrongStatic*/#value;
                    this./*wrongInstance*/#static;
                }
            }
            """);

        foreach (string marker in new[] { "any", "unrelated", "wrongStatic", "wrongInstance" })
            Assert.Empty(await project.QueryAsync(marker));
    }

    [Fact]
    public async Task WidenedWritesDoNotChooseOnlyTheFlowNarrowedClass()
    {
        using var project = new MemberProject("""
            class A { value: number = 1; }
            class B { value: number = 2; }
            let box: A | B = new A();
            box./*use*/value = 3;
            """);

        Assert.Empty(await project.QueryAsync("use"));
    }

    [Fact]
    public async Task ValidReadProofSurvivesWrongCallArgumentsAndUnrelatedErrors()
    {
        using var project = new MemberProject("""
            class Box {
                /*field*/value: number = 1;
                /*method*/read(input: number): number { return input; }
            }
            const wrong: number = 'wrong';
            const box = new Box();
            box./*call*/read('wrong');
            box./*get*/value;
            """);

        await project.AssertTargetsAsync("call", ("method", "read"));
        await project.AssertTargetsAsync("get", ("field", "value"));
    }

    [Fact]
    public async Task UnvisitedSourceBodyCannotReuseAnOtherwiseKnownMemberName()
    {
        using var project = new MemberProject("""
            class Box { /*origin*/value: number = 1; }
            const box = new Box();
            try {} catch { box./*skipped*/value; }
            box./*known*/value;
            """);

        Assert.Empty(await project.QueryAsync("skipped"));
        await project.AssertTargetsAsync("known", ("origin", "value"));
    }

    [Theory]
    [InlineData("const box = new Box(); box./*use*/;")]
    [InlineData("const box = new Box(); box[/*use*/];")]
    public async Task UnfinishedMemberSyntaxDoesNotInventADefinition(string use)
    {
        using var project = new MemberProject("class Box { value: number = 1; } " + use);

        Assert.Empty(await project.QueryAsync("use"));
    }

    [Fact]
    public async Task ContextualKeywordNamesUseTheirWrittenTokenRange()
    {
        using var project = new MemberProject("class Box { /*field*/get: number = 1; } new Box()./*use*/get;");

        await project.AssertTargetsAsync("use", ("field", "get"));
    }

    [Theory]
    [InlineData("\n", "main.ts")]
    [InlineData("\r\n", "main.ts")]
    [InlineData("\n", "main.tsx")]
    [InlineData("\r\n", "main.tsx")]
    public async Task Utf16LineEndingsAndTsxReturnExactNameRangesWithoutDuplicatePasses(string newline, string fileName)
    {
        string source = string.Join(newline,
            "// astral character: 😀",
            "class Box { /*origin*/value: number = 1; }",
            "const text = '😀'; new Box()./*use*/value;");
        using var project = new MemberProject(source, fileName);

        await project.AssertTargetsAsync("use", ("origin", "value"));
        await project.AssertTargetsAsync("origin", ("origin", "value"));
    }

    [Fact]
    public async Task LexicalClassValueAndTypeFacetsStillDeduplicateToOneDeclaration()
    {
        using var project = new MemberProject("class /*class*/Box { value: number = 1; } const box: /*type*/Box = new /*value*/Box();");

        await project.AssertTargetsAsync("type", ("class", "Box"));
        await project.AssertTargetsAsync("value", ("class", "Box"));
    }

    private sealed class MemberProject : IDisposable
    {
        private readonly TempTestDirectory _directory = CliTestHelper.CreateTempDirectory();
        private readonly SemanticAnalysisService _analysis = new();
        private readonly DefinitionService _definitions;
        private readonly DocumentStore _store = new();

        public MemberProject(string source, string fileName = "main.ts")
        {
            Source = source;
            _directory.CreateFile("tsconfig.json", """{"compilerOptions":{"noLib":true,"types":[]},"include":["*.ts","*.tsx"]}""");
            string path = _directory.CreateFile(fileName, source);
            Uri = DocumentUri.FromFileSystemPath(path);
            _store.Set(Uri.ToString(), source);
            _definitions = new(_analysis);
        }

        private string Source { get; }
        private DocumentUri Uri { get; }

        public async Task<IReadOnlyList<Location>> QueryAsync(string marker)
        {
            Assert.True(_store.TryCapture(Uri.ToString(), out var capture));
            var lines = new LineIndex(Source);
            var (line, column) = lines.ToPosition(Offset(marker));
            var result = await _definitions.FindDefinitionsAsync(capture!, new Position(line - 1, column - 1), CancellationToken.None);
            Assert.True(result.IsCurrent());
            return result.Locations;
        }

        public async Task AssertTargetsAsync(string use, params (string Marker, string Name)[] targets)
        {
            var actual = await QueryAsync(use);
            var expected = targets.Select(target => TargetRange(target.Marker, target.Name)).ToArray();
            Assert.Equal(expected, actual.Select(location => location.Range));
            Assert.All(actual, location => Assert.Equal(Uri, location.Uri));
        }

        private Range TargetRange(string marker, string name)
        {
            int start = Offset(marker);
            Assert.Equal(name, Source.Substring(start, name.Length));
            var lines = new LineIndex(Source);
            var (startLine, startColumn) = lines.ToPosition(start);
            var (endLine, endColumn) = lines.ToPosition(start + name.Length);
            return new Range(startLine - 1, startColumn - 1, endLine - 1, endColumn - 1);
        }

        private int Offset(string marker)
        {
            string comment = "/*" + marker + "*/";
            int start = Source.IndexOf(comment, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing marker " + marker);
            return start + comment.Length;
        }

        public void Dispose()
        {
            _definitions.Dispose();
            _analysis.Dispose();
            _directory.Dispose();
        }
    }
}
