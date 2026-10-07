using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Conversions;
using SharpTS.LanguageServer.Project;

// Entry point for the `sharpts-lsp` tool: this executable *is* the language server
// (LSP over stdio). Parses the same assembly-reference options the old `sharpts lsp`
// command did, then hands off to the server host.
string? projectFile = null, sdkPath = null;
var references = new List<string>();

// Standalone clients have no other TypeScript server, so navigation is on by default here. The
// VS Code extension passes interop-only, where tsserver already provides it.
var mode = LanguageFeatureMode.Full;
var diagnosticMode = DiagnosticPublishMode.SharpTsOnly;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--project" when i + 1 < args.Length: projectFile = args[++i]; break;
        case "--sdk-path" when i + 1 < args.Length: sdkPath = args[++i]; break;
        case "-r" or "--reference" when i + 1 < args.Length: references.Add(args[++i]); break;
        case "--language-features" when i + 1 < args.Length:
            string requested = args[++i];
            switch (requested)
            {
                case "interop-only": mode = LanguageFeatureMode.InteropOnly; break;
                case "full": mode = LanguageFeatureMode.Full; break;
                default:
                    await Console.Error.WriteLineAsync(
                        $"[LSP Fatal] Unknown --language-features value '{requested}'. Expected 'interop-only' or 'full'.");
                    Environment.Exit(64);
                    break;
            }
            break;
        case "--diagnostics" when i + 1 < args.Length:
            string requestedDiagnostics = args[++i];
            if (!SharpTS.LanguageServer.Services.DiagnosticsSettings.TryParse(
                    requestedDiagnostics,
                    out diagnosticMode))
            {
                await Console.Error.WriteLineAsync(
                    $"[LSP Fatal] Unknown --diagnostics value '{requestedDiagnostics}'. " +
                    "Expected 'sharpts-only', 'all', or 'off'.");
                Environment.Exit(64);
            }
            break;
    }
}

try
{
    // Acquire manifest packages once at startup, preserving the existing startup behavior.
    // All later configuration validation and metadata captures are read-only: they inspect
    // already restored assets and never launch restore during editor requests.
    try
    {
        SharpTS.References.DotNetReferences.Resolve(Environment.CurrentDirectory, []);
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[LSP] sharpts.json ignored: {ex.Message}");
    }

    using var metadata = new AnalysisMetadataProvider(
        projectFile,
        references,
        sdkPath,
        Environment.CurrentDirectory,
        message => Console.Error.WriteLine($"[LSP] {message}"));

    await SharpTSLanguageServer.RunAsync(
        metadata.Resolve,
        metadata.GetTypeNames,
        mode,
        diagnosticMode,
        metadataProvider: metadata);
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"[LSP Fatal] {ex.Message}");
    Environment.Exit(1);
}
