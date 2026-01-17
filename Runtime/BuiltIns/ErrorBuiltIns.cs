using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for JavaScript Error object members.
/// Handles property access (name, message, stack) and methods (toString).
/// </summary>
public static class ErrorBuiltIns
{
    /// <summary>
    /// Gets an instance member (property or method) for an Error object.
    /// </summary>
    public static object? GetMember(SharpTSError receiver, string name)
    {
        return name switch
        {
            "name" => receiver.Name,
            "message" => receiver.Message,
            "stack" => receiver.Stack,
            "toString" => new BuiltInMethod("toString", 0, (_, recv, _) =>
                ((SharpTSError)recv!).ToString()),

            // For AggregateError, also expose the errors property
            "errors" when receiver is SharpTSAggregateError aggregateError =>
                aggregateError.Errors,

            _ => null
        };
    }

    /// <summary>
    /// Sets a member property on an Error object.
    /// </summary>
    public static bool SetMember(SharpTSError receiver, string name, object? value)
    {
        switch (name)
        {
            case "name":
                receiver.Name = value?.ToString() ?? "";
                return true;
            case "message":
                receiver.Message = value?.ToString() ?? "";
                return true;
            case "stack":
                receiver.Stack = value?.ToString() ?? "";
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Creates an Error instance from constructor arguments.
    /// </summary>
    public static SharpTSError CreateError(string errorType, List<object?> args)
    {
        var message = args.Count > 0 ? args[0]?.ToString() : null;

        return errorType switch
        {
            "Error" => new SharpTSError(message),
            "TypeError" => new SharpTSTypeError(message),
            "RangeError" => new SharpTSRangeError(message),
            "ReferenceError" => new SharpTSReferenceError(message),
            "SyntaxError" => new SharpTSSyntaxError(message),
            "URIError" => new SharpTSURIError(message),
            "EvalError" => new SharpTSEvalError(message),
            "AggregateError" => CreateAggregateError(args),
            _ => new SharpTSError(message)
        };
    }

    /// <summary>
    /// Creates an AggregateError with errors array and optional message.
    /// </summary>
    private static SharpTSAggregateError CreateAggregateError(List<object?> args)
    {
        SharpTSArray errors;
        if (args.Count > 0)
        {
            if (args[0] is SharpTSArray arr)
            {
                errors = arr;
            }
            else if (args[0] is IList<object?> list)
            {
                errors = new SharpTSArray(list.ToList());
            }
            else if (args[0] is System.Collections.IList objList)
            {
                // Handle List<object> from compiled code
                var elements = new List<object?>();
                foreach (var item in objList)
                    elements.Add(item);
                errors = new SharpTSArray(elements);
            }
            else
            {
                errors = new SharpTSArray([]);
            }
        }
        else
        {
            errors = new SharpTSArray([]);
        }

        var message = args.Count > 1 ? args[1]?.ToString() : null;

        return new SharpTSAggregateError(errors, message);
    }
}
