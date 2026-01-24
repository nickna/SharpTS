// =============================================================================
// DiagnosticCode.cs - Error classification codes
// =============================================================================
//
// Error codes for SharpTS diagnostics. These map to MSBuild error codes like:
//   SHARPTS001 - Type Error
//   SHARPTS101 - Type Mismatch (specific type error)
//   SHARPTS201 - Unexpected Token (specific parse error)
//   etc.
//
// Major categories (X00):
//   0XX - General
//   1XX - Type errors
//   2XX - Parse errors
//   3XX - Module errors
//   4XX - Compile errors
//   5XX - Config errors
//   6XX - Runtime errors
//
// =============================================================================

namespace SharpTS.Diagnostics;

/// <summary>
/// Error codes for SharpTS diagnostics.
/// </summary>
public enum DiagnosticCode
{
    // General (0XX)
    /// <summary>General/unclassified error.</summary>
    General = 0,

    // Type errors (1XX)
    /// <summary>Type checking error (general).</summary>
    TypeError = 1,
    /// <summary>Type mismatch - value not assignable to expected type.</summary>
    TypeMismatch = 101,
    /// <summary>Property or method doesn't exist on type.</summary>
    UndefinedMember = 102,
    /// <summary>Invalid function/method call.</summary>
    InvalidCall = 103,
    /// <summary>Invalid type operation (e.g., super outside class).</summary>
    TypeOperation = 104,

    // Parse errors (2XX)
    /// <summary>Parsing error (general).</summary>
    ParseError = 2,
    /// <summary>Unexpected token encountered.</summary>
    UnexpectedToken = 201,
    /// <summary>Syntax error.</summary>
    SyntaxError = 202,

    // Module errors (3XX)
    /// <summary>Module resolution error (general).</summary>
    ModuleError = 3,
    /// <summary>Module not found.</summary>
    ModuleNotFound = 301,
    /// <summary>Circular dependency detected.</summary>
    CircularDependency = 302,

    // Compile errors (4XX)
    /// <summary>IL compilation error (general).</summary>
    CompileError = 4,
    /// <summary>IL validation error.</summary>
    ILValidation = 401,

    // Config errors (5XX)
    /// <summary>Configuration error.</summary>
    ConfigError = 5,

    // Runtime errors (6XX)
    /// <summary>Runtime error (general).</summary>
    RuntimeError = 6,
    /// <summary>Division by zero.</summary>
    DivisionByZero = 601,
    /// <summary>Null reference.</summary>
    NullReference = 602,
    /// <summary>Index out of range.</summary>
    IndexOutOfRange = 603,
    /// <summary>Invalid operation.</summary>
    InvalidOperation = 604,
}
