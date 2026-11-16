using System.Reflection;
using Xunit;
using Xunit.Sdk;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Explicit opt-in for exhaustive deployment permutations. Semantic regressions
/// remain in the ordinary suite. Local/default and daily runs enumerate every row;
/// PRs retain two examples of every boolean/enum mode for EACH annotated method.
/// Ordinary MemberData, InlineData and Facts are never sampled.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class DeploymentDataAttribute(string memberName, params object[] parameters) : DataAttribute
{
    public Type? MemberType { get; set; }

    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        var data = new MemberDataAttribute(memberName, parameters) { MemberType = MemberType };
        string coverage = Environment.GetEnvironmentVariable("SHARPTS_DEPLOYMENT_COVERAGE") ?? "Full";
        // Workflow routing expands changed tests and their direct data providers.
        // These hints may only add rows, never remove them from the PR selection.
        if (coverage == "Pr" && IsAffected(testMethod.Name, MemberType?.Name,
            Environment.GetEnvironmentVariable("SHARPTS_DEPLOYMENT_FULL_METHODS"),
            Environment.GetEnvironmentVariable("SHARPTS_DEPLOYMENT_FULL_TYPES")))
            coverage = "Full";
        return SelectRows(data.GetData(testMethod), coverage);
    }

    internal static bool IsAffected(string method, string? type, string? methods, string? types)
        => (methods ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries).Contains(method, StringComparer.Ordinal)
        || (type is not null && (types ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries).Contains(type, StringComparer.Ordinal));

    internal static IEnumerable<object[]> SelectRows(IEnumerable<object[]> rows, string coverage)
    {
        if (coverage is not ("Full" or "Pr"))
            throw new ArgumentException($"Unknown deployment coverage: {coverage}");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            // Preserve flags such as hosted/noLib and enum execution modes. Source,
            // expected output and descriptive case names do not form mode axes.
            string mode = string.Join("|", row.Select((value, index) => value switch
            {
                bool flag => $"{index}:bool:{flag}",
                Enum choice => $"{index}:{choice.GetType().FullName}:{choice}",
                _ => ""
            }));
            counts.TryGetValue(mode, out int count);
            counts[mode] = count + 1;
            if (coverage == "Full" || count < 2)
                yield return row;
        }
    }
}
