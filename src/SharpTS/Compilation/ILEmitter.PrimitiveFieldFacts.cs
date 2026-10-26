using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    private readonly HashSet<(object Receiver, MethodBuilder Getter)> _nativePrimitiveFieldFacts = [];

    public override void EmitExpression(Expr expression)
    {
        // A fact covers one immediate, effect-free read after a native store.
        // Calls, writes, joins and all unmodeled expressions invalidate it.
        if (expression is not (Expr.Literal or Expr.Variable or Expr.This or Expr.Get
            or Expr.Binary or Expr.Grouping or Expr.TypeAssertion or Expr.Satisfies
            or Expr.NonNullAssertion))
            _nativePrimitiveFieldFacts.Clear();
        base.EmitExpression(expression);
    }

    private object? PrimitiveFieldReceiverIdentity(Expr receiver)
    {
        if (receiver is Expr.This && _ctx.IsInstanceMethod && !_ctx.HasGuestReceiver)
            return this;
        if (receiver is not Expr.Variable variable)
            return null;
        if (_ctx.Locals.TryGetLocal(variable.Name.Lexeme, out var local))
            return local;
        if (_ctx.TryGetParameter(variable.Name.Lexeme, out int index))
            return index;
        return null;
    }

    private bool HasNominalClassCarrier(Expr receiver)
    {
        if (receiver is Expr.This)
            return _ctx.IsInstanceMethod && !_ctx.HasGuestReceiver;
        if (receiver is Expr.Variable variable)
        {
            if (_ctx.Locals.TryGetLocal(variable.Name.Lexeme, out var local))
                return _ctx.TypeMapper.IsUserClassReferenceType(local.LocalType);
            if (_ctx.TryGetParameterType(variable.Name.Lexeme, out var parameter) && parameter is not null)
                return _ctx.TypeMapper.IsUserClassReferenceType(parameter);
        }
        return false;
    }

    private bool ConsumeNativePrimitiveFieldFact(Expr receiver, MethodBuilder getter)
    {
        object? key = PrimitiveFieldReceiverIdentity(receiver);
        bool proven = key is not null && _nativePrimitiveFieldFacts.Contains((key, getter));
        _nativePrimitiveFieldFacts.Clear();
        return proven;
    }

    private bool NativeFieldFactsCover(Expr expression) => expression switch
    {
        Expr.Literal => true,
        Expr.Variable variable =>
            (_ctx.Locals.TryGetLocal(variable.Name.Lexeme, out var local) &&
                (local.LocalType == _ctx.Types.Double || local.LocalType == _ctx.Types.Boolean || local.LocalType == _ctx.Types.String)) ||
            (_ctx.TryGetParameterType(variable.Name.Lexeme, out var type) &&
                (type == _ctx.Types.Double || type == _ctx.Types.Boolean || type == _ctx.Types.String)),
        Expr.Grouping grouping => NativeFieldFactsCover(grouping.Expression),
        Expr.Binary binary => NativeFieldFactsCover(binary.Left) && NativeFieldFactsCover(binary.Right),
        Expr.Get get => HasNativePromotedField(get) ||
            (PrimitiveFieldReceiverIdentity(get.Object) is { } receiver &&
                _nativePrimitiveFieldFacts.Any(fact => Equals(fact.Receiver, receiver) &&
                    fact.Getter.Name == "get_" + NamingConventions.ToPascalCase(get.Name.Lexeme))),
        _ => false
    };

    private bool HasNativePromotedField(Expr.Get get) => get.Object is Expr.Variable variable &&
        _ctx.TryGetPromotedObjectLocal(variable.Name.Lexeme) is { } promoted &&
        promoted.Shape.FieldBuilders.TryGetValue(get.Name.Lexeme, out var field) &&
        (field.FieldType == _ctx.Types.Double || field.FieldType == _ctx.Types.Boolean || field.FieldType == _ctx.Types.String);

    private void RecordNativePrimitiveFieldFact(Expr receiver, Expr value, MethodBuilder getter)
    {
        // Do not attach a saved receiver's store to a binding changed during RHS
        // evaluation. This deliberately small proof admits only literal/slot loads.
        if (value is not (Expr.Literal or Expr.Variable) ||
            _ctx.RuntimeFeatures?.UsesObjectIntegrityMutation != false || !HasNominalClassCarrier(receiver))
            return;
        if (PrimitiveFieldReceiverIdentity(receiver) is { } key)
            _nativePrimitiveFieldFacts.Add((key, getter));
    }
}
