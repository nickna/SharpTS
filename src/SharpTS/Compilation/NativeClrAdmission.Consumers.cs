using SharpTS.Parsing;

namespace SharpTS.Compilation;

internal sealed partial class NativeClrAdmission
{
    internal enum InputConversion { Argument, Receiver }
    private sealed record Consumer(Expr Operation, Expr Operand, Type Target, InputConversion Conversion, StackType Storage);
    private readonly List<Consumer> _consumers = [];
    // Inventory observations are deliberately not positive CLR producer evidence.
    private readonly HashSet<Expr> _clrProducers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr> _scalarResults = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr, HashSet<Expr>> _validatedInputs = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr, HashSet<Expr>> _metadataEdges = new(ReferenceEqualityComparer.Instance);

    internal void RecordInput(Expr? operation, Expr? operand, Type target, InputConversion conversion, StackType storage)
    {
        EnsureOpen();
        if (operation is null || operand is null || Source(operation) is not Expr owner || Source(operand) is not Expr value ||
            !_parents.ContainsKey(owner) || !_parents.ContainsKey(value) || _sharedSourceUses.Contains(value))
        {
            Fail("CLR conversion has no unambiguous original source operand", operation);
            return;
        }
        _consumers.Add(new(owner, value, target, conversion, storage));
    }

    internal void RefuseUnclassifiedInput(Expr? operation)
    {
        EnsureOpen();
        Fail("overloaded CLR member update has unclassified staged input conversions", operation);
    }

    private void CheckConsumerConversions()
    {
        foreach (var input in _consumers)
        {
            bool safe;
            if (input.Conversion == InputConversion.Argument && input.Target.IsArray)
                safe = IsIndependentScalarArray(input.Target);
            else if (input.Conversion == InputConversion.Argument && typeof(Delegate).IsAssignableFrom(input.Target))
                safe = true; // Actual adapter creation checks both ingress and return slots.
            else safe = IsScalarEnforcingConversion(input.Target) ||
                input.Storage is StackType.Double or StackType.Boolean or StackType.String or StackType.Null ||
                HasClrProducer(input.Operand, new(ReferenceEqualityComparer.Instance)) ||
                IsScalarValue(input.Operand, new(ReferenceEqualityComparer.Instance));
            if (!safe)
            {
                Fail($"unproved guest reference pass-through to CLR {input.Conversion.ToString().ToLowerInvariant()} slot {input.Target.FullName}", input.Operand);
                continue;
            }
            AddEdge(_validatedInputs, input.Operation, input.Operand);
        }
    }

    private static bool IsScalarEnforcingConversion(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying) return IsScalarEnforcingConversion(underlying);
        return type == typeof(string) || type == typeof(bool) || type == typeof(double) || type == typeof(float) ||
            type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
            type == typeof(decimal) || type == typeof(char) || type.IsEnum;
    }

    private static bool IsIndependentScalarArray(Type type) => type.IsArray && type.GetArrayRank() == 1 &&
        (type.GetElementType()!.IsArray ? IsIndependentScalarArray(type.GetElementType()!) : IsScalarEnforcingConversion(type.GetElementType()!));

    private bool HasClrProducer(Expr expression, HashSet<object> seen)
    {
        expression = (Expr)Source(expression);
        if (!seen.Add(expression)) return false;
        if (_clrProducers.Contains(expression)) return true;
        if (TransparentOperand(expression) is { } operand) return HasClrProducer(operand, seen);
        return expression is Expr.Variable variable && SourceBinding(variable.Name) is { } binding &&
            _constants.TryGetValue(binding, out var declaration) && !_exports.Contains(binding) && HasClrProducer(declaration.Initializer, seen);
    }

    private bool IsScalarValue(Expr expression, HashSet<object> seen)
    {
        expression = (Expr)Source(expression);
        if (!seen.Add(expression)) return false;
        if (expression is Expr.Literal { Value: null or string or bool or double or int } || _scalarResults.Contains(expression)) return true;
        if (TransparentOperand(expression) is { } operand) return IsScalarValue(operand, seen);
        return expression is Expr.Variable variable && SourceBinding(variable.Name) is { } binding &&
            _constants.TryGetValue(binding, out var declaration) && !_exports.Contains(binding) && IsScalarValue(declaration.Initializer, seen);
    }

    private static Expr? TransparentOperand(Expr expression) => expression switch
    {
        Expr.Grouping value => value.Expression,
        Expr.TypeAssertion value => value.Expression,
        Expr.Satisfies value => value.Expression,
        Expr.NonNullAssertion value => value.Expression,
        Expr.Instantiation value => value.Expression,
        _ => null
    };

    private bool HasValidatedInput(Expr parent, Expr operand)
    {
        if (_metadataEdges.TryGetValue(parent, out var metadata) && metadata.Contains(operand)) return true;
        var operation = parent is Expr.Get && _parents.GetValueOrDefault(parent) is Expr.Call call && ReferenceEquals(call.Callee, parent)
            ? call : parent;
        return _validatedInputs.TryGetValue(operation, out var inputs) && inputs.Contains(operand);
    }

    private static void AddEdge(Dictionary<Expr, HashSet<Expr>> map, Expr parent, Expr child)
    {
        if (!map.TryGetValue(parent, out var values)) map[parent] = values = new(ReferenceEqualityComparer.Instance);
        values.Add(child);
    }
}
