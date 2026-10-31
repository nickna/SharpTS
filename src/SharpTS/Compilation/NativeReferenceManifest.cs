using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace SharpTS.Compilation;

/// <summary>Shared byte validation for exported runtime references, SDK resolution and cache identity.</summary>
internal static class NativeReferenceManifest
{
    internal sealed record Entry(string Name, string Path, string Sha256, string SourcePath, string Identity, IReadOnlyList<string> Dependencies);
    internal static IReadOnlyList<Entry> ReadAndVerify(string directory)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(System.IO.Path.Combine(directory, "native-project.json")));
        var references = document.RootElement.GetProperty("managedReferences");
        var entries = new List<Entry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string prefix = System.IO.Path.GetFullPath(directory) + System.IO.Path.DirectorySeparatorChar;
        foreach (var item in references.EnumerateArray())
        {
            string name = item.GetProperty("name").GetString()!;
            if (!NativeReferenceFileName.IsSupported(name))
                throw new InvalidDataException($"Native managed reference '{name}' has an unsupported assembly file name.");
            string relative = item.GetProperty("path").GetString()!;
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, relative));
            string hash = item.GetProperty("sha256").GetString()!;
            if (!names.Add(name) || relative != "references/" + name + ".dll" ||
                !path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) != hash)
                throw new InvalidDataException($"Native managed reference '{name}' no longer matches its captured bytes.");
            entries.Add(new(name, relative, hash, item.GetProperty("sourcePath").GetString()!,
                item.GetProperty("identity").GetString()!, item.GetProperty("dependencies").EnumerateArray().Select(value => value.GetString()!).ToArray()));
        }
        string project = System.IO.Path.Combine(directory, "NativeApp.csproj");
        if (File.Exists(project))
        {
            var declared = XDocument.Load(project).Descendants("Reference").ToArray();
            if (declared.Length != entries.Count + 1 || declared.Count(item =>
                    (string?)item.Attribute("Include") == "Guest" && (string?)item.Element("HintPath") == "guest/Guest.dll") != 1 ||
                entries.Any(entry => declared.Count(item => (string?)item.Attribute("Include") == entry.Name &&
                    (string?)item.Element("HintPath") == entry.Path) != 1))
                throw new InvalidDataException("Native project references no longer match its complete captured managed graph.");
        }
        return entries.AsReadOnly();
    }
}
