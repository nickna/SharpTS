using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class EditorReceiverQueryTests
{
    private sealed record Checked(SourceDocument Document, FrozenEditorSemanticIndex Facts,
        TypeMap Types, IReadOnlyList<Stmt> Statements, TypeChecker Checker);

    private static Checked Check(string source, bool enabled = true)
    {
        var document = new SourceDocument("editor-receivers.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document)
            .WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker().WithEditorMetadata(enabled);
        var checkedResult = checker.CheckWithRecovery(statements, document);
        return new(document, checker.EditorFacts.Freeze(), checkedResult.TypeMap, statements, checker);
    }

    private static EditorReceiverSet Members(Checked analysis, string marker)
    {
        int offset = analysis.Document.Text.IndexOf("/*" + marker + "*/", StringComparison.Ordinal) + marker.Length + 4;
        var member = analysis.Document.EditorSyntax!.FindMember(offset);
        Assert.NotNull(member);
        return analysis.Facts.GetReceiverMembers(analysis.Document, member.Receiver);
    }

    [Fact]
    public void ClassCandidatesRespectLexicalAccessStaticFacetsAndPrivateBrands()
    {
        var analysis = Check("""
            class Base {
                visible: number = 1;
                protected guarded: string = "x";
                private hidden: boolean = true;
                #brand: number = 2;
                static shared: number = 3;
                inspect(): number { return this./*inside*/visible; }
            }
            class Derived extends Base {
                derived(): number { return this./*derived*/visible; }
            }
            const value = new Base(); value./*outside*/visible;
            Base./*static*/shared;
            """);
        var inside = Members(analysis, "inside").Members.Select(member => member.Name).ToArray();
        Assert.Contains("visible", inside); Assert.Contains("guarded", inside);
        Assert.Contains("hidden", inside); Assert.Contains("#brand", inside);
        Assert.DoesNotContain("shared", inside);
        var derived = Members(analysis, "derived").Members.Select(member => member.Name).ToArray();
        Assert.Contains("guarded", derived); Assert.DoesNotContain("hidden", derived); Assert.DoesNotContain("#brand", derived);
        var outside = Members(analysis, "outside").Members.Select(member => member.Name).ToArray();
        Assert.Contains("visible", outside); Assert.DoesNotContain("guarded", outside);
        Assert.DoesNotContain("hidden", outside); Assert.DoesNotContain("#brand", outside);
        var statics = Members(analysis, "static").Members;
        Assert.Contains(statics, member => member.Name == "shared" && member.Facet == MemberFacet.Static);
        Assert.DoesNotContain(statics, member => member.Name == "visible");
    }

    [Fact]
    public void AForeignPrivateSpellingDoesNotHideTheLexicalBaseBrand()
    {
        var analysis = Check("""
            class Base {
                #value: number = 1;
                inspect(): number {
                    class Derived extends Base { #value: string = "derived"; }
                    const value = new Derived();
                    return value./*brand*/#value;
                }
            }
            """);
        var member = Assert.Single(Members(analysis, "brand").Members, candidate => candidate.Name == "#value");
        Assert.Equal("number", member.Type.Text);
        Assert.Equal(MemberFacet.PrivateInstance, member.Facet);
        Assert.NotNull(member.Source);
        Assert.Equal(analysis.Document.Text.IndexOf("#value", StringComparison.Ordinal),
            Assert.Single(member.Source.Declarations).Name.Start);
    }

    [Fact]
    public void InheritedGenericPresentationComposesKnownBaseArgumentsWithoutInventingMethodInference()
    {
        const string source = """
            class Base<T> {
                value: T;
                method<U>(input: U): U { return input; }
            }
            class Derived<T> extends Base<T[]> { }
            const value = new Derived<number>(); value./*member*/value;
            """;
        var analysis = Check(source);
        var members = Members(analysis, "member");
        Assert.True(members.IsComplete);
        Assert.Equal("Array<number>", Assert.Single(members.Members, member => member.Name == "value").Type.Text);
        string method = Assert.Single(members.Members, member => member.Name == "method").Type.Text;
        var ordinary = Check(source, enabled: false);
        var sourceClass = Assert.IsType<Stmt.Class>(ordinary.Statements[0]);
        var actualMethod = ordinary.Types.GetClassType(sourceClass)!.Methods["method"];
        Assert.Equal(EditorTypeRenderer.Render(actualMethod, EditorTypeRenderContext.Value).Text, method);
        // This checker path currently erases the generic method's local parameter to any.
        // Editor projection preserves that result; renderer shadowing has a separate test.
        Assert.Equal("(input: any) => any", method);
        Assert.NotNull(Assert.Single(members.Members, member => member.Name == "value").Source);
    }

    [Fact]
    public void StructuralCandidatesRetainKnownTypesWithoutInventingClassOrigins()
    {
        var analysis = Check("""
            interface Shape { readonly name: string; count?: number; }
            const value: Shape = { name: "x" }; value./*member*/name;
            const loose: any = {}; loose./*any*/missing;
            """);
        var members = Members(analysis, "member");
        var name = Assert.Single(members.Members, member => member.Name == "name");
        Assert.Equal("string", name.Type.Text); Assert.True(name.IsReadonly); Assert.Null(name.Source);
        Assert.True(Assert.Single(members.Members, member => member.Name == "count").IsOptional);
        Assert.Empty(Members(analysis, "any").Members);
        Assert.False(Members(analysis, "any").IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInheritedClassBrandWinsOverAMergedPublicInterfaceSpelling(bool generic)
    {
        string source = """
            class Base { private hidden: number = 1; public visible: number = 2; }
            interface ShapeTYPEPARAM extends Base { hidden: number; }
            declare let value: ShapeTYPEARG;
            value./*member*/visible;
            """;
        var analysis = Check(source.Replace("TYPEPARAM", generic ? "<T>" : "", StringComparison.Ordinal)
            .Replace("TYPEARG", generic ? "<number>" : "", StringComparison.Ordinal));
        var candidates = Members(analysis, "member").Members;
        Assert.Contains(candidates, member => member.Name == "visible");
        Assert.DoesNotContain(candidates, member => member.Name == "hidden");
    }

    [Fact]
    public void MissingSuperMethodKeepsKnownReceiverMethodsWithoutAStaleExpressionType()
    {
        var analysis = Check("""
            class Base { field: number = 1; method(): number { return 1; } }
            class Derived extends Base { inspect(): number { return super./*member*/missing(); } }
            """);
        var members = Members(analysis, "member");
        Assert.Contains(members.Members, member => member.Name == "method");
        Assert.DoesNotContain(members.Members, member => member.Name == "field");
        var super = analysis.Document.EditorSyntax!.Members.Single().Receiver;
        Assert.False(analysis.Facts.GetOccurrence(analysis.Document, super)?.Type.IsAvailable ?? false);
    }

    [Fact]
    public void OptionalProjectionCapsRawUnionConstituentsBeforeFilteringNullishTypes()
    {
        var analysis = Check("const value = { field: 1 }; value?.field;");
        var receiver = Assert.Single(analysis.Document.EditorSyntax!.Members).Receiver;
        var types = Enumerable.Repeat<TypeInfo>(TypeInfo.Null.Shared, 10000).ToList();
        types.Add(analysis.Types.Get(receiver)!);
        var capture = typeof(TypeChecker).GetMethod("CaptureEditorReceiver",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        capture.Invoke(analysis.Checker, [receiver, new TypeInfo.Union(types)]);
        var result = analysis.Checker.EditorFacts.Freeze().GetReceiverMembers(analysis.Document, receiver);
        Assert.True(result.IsTruncated);
        Assert.False(result.IsComplete);
        Assert.Empty(result.Members);
    }

    [Fact]
    public void LargeCandidateSetsAreTruncatedAndCaptureIsOptIn()
    {
        string source = "class Huge { " + string.Join(" ", Enumerable.Range(0, 400).Select(index => $"p{index}: number = {index};")) +
            " } const value = new Huge(); value./*member*/p0;";
        var analysis = Check(source);
        var members = Members(analysis, "member");
        Assert.True(members.IsTruncated); Assert.False(members.IsComplete); Assert.Equal(256, members.Members.Count);
        var disabled = Check(source, enabled: false);
        Assert.Equal(0, disabled.Facts.Count);
        Assert.Empty(Members(disabled, "member").Members);
    }
}
