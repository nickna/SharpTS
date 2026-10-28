using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GlobalThisSelfIndexTests
{
    [Fact]
    public void PinnedIndexedGlobalObjectAndRepeatedSelfAccessKeepTheLoadedShape()
    {
        const string source = """
            declare const w_e: (typeof globalThis)["globalThis"];
            declare const again: (typeof w_e)["globalThis"];
            w_e.globalThis.setting;
            globalThis["globalThis"].setting;
            again.setting;
            """;
        var document = new SourceDocument("global-this.ts", source);
        var entry = new ParsedModule(document.Path, new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow()) { Document = document };
        var library = new ParsedModule("lib.global.d.ts", new Parser(new Lexer("declare var setting: number;").ScanTokens()).ParseOrThrow())
            { IsDefaultLibrary = true, IsDeclarationFile = true };
        var checker = new TypeChecker(strictNullChecks: true).WithEditorMetadata();
        checker.CheckModules([library, entry], new ModuleResolver(Path.GetTempPath()));
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var facts = checker.EditorFacts.GetSourceTypeObservations(document);
        foreach (string text in new[] { "w_e", "again", "w_e.globalThis", "globalThis[\"globalThis\"]" })
            Assert.Contains(facts, fact => source[fact.Span.Start..fact.Span.End] == text && fact.Display.Text == "typeof globalThis");
        foreach (string text in new[] { "w_e.globalThis.setting", "globalThis[\"globalThis\"].setting", "again.setting" })
            Assert.Contains(facts, fact => source[fact.Span.Start..fact.Span.End] == text && fact.Display.Text == "number");
    }
}
