using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Property access and collection emission methods for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    private void EmitGet(Expr.Get g)
    {
        // Special case: Math properties
        if (g.Object is Expr.Variable v && v.Name.Lexeme == "Math")
        {
            switch (g.Name.Lexeme)
            {
                case "PI":
                    IL.Emit(OpCodes.Ldc_R8, Math.PI);
                    IL.Emit(OpCodes.Box, typeof(double));
                    return;
                case "E":
                    IL.Emit(OpCodes.Ldc_R8, Math.E);
                    IL.Emit(OpCodes.Box, typeof(double));
                    return;
            }
        }

        // Special case: Number properties (constants)
        if (g.Object is Expr.Variable numV && numV.Name.Lexeme == "Number")
        {
            switch (g.Name.Lexeme)
            {
                case "MAX_VALUE":
                    IL.Emit(OpCodes.Ldc_R8, double.MaxValue);
                    IL.Emit(OpCodes.Box, typeof(double));
                    return;
                case "MIN_VALUE":
                    IL.Emit(OpCodes.Ldc_R8, double.Epsilon); // JS MIN_VALUE = smallest positive
                    IL.Emit(OpCodes.Box, typeof(double));
                    return;
                case "NaN":
                    IL.Emit(OpCodes.Ldc_R8, double.NaN);
                    IL.Emit(OpCodes.Box, typeof(double));
                    return;
                case "POSITIVE_INFINITY":
                    IL.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
                    IL.Emit(OpCodes.Box, typeof(double));
                    return;
                case "NEGATIVE_INFINITY":
                    IL.Emit(OpCodes.Ldc_R8, double.NegativeInfinity);
                    IL.Emit(OpCodes.Box, typeof(double));
                    return;
                case "MAX_SAFE_INTEGER":
                    IL.Emit(OpCodes.Ldc_R8, 9007199254740991.0); // 2^53 - 1
                    IL.Emit(OpCodes.Box, typeof(double));
                    return;
                case "MIN_SAFE_INTEGER":
                    IL.Emit(OpCodes.Ldc_R8, -9007199254740991.0); // -(2^53 - 1)
                    IL.Emit(OpCodes.Box, typeof(double));
                    return;
                case "EPSILON":
                    IL.Emit(OpCodes.Ldc_R8, 2.220446049250313e-16); // 2^-52
                    IL.Emit(OpCodes.Box, typeof(double));
                    return;
            }
        }

        // Enum forward mapping: Direction.Up -> 0 or Status.Success -> "SUCCESS"
        if (g.Object is Expr.Variable enumVar &&
            _ctx.EnumMembers?.TryGetValue(enumVar.Name.Lexeme, out var members) == true &&
            members.TryGetValue(g.Name.Lexeme, out var value))
        {
            if (value is double d)
            {
                IL.Emit(OpCodes.Ldc_R8, d);
                IL.Emit(OpCodes.Box, typeof(double));
            }
            else if (value is string s)
            {
                IL.Emit(OpCodes.Ldstr, s);
            }
            return;
        }

        // Handle static member access via class name
        if (g.Object is Expr.Variable classVar && _ctx.Classes.TryGetValue(classVar.Name.Lexeme, out var classBuilder))
        {
            // Try to find static field using stored FieldBuilders
            if (_ctx.StaticFields != null &&
                _ctx.StaticFields.TryGetValue(classVar.Name.Lexeme, out var classFields) &&
                classFields.TryGetValue(g.Name.Lexeme, out var staticField))
            {
                IL.Emit(OpCodes.Ldsfld, staticField);
                return;
            }

            // Static methods are handled in EmitCall, so just fall through for now
            // If we get here for a method reference (not call), we'll use the generic path
        }

        // Try direct getter dispatch for known class instance types
        TypeInfo? objType = _ctx.TypeMap?.Get(g.Object);
        if (TryEmitDirectGetterCall(g.Object, objType, g.Name.Lexeme))
            return;

        // Special case: Map.size property
        if (objType is TypeInfo.Map && g.Name.Lexeme == "size")
        {
            EmitExpression(g.Object);
            EmitBoxIfNeeded(g.Object);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.MapSize);
            IL.Emit(OpCodes.Box, typeof(double));
            return;
        }

        // Special case: Set.size property
        if (objType is TypeInfo.Set && g.Name.Lexeme == "size")
        {
            EmitExpression(g.Object);
            EmitBoxIfNeeded(g.Object);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetSize);
            IL.Emit(OpCodes.Box, typeof(double));
            return;
        }

        EmitExpression(g.Object);
        EmitBoxIfNeeded(g.Object);

        if (g.Optional)
        {
            var nullLabel = IL.DefineLabel();
            var endLabel = IL.DefineLabel();

            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Brfalse, nullLabel);

            IL.Emit(OpCodes.Ldstr, g.Name.Lexeme);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);
            IL.Emit(OpCodes.Br, endLabel);

            IL.MarkLabel(nullLabel);
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Ldnull);

            IL.MarkLabel(endLabel);
        }
        else
        {
            IL.Emit(OpCodes.Ldstr, g.Name.Lexeme);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);
        }
    }

    private void EmitSet(Expr.Set s)
    {
        // Handle static property assignment via class name
        if (s.Object is Expr.Variable classVar && _ctx.Classes.TryGetValue(classVar.Name.Lexeme, out var classBuilder))
        {
            if (_ctx.StaticFields != null &&
                _ctx.StaticFields.TryGetValue(classVar.Name.Lexeme, out var classFields) &&
                classFields.TryGetValue(s.Name.Lexeme, out var staticField))
            {
                EmitExpression(s.Value);
                EmitBoxIfNeeded(s.Value);
                IL.Emit(OpCodes.Dup); // Keep value for expression result
                IL.Emit(OpCodes.Stsfld, staticField);
                return;
            }
        }

        // Try direct setter dispatch for known class instance types
        TypeInfo? objType = _ctx.TypeMap?.Get(s.Object);
        if (TryEmitDirectSetterCall(s.Object, objType, s.Name.Lexeme, s.Value))
            return;

        // Build stack for SetProperty(obj, name, value)
        EmitExpression(s.Object);
        EmitBoxIfNeeded(s.Object);
        IL.Emit(OpCodes.Ldstr, s.Name.Lexeme);
        EmitExpression(s.Value);
        EmitBoxIfNeeded(s.Value);

        // Stack: [obj, name, value]
        // Dup value for expression result, then store it
        IL.Emit(OpCodes.Dup);
        var resultTemp = IL.DeclareLocal(typeof(object));
        IL.Emit(OpCodes.Stloc, resultTemp);

        // Stack: [obj, name, value] - call SetProperty
        IL.Emit(OpCodes.Call, _ctx.Runtime!.SetProperty);

        // Put result back on stack
        IL.Emit(OpCodes.Ldloc, resultTemp);
    }

    private void EmitGetIndex(Expr.GetIndex gi)
    {
        // Enum reverse mapping: Direction[0] -> "Up"
        if (gi.Object is Expr.Variable enumVar &&
            _ctx.EnumReverse?.TryGetValue(enumVar.Name.Lexeme, out var reverse) == true)
        {
            // Check if index is a literal we can resolve at compile time
            if (gi.Index is Expr.Literal lit && lit.Value is double d && reverse.TryGetValue(d, out var memberName))
            {
                IL.Emit(OpCodes.Ldstr, memberName);
                return;
            }

            // Runtime lookup using cached helper
            // Call RuntimeTypes.GetEnumMemberName(enumName, value, keys[], values[])
            var keys = reverse.Keys.ToArray();
            var values = reverse.Values.ToArray();

            // Arg 1: enum name
            IL.Emit(OpCodes.Ldstr, enumVar.Name.Lexeme);

            // Arg 2: value (the index expression as double)
            EmitExpression(gi.Index);
            EmitUnboxToDouble();

            // Arg 3: keys array
            IL.Emit(OpCodes.Ldc_I4, keys.Length);
            IL.Emit(OpCodes.Newarr, typeof(double));
            for (int i = 0; i < keys.Length; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                IL.Emit(OpCodes.Ldc_R8, keys[i]);
                IL.Emit(OpCodes.Stelem_R8);
            }

            // Arg 4: values array
            IL.Emit(OpCodes.Ldc_I4, values.Length);
            IL.Emit(OpCodes.Newarr, typeof(string));
            for (int i = 0; i < values.Length; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                IL.Emit(OpCodes.Ldstr, values[i]);
                IL.Emit(OpCodes.Stelem_Ref);
            }

            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetEnumMemberName);
            return;
        }

        EmitExpression(gi.Object);
        EmitBoxIfNeeded(gi.Object);
        EmitExpression(gi.Index);
        EmitBoxIfNeeded(gi.Index);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetIndex);
    }

    private void EmitSetIndex(Expr.SetIndex si)
    {
        // Store value in a temp local so we can use it twice:
        // once for SetIndex, once for the expression result
        EmitExpression(si.Value);
        EmitBoxIfNeeded(si.Value);
        var valueLocal = IL.DeclareLocal(typeof(object));
        IL.Emit(OpCodes.Stloc, valueLocal);

        // Call SetIndex(object, index, value)
        EmitExpression(si.Object);
        EmitBoxIfNeeded(si.Object);
        EmitExpression(si.Index);
        EmitBoxIfNeeded(si.Index);
        IL.Emit(OpCodes.Ldloc, valueLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndex);

        // Push value back for expression result
        IL.Emit(OpCodes.Ldloc, valueLocal);
    }

    private void EmitArrayLiteral(Expr.ArrayLiteral a)
    {
        // Check if any element is a spread
        bool hasSpreads = a.Elements.Any(e => e is Expr.Spread);

        if (!hasSpreads)
        {
            // Simple case: no spreads, just create array directly
            IL.Emit(OpCodes.Ldc_I4, a.Elements.Count);
            IL.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < a.Elements.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                EmitExpression(a.Elements[i]);
                EmitBoxIfNeeded(a.Elements[i]);
                IL.Emit(OpCodes.Stelem_Ref);
            }

            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateArray);
        }
        else
        {
            // Complex case: has spreads, use ConcatArrays
            // Build array of arrays/elements to concat
            IL.Emit(OpCodes.Ldc_I4, a.Elements.Count);
            IL.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < a.Elements.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);

                if (a.Elements[i] is Expr.Spread spread)
                {
                    // Spread: emit the array directly
                    EmitExpression(spread.Expression);
                    EmitBoxIfNeeded(spread.Expression);
                }
                else
                {
                    // Non-spread: wrap in single-element array
                    IL.Emit(OpCodes.Ldc_I4, 1);
                    IL.Emit(OpCodes.Newarr, typeof(object));
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, 0);
                    EmitExpression(a.Elements[i]);
                    EmitBoxIfNeeded(a.Elements[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateArray);
                }

                IL.Emit(OpCodes.Stelem_Ref);
            }

            IL.Emit(OpCodes.Call, _ctx.Runtime!.ConcatArrays);
        }
    }

    private void EmitObjectLiteral(Expr.ObjectLiteral o)
    {
        // Check if any property is a spread or computed key
        bool hasSpreads = o.Properties.Any(p => p.IsSpread);
        bool hasComputedKeys = o.Properties.Any(p => p.Key is Expr.ComputedKey);

        if (!hasSpreads && !hasComputedKeys)
        {
            // Simple case: no spreads, no computed keys
            IL.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);

            foreach (var prop in o.Properties)
            {
                IL.Emit(OpCodes.Dup);
                EmitStaticPropertyKey(prop.Key!);
                EmitExpression(prop.Value);
                EmitBoxIfNeeded(prop.Value);
                IL.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
            }

            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateObject);
        }
        else
        {
            // Complex case: has spreads or computed keys, use Dictionary<string, object?> and SetIndex
            IL.Emit(OpCodes.Newobj, typeof(Dictionary<string, object?>).GetConstructor([])!);

            foreach (var prop in o.Properties)
            {
                IL.Emit(OpCodes.Dup);

                if (prop.IsSpread)
                {
                    // Spread: merge the object into target
                    EmitExpression(prop.Value);
                    EmitBoxIfNeeded(prop.Value);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.MergeIntoObject);
                }
                else if (prop.Key is Expr.ComputedKey ck)
                {
                    // Computed key: evaluate key expression and use SetIndex
                    EmitExpression(ck.Expression);
                    EmitBoxIfNeeded(ck.Expression);
                    EmitExpression(prop.Value);
                    EmitBoxIfNeeded(prop.Value);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndex);
                }
                else
                {
                    // Static key: set directly
                    EmitStaticPropertyKey(prop.Key!);
                    EmitExpression(prop.Value);
                    EmitBoxIfNeeded(prop.Value);
                    IL.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object?>).GetMethod("set_Item")!);
                }
            }

            // Result is already Dictionary<string, object?>, no CreateObject needed
        }
    }

    /// <summary>
    /// Emits a static property key (identifier, string literal, or number literal) as a string.
    /// </summary>
    private void EmitStaticPropertyKey(Expr.PropertyKey key)
    {
        switch (key)
        {
            case Expr.IdentifierKey ik:
                IL.Emit(OpCodes.Ldstr, ik.Name.Lexeme);
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING:
                IL.Emit(OpCodes.Ldstr, (string)lk.Literal.Literal!);
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER:
                // Number keys are converted to strings in JS/TS
                IL.Emit(OpCodes.Ldstr, lk.Literal.Literal!.ToString()!);
                break;
            default:
                throw new Exception($"Internal Error: Unexpected static property key type: {key.GetType().Name}");
        }
    }

    /// <summary>
    /// Try to emit a direct getter call for known class instance types.
    /// Returns true if direct dispatch was emitted, false to fall back to runtime dispatch.
    /// </summary>
    private bool TryEmitDirectGetterCall(Expr receiver, TypeInfo? receiverType, string propertyName)
    {
        // Only handle Instance types (e.g., let p: Person = ...)
        if (receiverType is not TypeInfo.Instance instance)
            return false;

        // Extract the class name from the instance's class type
        string? className = instance.ClassType switch
        {
            TypeInfo.Class c => c.Name,
            _ => null
        };
        if (className == null)
            return false;

        // Look up the getter in the class hierarchy
        var getterBuilder = _ctx.ResolveInstanceGetter(className, propertyName);
        if (getterBuilder == null)
            return false;

        // Get the class type builder to cast the receiver
        if (!_ctx.Classes.TryGetValue(className, out var classType))
            return false;

        // Emit: ((ClassName)receiver).get_PropertyName()
        EmitExpression(receiver);
        EmitBoxIfNeeded(receiver);
        IL.Emit(OpCodes.Castclass, classType);
        IL.Emit(OpCodes.Callvirt, getterBuilder);
        return true;
    }

    /// <summary>
    /// Try to emit a direct setter call for known class instance types.
    /// Returns true if direct dispatch was emitted, false to fall back to runtime dispatch.
    /// </summary>
    private bool TryEmitDirectSetterCall(Expr receiver, TypeInfo? receiverType, string propertyName, Expr value)
    {
        // Only handle Instance types (e.g., let p: Person = ...)
        if (receiverType is not TypeInfo.Instance instance)
            return false;

        // Extract the class name from the instance's class type
        string? className = instance.ClassType switch
        {
            TypeInfo.Class c => c.Name,
            _ => null
        };
        if (className == null)
            return false;

        // Look up the setter in the class hierarchy
        var setterBuilder = _ctx.ResolveInstanceSetter(className, propertyName);
        if (setterBuilder == null)
            return false;

        // Get the class type builder to cast the receiver
        if (!_ctx.Classes.TryGetValue(className, out var classType))
            return false;

        // Emit: ((ClassName)receiver).set_PropertyName(value)
        // Also need to keep the value on the stack as the expression result
        EmitExpression(receiver);
        EmitBoxIfNeeded(receiver);
        IL.Emit(OpCodes.Castclass, classType);

        EmitExpression(value);
        EmitBoxIfNeeded(value);

        // Dup value for expression result before calling setter
        IL.Emit(OpCodes.Dup);
        var resultTemp = IL.DeclareLocal(typeof(object));
        IL.Emit(OpCodes.Stloc, resultTemp);

        // Call the setter (it returns a value we need to discard)
        IL.Emit(OpCodes.Callvirt, setterBuilder);
        IL.Emit(OpCodes.Pop);  // Discard setter's return value

        // Put saved value back on stack as expression result
        IL.Emit(OpCodes.Ldloc, resultTemp);
        return true;
    }
}
