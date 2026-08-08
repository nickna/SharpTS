using System;
using System.Collections.Generic;
using System.IO;
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
}
