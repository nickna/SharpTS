using SharpTS.IO;
using SharpTS.LanguageServer.Project;

namespace SharpTS.LanguageServer.Services;

/// <summary>Captures interop/GUI inputs without starting general semantic analysis.</summary>
internal sealed class EditorRequestGuard : IDisposable
{
    private readonly ObservingCompilerFileSystem _inputs = new();
    private readonly IDisposable _fileScope;
    private readonly AnalysisMetadataProvider? _metadata;
    private readonly AnalysisMetadataView? _view;
    private readonly IDisposable? _metadataScope;
    private readonly DocumentStore _store;
    private readonly DocumentRequestSnapshot _request;

    public EditorRequestGuard(DocumentStore store, DocumentRequestSnapshot request,
        AnalysisMetadataProvider? metadata, CancellationToken cancellationToken)
    {
        _store = store; _request = request; _metadata = metadata;
        _fileScope = CompilerFileSystem.Use(_inputs, cancellationToken);
        try
        {
            _view = metadata?.Capture();
            _metadataScope = _view?.EnterScope();
        }
        catch { _view?.Dispose(); _fileScope.Dispose(); throw; }
    }

    public bool IsCurrent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_inputs.Complete().IsCurrent(cancellationToken)) return false;
        using var physicalScope = CompilerFileSystem.Use(CompilerFileSystem.Physical, cancellationToken);
        if (_metadata is not null && _metadata.RefreshGeneration() != _view!.Generation) return false;
        return _store.IsCurrent(_request.Document.Uri, _request.Document.Version, _request.WorkspaceVersion);
    }

    public void Dispose()
    {
        _metadataScope?.Dispose();
        _view?.Dispose();
        _fileScope.Dispose();
    }
}
