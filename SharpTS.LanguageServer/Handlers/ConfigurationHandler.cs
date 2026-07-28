using MediatR;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using SharpTS.LanguageServer.Conversions;
using SharpTS.LanguageServer.Services;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>Applies live <c>sharpts.diagnostics</c> changes and republishes open files.</summary>
public sealed class ConfigurationHandler : DidChangeConfigurationHandlerBase
{
    private readonly DiagnosticsSettings _settings;
    private readonly DiagnosticsCoordinator _diagnostics;

    public ConfigurationHandler(
        DiagnosticsSettings settings,
        DiagnosticsCoordinator diagnostics)
    {
        _settings = settings;
        _diagnostics = diagnostics;
    }

    public override Task<Unit> Handle(
        DidChangeConfigurationParams request,
        CancellationToken cancellationToken)
    {
        string? configured = FindDiagnosticsValue(request.Settings);
        if (DiagnosticsSettings.TryParse(
                configured,
                out DiagnosticPublishMode mode) &&
            mode != _settings.Mode)
        {
            _settings.Mode = mode;
            _diagnostics.RepublishAll();
        }

        return Unit.Task;
    }

    internal static string? FindDiagnosticsValue(JToken? settings) =>
        settings?.SelectToken("sharpts.diagnostics")?.Value<string>() ??
        settings?["diagnostics"]?.Value<string>();
}
