using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace SharpTS.Sdk.Tasks;

public sealed class WriteGuiManifestTask : Task
{
    [Required]
    public string OutputPath { get; set; } = string.Empty;

    [Required]
    public string EntryPath { get; set; } = string.Empty;

    [Required]
    public string CompiledAssembly { get; set; } = string.Empty;

    [Required]
    public string HostedAbiVersion { get; set; } = string.Empty;

    [Required]
    public string GuiApiVersion { get; set; } = string.Empty;

    [Required]
    public string DescriptorSchemaVersion { get; set; } = string.Empty;

    [Required]
    public string DescriptorSchemaHash { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            string outputPath = Path.GetFullPath(OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var manifest = new Dictionary<string, object?>
            {
                ["entryPath"] = EntryPath.Replace('\\', '/'),
                ["compiledAssembly"] = CompiledAssembly.Replace('\\', '/'),
                ["hostedAbiVersion"] = int.Parse(
                    HostedAbiVersion,
                    System.Globalization.CultureInfo.InvariantCulture),
                ["guiApiVersion"] = int.Parse(
                    GuiApiVersion,
                    System.Globalization.CultureInfo.InvariantCulture),
                ["descriptorSchemaVersion"] = int.Parse(
                    DescriptorSchemaVersion,
                    System.Globalization.CultureInfo.InvariantCulture),
                ["descriptorSchemaHash"] = ValidateHash(DescriptorSchemaHash),
            };
            File.WriteAllText(outputPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
            }) + Environment.NewLine);
            return true;
        }
        catch (Exception exception)
        {
            Log.LogErrorFromException(exception, showStackTrace: true);
            return false;
        }
    }

    private static string ValidateHash(string value)
    {
        string hash = value.Trim().ToLowerInvariant();
        if (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("DescriptorSchemaHash must be a 64-character SHA-256 value.");
        return hash;
    }
}
