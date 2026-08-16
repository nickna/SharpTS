using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer.Conversions;
using SharpTS.LanguageServer.Services;
using Xunit;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.Tests.LanguageServer;

public class InteropCodeActionTests
{
    [Fact]
    public void MissingMemberCarriesStructuredReplacement()
    {
        Diagnostic diagnostic = Assert.Single(Analyze(
            """
            @DotNetType("System.Text.StringBuilder")
            declare class SB { appendx(value: string): SB; }
            """));

        AssertReplacement(
            diagnostic,
            "Change member to 'append'",
            "append");
        Assert.Equal(new Range(1, 19, 1, 26), diagnostic.Range);
    }

    [Fact]
    public void StaticMismatchCanMakeMemberStatic()
    {
        Diagnostic diagnostic = Assert.Single(Analyze(
            """
            @DotNetType("System.Math")
            declare class MathBinding { abs(value: number): number; }
            """));

        AssertReplacement(
            diagnostic,
            "Make 'abs' static",
            "static abs");
    }

    [Fact]
    public void UnknownClrTypeCanReplaceWholeDecorator()
    {
        Diagnostic diagnostic = Assert.Single(Analyze(
            """
            @DotNetType("System.Text.StringBulder")
            declare class SB {}
            """,
            () => ["System.Text.StringBuilder", "System.Text.Encoding"]));

        AssertReplacement(
            diagnostic,
            "Change .NET type to 'System.Text.StringBuilder'",
            "@DotNetType(\"System.Text.StringBuilder\")");
        Assert.Equal(new Range(0, 0, 0, 39), diagnostic.Range);
    }

    [Fact]
    public void BadOverloadHintCanReplaceWholeDecorator()
    {
        Diagnostic diagnostic = Assert.Single(Analyze(
            """
            @DotNetType("System.Convert")
            declare class Conv {
              @DotNetOverload("flot")
              static toInt32(value: number): number;
            }
            """));

        AssertReplacement(
            diagnostic,
            "Change overload type to 'float'",
            "@DotNetOverload(\"float\")");
        Assert.Equal(new Range(2, 2, 2, 25), diagnostic.Range);
    }

    [Fact]
    public void DefaultDotNetImportCanConvertToNamedImport()
    {
        Diagnostic diagnostic = Assert.Single(Analyze(
            """
            import SB from "dotnet:System.Text.StringBuilder";
            """));

        AssertReplacement(
            diagnostic,
            "Convert to named import 'StringBuilder'",
            "{ StringBuilder as SB }");
        Assert.Equal(new Range(0, 7, 0, 9), diagnostic.Range);
    }

    [Fact]
    public void ServiceBuildsPreferredQuickFixWithoutParsingMessage()
    {
        Diagnostic diagnostic = Assert.Single(Analyze(
            """
            @DotNetType("System.Text.StringBuilder")
            declare class SB { appendx(value: string): SB; }
            """));
        diagnostic = diagnostic with
        {
            Message = "Localized or otherwise changed message text",
        };
        DocumentUri uri = DocumentUri.FromFileSystemPath(
            Path.GetFullPath("interop-action.ts"));
        var request = new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Range = diagnostic.Range,
            Context = new CodeActionContext
            {
                Diagnostics = new Container<Diagnostic>(diagnostic),
            },
        };

        CommandOrCodeAction action = Assert.Single(
            new InteropCodeActionService().Create(request));
        CodeAction codeAction = Assert.IsType<CodeAction>(
            action.CodeAction);
        Assert.Equal("Change member to 'append'", codeAction.Title);
        Assert.Equal(CodeActionKind.QuickFix, codeAction.Kind);
        Assert.True(codeAction.IsPreferred);
        TextEdit edit = Assert.Single(
            codeAction.Edit!.Changes![uri]);
        Assert.Equal(diagnostic.Range, edit.Range);
        Assert.Equal("append", edit.NewText);
    }

    [Fact]
    public void NonSharpTsOrUnstructuredDiagnosticsProduceNoAction()
    {
        var range = new Range(0, 0, 0, 1);
        DocumentUri uri = DocumentUri.FromFileSystemPath(
            Path.GetFullPath("interop-action.ts"));
        var request = new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Range = range,
            Context = new CodeActionContext
            {
                Diagnostics = new Container<Diagnostic>(
                    new Diagnostic
                    {
                        Range = range,
                        Message = "no structured data",
                        Source = "sharpts",
                    },
                    new Diagnostic
                    {
                        Range = range,
                        Message = "foreign",
                        Source = "typescript",
                        Data = JObject.FromObject(new
                        {
                            sharpts = new { codeAction = new { } },
                        }),
                    }),
            },
        };

        Assert.Empty(new InteropCodeActionService().Create(request));
    }

    private static List<Diagnostic> Analyze(
        string source,
        Func<IEnumerable<string>>? typeNames = null) =>
        new DiagnosticsService(typeNames: typeNames).Analyze(
            source,
            DiagnosticPublishMode.SharpTsOnly);

    private static void AssertReplacement(
        Diagnostic diagnostic,
        string title,
        string newText)
    {
        JObject data = Assert.IsType<JObject>(diagnostic.Data);
        Assert.Equal(1, data.Value<int>("sharpts.codeAction.version"));
        Assert.Equal(
            "replaceDiagnosticRange",
            data.Value<string>("sharpts.codeAction.kind"));
        Assert.Equal(
            title,
            data.Value<string>("sharpts.codeAction.title"));
        Assert.Equal(
            newText,
            data.Value<string>("sharpts.codeAction.newText"));
    }
}
