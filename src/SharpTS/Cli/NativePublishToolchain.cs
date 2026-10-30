using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Cli;

/// <summary>Pins the selected SDK and fingerprints its resolved publish inputs.</summary>
internal sealed record NativePublishToolchain(string DotnetPath, string SdkVersion, string SdkDirectory)
{
    internal static async Task<NativePublishToolchain> ResolveAsync(string workingDirectory, CancellationToken token)
    {
        string? dotnet = FindInPath(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        if (dotnet is null) throw new CompileException(DiagnosticCode.NativeGuestToolchain, "Native output requires the .NET SDK and native compiler/linker prerequisites. Install dotnet on PATH or use --target dll.");
        var version = await RunCapture(dotnet, workingDirectory, token, "--version").ConfigureAwait(false);
        string selected = version.Output.Trim();
        if (version.ExitCode != 0 || !System.Text.RegularExpressions.Regex.IsMatch(selected, @"^\d+\.\d+\.\d+[\w.\-]*$"))
            throw new CompileException(DiagnosticCode.NativeGuestToolchain, "Could not resolve the .NET SDK selected by this source directory and its global.json policy. " + version.Error.Trim());
        var sdks = await RunCapture(dotnet, workingDirectory, token, "--list-sdks").ConfigureAwait(false);
        string prefix = selected + " [";
        string? line = sdks.Output.Split('\n').Select(value => value.Trim()).FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith(']'));
        string? directory = line is null ? null : Path.Combine(line[prefix.Length..^1], selected);
        if (directory is null || !Directory.Exists(directory))
            throw new CompileException(DiagnosticCode.NativeGuestToolchain, "The selected .NET SDK directory could not be identified.");
        return new(dotnet, selected, Path.GetFullPath(directory));
    }

    internal void PinSdk(string projectDirectory)
    {
        using var stream = new FileStream(Path.Combine(projectDirectory, "global.json"), FileMode.CreateNew);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject(); writer.WriteStartObject("sdk"); writer.WriteString("version", SdkVersion);
        writer.WriteString("rollForward", "disable"); writer.WriteBoolean("allowPrerelease", true); writer.WriteEndObject(); writer.WriteEndObject();
    }

    internal ProcessStartInfo Start(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo(DotnetPath) { WorkingDirectory = directory };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    internal async Task<(string Fingerprint, bool CanReuse)> FingerprintAsync(NativePublishWorkspace workspace, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Text(string value) { hash.AppendData(Encoding.UTF8.GetBytes(value)); hash.AppendData([0]); }
        void FileInput(string path)
        {
            token.ThrowIfCancellationRequested();
            Text(Path.GetFullPath(path)); using var stream = File.OpenRead(path); hash.AppendData(SHA256.HashData(stream));
        }
        void DirectoryInputs(string path, Func<string, bool>? include = null)
        {
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(file => include?.Invoke(file) ?? true).Order(StringComparer.Ordinal))
                FileInput(file);
        }
        Text("SharpTS.NativeToolchain.v1"); Text(SdkVersion); FileInput(DotnetPath);
        FileInput(Path.Combine(SdkDirectory, "dotnet.dll"));
        DirectoryInputs(Path.Combine(SdkDirectory, "Sdks", "Microsoft.NET.Sdk"));
        DirectoryInputs(Path.Combine(SdkDirectory, "Roslyn", "bincore"));
        // These values can change SDK and native tool selection. Hash values, never persist them.
        foreach (string key in new[] { "PATH", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64", "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR",
            "LIB", "LIBPATH", "INCLUDE", "VSINSTALLDIR", "VCINSTALLDIR", "VCToolsInstallDir", "VCToolsVersion", "WindowsSdkDir", "WindowsSDKVersion", "UniversalCRTSdkDir", "UCRTVersion", "CC", "CXX", "LD", "AR", "SDKROOT", "DEVELOPER_DIR" })
        { Text(key); Text(Environment.GetEnvironmentVariable(key) ?? ""); }
        if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
            Text(typeof(NativePublishToolchain).Module.ModuleVersionId.ToString("D"));
        string managedCompiler = Path.Combine(AppContext.BaseDirectory, "SharpTS.dll");
        string compiler = System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported && File.Exists(managedCompiler)
            ? managedCompiler : Environment.ProcessPath!;
        FileInput(compiler);
        string assets = Path.Combine(workspace.ProjectDirectory, "obj", "project.assets.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(assets));
        // Attempt-specific paths carry no build meaning; all other restore data participates.
        Text(CanonicalJson(document.RootElement, workspace.DirectoryPath));
        string[] packageFolders = document.RootElement.GetProperty("packageFolders").EnumerateObject().Select(property => property.Name).ToArray();
        foreach (var library in document.RootElement.GetProperty("libraries").EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            if (library.Value.GetProperty("type").GetString() != "package") continue;
            string relative = library.Value.GetProperty("path").GetString()!;
            string? package = packageFolders.Select(folder => Path.Combine(folder, relative)).FirstOrDefault(Directory.Exists);
            if (package is null) throw new IOException("A resolved native publish package is missing.");
            foreach (var item in library.Value.GetProperty("files").EnumerateArray())
                FileInput(Path.Combine(package, item.GetString()!));
        }
        bool nativeKnown = false;
        if (OperatingSystem.IsWindows())
        {
            string vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
            if (File.Exists(vswhere))
            {
                FileInput(vswhere);
                var installations = await RunCapture(vswhere, workspace.ProjectDirectory, token, "-all", "-prerelease", "-products", "*", "-format", "json", "-utf8").ConfigureAwait(false);
                if (installations.ExitCode == 0)
                {
                    using var instances = JsonDocument.Parse(installations.Output);
                    Text(CanonicalJson(instances.RootElement, ""));
                    string architecture = workspace.Options.RuntimeIdentifier.EndsWith("arm64", StringComparison.Ordinal) ? "arm64" : "x64";
                    string host = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "Hostarm64" : "Hostx64";
                    bool linker = false, runtimeLibraries = false;
                    foreach (var instance in instances.RootElement.EnumerateArray())
                    {
                        string root = Path.Combine(instance.GetProperty("installationPath").GetString()!, "VC", "Tools", "MSVC");
                        if (!Directory.Exists(root)) continue;
                        // Cover the finite candidate search directories, not the VS installation.
                        foreach (string version in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
                        {
                            Text(version);
                            string binaries = Path.Combine(version, "bin", host, architecture);
                            if (File.Exists(Path.Combine(binaries, "link.exe"))) { DirectoryInputs(binaries); linker = true; }
                            foreach (string libraries in new[] { Path.Combine(version, "lib", architecture), Path.Combine(version, "ATLMFC", "lib", architecture) })
                                if (Directory.Exists(libraries)) DirectoryInputs(libraries);
                        }
                    }
                    string kits = Environment.GetEnvironmentVariable("WindowsSdkDir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10");
                    string libRoot = Path.Combine(kits, "Lib");
                    if (Directory.Exists(libRoot))
                        foreach (string version in Directory.EnumerateDirectories(libRoot).Order(StringComparer.Ordinal))
                            foreach (string kind in new[] { "um", "ucrt" })
                            {
                                string libraries = Path.Combine(version, kind, architecture);
                                if (!Directory.Exists(libraries)) continue;
                                DirectoryInputs(libraries); runtimeLibraries = true;
                            }
                    string netfx = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "NETFXSDK");
                    if (Directory.Exists(netfx))
                        foreach (string version in Directory.EnumerateDirectories(netfx).Order(StringComparer.Ordinal))
                        {
                            string libraries = Path.Combine(version, "lib", "um", architecture);
                            if (Directory.Exists(libraries)) DirectoryInputs(libraries);
                        }
                    foreach (string path in (Environment.GetEnvironmentVariable("LIB") ?? "").Split(Path.PathSeparator).Where(Directory.Exists)) DirectoryInputs(path);
                    nativeKnown = linker && runtimeLibraries;
                }
            }
        }
        // Unidentified toolchains still publish; they cannot reuse a final binary.
        Text(nativeKnown ? "identified-native-inputs" : "unidentified-native-inputs");
        return (Convert.ToHexStringLower(hash.GetHashAndReset()), nativeKnown);
    }

    internal static string CanonicalJson(JsonElement element, string ownedRoot)
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) Write(element, writer);
        return Encoding.UTF8.GetString(stream.ToArray());
        void Write(JsonElement value, Utf8JsonWriter writer)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject(); foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal)) { writer.WritePropertyName(property.Name); Write(property.Value, writer); } writer.WriteEndObject(); break;
                case JsonValueKind.Array:
                    writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Write(item, writer); writer.WriteEndArray(); break;
                case JsonValueKind.String:
                    string text = value.GetString()!;
                    if (ownedRoot.Length > 0) text = text.Replace(ownedRoot, "<owned-attempt>", StringComparison.Ordinal).Replace(ownedRoot.Replace('\\', '/'), "<owned-attempt>", StringComparison.Ordinal);
                    writer.WriteStringValue(text); break;
                default: value.WriteTo(writer); break;
            }
        }
    }

    private static Task<NativePublishProcess.Result> RunCapture(string path, string directory, CancellationToken token, params string[] args)
    {
        var start = new ProcessStartInfo(path) { WorkingDirectory = directory };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        return NativePublishProcess.RunAsync(start, TextWriter.Null, TextWriter.Null, token);
    }

    private static string? FindInPath(string name)
    {
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            string path = Path.GetFullPath(Path.Combine(directory.Trim('"'), name));
            if (File.Exists(path)) return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }
        return null;
    }
}
