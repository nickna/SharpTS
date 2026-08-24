namespace SharpTS.Cli;

/// <summary>
/// Deployment-time garbage-collection policy for a compiled SharpTS application.
/// </summary>
public enum GcProfile
{
    /// <summary>
    /// Concurrent workstation GC. Best for small, interactive, and otherwise unknown workloads.
    /// </summary>
    Workstation,

    /// <summary>
    /// Concurrent server GC with dynamic adaptation (DATAS). Recommended for sustained,
    /// allocation-heavy services after measuring their memory and latency requirements.
    /// </summary>
    Adaptive,

    /// <summary>
    /// Concurrent fixed server GC. An expert opt-in for measured throughput workloads because
    /// it can reserve substantially more memory than the adaptive profile.
    /// </summary>
    Throughput
}

internal static class GcProfileSettings
{
    private static readonly IReadOnlyDictionary<string, object?> WorkstationProperties =
        new Dictionary<string, object?>
        {
            ["System.GC.Server"] = false,
            ["System.GC.Concurrent"] = true,
        };

    private static readonly IReadOnlyDictionary<string, object?> AdaptiveProperties =
        new Dictionary<string, object?>
        {
            ["System.GC.Server"] = true,
            ["System.GC.Concurrent"] = true,
            ["System.GC.DynamicAdaptationMode"] = 1,
        };

    private static readonly IReadOnlyDictionary<string, object?> ThroughputProperties =
        new Dictionary<string, object?>
        {
            ["System.GC.Server"] = true,
            ["System.GC.Concurrent"] = true,
            ["System.GC.DynamicAdaptationMode"] = 0,
        };

    internal static IReadOnlyDictionary<string, object?> RuntimeConfigProperties(GcProfile profile) =>
        profile switch
        {
            GcProfile.Workstation => WorkstationProperties,
            GcProfile.Adaptive => AdaptiveProperties,
            GcProfile.Throughput => ThroughputProperties,
            _ => throw new ArgumentOutOfRangeException(nameof(profile))
        };

    internal static bool TryParse(string value, out GcProfile profile)
    {
        switch (value.ToLowerInvariant())
        {
            case "workstation": profile = GcProfile.Workstation; return true;
            case "adaptive": profile = GcProfile.Adaptive; return true;
            case "throughput": profile = GcProfile.Throughput; return true;
            default: profile = default; return false;
        }
    }

    internal static string CliValue(GcProfile profile) => profile switch
    {
        GcProfile.Workstation => "workstation",
        GcProfile.Adaptive => "adaptive",
        GcProfile.Throughput => "throughput",
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };
}
