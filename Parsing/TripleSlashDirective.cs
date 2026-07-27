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
    /// /// &lt;reference types="..." /&gt; - references a visible declaration package.
    /// </summary>
    Types,

    /// <summary>
    /// /// &lt;reference lib="..." /&gt; - references an installed TypeScript lib declaration.
    /// </summary>
    Lib,

    /// <summary>
    /// /// &lt;reference no-default-lib="true" /&gt; - excludes TypeScript's default lib set.
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
