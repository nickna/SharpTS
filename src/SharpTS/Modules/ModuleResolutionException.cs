namespace SharpTS.Modules;

/// <summary>The machine-readable reason a module could not be resolved.</summary>
public enum ModuleResolutionFailure
{
    /// <summary>No matching module was found under the selected resolution rules.</summary>
    NotFound,
}

/// <summary>A resolution failure whose diagnostic wording does not control recovery.</summary>
public sealed class ModuleResolutionException(ModuleResolutionFailure reason, string message)
    : Exception(message)
{
    public ModuleResolutionFailure Reason { get; } = reason;
}
