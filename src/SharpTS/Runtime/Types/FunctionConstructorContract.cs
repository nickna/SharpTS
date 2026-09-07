namespace SharpTS.Runtime.Types;

internal static class FunctionConstructorContract
{
    // A bounded package-compatibility grammar, not a general source evaluator.
    // ECMAScript whitespace is explicit: .NET \s also accepts non-JS whitespace.
    // A line terminator between return and this would change the return via ASI.
    private const string Space = @"[\t\v\f \u00a0\u1680\u2000-\u200a\u202f\u205f\u3000\ufeff]";
    private const string Trivia = @"[\t\v\f \r\n\u2028\u2029\u00a0\u1680\u2000-\u200a\u202f\u205f\u3000\ufeff]*";
    internal const string ReturnThisPattern = @"\A" + Trivia + "return" + Space + "+this" + Trivia + ";?" + Trivia + @"\z";

    internal const string UnsupportedSourceMessage =
        "Runtime Error: Dynamic Function() construction with source text is not supported. " +
        "Only zero arguments or a single 'return this' body (whitespace and an optional semicolon) are supported.";
}
