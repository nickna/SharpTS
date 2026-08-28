using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace SharpTS.Sdk.Tasks;

/// <summary>
/// MSBuild task that writes a GUI application manifest file for SharpTS GUI applications.
/// </summary>
public sealed class WriteGuiManifestTask : Task
{
    /// <summary>
    /// Path where the manifest file will be written.
    /// </summary>
    [Required]
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>
    /// Entry point path for the GUI application.
    /// </summary>
    [Required]
    public string EntryPath { get; set; } = string.Empty;

    /// <summary>
    /// Path to the compiled assembly.
    /// </summary>
    [Required]
    public string CompiledAssembly { get; set; } = string.Empty;

    /// <summary>
    /// Hosted ABI version number.
    /// </summary>
    [Required]
    public string HostedAbiVersion { get; set; } = string.Empty;

    /// <summary>
    /// GUI API version number.
    /// </summary>
    [Required]
    public string GuiApiVersion { get; set; } = string.Empty;

    /// <summary>
    /// Descriptor schema version number.
    /// </summary>
    [Required]
    public string DescriptorSchemaVersion { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of the descriptor schema.
    /// </summary>
    [Required]
    public string DescriptorSchemaHash { get; set; } = string.Empty;

    /// <summary>
    /// Executes the task to write the GUI manifest file.
    /// </summary>
    /// <returns>True if successful; otherwise, false.</returns>
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
