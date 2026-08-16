using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.DotNet;

/// <summary>
/// Translates .NET exceptions thrown from @DotNetType method calls into JavaScript-style
/// Error objects. Shared between interpreter and compiled-mode interop so user code
/// sees a consistent error hierarchy.
/// </summary>
/// <remarks>
/// Mapping rules (most specific first):
/// <list type="table">
///   <item><description>ArgumentNullException, ArgumentException → TypeError</description></item>
///   <item><description>InvalidCastException → TypeError</description></item>
///   <item><description>NullReferenceException → TypeError</description></item>
///   <item><description>IndexOutOfRangeException, ArgumentOutOfRangeException → RangeError</description></item>
///   <item><description>OverflowException, DivideByZeroException → RangeError</description></item>
///   <item><description>FormatException → SyntaxError</description></item>
///   <item><description>everything else → Error</description></item>
/// </list>
/// The original .NET exception is preserved as <c>cause</c> on the JS error so the
/// underlying stack trace remains inspectable from user code.
/// </remarks>
public static class DotNetExceptionMapper
{
    /// <summary>
    /// Unwraps <see cref="System.Reflection.TargetInvocationException"/> and converts
    /// the inner .NET exception to a JS-style <see cref="SharpTSError"/>.
    /// </summary>
    public static SharpTSError Map(Exception exception)
    {
        while (exception is System.Reflection.TargetInvocationException { InnerException: { } inner })
        {
            exception = inner;
        }

        string errorName = ClassifyAsJsErrorName(exception);
        var error = new SharpTSError(exception.Message)
        {
            Name = errorName,
            Cause = exception,
            HasCause = true
        };
        return error;
    }

    /// <summary>
    /// Returns the JavaScript error name (e.g. "TypeError") that best matches a
    /// given .NET exception. Exposed publicly so compiled-mode interop can emit
    /// the same classification if it wraps calls in a try/catch.
    /// </summary>
    public static string ClassifyAsJsErrorName(Exception exception) => exception switch
    {
        ArgumentNullException => "TypeError",
        ArgumentOutOfRangeException => "RangeError",
        ArgumentException => "TypeError",
        InvalidCastException => "TypeError",
        NullReferenceException => "TypeError",
        IndexOutOfRangeException => "RangeError",
        OverflowException => "RangeError",
        DivideByZeroException => "RangeError",
        FormatException => "SyntaxError",
        _ => "Error"
    };
}
