namespace SharpTS.TypeSystem.Exceptions;

/// <summary>
/// Base exception for all type checking errors.
/// Provides structured error information including optional line/column numbers.
/// </summary>
public class TypeCheckException : Exception
{
    /// <summary>
    /// Optional line number where the type error occurred.
    /// </summary>
    public int? Line { get; init; }

    /// <summary>
    /// Optional column number where the type error occurred.
    /// </summary>
    public int? Column { get; init; }

    public TypeCheckException(string message, int? line = null, int? column = null)
        : base(FormatMessage(message, line, column))
    {
        Line = line;
        Column = column;
    }

    public TypeCheckException(string message, Exception innerException, int? line = null, int? column = null)
        : base(FormatMessage(message, line, column), innerException)
    {
        Line = line;
        Column = column;
    }

    private static string FormatMessage(string message, int? line, int? column)
    {
        if (line.HasValue && column.HasValue)
            return $"Type Error at line {line}, column {column}: {message}";
        if (line.HasValue)
            return $"Type Error at line {line}: {message}";
        return $"Type Error: {message}";
    }
}
