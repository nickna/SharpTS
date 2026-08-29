using System.Diagnostics;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpTS.Parsing;
using SharpTS.References;

namespace SharpTS.Cli;

internal static class GuiApplicationCli
{
    internal const string DefaultSdkVersion = GuiVersion.Value;

    public static int Create(ParsedCommand.NewDesktop command)
    {
        string root = Path.GetFullPath(command.OutputDirectory);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidOperationException($"Output directory is not empty: {root}");
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        string serializedVersion = QuoteJsonString(command.GuiSdkVersion);
        string serializedName = QuoteJsonString(command.Name);
        File.WriteAllText(Path.Combine(root, "sharpts.json"), $$"""
            {
              "application": {
                "type": "desktop",
                "host": "avalonia",
                "entry": "main.tsx",
                "guiSdkVersion": {{serializedVersion}}
              }
            }
            """);
        File.WriteAllText(Path.Combine(root, "tsconfig.json"), """
            {
              "compilerOptions": { "strict": true, "target": "ES2022", "module": "ESNext" }
            }
            """);
        File.WriteAllText(Path.Combine(root, "main.tsx"), $$"""
            import { Button, StackPanel, TextBlock, Window, createDesktopApplication, useState } from "@sharpts/gui";

            function App() {
                const [count, setCount] = useState(0);
                return <Window title={{{serializedName}}} width={420} height={240}>
                    <StackPanel spacing={12} margin={24}>
                        <TextBlock fontSize={24}>{{{serializedName}}}</TextBlock>
                        <TextBlock key="count">{`Count: ${count}`}</TextBlock>
                        <Button key="increment" onClick={() => setCount(value => value + 1)}>Increment</Button>
                    </StackPanel>
                </Window>;
            }
            const application = createDesktopApplication();
            application.createWindow(<App />, { main: true });
            """);
        File.WriteAllText(Path.Combine(root, "headless.tests.tsx"), """
            import { TextBlock, Window, createDesktopApplication } from "@sharpts/gui";
            import { createDesktopTestDriver } from "@sharpts/gui/testing";
            const application = createDesktopApplication();
            const window = application.createWindow(<Window title="Headless" width={320} height={160}>
                <TextBlock key="message">CLI Headless test</TextBlock>
            </Window>, { main: true });
            const driver = createDesktopTestDriver(window);
            if (driver.getText("message") !== "CLI Headless test") throw new Error("CLI Headless assertion failed.");
            setTimeout((() => application.dispose()) as any, 0);
            """);
        File.WriteAllText(Path.Combine(root, "Assets", "README.txt"),
            "Files in this directory are embedded under asset:/// paths." + Environment.NewLine);
        Console.WriteLine($"Created SharpTS desktop application '{command.Name}' in {root}");
        return 0;
    }

    private static string QuoteJsonString(string value) =>
        $"\"{JsonEncodedText.Encode(value)}\"";

    public static int Run(ParsedCommand.Application command)
    {
        string start = Environment.CurrentDirectory;
        SharpTsManifest? manifest = SharpTsManifestLoader.FindAndLoad(start);
        string root = manifest?.ManifestDirectory ?? start;
        string entry = ResolveEntry(root, command.Entry ?? manifest?.Application?.Entry);
        string host = ResolveHost(command.Host, manifest?.Application?.Host, entry);
        ValidateApplicationType(manifest?.Application?.Type, host);
        if (host == "console")
        {
            if (command.Action != "run")
                throw new InvalidOperationException(
                    "Console application build/publish uses 'sharpts --compile'; use '--host avalonia' for the SDK desktop host.");
            return SharpTSCli.Run([entry, .. command.ApplicationArgs]);
        }
        if (host != "avalonia")
            throw new InvalidOperationException($"Unknown application host '{host}'; expected avalonia or console.");

        string version = command.GuiSdkVersion ?? manifest?.Application?.GuiSdkVersion ?? DefaultSdkVersion;
        string? source = command.GuiSdkSource ?? manifest?.Application?.GuiSdkSource;
        string project = MaterializeProject(root, entry, version, command.GcProfile);
        var restore = new List<string> { "restore", project };
        AddGeneratedProjectProperties(restore, root);
        if (!string.IsNullOrWhiteSpace(command.RuntimeIdentifier))
        {
            restore.Add("-r");
            restore.Add(command.RuntimeIdentifier);
        }
        if (!string.IsNullOrWhiteSpace(source))
        {
            restore.Add("--source");
            restore.Add(ResolvePackageSource(source, root));
            restore.Add("--source");
            restore.Add("https://api.nuget.org/v3/index.json");
        }
        int restored = InvokeDotNet(restore, root);
        if (restored != 0) return restored;

        return command.Action switch
        {
            "run" => RunApplication(command, project, root),
            "build" => BuildApplication(command, project, root),
            "publish" => PublishApplication(command, project, root),
            _ => throw new InvalidOperationException($"Unknown application action '{command.Action}'.")
        };
    }

    private static int BuildApplication(ParsedCommand.Application command, string project, string root)
    {
        var args = new List<string> { "build", project, "-c", command.Configuration, "--no-restore" };
        AddGeneratedProjectProperties(args, root);
        return InvokeDotNet(args, root);
    }

    private static int RunApplication(ParsedCommand.Application command, string project, string root)
    {
        var args = new List<string> { "run", "--project", project, "-c", command.Configuration, "--no-restore" };
        AddGeneratedProjectProperties(args, root);
        args.Add("--");
        if (!string.IsNullOrWhiteSpace(command.Mode))
        {
            args.Add("--mode");
            args.Add(command.Mode);
        }
        args.AddRange(command.ApplicationArgs);
        return InvokeDotNet(args, root);
    }

    private static int PublishApplication(ParsedCommand.Application command, string project, string root)
    {
        string rid = command.RuntimeIdentifier ?? "win-x64";
        bool singleFile = command.SingleFile ?? true;
        bool selfContained = command.SelfContained ?? singleFile;
        if (singleFile && !selfContained)
            throw new InvalidOperationException("--single-file true cannot be combined with --self-contained false.");
        string output = Path.GetFullPath(command.OutputDirectory ?? Path.Combine("dist", rid), root);
        var args = new List<string>
        {
            "publish", project, "-c", command.Configuration, "-r", rid,
            "--self-contained", selfContained.ToString().ToLowerInvariant(), "--no-restore",
            $"-p:SharpTSGuiPublishMode={(singleFile ? "SingleFile" : "Directory")}",
            $"-p:SharpTSGuiIncludeSourcePayload={(!singleFile).ToString().ToLowerInvariant()}",
            "-o", output
        };
        AddGeneratedProjectProperties(args, root);
        return InvokeDotNet(args, root);
    }

    internal static string MaterializeProject(
        string root,
        string entry,
        string version,
        GcProfile gcProfile = GcProfile.Workstation)
    {
        string relativeEntry = Path.GetRelativePath(root, entry).Replace('/', '\\');
        if (relativeEntry.StartsWith("..", StringComparison.Ordinal))
            throw new InvalidOperationException("The application entry must be inside the project directory.");
        string assemblyName = Regex.Replace(new DirectoryInfo(root).Name, "[^A-Za-z0-9_.-]", "_");
        if (string.IsNullOrWhiteSpace(assemblyName)) assemblyName = "SharpTSGuiApp";
        string project = Path.Combine(root, ".sharpts-gui.generated.csproj");
        string xml = $$"""
            <Project Sdk="SharpTS.Gui.Sdk/{{SecurityElement.Escape(version)}}">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>{{SecurityElement.Escape(assemblyName)}}</AssemblyName>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                <SharpTSEntryPoint>{{SecurityElement.Escape(relativeEntry)}}</SharpTSEntryPoint>
                <SharpTSTsConfigPath Condition="Exists('$(MSBuildProjectDirectory)\tsconfig.json')">$(MSBuildProjectDirectory)\tsconfig.json</SharpTSTsConfigPath>
                <SharpTSVerifyIL>true</SharpTSVerifyIL>
                <SharpTSGcProfile>{{GcProfileSettings.CliValue(gcProfile)}}</SharpTSGcProfile>
              </PropertyGroup>
            </Project>
            """;
        if (!File.Exists(project) || File.ReadAllText(project) != xml)
            File.WriteAllText(project, xml);
        return project;
    }

    private static string ResolveEntry(string root, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            string explicitPath = Path.GetFullPath(requested, root);
            if (!File.Exists(explicitPath)) throw new FileNotFoundException($"Application entry not found: {explicitPath}");
            return explicitPath;
        }
        foreach (string candidate in new[] { "main.tsx", "main.ts" })
        {
            string path = Path.Combine(root, candidate);
            if (File.Exists(path)) return path;
        }
        string[] sources = Directory.GetFiles(root, "*.ts*", SearchOption.TopDirectoryOnly);
        if (sources.Length == 1) return sources[0];
        throw new InvalidOperationException(
            sources.Length == 0 ? "No application entry was found." :
            "Application entry inference is ambiguous; pass an entry path or set application.entry in sharpts.json.");
    }

    internal static string ResolveHost(string? explicitHost, string? manifestHost, string entry)
    {
        string? selected = !string.IsNullOrWhiteSpace(explicitHost) ? explicitHost : manifestHost;
        if (!string.IsNullOrWhiteSpace(selected)) return selected.Trim().ToLowerInvariant();
        string source = File.ReadAllText(entry);
        (bool guiImport, bool otherJsxRuntime) = InspectRuntimeImports(source);
        if (guiImport && otherJsxRuntime)
            throw new InvalidOperationException(
                "Application host inference is ambiguous; pass --host avalonia or --host console.");
        return guiImport ? "avalonia" : "console";
    }

    internal static void ValidateApplicationType(string? applicationType, string host)
    {
        if (string.IsNullOrWhiteSpace(applicationType)) return;
        string normalizedType = applicationType.Trim().ToLowerInvariant();
        if (normalizedType != "desktop")
            throw new InvalidOperationException(
                $"Unknown application type '{normalizedType}'; expected desktop.");
        if (host != "avalonia")
            throw new InvalidOperationException(
                $"Application type 'desktop' requires the avalonia host, not '{host}'.");
    }

    private static (bool GuiImport, bool OtherJsxRuntime) InspectRuntimeImports(string source)
    {
        try
        {
            var lexer = new Lexer(source) { JsxTolerant = true };
            List<Token> tokens = lexer.ScanTokens();
            JsxParseOptions jsx = (JsxParseOptions.Default with { ImportSource = "@sharpts/gui" })
                .ApplyPragmas(lexer.Pragmas);
            var parsed = new Parser(tokens).WithJsx(source, jsx).Parse();
            string[] modules = parsed.Statements
                .OfType<Stmt.Import>()
                .Where(import => !import.IsSynthesizedJsxRuntime && HasRuntimeImport(import))
                .Select(import => import.ModulePath)
                .ToArray();
            bool gui = modules.Any(IsGuiModule);
            bool other = modules.Any(IsOtherJsxRuntime) ||
                lexer.Pragmas.JsxImportSource is { } pragma && !IsGuiModule(pragma);
            return (gui, other);
        }
        catch
        {
            // Inference must never reinterpret malformed or lexically ambiguous input as GUI.
            // The console default will report the source diagnostic; --host or the manifest can
            // still select Avalonia explicitly.
            return (false, false);
        }
    }

    private static bool HasRuntimeImport(Stmt.Import import) =>
        !import.IsTypeOnly &&
        (import.NamedImports is null || import.NamedImports.Any(specifier => !specifier.IsTypeOnly) ||
         import.DefaultImport is not null || import.NamespaceImport is not null);

    private static bool IsGuiModule(string module) =>
        module == "@sharpts/gui" || module.StartsWith("@sharpts/gui/", StringComparison.Ordinal);

    private static bool IsOtherJsxRuntime(string module)
    {
        foreach (string package in new[] { "react", "react-dom", "preact", "solid-js" })
        {
            if (module == package || module.StartsWith(package + "/", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    internal static string ResolvePackageSource(string source, string root)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) &&
            uri.Scheme is "http" or "https" or "file")
            return source;
        return Path.GetFullPath(source, root);
    }

    private static void AddGeneratedProjectProperties(List<string> arguments, string root)
    {
        arguments.Add($"-p:BaseIntermediateOutputPath={Path.Combine(root, ".sharpts", "gui", "obj")}{Path.DirectorySeparatorChar}");
        arguments.Add($"-p:BaseOutputPath={Path.Combine(root, ".sharpts", "gui", "bin")}{Path.DirectorySeparatorChar}");
    }

    private static int InvokeDotNet(IEnumerable<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the .NET SDK.");
        process.WaitForExit();
        return process.ExitCode;
    }
}
