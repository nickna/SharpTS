using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for JavaScript Error object members.
/// Handles property access (name, message, stack) and methods (toString).
/// This class is the central source of truth for Error type knowledge.
/// </summary>
/// <remarks>
/// IMPORTANT: This class must be kept in sync with BuiltInTypes.RegisterErrorTypes().
/// When adding new Error types, update both TypeNames here and the registration in BuiltInTypes.
/// </remarks>
public static class ErrorBuiltIns
{
    /// <summary>
    /// The set of all built-in JavaScript Error type names.
    /// Used by TypeChecker, Interpreter, and ILEmitter to identify Error types.
    /// </summary>
    public static readonly HashSet<string> TypeNames = new(StringComparer.Ordinal)
    {
        "Error", "TypeError", "RangeError", "ReferenceError",
        "SyntaxError", "URIError", "EvalError", "AggregateError"
    };

    /// <summary>
    /// Checks if a name is a built-in JavaScript Error type name.
    /// </summary>
    /// <param name="name">The type name to check.</param>
    /// <returns>True if the name is a built-in Error type; otherwise false.</returns>
    public static bool IsErrorTypeName(string name) => TypeNames.Contains(name);

    /// <summary>
    /// The set of mutable property names on Error objects.
    /// These properties (name, message, stack) can be assigned new values.
    /// </summary>
    public static readonly HashSet<string> MutableProperties = new(StringComparer.Ordinal)
    {
        "name", "message", "stack"
    };

    /// <summary>
    /// Checks if a property on an Error object can be set (is mutable).
    /// </summary>
    /// <param name="propertyName">The property name to check.</param>
    /// <returns>True if the property can be set; otherwise false.</returns>
    public static bool CanSetProperty(string propertyName) => MutableProperties.Contains(propertyName);

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
