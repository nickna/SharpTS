using System;
using System.IO;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace SharpTS.Sdk.Tasks;

/// <summary>
/// MSBuild task that reads tsconfig.json and extracts compiler options
/// for use in SharpTS SDK builds.
/// </summary>
public class ReadTsConfigTask : Task
{
    /// <summary>
    /// Path to the tsconfig.json file to read.
    /// </summary>
    [Required]
    public string TsConfigPath { get; set; } = string.Empty;

    /// <summary>
    /// Output: Whether preserveConstEnums is enabled in compilerOptions.
    /// </summary>
    [Output]
    public bool PreserveConstEnums { get; set; }

    /// <summary>
    /// Output: Whether experimentalDecorators is enabled in compilerOptions.
    /// </summary>
    [Output]
    public bool ExperimentalDecorators { get; set; }

    /// <summary>
    /// Output: Whether TC39 Stage 3 decorators are enabled in compilerOptions.
    /// </summary>
    [Output]
    public bool Decorators { get; set; }

    /// <summary>
    /// Output: Whether emitDecoratorMetadata is enabled in compilerOptions.
    /// </summary>
    [Output]
    public bool EmitDecoratorMetadata { get; set; }

    /// <summary>
    /// Output: The first entry file from the files array, if present.
    /// </summary>
    [Output]
    public string EntryFile { get; set; } = string.Empty;

    /// <summary>
    /// Output: The rootDir from compilerOptions, if present.
    /// </summary>
    [Output]
    public string RootDir { get; set; } = string.Empty;

    /// <summary>
    /// Output: The outDir from compilerOptions, if present.
    /// </summary>
    [Output]
    public string OutDir { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            if (!File.Exists(TsConfigPath))
            {
                Log.LogMessage(MessageImportance.Low, $"tsconfig.json not found at {TsConfigPath}");
                return true; // Not an error - just use defaults
            }

            var json = File.ReadAllText(TsConfigPath);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = doc.RootElement;

            // Read compilerOptions
            if (root.TryGetProperty("compilerOptions", out var compilerOptions))
            {
                PreserveConstEnums = GetBoolOption(compilerOptions, "preserveConstEnums");
                ExperimentalDecorators = GetBoolOption(compilerOptions, "experimentalDecorators");
                Decorators = GetBoolOption(compilerOptions, "decorators");
                EmitDecoratorMetadata = GetBoolOption(compilerOptions, "emitDecoratorMetadata");

                if (compilerOptions.TryGetProperty("rootDir", out var rootDir))
                {
                    RootDir = rootDir.GetString() ?? string.Empty;
                }

                if (compilerOptions.TryGetProperty("outDir", out var outDir))
                {
                    OutDir = outDir.GetString() ?? string.Empty;
                }
            }

            // Read files array for entry point
            if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                var enumerator = files.EnumerateArray();
                if (enumerator.MoveNext())
                {
                    var firstFile = enumerator.Current.GetString();
                    if (!string.IsNullOrEmpty(firstFile))
                    {
                        // Make relative to tsconfig.json directory
                        var tsConfigDir = Path.GetDirectoryName(TsConfigPath) ?? ".";
                        EntryFile = Path.Combine(tsConfigDir, firstFile);
                    }
                }
            }

            Log.LogMessage(MessageImportance.Low,
                $"Read tsconfig.json: preserveConstEnums={PreserveConstEnums}, " +
                $"experimentalDecorators={ExperimentalDecorators}, decorators={Decorators}, " +
                $"emitDecoratorMetadata={EmitDecoratorMetadata}, entryFile={EntryFile}");

            return true;
        }
        catch (JsonException ex)
        {
            Log.LogError($"Failed to parse tsconfig.json at {TsConfigPath}: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to read tsconfig.json at {TsConfigPath}: {ex.Message}");
            return false;
        }
    }

    private static bool GetBoolOption(JsonElement obj, string propertyName)
    {
        if (obj.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True)
        {
            return true;
        }
        return false;
    }
}
