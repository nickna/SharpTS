using System.Collections.Immutable;

namespace SharpTS.Compilation;

/// <summary>
/// Deployment signals for one compilation. Runtime features and guest call sites may
/// contribute until the compiler completes guest emission; completed signals are frozen.
/// </summary>
public sealed class EmittedDeploymentRequirements
{
    private const SharpTSRuntimeRequirements KnownRequirements =
        SharpTSRuntimeRequirements.RuntimeAssembly |
        SharpTSRuntimeRequirements.FullDependencyClosure |
        SharpTSRuntimeRequirements.ManagedCompilerHost;

    private ImmutableSortedSet<string> _reasons = ImmutableSortedSet.Create<string>(StringComparer.Ordinal);

    internal EmittedDeploymentRequirements() { }

    /// <summary>Immutable, ordinally sorted diagnostic snapshot of recorded late-binding paths.</summary>
    public IReadOnlyCollection<string> Reasons => _reasons;

    /// <summary>Stable capabilities used by hosts and the CLI to select deployment policy.</summary>
    public SharpTSRuntimeRequirements Requirements { get; private set; }

    public bool IsComplete { get; private set; }

    /// <summary>
    /// Records a normal-execution dependency on SharpTS.dll. Repeated reasons coalesce,
    /// while their capabilities accumulate. Graceful fallback paths are not recorded.
    /// </summary>
    internal void Require(string reason,
        SharpTSRuntimeRequirements requirements = SharpTSRuntimeRequirements.RuntimeAssembly)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if ((requirements & ~KnownRequirements) != 0)
            throw new ArgumentOutOfRangeException(nameof(requirements), requirements, "Unknown deployment capabilities.");
        _reasons = _reasons.Add(reason);
        Requirements |= requirements | SharpTSRuntimeRequirements.RuntimeAssembly;
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        if ((_reasons.Count == 0) != (Requirements == SharpTSRuntimeRequirements.None) ||
            (_reasons.Count > 0 && !Requirements.HasFlag(SharpTSRuntimeRequirements.RuntimeAssembly)) ||
            (Requirements & ~KnownRequirements) != 0)
            throw new InvalidOperationException("Deployment reasons and capabilities are inconsistent.");
        IsComplete = true;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Deployment requirement recording is already complete.");
    }
}
