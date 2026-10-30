using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    private static readonly string[] DateConstructorInitialPropertyNames =
        ["length", "name", "prototype", "now", "parse", "UTC"];

    private static readonly string[] ConstructorInitialMetadataNames = ["length", "name", "prototype"];

    private static bool IsIntrinsicDateConstructor(SharpTSBuiltInConstructor constructor)
        => ReferenceEquals(_globalConstants[BuiltInNames.Date], constructor);

    internal bool HasBuiltInConstructorInitialInventory(SharpTSBuiltInConstructor constructor)
        => IsIntrinsicDateConstructor(constructor) || GetPromiseConstructorState(constructor) is not null;

    private double? GetBuiltInConstructorInitialLength(SharpTSBuiltInConstructor constructor)
        => IsIntrinsicDateConstructor(constructor) ? 7d
            : GetPromiseConstructorState(constructor) is not null ? 1d : null;

    private IEnumerable<string> GetBuiltInConstructorInitialPropertyNames(SharpTSBuiltInConstructor constructor)
    {
        if (IsIntrinsicDateConstructor(constructor)) return DateConstructorInitialPropertyNames;
        if (GetPromiseConstructorState(constructor) is not null)
            return ConstructorInitialMetadataNames.Concat(PromiseBuiltIns.StaticMemberNames);
        return [];
    }

    private bool IsBuiltInConstructorInitialProperty(SharpTSBuiltInConstructor constructor, string name)
        => GetBuiltInConstructorInitialPropertyNames(constructor).Contains(name, StringComparer.Ordinal);

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
        IEnumerable<string> keys = GetBuiltInConstructorInitialPropertyNames(constructor).Where(name =>
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