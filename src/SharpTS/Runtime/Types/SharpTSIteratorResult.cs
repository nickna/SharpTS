using SharpTS.Execution;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents the result of a generator's next() method.
/// </summary>
/// <remarks>
/// Provides a typed host view of results returned by <see cref="SharpTSGenerator"/>.
/// Incomplete delegated results read their value and done properties lazily, invoking any
/// guest accessors. JavaScript consumers receive the original delegated object unchanged.
/// </remarks>
public class SharpTSIteratorResult : SharpTSObject
{
    private readonly object? _value;
    private readonly bool _done;
    private readonly Interpreter? _interpreter;
    internal object? DelegatedResult { get; }
    internal object GuestResult => DelegatedResult ?? this;

    public object? Value => DelegatedResult is { } result
        ? _interpreter!.GetPropertyValue(result, "value") : _value;
    public bool Done => DelegatedResult is { } result
        ? RuntimeValue.FromBoxed(_interpreter!.GetPropertyValue(result, "done")).IsTruthy() : _done;

    public SharpTSIteratorResult(object? value, bool done)
        : base(new Dictionary<string, object?>
        {
            ["value"] = value,
            ["done"] = done
        })
    {
        _value = value;
        _done = done;
    }

    // Preserve the typed host API without reading value or normalizing the
    // delegate's public result. The JavaScript boundary exposes GuestResult.
    internal SharpTSIteratorResult(Interpreter interpreter, object result)
        : this(SharpTSUndefined.Instance, done: false)
    {
        _interpreter = interpreter;
        DelegatedResult = result;
    }

    public override string ToString() =>
        $"{{ value: {Value ?? "undefined"}, done: {(Done ? "true" : "false")} }}";
}
