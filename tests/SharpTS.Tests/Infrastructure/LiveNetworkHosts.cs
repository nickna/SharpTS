namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// The single sanctioned home for external (real-internet) host literals used by
/// tests that genuinely exercise live network resolution.
/// </summary>
/// <remarks>
/// <para>
/// Tests that issue a real outbound DNS/HTTP call are inherently non-hermetic: they
/// depend on the CI runner's network, which makes them a recurring false-red source
/// (see issue #495). Such tests MUST be tagged <c>[Trait("Category", "LiveNetwork")]</c>
/// — which CI excludes via <c>--filter "Category!=LiveNetwork"</c> — and they reference
/// the host names from this class rather than inlining literals.
/// </para>
/// <para>
/// The <see cref="CompilerTests.LiveNetworkHermeticityTests"/> guardrail enforces this:
/// it fails the build if an external host literal (e.g. <c>google.com</c>, a
/// <c>*.example</c> name, or a non-loopback <c>fetch('http://…')</c>) appears anywhere
/// outside this file and the loopback test infrastructure. Keeping the literals here
/// centralizes the (small, deliberate) live-network surface so it can be audited and so
/// hermetic tests can never accidentally depend on the real internet.
/// </para>
/// </remarks>
public static class LiveNetworkHosts
{
    /// <summary>
    /// A stable public domain that reliably has MX/NS records, for live DNS smoke tests.
    /// </summary>
    public const string Stable = "google.com";

    /// <summary>
    /// A name that does not resolve, for live DNS error-path smoke tests. Relies on the
    /// resolver returning NXDOMAIN (the <c>.example</c> TLD is reserved by RFC 2606, but a
    /// captive portal / NXDOMAIN-hijacking resolver can still answer — hence LiveNetwork).
    /// </summary>
    public const string Nonexistent = "this.hostname.definitely.does.not.exist.example";
}
