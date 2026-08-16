using SharpTS.LanguageServer.Conversions;

namespace SharpTS.LanguageServer.Services;

/// <summary>Thread-safe diagnostic publication mode for the lifetime of an LSP server.</summary>
public sealed class DiagnosticsSettings
{
    private int _mode;

    public DiagnosticsSettings(
        DiagnosticPublishMode mode = DiagnosticPublishMode.SharpTsOnly)
    {
        _mode = (int)mode;
    }

    public DiagnosticPublishMode Mode
    {
        get => (DiagnosticPublishMode)Volatile.Read(ref _mode);
        set => Volatile.Write(ref _mode, (int)value);
    }

    public static bool TryParse(
        string? value,
        out DiagnosticPublishMode mode)
    {
        mode = value switch
        {
            "sharpts-only" => DiagnosticPublishMode.SharpTsOnly,
            "all" => DiagnosticPublishMode.All,
            "off" => DiagnosticPublishMode.Off,
            _ => default,
        };
        return value is "sharpts-only" or "all" or "off";
    }
}
