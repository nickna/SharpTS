namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents the result of a generator's next() method.
/// </summary>
/// <remarks>
/// Matches TypeScript's IteratorResult&lt;T&gt; interface with { value: T, done: boolean }.
/// Used by <see cref="SharpTSGenerator"/> to return values from next() calls.
/// </remarks>
public class SharpTSIteratorResult : SharpTSObject
{
    public object? Value { get; }
    public bool Done { get; }

    public SharpTSIteratorResult(object? value, bool done)
        : base(new Dictionary<string, object?>
        {
            ["value"] = value,
            ["done"] = done
        })
    {
        Value = value;
        Done = done;
    }

    public override string ToString() =>
        $"{{ value: {Value ?? "undefined"}, done: {(Done ? "true" : "false")} }}";
}
