using SharpTS.LanguageServer.Conversions;
using SharpTS.Parsing;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;

namespace SharpTS.LanguageServer.Services;

/// <summary>
/// Produces the LSP diagnostics for a document. Phase 1 = the @DotNetType interop analyzer
/// (the tsserver-impossible value). Parse errors are left to the built-in TypeScript server.
/// </summary>
public sealed class DiagnosticsService
{
    private readonly InteropAnalyzer _interop;

    /// <param name="resolve">CLR type resolver. Null = in-process registry (BCL only);
    /// the server injects an AssemblyReferenceLoader when a project/references are configured
    /// so the user's own @DotNetType targets resolve too.</param>
    public DiagnosticsService(Func<string, Type?>? resolve = null) => _interop = new InteropAnalyzer(resolve);

    public List<LspDiagnostic> Analyze(string text, DiagnosticPublishMode mode = DiagnosticPublishMode.SharpTsOnly, string? fileName = null)
    {
        // Stage3 decorators are the run-mode default and are required for @DotNetType to parse.
        var tokens = new Lexer(text).ScanTokens();
        var parser = new Parser(tokens, DecoratorMode.Stage3);
        if (fileName is not null &&
            (fileName.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
             fileName.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase)))
        {
            parser.WithJsx(text, JsxParseOptions.Default);
        }
        var parsed = parser.Parse();
        if (!parsed.IsSuccess)
            return new List<LspDiagnostic>(); // syntax errors belong to tsserver

        var diags = _interop.Analyze(parsed.Statements, new PositionMap(text));
        return LspConversions.ToLsp(diags, text, mode);
    }
}
