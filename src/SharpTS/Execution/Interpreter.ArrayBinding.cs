using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    private sealed class ArrayBindingCursor(object iterator, object? next)
    {
        internal readonly object Iterator = iterator;
        internal readonly object? Next = next;
        internal bool Done;
    }

    internal object? ExecuteArrayBinding(IReadOnlyList<object?> args)
    {
        switch ((string)args[0]!)
        {
            case "scope": return new List<ArrayBindingCursor>();
            case "start":
            {
                var scope = (List<ArrayBindingCursor>)args[1]!;
                var source = args[2];
                // A getter may remove its own descriptor. Retain the guest claim
                // before lookup so that mutation cannot restore a native bridge.
                bool hasIteratorClaim = source is SharpTSTypedArray or SharpTSBuffer or SharpTSIterator
                    && ArrayBindingHasIteratorClaim(source);
                object? factory = source switch
                {
                    bool => GetSymbolPropertyValue(GetBooleanPrototype(), SharpTSSymbol.Iterator, source),
                    double => GetSymbolPropertyValue(GetNumberPrototype(), SharpTSSymbol.Iterator, source),
                    string => GetSymbolPropertyValue(GetStringPrototype(), SharpTSSymbol.Iterator, source),
                    null or SharpTSUndefined => SharpTSUndefined.Instance,
                    _ => GetSymbolPropertyValue(source, SharpTSSymbol.Iterator)
                };
                object? iterator;
                if (factory is SharpTSUndefined && source is SharpTSTypedArray or SharpTSBuffer or SharpTSIterator
                    && !hasIteratorClaim)
                {
                    iterator = source switch
                    {
                        SharpTSTypedArray typed => new SharpTSIterator(
                            Enumerable.Range(0, typed.Length).Select(index => typed[index])),
                        SharpTSBuffer buffer => new SharpTSIterator(buffer.Data.Select(b => (object?)(double)b)),
                        _ => source
                    };
                }
                else
                {
                    if (factory is not ISharpTSCallable callable)
                        throw new ThrowException(new SharpTSTypeError("source is not iterable"));
                    iterator = FunctionBuiltIns.CallWithThis(this, callable, source, []);
                }
                if (!RuntimeValue.FromBoxed(iterator).IsObject)
                    throw new ThrowException(new SharpTSTypeError("Iterator must be an object"));
                // A noncallable next fails on stepping, not on acquisition (even []
                // acquires next before closing, but it does not invoke that value).
                var cursor = new ArrayBindingCursor(iterator!, GetPropertyValue(iterator, "next"));
                scope.Add(cursor);
                return cursor;
            }
            case "next": return StepArrayBinding((ArrayBindingCursor)args[1]!, (bool)args[3]!);
            case "rest":
            {
                var cursor = (ArrayBindingCursor)args[1]!;
                var result = new List<object?>();
                while (!cursor.Done)
                {
                    object? value = StepArrayBinding(cursor, true);
                    if (!cursor.Done) result.Add(value);
                }
                return new SharpTSArray(result);
            }
            case "close": CloseArrayBinding((ArrayBindingCursor)args[1]!, false); break;
            case "abort": CloseArrayBindingScope((List<ArrayBindingCursor>)args[1]!, true); break;
            case "finish": CloseArrayBindingScope((List<ArrayBindingCursor>)args[1]!, false); break;
            default: throw new InvalidOperationException("Unknown array binding operation");
        }
        return SharpTSUndefined.Instance;
    }

    private bool ArrayBindingHasIteratorClaim(object source)
    {
        object? owner = source;
        for (int depth = 0; depth < 64 && owner is not null and not SharpTSUndefined; depth++)
        {
            if (owner is SharpTSProxy || ObjectBuiltIns.RuntimeGetOwnPropertyDescriptorInRealm(
                    this, owner, SharpTSSymbol.Iterator) is not (null or SharpTSUndefined))
                return true;
            owner = ObjectBuiltIns.PrototypeOf(this, owner);
        }
        return false;
    }

    private object? StepArrayBinding(ArrayBindingCursor cursor, bool readValue)
    {
        if (cursor.Done) return SharpTSUndefined.Instance;
        // IteratorStepValue failures complete this cursor. An elision executes
        // IteratorStep without observing the value getter.
        cursor.Done = true;
        ThrowIfExecutionCancelled();
        if (cursor.Next is not ISharpTSCallable callable)
            throw new ThrowException(new SharpTSTypeError("Iterator next must be callable"));
        object? result = FunctionBuiltIns.CallWithThis(this, callable, cursor.Iterator, []);
        if (!RuntimeValue.FromBoxed(result).IsObject)
            throw new ThrowException(new SharpTSTypeError("Iterator result must be an object"));
        if (IsTruthy(GetPropertyValue(result, "done"))) return SharpTSUndefined.Instance;
        object? value = readValue ? GetPropertyValue(result, "value") : SharpTSUndefined.Instance;
        cursor.Done = false;
        return value;
    }

    private void CloseArrayBinding(ArrayBindingCursor cursor, bool preserveThrow)
    {
        if (cursor.Done) return;
        cursor.Done = true;
        CloseIterator(cursor.Iterator, preserveThrow);
    }

    private void CloseArrayBindingScope(List<ArrayBindingCursor> scope, bool preserveThrow)
    {
        try
        {
            for (int index = scope.Count - 1; index >= 0; index--)
                CloseArrayBinding(scope[index], preserveThrow);
        }
        catch
        {
            // A close failure replaces a return completion. Finish closing the
            // enclosing cursors with that failure as the incoming throw.
            for (int index = scope.Count - 1; index >= 0; index--)
                CloseArrayBinding(scope[index], true);
            throw;
        }
    }
}
