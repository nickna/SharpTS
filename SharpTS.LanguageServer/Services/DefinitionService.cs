using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.LanguageServer.Services;

/// <summary>
/// Resolves source positions to checker-bound local value declarations.
/// </summary>
/// <remarks>
/// The service intentionally returns no result for names the checker did not bind. In particular,
/// property/member navigation and cross-module import targets need their own complete semantic
/// domains; guessing from matching text would produce incorrect navigation under shadowing.
/// </remarks>
public sealed class DefinitionService
{
    /// <summary>
    /// Finds the local value declaration selected by an LSP position.
    /// </summary>
    public IReadOnlyList<Location> FindDefinitions(
        string path,
        string text,
        Position position)
    {
        var document = new SourceDocument(path, text);

        ParseDiagnosticResult parsed;
        try
        {
            var parser = new Parser(
                    new Lexer(text) { JsxTolerant = IsJsx(path) }.ScanTokens(),
                    DecoratorMode.Stage3)
                .WithSourceDocument(document);
            if (IsJsx(path))
                parser.WithJsx(text, JsxParseOptions.Default);
            parsed = parser.Parse();
        }
        catch
        {
            return [];
        }

        if (!parsed.IsSuccess)
            return [];

        var checker = new TypeChecker().WithFilePath(path);
        try
        {
            // Recovery keeps useful bindings from the rest of a file when an unrelated statement
            // has a type error. A failure in checker setup still degrades safely to no definition.
            checker.CheckWithRecovery(parsed.Statements, document);
        }
        catch
        {
            return [];
        }

        int offset = document.Lines.ToOffset(
            (int)position.Line + 1,
            (int)position.Character + 1);
        return checker.Bindings.FindDefinitions(document, offset)
            .Select(ToLocation)
            .ToArray();
    }

    private static Location ToLocation(BindingDeclaration declaration)
    {
        var document = declaration.Document;
        var (startLine, startColumn) = document.Lines.ToPosition(declaration.Name.Start);
        var (endLine, endColumn) = document.Lines.ToPosition(declaration.Name.End);
        return new Location
        {
            Uri = DocumentUri.FromFileSystemPath(document.Path),
            Range = new Range(
                startLine - 1,
                startColumn - 1,
                endLine - 1,
                endColumn - 1),
        };
    }

    private static bool IsJsx(string path) =>
        path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);
}
