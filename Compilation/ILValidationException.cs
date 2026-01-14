namespace SharpTS.Compilation;

/// <summary>
/// Exception thrown when IL validation fails during emit.
/// </summary>
/// <remarks>
/// This exception is thrown by <see cref="ValidatedILBuilder"/> when it detects
/// an invalid IL emission pattern at compile time. The error is caught early,
/// before the generated assembly would fail PEVerify or crash at runtime.
///
/// <para>Common causes:</para>
/// <list type="bullet">
/// <item>A label was defined but never marked (forgotten MarkLabel call)</item>
/// <item>Stack depth mismatch at a branch target (inconsistent code paths)</item>
/// <item>Using Br inside an exception block (should use Leave)</item>
/// <item>Using Leave outside an exception block</item>
/// <item>Attempting to Box a reference type</item>
/// <item>Attempting to Unbox a value type</item>
/// <item>Insufficient values on the stack for an operation</item>
/// </list>
/// </remarks>
public class ILValidationException : Exception
{
    /// <summary>
    /// Creates a new IL validation exception.
    /// </summary>
    /// <param name="message">Description of the validation failure.</param>
    public ILValidationException(string message)
        : base($"IL Validation Error: {message}")
    {
    }

    /// <summary>
    /// Creates a new IL validation exception with an inner exception.
    /// </summary>
    /// <param name="message">Description of the validation failure.</param>
    /// <param name="innerException">The underlying exception.</param>
    public ILValidationException(string message, Exception innerException)
        : base($"IL Validation Error: {message}", innerException)
    {
    }
}
