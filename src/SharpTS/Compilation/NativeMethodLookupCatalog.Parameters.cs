using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

internal sealed partial class NativeMethodLookupCatalog
{
    private readonly List<(MethodBuilder Producer, ILGenerator Body, int Offset, Type Owner, BindingFlags Flags, string? Name)> _queryReceipts = [];
    private NativeCompletedMethodQueries? _completedQueries;

    internal NativeCompletedMethodQueries CompletedQueries => IsComplete ? _completedQueries! :
        throw new InvalidOperationException("Native method query producers have not completed.");

    private void RecordQuery(MethodBuilder producer, ILGenerator body, int offset, Type owner, BindingFlags flags, string? name)
    {
        if (IsComplete || !ReferenceEquals(producer.Module, _owner.Module) ||
            !ReferenceEquals(producer.GetILGenerator(), body) || offset < 0 || body.ILOffset <= offset)
            throw new InvalidOperationException("Native method receipts require the actual literal query operation.");
        _queryReceipts.Add((producer, body, offset, owner, flags, name));
    }

    private void CompleteQueryReceipts()
    {
        var rows = new List<NativeCompletedMethodQuery>();
        var blockers = new List<string>();
        foreach (var query in _queryReceipts)
        {
            if (query.Body.ILOffset <= query.Offset ||
                query.Owner is TypeBuilder builder && !builder.IsCreated())
                throw new InvalidOperationException("Native method query bodies and literal owners must complete first.");
            // This is the full result domain of the actual literal query. SafeGetMethod's
            // ambiguity handling may select any name-matching member from this array.
            // No replacement method or parameter metadata is emitted from this snapshot.
            try
            {
                var members = _types.GetMethods(query.Owner, query.Flags)
                    .Where(method => query.Name is null || method.Name == query.Name).ToArray();
                rows.Add(new(query.Producer, query.Offset, query.Body.ILOffset, query.Owner,
                    query.Flags, query.Name, Array.AsReadOnly(members)));
            }
            catch (NotSupportedException)
            {
                // A closed builder instantiation may not expose its members here.
                // Preserve the query and force this consumer's original endpoint.
                blockers.Add("Unreadable actual native method query owner: " + query.Owner);
            }
        }
        _completedQueries = new(_owner.Module, UsesLegacy, rows.AsReadOnly(), blockers.AsReadOnly());
    }
}

internal sealed record NativeCompletedMethodQuery(MethodBuilder Producer, int CallOffset, int CompletedBodyLength,
    Type Owner, BindingFlags Flags, string? LiteralName, IReadOnlyList<MethodInfo> Members);
internal sealed record NativeCompletedMethodQueries(Module Module, bool UsesLegacy, IReadOnlyList<NativeCompletedMethodQuery> Rows, IReadOnlyList<string> Blockers);
