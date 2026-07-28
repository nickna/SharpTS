using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.Configuration;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.LanguageServer.Services;

internal sealed record CheckedNavigationModel(
    TypeChecker Checker,
    SourceDocument Document);

/// <summary>
/// Builds the semantic module graph shared by definition, references, and later rename support.
/// </summary>
internal static class NavigationModelBuilder
{
    public static CheckedNavigationModel? TryBuild(
        string path,
        string text,
        IReadOnlyDictionary<string, string>? openDocuments)
    {
        try
        {
            string absolutePath = Path.GetFullPath(path);
            var overlay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (openDocuments is not null)
            {
                foreach (var (documentPath, documentText) in openDocuments)
                    overlay[Path.GetFullPath(documentPath)] = documentText;
            }
            overlay[absolutePath] = text;

            var resolver = new ModuleResolver(
                absolutePath,
                ModuleResolutionOptions.Default,
                overlay,
                new TypeScriptProgramOptions { PreferDeclarationFiles = true },
                virtualFilesFallBackToDisk: true);
            ParsedModule entry = resolver.LoadModule(absolutePath, DecoratorMode.Stage3);
            if (entry.Document is not { } document)
                return null;

            // Load open roots to discover reverse importers, then check only the undirected module
            // component containing the requested file. This includes open importers and forward
            // dependencies without merging globals from unrelated open script files.
            List<ParsedModule> loadedRoots = [entry];
            foreach (string openPath in overlay.Keys.Order(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(openPath, absolutePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    ParsedModule root = resolver.LoadModule(openPath, DecoratorMode.Stage3);
                    if (!loadedRoots.Contains(root))
                        loadedRoots.Add(root);
                }
                catch
                {
                    // The requested root remains authoritative and independently useful.
                }
            }

            List<ParsedModule> loadedModules = resolver.GetModulesInOrder(loadedRoots);
            HashSet<ParsedModule> component = FindConnectedComponent(
                entry,
                loadedModules);
            List<ParsedModule> connectedRoots = loadedRoots
                .Where(component.Contains)
                .ToList();

            var checker = new TypeChecker().WithFilePath(absolutePath);
            checker.CheckModules(
                resolver.GetModulesInOrder(connectedRoots),
                resolver);
            return new CheckedNavigationModel(checker, document);
        }
        catch
        {
            return null;
        }
    }

    private static HashSet<ParsedModule> FindConnectedComponent(
        ParsedModule entry,
        IReadOnlyList<ParsedModule> modules)
    {
        HashSet<ParsedModule> component = [entry];
        Queue<ParsedModule> pending = new([entry]);

        while (pending.TryDequeue(out var current))
        {
            foreach (var adjacent in current.Dependencies.Concat(current.ReferencedScripts))
            {
                if (component.Add(adjacent))
                    pending.Enqueue(adjacent);
            }

            foreach (var candidate in modules)
            {
                if ((candidate.Dependencies.Contains(current) ||
                     candidate.ReferencedScripts.Contains(current)) &&
                    component.Add(candidate))
                {
                    pending.Enqueue(candidate);
                }
            }
        }

        return component;
    }
}

internal static class NavigationLocations
{
    public static Location From(SourceDocument document, Token token)
    {
        var (startLine, startColumn) = document.Lines.ToPosition(token.Start);
        var (endLine, endColumn) = document.Lines.ToPosition(token.End);
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
