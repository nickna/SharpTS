using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    protected override void EmitNullishCoalescing(Expr.NullishCoalescing nc)
    {
        _helpers.EmitNullishCoalescing(
            () => EmitExpression(nc.Left),
            () => EmitExpression(nc.Right));
    }

    protected override void EmitTemplateLiteral(Expr.TemplateLiteral tl)
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

    protected override void EmitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral ttl)
    {
        // 1. Emit the tag function reference
        EmitExpression(ttl.Tag);
        EnsureBoxed();

        // 2. Create cooked strings array (object?[] to allow null for invalid escapes)
        _il.Emit(OpCodes.Ldc_I4, ttl.CookedStrings.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < ttl.CookedStrings.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            if (ttl.CookedStrings[i] != null)
            {
                _il.Emit(OpCodes.Ldstr, ttl.CookedStrings[i]!);
            }
            else
            {
                _il.Emit(OpCodes.Ldnull);
            }
            _il.Emit(OpCodes.Stelem_Ref);
        }

        // 3. Create raw strings array
        _il.Emit(OpCodes.Ldc_I4, ttl.RawStrings.Count);
        _il.Emit(OpCodes.Newarr, typeof(string));
        for (int i = 0; i < ttl.RawStrings.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldstr, ttl.RawStrings[i]);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        // 4. Create expressions array
        _il.Emit(OpCodes.Ldc_I4, ttl.Expressions.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < ttl.Expressions.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(ttl.Expressions[i]);
            EnsureBoxed();
            _il.Emit(OpCodes.Stelem_Ref);
        }

        // 5. Call runtime helper: InvokeTaggedTemplate(tag, cooked, raw, exprs)
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.InvokeTaggedTemplate);
        SetStackUnknown();
    }

    protected override void EmitSet(Expr.Set s)
    {
        // Handle static field assignment: Class.field = value
        if (s.Object is Expr.Variable classVar &&
            _ctx!.Classes.TryGetValue(_ctx.ResolveClassName(classVar.Name.Lexeme), out var classBuilder))
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);
            if (_ctx.StaticFields != null &&
                _ctx.StaticFields.TryGetValue(resolvedClassName, out var classFields) &&
                classFields.TryGetValue(s.Name.Lexeme, out var staticField))
            {
                // Emit value
                EmitExpression(s.Value);
                EnsureBoxed();

                // Dup for expression result (assignment returns the value)
                _il.Emit(OpCodes.Dup);

                // Store to static field
                _il.Emit(OpCodes.Stsfld, staticField);
                SetStackUnknown();
                return;
            }
        }

        // Default: dynamic property assignment
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

    protected override void EmitGetPrivate(Expr.GetPrivate gp)
    {
        throw new NotImplementedException($"Private field '{gp.Name.Lexeme}' access not supported in compiled async methods.");
    }

    protected override void EmitSetPrivate(Expr.SetPrivate sp)
    {
        throw new NotImplementedException($"Private field '{sp.Name.Lexeme}' assignment not supported in compiled async methods.");
    }

    protected override void EmitCallPrivate(Expr.CallPrivate cp)
    {
        throw new NotImplementedException($"Private method '{cp.Name.Lexeme}' call not supported in compiled async methods.");
    }

    protected override void EmitCompoundSet(Expr.CompoundSet cs)
    {
        // Compound assignment on object property: obj.prop += x
        // 1. Get current value
        // 2. Apply operation
        // 3. Store back

        // Handle static field compound assignment: Class.field += x
        if (cs.Object is Expr.Variable classVar &&
            _ctx!.Classes.TryGetValue(_ctx.ResolveClassName(classVar.Name.Lexeme), out var classBuilder))
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);
            if (_ctx.StaticFields != null &&
                _ctx.StaticFields.TryGetValue(resolvedClassName, out var classFields) &&
                classFields.TryGetValue(cs.Name.Lexeme, out var staticField))
            {
                // IMPORTANT: Emit value first (may contain await which clears stack)
                EmitExpression(cs.Value);
                EnsureBoxed();
                var valueTemp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, valueTemp);

                // Get current value from static field
                _il.Emit(OpCodes.Ldsfld, staticField);

                // Load value and apply operation
                _il.Emit(OpCodes.Ldloc, valueTemp);
                EmitCompoundOperation(cs.Operator.Type);

                // Dup for expression result (assignment returns the value)
                _il.Emit(OpCodes.Dup);

                // Store to static field
                _il.Emit(OpCodes.Stsfld, staticField);
                SetStackUnknown();
                return;
            }
        }

        // Default: dynamic property compound assignment
        // IMPORTANT: Emit value first (may contain await which clears stack)
        EmitExpression(cs.Value);
        EnsureBoxed();
        var valueTemp2 = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp2);

        // Get current value: GetProperty(obj, name)
        EmitExpression(cs.Object);
        EnsureBoxed();
        var objTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objTemp);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);

        // Load value and apply operation
        _il.Emit(OpCodes.Ldloc, valueTemp2);
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

    protected override void EmitCompoundSetIndex(Expr.CompoundSetIndex csi)
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
        // Use centralized helper for compound operations
        _helpers.EmitCompoundOperation(opType, _ctx!.Runtime!.Add);
    }
}
