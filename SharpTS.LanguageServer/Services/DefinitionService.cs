using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.TypeSystem;

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
        if (NavigationModelBuilder.TryBuild(path, text, openDocuments) is not { } model)
            return [];

        int offset = model.Document.Lines.ToOffset(
            (int)position.Line + 1,
            (int)position.Character + 1);
        IReadOnlyList<BindingDeclaration> declarations =
            model.Checker.Bindings.FindDefinitions(
                model.Document,
                offset,
                BindingNamespace.Value);
        if (declarations.Count == 0)
        {
            declarations =
                model.Checker.Bindings.FindDefinitions(
                    model.Document,
                    offset,
                    BindingNamespace.Type);
        }
        return declarations
            .Select(declaration =>
                NavigationLocations.From(declaration.Document, declaration.Name))
            .ToArray();
    }
}
