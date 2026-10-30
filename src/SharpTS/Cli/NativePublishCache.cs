using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SharpTS.Cli;

/// <summary>Content-addressed unsigned native artifacts; manifests are committed last.</summary>
internal static class NativePublishCache
{
    internal static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharpTS", "native-publish", "v1");

    internal static string Key(NativePublishWorkspace workspace, NativeGuestRevision guest, string toolchain)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string text) { hash.AppendData(Encoding.UTF8.GetBytes(text)); hash.AppendData([0]); }
        Add("SharpTS.NativePublishCache.v1"); Add(guest.RevisionSha256); Add(toolchain); Add(workspace.Options.RuntimeIdentifier);
        foreach (string name in new[] { "NativeApp.csproj", "Program.cs", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json" })
        { Add(name); hash.AppendData(File.ReadAllBytes(Path.Combine(workspace.ProjectDirectory, name))); }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static async Task<FileStream> LockAsync(string root, string key, CancellationToken token)
    {
        string directory = Path.Combine(root, "locks"); Directory.CreateDirectory(directory);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(directory, key + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 11 or 32 or 33)
            { await Task.Delay(100, token).ConfigureAwait(false); }
        }
    }

    internal static string? Find(string root, string key, string nativeName)
    {
        string entry = Path.Combine(root, "entries", key);
        try
        {
            string manifest = Path.Combine(entry, "cache.json");
            if (!File.Exists(manifest) || NativePublishWorkspace.IsLink(entry)) return null;
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifest));
            if (document.RootElement.GetProperty("format").GetString() != "SharpTS.NativeCache.v1" || document.RootElement.GetProperty("key").GetString() != key) return null;
            var listed = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var artifact in document.RootElement.GetProperty("files").EnumerateArray())
            {
                string name = artifact.GetProperty("name").GetString()!;
                string full = Path.GetFullPath(Path.Combine(entry, name));
                if (!full.StartsWith(entry + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                    !File.Exists(full) || NativePublishWorkspace.IsLink(full) || Hash(full) != artifact.GetProperty("sha256").GetString()) return null;
                if (!OperatingSystem.IsWindows() && (!artifact.TryGetProperty("unixMode", out var mode) ||
                    (int)File.GetUnixFileMode(full) != mode.GetInt32())) return null;
                if (!listed.Add(full)) return null;
            }
            var actual = NativePublishWorkspace.RegularFiles(entry)
                .Where(file => Path.GetFileName(file) is not ("cache.json" or ".sharpts-native-owner")).ToHashSet(listed.Comparer);
            if (!listed.SetEquals(actual) || !listed.Contains(Path.Combine(entry, "guest", "Guest.dll"))) return null;
            string artifacts = Path.Combine(entry, "artifacts");
            if (!File.Exists(Path.Combine(artifacts, nativeName))) return null;
            NativeAotPublishDriver.ValidateNativeImage(Path.Combine(artifacts, nativeName), document.RootElement.GetProperty("rid").GetString()!);
            return artifacts;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or BadImageFormatException) { return null; }
    }

    internal static void Store(string root, string key, NativePublishWorkspace workspace, string nativeName, NativeGuestRevision revision)
    {
        string entries = Path.Combine(root, "entries"); Directory.CreateDirectory(entries);
        string destination = Path.Combine(entries, key);
        // Repair only entries carrying our exact ownership marker; leave unknown directories alone.
        if (Directory.Exists(destination)) NativePublishWorkspace.DeleteOwned(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) return;
        string pending = NativePublishWorkspace.CreateOwned(entries, "cache");
        try
        {
            string artifacts = Path.Combine(pending, "artifacts"); Directory.CreateDirectory(artifacts);
            foreach (string name in new[] { nativeName, workspace.AssemblyName + ".pdb", workspace.AssemblyName + ".dbg", workspace.AssemblyName + ".dSYM" }.Distinct(StringComparer.Ordinal))
            {
                string source = Path.Combine(workspace.PublishDirectory, name);
                if (File.Exists(source) || Directory.Exists(source)) NativePublishOutput.CopyTree(source, Path.Combine(artifacts, name));
            }
            string guest = Path.Combine(pending, "guest"); Directory.CreateDirectory(guest);
            NativePublishOutput.CopyTree(workspace.GuestPath, Path.Combine(guest, "Guest.dll"));
            if (revision.PdbSha256 is not null) NativePublishOutput.CopyTree(Path.ChangeExtension(workspace.GuestPath, ".pdb"), Path.Combine(guest, "Guest.pdb"));
            using (var stream = new FileStream(Path.Combine(pending, "cache.json"), FileMode.CreateNew))
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject(); writer.WriteString("format", "SharpTS.NativeCache.v1"); writer.WriteString("key", key);
                writer.WriteString("rid", workspace.Options.RuntimeIdentifier); writer.WriteString("guestRevision", revision.RevisionSha256);
                writer.WriteString("guestSha256", revision.ImageSha256); writer.WriteString("guestPdbSha256", revision.PdbSha256);
                writer.WriteStartArray("files");
                foreach (string file in NativePublishWorkspace.RegularFiles(pending).Where(file => Path.GetFileName(file) is not ("cache.json" or ".sharpts-native-owner")).Order(StringComparer.Ordinal))
                {
                    writer.WriteStartObject(); writer.WriteString("name", Path.GetRelativePath(pending, file)); writer.WriteString("sha256", Hash(file));
                    if (!OperatingSystem.IsWindows()) writer.WriteNumber("unixMode", (int)File.GetUnixFileMode(file));
                    writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            Directory.Move(pending, destination);
        }
        finally { NativePublishWorkspace.DeleteOwned(pending); }
    }

    private static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
}
