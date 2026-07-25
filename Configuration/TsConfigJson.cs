using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SharpTS.Configuration;

// =============================================================================
// The tsconfig.json JSON contract — the SINGLE source of truth, shared by the CLI
// and the MSBuild task.
//
// SharpTS.Sdk.Tasks source-links THIS FILE (<Compile Include ... Link>) rather than
// referencing SharpTS.dll: the SDK package ships SharpTS.Sdk.Tasks.dll as one loose
// file consumed via <UsingTask AssemblyFile=...>, so a second assembly on its
// reference graph would have to be packed into build\ as well.
//
// Two rules this file must keep:
//
//  1. INTERNAL ON PURPOSE. SharpTS.Tests ProjectReferences both SharpTS and
//     SharpTS.Sdk.Tasks, so a `public` type compiled into both assemblies gives
//     every test that names it CS0433 ("exists in both"). SharpTS.csproj's
//     <InternalsVisibleTo Include="SharpTS.Tests" /> keeps the main copy testable.
//
//  2. EXPLICIT USINGS, NO SHARPTS TYPES. SharpTS.Sdk.Tasks sets
//     ImplicitUsings=disable and cannot see anything from SharpTS.
//
// CLI-only members (strictness, extends, include/exclude, unknown-key capture) live
// in the partials in TsConfigJson.Cli.cs, which is NOT linked — so the task's
// source-generated serializer never sees them.
// =============================================================================

/// <summary>Represents a tsconfig.json file.</summary>
internal sealed partial class TsConfigJson
{
    [JsonPropertyName("compilerOptions")]
    public TsConfigCompilerOptions? CompilerOptions { get; set; }

    [JsonPropertyName("files")]
    public string[]? Files { get; set; }
}

/// <summary>
/// The <c>compilerOptions</c> SharpTS acts on. Every member is nullable so an absent key
/// stays distinguishable from an explicit <c>false</c> — the CLI's precedence rules depend
/// on that distinction.
/// </summary>
internal sealed partial class TsConfigCompilerOptions
{
    [JsonPropertyName("preserveConstEnums")]
    public bool? PreserveConstEnums { get; set; }

    [JsonPropertyName("experimentalDecorators")]
    public bool? ExperimentalDecorators { get; set; }

    [JsonPropertyName("decorators")]
    public bool? Decorators { get; set; }

    [JsonPropertyName("emitDecoratorMetadata")]
    public bool? EmitDecoratorMetadata { get; set; }

    [JsonPropertyName("rootDir")]
    public string? RootDir { get; set; }

    [JsonPropertyName("outDir")]
    public string? OutDir { get; set; }
}
