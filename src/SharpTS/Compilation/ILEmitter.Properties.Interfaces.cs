using System.Collections.Frozen;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    private bool TryGetInterfaceReadShape(
        TypeInfo.Interface type, out JsonSerializationShape.Record shape)
    {
        shape = null!;
        if (_ctx.RuntimeFeatures is not { } features ||
            type.HasIndexSignature || type.IsCallable || type.IsConstructable ||
            type.GetAllOptionalMembers().Any())
            return false;

        // GetAllMembers may repeat inherited members in a diamond. Own members
        // come first and shadow inherited declarations.
        var members = type.GetAllMembers().GroupBy(member => member.Key)
            .ToFrozenDictionary(group => group.Key, group => group.First().Value);
        if (!JsonSerializationShapeAnalyzer.TryAnalyze(new TypeInfo.Record(members), out var analyzed) ||
            analyzed is not JsonSerializationShape.Record declared)
            return false;

        foreach (var candidate in features.CompactObjectRecordShapes.Values)
        {
            if (candidate.Fields.Count != declared.Fields.Count ||
                !candidate.Fields.All(field => declared.Fields.Any(other =>
                    other.Key == field.Key &&
                    StorageKind(other.Value) == StorageKind(field.Value))))
                continue;

            shape = candidate;
            return true;
        }
        return false;
    }

    // Nested arrays/records occupy object slots regardless of their inferred
    // element shape (e.g. a literal's (2|3)[] versus the interface's number[]).
    // Matching storage is sufficient: reads still guard the exact carrier type.
    private static int StorageKind(JsonSerializationShape shape) => shape switch
    {
        JsonSerializationShape.Number => 1,
        JsonSerializationShape.String => 2,
        JsonSerializationShape.Boolean => 3,
        _ => 0
    };
}
