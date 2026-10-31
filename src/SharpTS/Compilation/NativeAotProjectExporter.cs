using System.Security.Cryptography;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Compilation;

/// <summary>Writes a new, inspectable SDK project for publishing one native guest.</summary>
public static class NativeAotProjectExporter
{
    /// <summary>Refuses occupied destinations before compilation can write guest output.</summary>
    public static void ValidateDestination(NativeAotProjectOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateWrapperAssemblyName(options.AssemblyName);
        if (!NativeAotProjectOptions.SupportedRuntimeIdentifiers.Contains(options.RuntimeIdentifier))
            throw new ArgumentException($"Unsupported native guest RID '{options.RuntimeIdentifier}'.", nameof(options));
        if (options.RuntimeIdentifier != NativeAotProjectOptions.HostRuntimeIdentifier)
            throw new CompileException(DiagnosticCode.NativeGuestTargetMismatch,
                $"Experimental native guest export requires a matching compiler host RID ({NativeAotProjectOptions.HostRuntimeIdentifier ?? "unsupported host"}). " +
                "Generated process.platform and process.arch currently reflect the compiler host; export on the target OS and architecture.");
        string directory = Path.GetFullPath(options.OutputDirectory);
        if (File.Exists(directory) || Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new CompileException(DiagnosticCode.NativeGuestOutputDirectory,
                $"Native guest project destination '{directory}' is occupied. Choose a new or empty directory.");
    }

    internal static void ValidateWrapperAssemblyName(string? name)
    {
        if (name is null) return;
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ';', '%', '$', '\r', '\n']) >= 0)
            throw new CompileException(DiagnosticCode.NativeGuestPublishOutput, "Native output requires a plain wrapper assembly file name without MSBuild property syntax.");
        if (name.Equals("Guest", StringComparison.OrdinalIgnoreCase) ||
            PEPacker.EmbeddedReferenceAssemblyIndex.Default.TryGetIdentity(name, out _) ||
            name.Equals(typeof(object).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase) ||
            name.Equals(typeof(Uri).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
            throw new CompileException(DiagnosticCode.NativeGuestPublishOutput,
                $"Native wrapper assembly name '{name}' would shadow the generated Guest or a framework assembly. Choose a different native executable name.");
    }

    /// <summary>
    /// Writes a project only after compatibility succeeds. Failures preserve partial files for
    /// inspection; this exporter never recursively deletes or overwrites an existing project.
    /// </summary>
    public static void Export(NativeAotProjectOptions options, NativeAotCompatibilityReport compatibility,
        Action<string> saveGuest)
        => Export(options, compatibility, saveGuest, null);

    internal static void Export(NativeAotProjectOptions options, NativeAotCompatibilityReport compatibility,
        Action<string> saveGuest, NativeManagedReferenceClosure? references)
    {
        ArgumentNullException.ThrowIfNull(saveGuest);
        compatibility.ThrowIfIncompatible();
        ValidateDestination(options);
        string directory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(directory);
        string pending = Path.Combine(directory, "native-project.pending");
        using (var claim = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            byte[] notice = Encoding.UTF8.GetBytes("SharpTS experimental native guest export in progress. Partial output is preserved on failure.\n");
            claim.Write(notice);
        }
        var managedReferences = references?.Export(directory) ?? [];
        string guest = Path.Combine(directory, "guest", "Guest.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(guest)!);
        saveGuest(guest);
        if (options.AssemblyName is { } wrapperName)
        {
            using var image = new PEReader(File.OpenRead(guest));
            var metadata = image.GetMetadataReader();
            if (metadata.AssemblyReferences.Any(handle => wrapperName.Equals(metadata.GetString(metadata.GetAssemblyReference(handle).Name), StringComparison.OrdinalIgnoreCase)))
                throw new CompileException(DiagnosticCode.NativeGuestPublishOutput,
                    $"Native wrapper assembly name '{wrapperName}' would shadow a generated guest dependency. Choose a different native executable name.");
        }

        // Local empty build policy files isolate the inspectable project from
        // Directory.Build/Packages settings belonging to its parent repository.
        foreach (string policy in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
            WriteNew(Path.Combine(directory, policy), "<Project />\n");
        var project = new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup",
                new XElement("OutputType", "Exe"), new XElement("TargetFramework", "net10.0"),
                new XElement("RuntimeIdentifier", options.RuntimeIdentifier), new XElement("PublishAot", "true"),
                new XElement("SelfContained", "true"), new XElement("Nullable", "enable"),
                new XElement("IlcTreatWarningsAsErrors", "true"), new XElement("ILLinkTreatWarningsAsErrors", "true"),
                new XElement("TrimmerSingleWarn", "false")),
            new XElement("ItemGroup", new XElement("Reference", new XAttribute("Include", "Guest"),
                new XElement("HintPath", NativeAotProjectOptions.GuestAssemblyPath), new XElement("Private", "true")))));
        foreach (var reference in managedReferences)
            project.Root!.Element("ItemGroup")!.Add(new XElement("Reference", new XAttribute("Include", reference.Name),
                new XElement("HintPath", reference.Path), new XElement("Private", "true")));
        if (options.AssemblyName is not null)
            project.Root!.Element("PropertyGroup")!.Add(new XElement("AssemblyName", options.AssemblyName));
        WriteNew(Path.Combine(directory, "NativeApp.csproj"), project.ToString() + "\n");
        WriteNew(Path.Combine(directory, "Program.cs"), "return global::SharpTS.Generated.EntryPoint.Run();\n");
        using (var manifest = new FileStream(Path.Combine(directory, "native-project.json"), FileMode.CreateNew, FileAccess.Write))
        using (var writer = new Utf8JsonWriter(manifest, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", 1);
            writer.WriteString("profile", "experimental-native-guest-v1");
            writer.WriteString("targetFramework", "net10.0");
            writer.WriteString("runtimeIdentifier", options.RuntimeIdentifier);
            writer.WriteString("guestAssembly", NativeAotProjectOptions.GuestAssemblyPath);
            writer.WriteString("guestSha256", Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(guest))));
            writer.WriteString("entryPoint", "SharpTS.Generated.EntryPoint.Run()");
            writer.WriteString("runtimeRequirements", compatibility.RuntimeRequirements.ToString());
            writer.WriteStartArray("featureReasons");
            foreach (string feature in compatibility.FeatureReasons) writer.WriteStringValue(feature);
            writer.WriteEndArray();
            writer.WriteBoolean("thirdPartyReferences", managedReferences.Count != 0);
            writer.WriteStartArray("frameworkReferences");
            foreach (var framework in references?.FrameworkReferences ?? [])
            {
                writer.WriteStartObject(); writer.WriteString("name", framework.Name);
                writer.WriteString("identity", framework.Identity); writer.WriteString("sha256", framework.Sha256);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("managedReferences");
            foreach (var reference in managedReferences)
            {
                writer.WriteStartObject();
                writer.WriteString("name", reference.Name); writer.WriteString("path", reference.Path);
                writer.WriteString("sha256", reference.Sha256); writer.WriteString("sourcePath", reference.SourcePath);
                writer.WriteString("identity", reference.Identity); writer.WriteStartArray("dependencies");
                foreach (string dependency in reference.Dependencies) writer.WriteStringValue(dependency);
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteString("publishCommand", "dotnet publish NativeApp.csproj -c Release");
            writer.WriteEndObject();
        }
        WriteNew(Path.Combine(directory, "README.md"),
            "# Experimental SharpTS native guest project\n\n" +
            "Publish on a suitable matching OS/toolchain with `dotnet publish NativeApp.csproj -c Release`.\n" +
            "This publishes the generated guest itself through .NET NativeAOT. No compiler host is included.\n" +
            "Trim/AOT warnings are visible errors; no compatibility or warning-free release promise is made by project export.\n" +
            "Program arguments come from the native process command line; the facade follows ordinary DLL top-level entry semantics.\n" +
            "Native symbols use the platform SDK defaults. Guest portable PDBs do not promise TypeScript native stepping.\n" +
            "Explicit net10.0 AnyCPU implementation DLLs are captured under references/ with exact dependency identities and SHA256 hashes in native-project.json.\n" +
            "Package/project references, compile-only reference assemblies, native/resource assets and SharpTS-runtime-dependent features remain excluded.\n");
        File.Delete(pending);
    }

    private static void WriteNew(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }
}
