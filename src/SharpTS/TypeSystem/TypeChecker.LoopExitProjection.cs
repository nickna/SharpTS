namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    // A nested loop's resolved exit can contain values introduced on its own backedge.
    // Lift that transfer into the parent's input namespace instead of retaining the
    // first checked inner-iteration copy. Only immutable origin graphs are transformed.
    private static LoopOriginSnapshot[] ProjectLoopParentOrigins(LoopValueDomain domain, ValueFlowState state,
        IReadOnlyDictionary<BindingSymbol, TypeInfo> types)
    {
        var own = state.LoopOrigins.FirstOrDefault(snapshot => ReferenceEquals(snapshot.Owner, domain));
        if (own is null) return state.LoopOrigins;
        var backedges = domain.Backedges.Select(edge => edge.LoopOrigins
            .FirstOrDefault(snapshot => ReferenceEquals(snapshot.Owner, domain))).Where(snapshot => snapshot is not null).ToArray();
        return state.LoopOrigins.Select(Project).ToArray();

        LoopOriginSnapshot Project(LoopOriginSnapshot parent)
        {
            var entry = domain.Incoming.LoopOrigins.FirstOrDefault(snapshot => ReferenceEquals(snapshot.Owner, parent.Owner));
            if (entry is null) return parent;
            Dictionary<BindingSymbol, LoopValueOrigin> resolved = new(ReferenceEqualityComparer.Instance);
            HashSet<BindingSymbol> visiting = new(ReferenceEqualityComparer.Instance);
            Dictionary<LoopValueOrigin, LoopValueOrigin> lifted = new(ReferenceEqualityComparer.Instance);
            var origins = new Dictionary<BindingSymbol, LoopValueOrigin>(parent.Origins, ReferenceEqualityComparer.Instance);
            foreach (var (symbol, origin) in own.Origins)
                if (origins.ContainsKey(symbol)) origins[symbol] = Lift(origin);
            return new(parent.Owner, origins);

            LoopValueOrigin Input(BindingSymbol symbol)
            {
                if (resolved.TryGetValue(symbol, out var existing)) return existing;
                var initial = entry.Origins.GetValueOrDefault(symbol) ?? new LoopComputedValue(
                    domain.Incoming.Bindings.GetValueOrDefault(symbol)?.Type ?? TypeInfo.Any.Shared, [], Unknown: true);
                if (backedges.All(edge => edge!.Origins.GetValueOrDefault(symbol) is LoopCopiedValue copy &&
                    ReferenceEquals(copy.Source, symbol))) return resolved[symbol] = initial;
                if (!visiting.Add(symbol))
                {
                    var reads = new LoopExpressionReads();
                    foreach (var dependency in visiting)
                        if (entry.Origins.TryGetValue(dependency, out var input))
                            AddLoopOriginReads(input, types.GetValueOrDefault(dependency) ?? TypeInfo.Any.Shared, reads);
                        else reads.Unknown = true;
                    return new LoopComputedValue(types.GetValueOrDefault(symbol) ?? TypeInfo.Any.Shared,
                        reads.Reads.ToArray(), reads.Unknown);
                }
                var values = new List<LoopValueOrigin> { initial };
                foreach (var edge in backedges)
                    if (edge!.Origins.TryGetValue(symbol, out var written)) values.Add(Lift(written));
                visiting.Remove(symbol);
                return resolved[symbol] = JoinLoopOrigins(values);
            }

            LoopValueOrigin Lift(LoopValueOrigin origin)
            {
                if (lifted.TryGetValue(origin, out var value)) return value;
                value = origin switch
                {
                    LoopCopiedValue copy => Input(copy.Source),
                    LoopFilteredValue filter => filter with { Value = Lift(filter.Value) },
                    LoopUnaryValue unary => unary with { Operand = Lift(unary.Operand) },
                    LoopBinaryValue binary => binary with { Left = Lift(binary.Left), Right = Lift(binary.Right) },
                    LoopCallValue call => call with { Callee = Lift(call.Callee), Arguments = call.Arguments.Select(Lift).ToArray() },
                    LoopLogicalValue logical => logical with { Left = Lift(logical.Left), Right = Lift(logical.Right) },
                    LoopChoiceValue choice => choice with { Condition = Lift(choice.Condition), Then = Lift(choice.Then), Else = Lift(choice.Else) },
                    LoopComputedValue computed => computed with { Reads = computed.Reads.Select(read => read with { Value = Lift(read.Value) }).ToArray() },
                    LoopJoinedValues joined => JoinLoopOrigins(joined.Values.Select(Lift)),
                    _ => origin
                };
                lifted[origin] = value;
                return value;
            }
        }
    }
}
