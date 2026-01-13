using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    protected override void EmitArrayLiteral(Expr.ArrayLiteral a)
    {
        // Check if any element is a spread
        bool hasSpreads = a.Elements.Any(e => e is Expr.Spread);

        if (!hasSpreads)
        {
            // Simple case: no spreads, just create array directly
            _il.Emit(OpCodes.Ldc_I4, a.Elements.Count);
            _il.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < a.Elements.Count; i++)
            {
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Ldc_I4, i);
                EmitExpression(a.Elements[i]);
                EnsureBoxed();
                _il.Emit(OpCodes.Stelem_Ref);
            }

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateArray);
        }
        else
        {
            // Complex case: has spreads, use ConcatArrays
            _il.Emit(OpCodes.Ldc_I4, a.Elements.Count);
            _il.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < a.Elements.Count; i++)
            {
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Ldc_I4, i);

                if (a.Elements[i] is Expr.Spread spread)
                {
                    EmitExpression(spread.Expression);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldc_I4, 1);
                    _il.Emit(OpCodes.Newarr, typeof(object));
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4_0);
                    EmitExpression(a.Elements[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                    _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateArray);
                }

                _il.Emit(OpCodes.Stelem_Ref);
            }

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.ConcatArrays);
        }
        SetStackUnknown();
    }

    protected override void EmitObjectLiteral(Expr.ObjectLiteral o)
    {
        // Check if any property is a spread or computed key
        bool hasSpreads = o.Properties.Any(p => p.IsSpread);
        bool hasComputedKeys = o.Properties.Any(p => p.Key is Expr.ComputedKey);

        if (!hasSpreads && !hasComputedKeys)
        {
            // Simple case: no spreads, no computed keys
            _il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);

            foreach (var prop in o.Properties)
            {
                _il.Emit(OpCodes.Dup);
                EmitStaticPropertyKey(prop.Key!);
                EmitExpression(prop.Value);
                EnsureBoxed();
                _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
            }

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateObject);
        }
        else
        {
            // Complex case: has spreads or computed keys, use SetIndex
            _il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object?>).GetConstructor([])!);

            foreach (var prop in o.Properties)
            {
                _il.Emit(OpCodes.Dup);

                if (prop.IsSpread)
                {
                    EmitExpression(prop.Value);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Call, _ctx!.Runtime!.MergeIntoObject);
                }
                else if (prop.Key is Expr.ComputedKey ck)
                {
                    // Computed key: evaluate key expression and use SetIndex
                    EmitExpression(ck.Expression);
                    EnsureBoxed();
                    EmitExpression(prop.Value);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetIndex);
                }
                else
                {
                    // Static key: set directly
                    EmitStaticPropertyKey(prop.Key!);
                    EmitExpression(prop.Value);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object?>).GetMethod("set_Item")!);
                }
            }

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateObject);
        }
        SetStackUnknown();
    }

    /// <summary>
    /// Emits a static property key (identifier, string literal, or number literal) as a string.
    /// </summary>
    private void EmitStaticPropertyKey(Expr.PropertyKey key)
    {
        switch (key)
        {
            case Expr.IdentifierKey ik:
                _il.Emit(OpCodes.Ldstr, ik.Name.Lexeme);
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING:
                _il.Emit(OpCodes.Ldstr, (string)lk.Literal.Literal!);
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER:
                // Number keys are converted to strings in JS/TS
                _il.Emit(OpCodes.Ldstr, lk.Literal.Literal!.ToString()!);
                break;
            default:
                throw new Exception($"Internal Error: Unexpected static property key type: {key.GetType().Name}");
        }
    }

    protected override void EmitGetIndex(Expr.GetIndex gi)
    {
        EmitExpression(gi.Object);
        EnsureBoxed();
        EmitExpression(gi.Index);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetIndex);
        SetStackUnknown();
    }

    protected override void EmitSetIndex(Expr.SetIndex si)
    {
        EmitExpression(si.Object);
        EnsureBoxed();
        EmitExpression(si.Index);
        EnsureBoxed();
        EmitExpression(si.Value);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetIndex);
        SetStackUnknown();
    }
}
