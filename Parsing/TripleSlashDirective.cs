namespace SharpTS.Parsing;

/// <summary>
/// Types of triple-slash reference directives supported by TypeScript.
/// </summary>
public enum TripleSlashReferenceType
{
    /// <summary>
    /// /// &lt;reference path="..." /&gt; - references another script file.
    /// </summary>
    Path,

    /// <summary>
    /// /// &lt;reference types="..." /&gt; - references type declarations (future).
    /// </summary>
    Types,

    /// <summary>
    /// /// &lt;reference lib="..." /&gt; - references built-in lib declarations (future).
    /// </summary>
    Lib,

    /// <summary>
    /// /// &lt;reference no-default-lib="true" /&gt; - excludes default lib (future).
    /// </summary>
    NoDefaultLib
}

/// <summary>
/// Represents a parsed triple-slash directive from TypeScript source code.
/// </summary>
/// <remarks>
/// Triple-slash directives are special single-line comments at the top of a file
/// that instruct the compiler about file dependencies. They must appear before
/// any actual code (excluding other comments and triple-slash directives).
/// </remarks>
/// <param name="Type">The type of reference directive.</param>
/// <param name="Value">The value of the directive (file path, types name, lib name, etc.).</param>
/// <param name="Line">Line number where the directive appears (1-based).</param>
/// <param name="Column">Column number where the directive starts (1-based).</param>
public record TripleSlashDirective(
    TripleSlashReferenceType Type,
    string Value,
    int Line,
    int Column
);
