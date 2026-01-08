using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    private void EmitNullishCoalescing(Expr.NullishCoalescing nc)
    {
        var rightLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        // Emit left side
        EmitExpression(nc.Left);
        EnsureBoxed();

        // Check for null/undefined
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Brfalse, rightLabel);

        // Left is not null - use it
        _il.Emit(OpCodes.Br, endLabel);

        // Left was null - pop and emit right
        _il.MarkLabel(rightLabel);
        _il.Emit(OpCodes.Pop);
        EmitExpression(nc.Right);
        EnsureBoxed();

        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    private void EmitTemplateLiteral(Expr.TemplateLiteral tl)
    {
        // TemplateLiteral has Strings (literal parts) and Expressions (interpolated parts)
        // Structure: strings[0] + expressions[0] + strings[1] + expressions[1] + ... + strings[n]
        if (tl.Strings.Count == 0 && tl.Expressions.Count == 0)
        {
            _il.Emit(OpCodes.Ldstr, "");
            SetStackType(StackType.String);
            return;
        }

        // Start with first string
        _il.Emit(OpCodes.Ldstr, tl.Strings[0]);

        // Interleave expressions and remaining strings
        for (int i = 0; i < tl.Expressions.Count; i++)
        {
            // Emit expression, convert to string
            EmitExpression(tl.Expressions[i]);
            EnsureBoxed();
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.Stringify);
            _il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);

            // Emit next string part
            if (i + 1 < tl.Strings.Count)
            {
                _il.Emit(OpCodes.Ldstr, tl.Strings[i + 1]);
                _il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
            }
        }
        _stackType = StackType.String;
    }

    private void EmitSet(Expr.Set s)
    {
        // Build stack for SetProperty(obj, name, value)
        EmitExpression(s.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Ldstr, s.Name.Lexeme);
        EmitExpression(s.Value);
        EnsureBoxed();

        // Dup value for expression result (assignment returns the value)
        _il.Emit(OpCodes.Dup);
        var resultTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, resultTemp);

        // Call SetProperty (returns void)
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetProperty);

        // Put result back on stack
        _il.Emit(OpCodes.Ldloc, resultTemp);
        SetStackUnknown();
    }

    private void EmitCompoundSet(Expr.CompoundSet cs)
    {
        // Compound assignment on object property: obj.prop += x
        // 1. Get current value
        // 2. Apply operation
        // 3. Store back

        // IMPORTANT: Emit value first (may contain await which clears stack)
        EmitExpression(cs.Value);
        EnsureBoxed();
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);

        // Get current value: GetProperty(obj, name)
        EmitExpression(cs.Object);
        EnsureBoxed();
        var objTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objTemp);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);

        // Load value and apply operation
        _il.Emit(OpCodes.Ldloc, valueTemp);
        EmitCompoundOperation(cs.Operator.Type);

        // Store result: SetProperty(obj, name, value)
        var resultLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, resultLocal);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetProperty);

        // Leave result on stack
        _il.Emit(OpCodes.Ldloc, resultLocal);
        SetStackUnknown();
    }

    private void EmitCompoundSetIndex(Expr.CompoundSetIndex csi)
    {
        // Compound assignment on array element: arr[i] += x
        // 1. Get current value
        // 2. Apply operation
        // 3. Store back

        // IMPORTANT: Emit value first (may contain await which clears stack)
        EmitExpression(csi.Value);
        EnsureBoxed();
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);

        // Emit object and index, store them
        EmitExpression(csi.Object);
        EnsureBoxed();
        var objTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objTemp);

        EmitExpression(csi.Index);
        EnsureBoxed();
        var indexTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, indexTemp);

        // Get current value: GetIndex(obj, index)
        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldloc, indexTemp);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetIndex);

        // Load value and apply operation
        _il.Emit(OpCodes.Ldloc, valueTemp);
        EmitCompoundOperation(csi.Operator.Type);

        // Store result: SetIndex(obj, index, value)
        var resultLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, resultLocal);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldloc, indexTemp);
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetIndex);

        // Leave result on stack
        _il.Emit(OpCodes.Ldloc, resultLocal);
        SetStackUnknown();
    }

    private void EmitCompoundOperation(TokenType opType)
    {
        // Stack has: currentValue (object), operandValue (object)
        // Apply the compound operation

        if (opType == TokenType.PLUS_EQUAL)
        {
            // Use runtime Add for string concatenation support
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.Add);
            return;
        }

        // For other operations, convert to double
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);

        switch (opType)
        {
            case TokenType.MINUS_EQUAL:
                _il.Emit(OpCodes.Sub);
                break;
            case TokenType.STAR_EQUAL:
                _il.Emit(OpCodes.Mul);
                break;
            case TokenType.SLASH_EQUAL:
                _il.Emit(OpCodes.Div);
                break;
            case TokenType.PERCENT_EQUAL:
                _il.Emit(OpCodes.Rem);
                break;
            default:
                _il.Emit(OpCodes.Add);
                break;
        }

        _il.Emit(OpCodes.Box, typeof(double));
    }
}
