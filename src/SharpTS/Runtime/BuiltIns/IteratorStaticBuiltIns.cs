using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Static methods for the Iterator namespace (ES2025).
/// Currently provides Iterator.from() to wrap iterables into proper iterators with helper methods.
/// </summary>
public static class IteratorStaticBuiltIns
{
    private static readonly BuiltInStaticMemberLookup _staticLookup =
        BuiltInStaticBuilder.Create()
            .MethodV2("from", 1, FromV2)
            .Build();

    public static object? GetStaticMethod(string name)
        => _staticLookup.GetMember(name);

    private static object? From(Interpreter interp, List<object?> args)
    {
        var value = args.Count > 0 ? args[0] : null;

        if (value is SharpTSIterator iter)
            return iter;

        if (value is SharpTSGenerator gen)
            return new SharpTSIterator(GeneratorToEnumerable(gen));

        if (value is SharpTSArray arr)
            return new SharpTSIterator(arr);

        if (value is SharpTSSet set)
            return new SharpTSIterator(set.Values().Elements);

        if (value is SharpTSMap map)
            return new SharpTSIterator(map.Entries().Elements);

        throw new Exception("TypeError: Iterator.from requires an iterable or iterator-like argument.");
    }

    private static IEnumerable<object?> GeneratorToEnumerable(SharpTSGenerator gen)
    {
        while (true)
        {
            var result = gen.Next();
            if (result.Done) yield break;
            yield return result.Value;
        }
    }

    private static RuntimeValue FromV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
    {
        var list = new List<object?>(args.Length);
        foreach (var arg in args) list.Add(arg.ToObject());
        return RuntimeValue.FromBoxed(From(interp, list));
    }
}
