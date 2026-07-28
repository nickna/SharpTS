namespace SharpTS.LanguageServer;

/// <summary>
/// Which language features the server advertises and serves.
/// </summary>
/// <remarks>
/// <para>In VS Code, TypeScript files are already served by <c>tsserver</c>, which does ordinary
/// navigation better than SharpTS could and would collide with it. What SharpTS knows that
/// <c>tsserver</c> does not is the .NET interop surface, so the extension launches the server in
/// <see cref="InteropOnly"/> and contributes only that. A standalone client — Neovim, Helix, an
/// editor with no TypeScript support of its own — has no such other server, so the tool defaults to
/// <see cref="Full"/>.</para>
///
/// <para>The mode is fixed when the server starts. Capabilities are advertised during
/// initialization, and changing what a server serves afterwards requires dynamic
/// registration/unregistration, which this server does not implement — reading a setting later
/// cannot retract a capability a client has already been told about. Changing the mode therefore
/// means restarting the server.</para>
/// </remarks>
public enum LanguageFeatureMode
{
    /// <summary>
    /// Interop diagnostics, hover, completion, signature help, and SharpTS-specific code actions
    /// only. General navigation is left to whatever else serves the file.
    /// </summary>
    InteropOnly,

    /// <summary>
    /// Everything <see cref="InteropOnly"/> serves, plus general navigation: document symbols, and
    /// the definition, references and rename capabilities as they land.
    /// </summary>
    Full,
}
