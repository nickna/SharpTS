using SharpTS.Compilation;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Project;

// Entry point for the `sharpts-lsp` tool: this executable *is* the language server
// (LSP over stdio). Parses the same assembly-reference options the old `sharpts lsp`
// command did, then hands off to the server host.
string? projectFile = null, sdkPath = null;
var references = new List<string>();

// Standalone clients have no other TypeScript server, so navigation is on by default here. The
// VS Code extension passes interop-only, where tsserver already provides it.
var mode = LanguageFeatureMode.Full;

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
    }
}

try
{
    // Resolve @DotNetType targets against the project's referenced assemblies (via
    // MetadataLoadContext). With no project/refs the loader still resolves the BCL.
    var paths = new List<string>(references);
    if (projectFile != null && File.Exists(projectFile))
        paths.AddRange(CsprojParser.Parse(projectFile));

    // sharpts.json (walked up from the workspace root = CWD, matching the CLI's
    // discovery from the entry script) supplies more paths for the SAME loader:
    // Resolve, not Load — the editor process inspects workspace assemblies through
    // the MetadataLoadContext and never executes their code. Read once at startup,
    // like --project. A broken manifest must not kill the server: log and continue.
    try
    {
        var refSet = SharpTS.References.DotNetReferences.Resolve(Environment.CurrentDirectory, []);
        paths.AddRange(refSet.References.Select(r => r.Path));
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"[LSP] sharpts.json ignored: {ex.Message}");
    }

    using var loader = new AssemblyReferenceLoader(paths, sdkPath);
    Func<IEnumerable<string>> typeNames = () => loader.GetAllPublicTypes()
        .Select(t => t.FullName)
        .Where(n => !string.IsNullOrEmpty(n))
        .Cast<string>();

    await SharpTSLanguageServer.RunAsync(loader.TryResolve, typeNames, mode);
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"[LSP Fatal] {ex.Message}");
    Environment.Exit(1);
}
