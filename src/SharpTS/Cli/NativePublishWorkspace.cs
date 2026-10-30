using System.Text.Json;
using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Cli;

/// <summary>Owns an automatic publish attempt; exported user projects never enter this lifecycle.</summary>
internal sealed class NativePublishWorkspace : IDisposable
{
    private const string Marker = ".sharpts-native-owner";
    internal string DirectoryPath { get; }
    internal string ProjectDirectory => Path.Combine(DirectoryPath, "project");
    internal string GuestPath => Path.Combine(ProjectDirectory, NativeAotProjectOptions.GuestAssemblyPath);
    internal string PublishDirectory => Path.Combine(DirectoryPath, "publish");
    internal NativeAotPublishOptions Options { get; }
    internal string AssemblyName { get; }
    internal NativeAotProjectOptions ProjectOptions => new(ProjectDirectory, Options.RuntimeIdentifier) { AssemblyName = AssemblyName };

    internal NativePublishWorkspace(NativeAotPublishOptions options, string? attemptRoot = null)
    {
        Options = options with { OutputPath = Path.GetFullPath(options.OutputPath) };
        string file = Path.GetFileName(Options.OutputPath);
        AssemblyName = options.RuntimeIdentifier.StartsWith("win-", StringComparison.Ordinal) && file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? file[..^4] : file;
        NativeAotProjectExporter.ValidateWrapperAssemblyName(AssemblyName);
        DirectoryPath = CreateOwned(attemptRoot ?? Path.Combine(Path.GetTempPath(), "SharpTS", "native-publish-attempts"), "attempt");
    }

    internal static string CreateOwned(string parent, string label)
    {
        string path = Path.GetFullPath(Path.Combine(parent, $".{label}.{Guid.NewGuid():N}"));
        Directory.CreateDirectory(path);
        using var marker = new FileStream(Path.Combine(path, Marker), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        marker.Write("SharpTS.NativePublish.v1"u8);
        return path;
    }

    internal static void DeleteOwned(string path)
    {
        try
        {
            if (!Directory.Exists(path) || IsLink(path) || !File.Exists(Path.Combine(path, Marker)) ||
                File.ReadAllText(Path.Combine(path, Marker)) != "SharpTS.NativePublish.v1") return;
            // Never recursively follow a link planted in a build directory.
            if (!SafeTree(path)) return;
            Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        static bool SafeTree(string directory)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                if (IsLink(entry) || Directory.Exists(entry) && !SafeTree(entry)) return false;
            return true;
        }
    }

    internal static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    internal static IEnumerable<string> RegularFiles(string directory)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            if (IsLink(entry)) throw new IOException("Native artifact directories cannot contain symbolic links.");
            if (Directory.Exists(entry)) { foreach (string file in RegularFiles(entry)) yield return file; }
            else yield return entry;
        }
    }

    public void Dispose() => DeleteOwned(DirectoryPath);
}

/// <summary>Promotes a complete executable/symbol set, with rollback and an owned sidecar inventory.</summary>
internal static class NativePublishOutput
{
    internal static string ManifestName(string outputPath) => $".{Path.GetFileName(outputPath)}.sharpts-native.json";

    internal static void CopyTree(string source, string target)
    {
        if (NativePublishWorkspace.IsLink(source)) throw new IOException("Native artifacts cannot be symbolic links.");
        if (Directory.Exists(source))
        {
            Directory.CreateDirectory(target);
            foreach (string entry in Directory.EnumerateFileSystemEntries(source).Order(StringComparer.Ordinal))
                CopyTree(entry, Path.Combine(target, Path.GetFileName(entry)));
        }
        else
        {
            File.Copy(source, target, overwrite: false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, File.GetUnixFileMode(source));
        }
    }

    internal static async Task PromoteAsync(string artifactDirectory, string nativeName, NativePublishWorkspace workspace,
        AuthenticodeSigningOptions? signing, CancellationToken token,
        Func<string, AuthenticodeSigningOptions, CancellationToken, Task>? sign = null)
    {
        string output = workspace.Options.OutputPath;
        string identity = OperatingSystem.IsWindows() ? output.ToUpperInvariant() : output;
        string outputKey = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
        using var outputLock = await NativePublishCache.LockAsync(Path.Combine(Path.GetTempPath(), "SharpTS", "native-output-locks"), outputKey, token).ConfigureAwait(false);
        string parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        if (NativePublishWorkspace.IsLink(parent)) throw new IOException("Native output directory cannot be a symbolic link.");
        string stage = NativePublishWorkspace.CreateOwned(parent, Path.GetFileName(output) + ".stage");
        bool canClean = true;
        try
        {
            string ready = Path.Combine(stage, "ready"); Directory.CreateDirectory(ready);
            string manifestName = ManifestName(output);
            string finalName = Path.GetFileName(output);
            string stem = workspace.AssemblyName;
            string[] allowed = [finalName, stem + ".pdb", stem + ".dbg", stem + ".dSYM"];
            var names = new List<string> { finalName };
            CopyTree(Path.Combine(artifactDirectory, nativeName), Path.Combine(ready, finalName));
            foreach (string symbol in allowed.Skip(1).Distinct(StringComparer.Ordinal))
            {
                string source = Path.Combine(artifactDirectory, symbol);
                if (!File.Exists(source) && !Directory.Exists(source)) continue;
                CopyTree(source, Path.Combine(ready, symbol)); names.Add(symbol);
            }
            token.ThrowIfCancellationRequested();
            if (signing is not null)
            {
                if (!workspace.Options.RuntimeIdentifier.StartsWith("win-", StringComparison.Ordinal))
                    throw new CompileException(DiagnosticCode.NativeGuestPublishOutput, "Authenticode signing requires Windows native output.");
                await (sign ?? AuthenticodeSigner.SignAsync)(Path.Combine(ready, finalName), signing, token).ConfigureAwait(false);
            }
            var previous = new HashSet<string>(StringComparer.Ordinal);
            var previousHashes = new Dictionary<string, string>(StringComparer.Ordinal);
            string manifestPath = Path.Combine(parent, manifestName);
            if (File.Exists(manifestPath))
            {
                if (NativePublishWorkspace.IsLink(manifestPath)) throw new IOException("Native output manifest cannot be a symbolic link.");
                using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
                if (document.RootElement.GetProperty("format").GetString() != "SharpTS.NativeOutput.v1")
                    throw new IOException("Native output manifest is not owned by this driver.");
                foreach (var name in document.RootElement.GetProperty("artifacts").EnumerateArray())
                {
                    string value = name.GetString()!;
                    if (!allowed.Contains(value, StringComparer.Ordinal)) throw new IOException("Native output manifest contains an unexpected artifact.");
                    previous.Add(value);
                }
                if (!document.RootElement.TryGetProperty("sha256", out var hashes))
                    throw new IOException("Native output symbol ownership hashes are missing.");
                foreach (var hash in hashes.EnumerateObject()) previousHashes.Add(hash.Name, hash.Value.GetString()!);
            }
            foreach (string name in names.Concat(previous).Distinct(StringComparer.Ordinal))
            {
                string destination = Path.Combine(parent, name);
                if (!File.Exists(destination) && !Directory.Exists(destination)) continue;
                if (NativePublishWorkspace.IsLink(destination) || name == finalName && Directory.Exists(destination))
                    throw new IOException("Native output collides with a directory or symbolic link.");
                if (name != finalName && !previous.Contains(name)) throw new IOException($"Native symbol destination '{destination}' is not owned by this output.");
                if (name != finalName && (!previousHashes.TryGetValue(name, out string? expected) || ArtifactHash(destination) != expected))
                    throw new IOException($"Native symbol destination '{destination}' changed since publication and was preserved.");
            }
            using (var stream = File.Create(Path.Combine(ready, manifestName)))
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject(); writer.WriteString("format", "SharpTS.NativeOutput.v1");
                writer.WriteString("runtimeIdentifier", workspace.Options.RuntimeIdentifier);
                writer.WriteStartArray("artifacts"); foreach (string name in names) writer.WriteStringValue(name); writer.WriteEndArray();
                writer.WriteStartObject("sha256"); foreach (string name in names) writer.WriteString(name, ArtifactHash(Path.Combine(ready, name))); writer.WriteEndObject();
                writer.WriteEndObject();
            }
            token.ThrowIfCancellationRequested();
            string backup = Path.Combine(stage, "backup"); Directory.CreateDirectory(backup);
            var backedUp = new List<string>(); var promoted = new List<string>();
            try
            {
                foreach (string name in names.Concat(previous).Append(manifestName).Distinct(StringComparer.Ordinal))
                {
                    string destination = Path.Combine(parent, name);
                    if (!File.Exists(destination) && !Directory.Exists(destination)) continue;
                    Move(destination, Path.Combine(backup, name)); backedUp.Add(name);
                }
                foreach (string name in names.Append(manifestName))
                {
                    Move(Path.Combine(ready, name), Path.Combine(parent, name)); promoted.Add(name);
                }
            }
            catch
            {
                try
                {
                    foreach (string name in promoted) Move(Path.Combine(parent, name), Path.Combine(ready, name));
                    foreach (string name in backedUp) Move(Path.Combine(backup, name), Path.Combine(parent, name));
                }
                catch { canClean = false; throw new IOException($"Native output rollback could not complete. Prior artifacts are preserved in '{backup}'."); }
                throw;
            }
        }
        finally { if (canClean) NativePublishWorkspace.DeleteOwned(stage); }
    }

    private static void Move(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to);
    }

    private static string ArtifactHash(string path)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (string file in Directory.Exists(path) ? NativePublishWorkspace.RegularFiles(path) : [path])
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Directory.Exists(path) ? Path.GetRelativePath(path, file) : "file"));
            hash.AppendData([0]); using var stream = File.OpenRead(file); hash.AppendData(System.Security.Cryptography.SHA256.HashData(stream));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
