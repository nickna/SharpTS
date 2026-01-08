using System;
using System.IO;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace SharpTS.Sdk.Tasks;

/// <summary>
/// MSBuild task that reads tsconfig.json and extracts compiler options
/// for use in SharpTS SDK builds.
/// Uses JSON source generation for optimal performance.
/// </summary>
public sealed class ReadTsConfigTask : Task
{
    /// <summary>
    /// Path to the tsconfig.json file to read.
    /// </summary>
    [Required]
    public required string TsConfigPath { get; set; }

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
        // Validate input path
        if (string.IsNullOrWhiteSpace(TsConfigPath))
        {
            Log.LogError("TsConfigPath is required but was not provided.");
            return false;
        }

        // If file doesn't exist, return gracefully with defaults (existing behavior)
        if (!File.Exists(TsConfigPath))
        {
            Log.LogMessage(MessageImportance.Low,
                $"tsconfig.json not found at '{TsConfigPath}'. Using default compiler options.");

            // Set defaults (empty strings already set by property initializers)
            return true;
        }

        try
        {
            // Read and parse using source-generated JSON context
            var json = File.ReadAllText(TsConfigPath);
            var tsConfig = JsonSerializer.Deserialize(
                json,
                TsConfigSourceGenerationContext.Default.TsConfigJson);

            if (tsConfig == null)
            {
                Log.LogWarning($"Failed to parse tsconfig.json at '{TsConfigPath}'. Using defaults.");
                return true;
            }

            // Extract compiler options using null-conditional operators
            var opts = tsConfig.CompilerOptions;
            if (opts != null)
            {
                PreserveConstEnums = opts.PreserveConstEnums ?? false;
                ExperimentalDecorators = opts.ExperimentalDecorators ?? false;
                Decorators = opts.Decorators ?? false;
                EmitDecoratorMetadata = opts.EmitDecoratorMetadata ?? false;
                RootDir = opts.RootDir ?? string.Empty;
                OutDir = opts.OutDir ?? string.Empty;
            }

            // Extract entry file using collection expressions and pattern matching
            if (tsConfig.Files is { Length: > 0 } files)
            {
                var tsConfigDir = Path.GetDirectoryName(TsConfigPath) ?? string.Empty;
                EntryFile = Path.Combine(tsConfigDir, files[0]);
            }

            Log.LogMessage(MessageImportance.Low,
                $"Successfully loaded tsconfig.json from '{TsConfigPath}': " +
                $"preserveConstEnums={PreserveConstEnums}, " +
                $"experimentalDecorators={ExperimentalDecorators}, decorators={Decorators}, " +
                $"emitDecoratorMetadata={EmitDecoratorMetadata}, entryFile={EntryFile}");

            return true;
        }
        catch (JsonException ex)
        {
            Log.LogError($"Failed to parse tsconfig.json at '{TsConfigPath}': {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Log.LogError($"Unexpected error reading tsconfig.json at '{TsConfigPath}': {ex.Message}");
            return false;
        }
    }
}
