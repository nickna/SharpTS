using SharpTS.Parsing;
using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class EditorNamespaceReceiverTests
{
    [Fact]
    public void ImportedFlattenedNamespaceUsesCanonicalExportFacets()
    {
        string path = Path.GetFullPath("namespace-entry.ts");
        string dependency = Path.GetFullPath("namespace-dependency.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string>
        {
            [path] = "import * as Api from './namespace-dependency'; Api.value;",
            [dependency] = "export type TypeOnly = number; export const value: string = 'text'; " +
                "export interface Shared { id: string; } export const Shared: number = 1; export class Dual {}",
        }) { CaptureEditorSyntax = true };
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker().WithEditorMetadata();
        var types = checker.CheckModules(resolver.GetModulesInOrder([entry]), resolver);
        var receiver = Assert.Single(entry.Document!.EditorSyntax!.Members).Receiver;
        var actualNamespace = Assert.IsType<TypeInfo.Namespace>(types.Get(receiver));
        // Existing module namespace semantics flatten both facets; presentation must use
        // the already captured canonical export bindings rather than treating all as values.
        Assert.True(actualNamespace.Values.ContainsKey("TypeOnly"));
        Assert.True(actualNamespace.TypeBindings!.ContainsKey("TypeOnly"));
        Assert.False(actualNamespace.ValueBindings!.ContainsKey("TypeOnly"));
        var set = checker.EditorFacts.Freeze().GetReceiverMembers(entry.Document, receiver);

        Assert.Equal(BindingNamespace.Type,
            Assert.Single(set.Members, member => member.Name == "TypeOnly").NamespaceFacet);
        Assert.Equal(BindingNamespace.Value,
            Assert.Single(set.Members, member => member.Name == "value").NamespaceFacet);
        var shared = Assert.Single(set.Members, member => member.Name == "Shared");
        Assert.Equal(BindingNamespace.Value, shared.NamespaceFacet);
        Assert.Equal("number", shared.Type.Text);
        Assert.Equal(BindingNamespace.Value,
            Assert.Single(set.Members, member => member.Name == "Dual").NamespaceFacet);
    }

    [Fact]
    public void NamespaceProjectionKeepsTypeOnlyExportsSeparateAndValueWinsADualSpelling()
    {
        const string source = """
            namespace Api {
                export interface TypeOnly { id: number; }
                export const ValueOnly: string = 'text';
                export interface Shared { id: string; }
                export const Shared: number = 1;
                export class Dual { value: number = 1; }
            }
            Api.ValueOnly;
            """;
        var (document, facts, types) = Check(source);
        var receiver = Assert.Single(document.EditorSyntax!.Members).Receiver;
        Assert.IsType<TypeInfo.Namespace>(types.Get(receiver));
        var set = facts.GetReceiverMembers(document, receiver);

        Assert.True(set.IsComplete);
        Assert.Equal(BindingNamespace.Type,
            Assert.Single(set.Members, member => member.Name == "TypeOnly").NamespaceFacet);
        Assert.Equal(BindingNamespace.Value,
            Assert.Single(set.Members, member => member.Name == "ValueOnly").NamespaceFacet);
        var shared = Assert.Single(set.Members, member => member.Name == "Shared");
        Assert.Equal(BindingNamespace.Value, shared.NamespaceFacet);
        Assert.Equal("number", shared.Type.Text);
        Assert.Equal(BindingNamespace.Value,
            Assert.Single(set.Members, member => member.Name == "Dual").NamespaceFacet);
    }

    [Fact]
    public void UnionCannotPresentATypeOnlyExportAsACommonRuntimeValue()
    {
        const string source = """
            namespace Left { export const shared: number = 1; export const common: number = 1; }
            namespace Right { export interface shared { id: number; } export const common: number = 2; }
            const chosen = Left as typeof Left | typeof Right;
            chosen.common;
            """;
        var (document, facts, types) = Check(source);
        var receiver = Assert.Single(document.EditorSyntax!.Members).Receiver;
        Assert.IsType<TypeInfo.Union>(types.Get(receiver));
        var set = facts.GetReceiverMembers(document, receiver);

        Assert.DoesNotContain(set.Members, member => member.Name == "shared");
        Assert.Equal(BindingNamespace.Value,
            Assert.Single(set.Members, member => member.Name == "common").NamespaceFacet);
        Assert.False(set.IsComplete);
    }

    private static (SourceDocument, FrozenEditorSemanticIndex, TypeMap) Check(string source)
    {
        var document = new SourceDocument("namespace-receiver-facets.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document)
            .WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker().WithEditorMetadata();
        var result = checker.CheckWithRecovery(statements, document);
        return (document, checker.EditorFacts.Freeze(), result.TypeMap);
    }
}
