using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace SharpTS.LanguageServer.Services;

/// <summary>
/// Resolves a source position to every checker-bound occurrence of the same semantic symbol.
/// </summary>
public sealed class ReferenceService
{
    public IReadOnlyList<Location> FindReferences(
        string path,
        string text,
        Position position,
        bool includeDeclaration,
        IReadOnlyDictionary<string, string>? openDocuments = null)
    {
        if (NavigationModelBuilder.TryBuild(path, text, openDocuments) is not { } model)
            return [];

        int offset = model.Document.Lines.ToOffset(
            (int)position.Line + 1,
            (int)position.Character + 1);
        return model.Checker.Bindings
            .FindReferences(model.Document, offset, includeDeclaration)
            .Select(occurrence =>
                NavigationLocations.From(occurrence.Document, occurrence.Name))
            .ToArray();
    }
}
