namespace SharpTS.Compilation;

/// <summary>
/// Deployment capabilities required by emitted code that late-binds into SharpTS.dll.
/// Human-readable reasons are tracked separately for diagnostics; these flags are the stable
/// machine contract used by hosts and the CLI to choose a deployment strategy.
/// </summary>
[Flags]
public enum SharpTSRuntimeRequirements
{
    None = 0,

    /// <summary>SharpTS.dll must be loadable beside the emitted assembly.</summary>
    RuntimeAssembly = 1 << 0,

    /// <summary>
    /// SharpTS.dll plus its managed dependency closure and runtime metadata must be deployed.
    /// </summary>
    FullDependencyClosure = 1 << 1,

    /// <summary>
    /// The program must be emitted by the managed compiler SKU; Native AOT hosts cannot produce
    /// a complete runnable deployment for this feature.
    /// </summary>
    ManagedCompilerHost = 1 << 2,
}
