using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    private static readonly string[] DateConstructorInitialPropertyNames =
        ["length", "name", "prototype", "now", "parse", "UTC"];

    internal bool HasBuiltInConstructorInitialInventory(SharpTSBuiltInConstructor constructor)
        => ReferenceEquals(_globalConstants[BuiltInNames.Date], constructor);

    private double? GetBuiltInConstructorInitialLength(SharpTSBuiltInConstructor constructor)
        => HasBuiltInConstructorInitialInventory(constructor) ? 7d : null;

    private bool IsBuiltInConstructorInitialProperty(SharpTSBuiltInConstructor constructor, string name)
        => HasBuiltInConstructorInitialInventory(constructor)
            && DateConstructorInitialPropertyNames.Contains(name, StringComparer.Ordinal);

    internal bool TryGetBuiltInConstructorOwnStringKeys(
        SharpTSBuiltInConstructor constructor, out string[] names)
    {
        if (!HasBuiltInConstructorInitialInventory(constructor))
        {
            names = [];
            return false;
        }

        // Initial names are backed by this actual intrinsic's existing own
        // descriptors. A deleted virtual slot stays absent when an overlay
        // recreates it at a later stored-property creation position.
        IEnumerable<string> keys = DateConstructorInitialPropertyNames.Where(name =>
            !IsBuiltInConstructorPropertyDeleted(constructor, name)
                && HasBuiltInConstructorOwnProperty(constructor, name));
        if (GetConstructorPropertyTable(constructor)?.TryGetValue(constructor, out var properties) == true)
            keys = keys.Concat(properties.Keys);
        names = keys.Distinct(StringComparer.Ordinal)
            .OrderBy(name => PropertySemantics.TryGetArrayIndex(name, out uint index)
                ? (ulong)index : ulong.MaxValue).ToArray();
        return true;
    }
}