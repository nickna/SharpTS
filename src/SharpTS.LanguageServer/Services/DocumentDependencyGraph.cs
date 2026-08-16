using SharpTS.Modules;
using SharpTS.Parsing;

namespace SharpTS.LanguageServer.Services;

/// <summary>
/// Forward and reverse edges for open source documents. Updating a dependency returns the
/// transitive open-importer set that must be checked and republished.
/// </summary>
public sealed class DocumentDependencyGraph
{
    private readonly object _gate = new();
    private readonly Dictionary<string, HashSet<string>> _forward =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _reverse =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> Update(
        DocumentSnapshot document,
        IReadOnlyDictionary<string, string> overlay,
        IReadOnlyList<Stmt> statements,
        CancellationToken cancellationToken)
    {
        if (document.FilePath is null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string path = document.FilePath;
        HashSet<string> dependencies = ResolveDependencies(
            path,
            overlay,
            statements,
            cancellationToken);

        lock (_gate)
        {
            HashSet<string> affected = CollectImporters(path);
            RemoveForwardEdges(path);

            _forward[path] = dependencies;
            foreach (string dependency in dependencies)
            {
                if (!_reverse.TryGetValue(dependency, out HashSet<string>? importers))
                {
                    importers = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);
                    _reverse[dependency] = importers;
                }
                importers.Add(path);
            }

            affected.UnionWith(CollectImporters(path));
            affected.Add(path);
            return affected;
        }
    }

    public IReadOnlySet<string> Remove(string path)
    {
        path = Path.GetFullPath(path);
        lock (_gate)
        {
            HashSet<string> affected = CollectImporters(path);
            RemoveForwardEdges(path);
            _forward.Remove(path);
            affected.Add(path);
            return affected;
        }
    }

    internal IReadOnlySet<string> GetImporters(string path)
    {
        path = Path.GetFullPath(path);
        lock (_gate)
            return CollectImporters(path);
    }

    private HashSet<string> CollectImporters(string path)
    {
        var result = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase) { path };
        Queue<string> pending = new([path]);
        while (pending.TryDequeue(out string? current))
        {
            if (!_reverse.TryGetValue(current, out HashSet<string>? importers))
                continue;

            foreach (string importer in importers)
            {
                if (result.Add(importer))
                    pending.Enqueue(importer);
            }
        }
        return result;
    }

    private void RemoveForwardEdges(string path)
    {
        if (!_forward.TryGetValue(path, out HashSet<string>? oldDependencies))
            return;

        foreach (string dependency in oldDependencies)
        {
            if (!_reverse.TryGetValue(dependency, out HashSet<string>? importers))
                continue;

            importers.Remove(path);
            if (importers.Count == 0)
                _reverse.Remove(dependency);
        }
    }

    private static HashSet<string> ResolveDependencies(
        string path,
        IReadOnlyDictionary<string, string> overlay,
        IReadOnlyList<Stmt> statements,
        CancellationToken cancellationToken)
    {
        var dependencies = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var resolver = new ModuleResolver(path, overlay, fallBackToFileSystem: true);

        foreach ((string Specifier, ResolutionKind Kind) dependency in
                 EnumerateSpecifiers(statements))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string resolved = resolver.ResolveModulePath(
                    dependency.Specifier,
                    path,
                    dependency.Kind);
                if (Path.IsPathRooted(resolved))
                    dependencies.Add(Path.GetFullPath(resolved));
            }
            catch
            {
                // An unresolved edge cannot participate in invalidation yet. The importing
                // document itself is still returned as affected and will be checked.
            }
        }

        return dependencies;
    }

    private static IEnumerable<(string Specifier, ResolutionKind Kind)>
        EnumerateSpecifiers(IEnumerable<Stmt> statements)
    {
        foreach (Stmt statement in statements)
        {
            switch (statement)
            {
                case Stmt.Import import:
                    yield return (import.ModulePath, ResolutionKind.Esm);
                    break;
                case Stmt.Export { FromModulePath: { } from }:
                    yield return (from, ResolutionKind.Esm);
                    break;
                case Stmt.ImportRequire import:
                    yield return (import.ModulePath, ResolutionKind.Cjs);
                    break;
            }
        }
    }
}
