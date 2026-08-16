using System.Text.RegularExpressions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Guardrail meta-tests that keep the suite hermetic: a test must either make no
/// real outbound network call, or be explicitly opted into the live-network set
/// (tagged <c>[Trait("Category","LiveNetwork")]</c>, which CI excludes via
/// <c>--filter "Category!=LiveNetwork"</c>). See issue #495.
/// </summary>
/// <remarks>
/// Modeled on <see cref="StandaloneDllTests"/>'s source-scanning guards. The earlier
/// band-aid (<c>Skip.If(output.Length==0)</c>) silently masked regressions; this
/// replaces it with a positive, enforced invariant. The two checks below enforce:
/// <list type="number">
///   <item>No external host literal (a non-loopback <c>fetch('http://…')</c>,
///   <c>google.com</c>, or a reserved <c>*.example</c> name) appears anywhere
///   outside <see cref="Infrastructure.LiveNetworkHosts"/> — so every live-network
///   host is centralized and auditable, and hermetic tests can't inline one.</item>
///   <item>Any file that uses a <see cref="Infrastructure.LiveNetworkHosts"/>
///   constant carries the <c>LiveNetwork</c> trait — so live tests are actually
///   excluded from CI rather than silently false-redding it.</item>
/// </list>
/// </remarks>
public class LiveNetworkHermeticityTests
{
    // The only files permitted to contain external (real-internet) host literals.
    private static readonly HashSet<string> Allowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "tests/SharpTS.Tests/Infrastructure/LiveNetworkHosts.cs",      // the sanctioned home for the literals
        "tests/SharpTS.Tests/CompilerTests/LiveNetworkHermeticityTests.cs", // this guard (contains the patterns)
    };

    // A fetch() call whose URL is a quoted absolute http(s) literal. The host is
    // checked against the loopback set in IsExternalHost (loopback fetches are
    // hermetic — they hit MockHttpServer / a test-local listener).
    private static readonly Regex ExternalFetch =
        new(@"fetch\(\s*[""'](?<url>https?://[^""']+)", RegexOptions.Compiled);

    // External host literals that have no legitimate hermetic use.
    private static readonly Regex GoogleLiteral = new(@"\bgoogle\.com\b", RegexOptions.Compiled);
    // A reserved-TLD name ending in ".example" (RFC 2606), NOT ".example.<tld>"
    // (e.g. the loopback fake-server's "ca.example.net" is fine).
    private static readonly Regex ExampleTld = new(@"\.example(?![\w.])", RegexOptions.Compiled);

    private static readonly Regex LiveNetworkHostUse =
        new(@"LiveNetworkHosts\.(Stable|Nonexistent)", RegexOptions.Compiled);
    private static readonly Regex LiveNetworkTrait =
        new(@"""Category""\s*,\s*""LiveNetwork""", RegexOptions.Compiled);

    [Fact]
    public void TestSources_ShouldNotInlineExternalNetworkLiterals()
    {
        var violations = new List<string>();

        foreach (var (relative, file) in EnumerateTestSources())
        {
            if (Allowlist.Contains(relative))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                    continue;

                foreach (Match m in ExternalFetch.Matches(line))
                {
                    if (IsExternalHost(m.Groups["url"].Value))
                        violations.Add($"{relative}:{i + 1}: non-loopback fetch URL — {trimmed}");
                }

                if (GoogleLiteral.IsMatch(line) || ExampleTld.IsMatch(line))
                    violations.Add($"{relative}:{i + 1}: external host literal — {trimmed}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Found inline external-network literals in test sources. Make the test hermetic " +
            "(loopback server / fake DNS), or — if it genuinely needs the live network — tag it " +
            "[Trait(\"Category\",\"LiveNetwork\")] and reference SharpTS.Tests.Infrastructure." +
            "LiveNetworkHosts.* instead of inlining the host.\n\n" +
            string.Join("\n", violations.Take(50)));
    }

    [Fact]
    public void FilesUsingLiveNetworkHosts_MustBeTaggedLiveNetwork()
    {
        var violations = new List<string>();

        foreach (var (relative, file) in EnumerateTestSources())
        {
            if (Allowlist.Contains(relative))
                continue;

            var text = File.ReadAllText(file);
            if (LiveNetworkHostUse.IsMatch(text) && !LiveNetworkTrait.IsMatch(text))
                violations.Add(relative);
        }

        Assert.True(
            violations.Count == 0,
            "These files use a LiveNetworkHosts constant but contain no " +
            "[Trait(\"Category\",\"LiveNetwork\")] — they would run (and can false-red) in CI. " +
            "Tag the live test(s) so CI's Category!=LiveNetwork filter excludes them:\n\n" +
            string.Join("\n", violations));
    }

    /// <summary>Enumerates (repo-relative path, absolute path) for every test .cs file, excluding bin/obj.</summary>
    private static IEnumerable<(string Relative, string Full)> EnumerateTestSources()
    {
        var repoRoot = RepoPaths.FindRepoRoot();
        var testsDir = Path.Combine(repoRoot, "tests", "SharpTS.Tests");
        foreach (var file in Directory.GetFiles(testsDir, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (relative.Contains("/obj/") || relative.Contains("/bin/"))
                continue;
            yield return (relative, file);
        }
    }

    private static bool IsExternalHost(string url)
    {
        var schemeIdx = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx < 0)
            return false;

        var host = url[(schemeIdx + 3)..].Split('/', '?', '#')[0];
        if (host.StartsWith('['))
        {
            var close = host.IndexOf(']');
            host = close > 1 ? host[1..close] : host;
        }
        else
        {
            var colon = host.IndexOf(':');
            if (colon >= 0)
                host = host[..colon];
        }

        host = host.Trim().ToLowerInvariant();
        return host is not ("localhost" or "127.0.0.1" or "::1" or "0.0.0.0" or "");
    }

}
