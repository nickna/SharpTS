using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.Configuration;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.LanguageServer.Services;

/// <summary>
/// Resolves source positions to checker-bound declarations across a module graph.
/// </summary>
/// <remarks>
/// The service intentionally returns no result for names the checker did not bind. In particular,
/// property/member navigation still needs its own complete semantic domain; guessing from matching
/// text would produce incorrect navigation under shadowing.
/// </remarks>
public sealed class DefinitionService
{
    /// <summary>
    /// Finds the value or type declaration selected by an LSP position.
    /// </summary>
    public IReadOnlyList<Location> FindDefinitions(
        string path,
        string text,
        Position position,
        IReadOnlyDictionary<string, string>? openDocuments = null)
    {
        string absolutePath = Path.GetFullPath(path);
        var overlay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (openDocuments is not null)
        {
            foreach (var (documentPath, documentText) in openDocuments)
                overlay[Path.GetFullPath(documentPath)] = documentText;
        }
        overlay[absolutePath] = text;

        ParsedModule entry;
        ModuleResolver resolver;
        try
        {
            resolver = new ModuleResolver(
                absolutePath,
                ModuleResolutionOptions.Default,
                overlay,
                new TypeScriptProgramOptions { PreferDeclarationFiles = true },
                virtualFilesFallBackToDisk: true);
            entry = resolver.LoadModule(absolutePath, DecoratorMode.Stage3);
        }
        catch
        {
            return [];
        }

        if (entry.Document is not { } document)
            return [];

        var checker = new TypeChecker().WithFilePath(absolutePath);
        try
        {
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        }
        catch
        {
            return [];
        }

        int offset = document.Lines.ToOffset(
            (int)position.Line + 1,
            (int)position.Character + 1);
        IReadOnlyList<BindingDeclaration> declarations =
            checker.Bindings.FindDefinitions(document, offset, BindingNamespace.Value);
        if (declarations.Count == 0)
        {
            declarations =
                checker.Bindings.FindDefinitions(document, offset, BindingNamespace.Type);
        }
        return declarations
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
}
