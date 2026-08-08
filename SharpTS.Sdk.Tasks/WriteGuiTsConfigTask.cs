using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace SharpTS.Sdk.Tasks;

public sealed class WriteGuiTsConfigTask : Task
{
    [Required]
    public string OutputPath { get; set; } = string.Empty;

    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    [Required]
    public string GuiPackageDirectory { get; set; } = string.Empty;

    public string? BaseTsConfigPath { get; set; }

    public override bool Execute()
    {
        try
        {
            string outputPath = Path.GetFullPath(OutputPath);
            string projectDirectory = Path.GetFullPath(ProjectDirectory);
            string packageDirectory = Path.GetFullPath(GuiPackageDirectory);
            string? baseConfig = string.IsNullOrWhiteSpace(BaseTsConfigPath)
                ? null
                : Path.GetFullPath(BaseTsConfigPath);

            if (baseConfig is not null && !File.Exists(baseConfig))
            {
                Log.LogError($"SharpTS GUI base tsconfig does not exist: {baseConfig}");
                return false;
            }

            var compilerOptions = new Dictionary<string, object?>
            {
                ["moduleResolution"] = "bundler",
                ["jsx"] = "react-jsx",
                ["jsxImportSource"] = "@sharpts/gui",
                ["baseUrl"] = Normalize(projectDirectory),
                ["paths"] = new Dictionary<string, string[]>
                {
                    ["@sharpts/gui"] = [Normalize(Path.Combine(packageDirectory, "index.ts"))],
                    ["@sharpts/gui/*"] = [Normalize(Path.Combine(packageDirectory, "*"))],
                },
            };
            var config = new Dictionary<string, object?>
            {
                ["compilerOptions"] = compilerOptions,
            };
            if (baseConfig is not null)
                config["extends"] = Normalize(baseConfig);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(config, new JsonSerializerOptions
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

    private static string Normalize(string path) => path.Replace('\\', '/');
}
