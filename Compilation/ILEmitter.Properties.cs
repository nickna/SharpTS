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
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    SetStackUnknown();
                    return;
                case "E":
                    IL.Emit(OpCodes.Ldc_R8, Math.E);
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    SetStackUnknown();
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
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    SetStackUnknown();
                    return;
                case "MIN_VALUE":
                    IL.Emit(OpCodes.Ldc_R8, double.Epsilon); // JS MIN_VALUE = smallest positive
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    SetStackUnknown();
                    return;
                case "NaN":
                    IL.Emit(OpCodes.Ldc_R8, double.NaN);
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    SetStackUnknown();
                    return;
                case "POSITIVE_INFINITY":
                    IL.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    SetStackUnknown();
                    return;
                case "NEGATIVE_INFINITY":
                    IL.Emit(OpCodes.Ldc_R8, double.NegativeInfinity);
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    SetStackUnknown();
                    return;
                case "MAX_SAFE_INTEGER":
                    IL.Emit(OpCodes.Ldc_R8, 9007199254740991.0); // 2^53 - 1
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    SetStackUnknown();
                    return;
                case "MIN_SAFE_INTEGER":
                    IL.Emit(OpCodes.Ldc_R8, -9007199254740991.0); // -(2^53 - 1)
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    SetStackUnknown();
                    return;
                case "EPSILON":
                    IL.Emit(OpCodes.Ldc_R8, 2.220446049250313e-16); // 2^-52
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    SetStackUnknown();
                    return;
            }
        }

        // Special case: Symbol well-known symbols
        if (g.Object is Expr.Variable symV && symV.Name.Lexeme == "Symbol")
        {
            switch (g.Name.Lexeme)
            {
                case "iterator":
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolIterator);
                    SetStackUnknown();
                    return;
                case "asyncIterator":
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolAsyncIterator);
                    SetStackUnknown();
                    return;
                case "toStringTag":
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolToStringTag);
                    SetStackUnknown();
                    return;
                case "hasInstance":
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolHasInstance);
                    SetStackUnknown();
                    return;
                case "isConcatSpreadable":
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolIsConcatSpreadable);
                    SetStackUnknown();
                    return;
                case "toPrimitive":
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolToPrimitive);
                    SetStackUnknown();
                    return;
                case "species":
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolSpecies);
                    SetStackUnknown();
                    return;
                case "unscopables":
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolUnscopables);
                    SetStackUnknown();
                    return;
            }
        }

        // Enum forward mapping: Direction.Up -> 0 or Status.Success -> "SUCCESS"
        if (g.Object is Expr.Variable enumVar &&
            _ctx.EnumMembers?.TryGetValue(_ctx.ResolveEnumName(enumVar.Name.Lexeme), out var members) == true &&
            members.TryGetValue(g.Name.Lexeme, out var value))
        {
            if (value is double d)
            {
                IL.Emit(OpCodes.Ldc_R8, d);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                SetStackUnknown();
            }
            else if (value is string s)
            {
                IL.Emit(OpCodes.Ldstr, s);
                SetStackType(StackType.String);
            }
            return;
        }

        // Handle static member access via class name
        if (g.Object is Expr.Variable classVar && _ctx.Classes.TryGetValue(_ctx.ResolveClassName(classVar.Name.Lexeme), out var classBuilder))
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);
            // Try to find static field using stored FieldBuilders
            if (_ctx.StaticFields != null &&
                _ctx.StaticFields.TryGetValue(resolvedClassName, out var classFields) &&
                classFields.TryGetValue(g.Name.Lexeme, out var staticField))
            {
                IL.Emit(OpCodes.Ldsfld, staticField);
                SetStackUnknown();
                return;
            }

            // Static methods are handled in EmitCall, so just fall through for now
            // If we get here for a method reference (not call), we'll use the generic path
        }

        // Handle static property access on external .NET types (@DotNetType)
        if (g.Object is Expr.Variable extVar && _ctx.TypeMapper.ExternalTypes.TryGetValue(extVar.Name.Lexeme, out var externalType))
        {
            if (TryEmitExternalStaticPropertyGet(externalType, g.Name.Lexeme))
                return;
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
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            SetStackUnknown();
            return;
        }

        // Special case: Set.size property
        if (objType is TypeInfo.Set && g.Name.Lexeme == "size")
        {
            EmitExpression(g.Object);
            EmitBoxIfNeeded(g.Object);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetSize);
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            SetStackUnknown();
            return;
        }

        // Special case: RegExp properties
        if (objType is TypeInfo.RegExp)
        {
            EmitExpression(g.Object);
            EmitBoxIfNeeded(g.Object);
            switch (g.Name.Lexeme)
            {
                case "source":
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetSource);
                    SetStackType(StackType.String);
                    return;
                case "flags":
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetFlags);
                    SetStackType(StackType.String);
                    return;
                case "global":
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetGlobal);
                    IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                    SetStackUnknown();
                    return;
                case "ignoreCase":
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetIgnoreCase);
                    IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                    SetStackUnknown();
                    return;
                case "multiline":
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetMultiline);
                    IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                    SetStackUnknown();
                    return;
                case "lastIndex":
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetLastIndex);
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    SetStackUnknown();
                    return;
            }
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
        if (s.Object is Expr.Variable classVar && _ctx.Classes.TryGetValue(_ctx.ResolveClassName(classVar.Name.Lexeme), out var classBuilder))
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);
            if (_ctx.StaticFields != null &&
                _ctx.StaticFields.TryGetValue(resolvedClassName, out var classFields) &&
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

        // Special case: RegExp.lastIndex setter
        if (objType is TypeInfo.RegExp && s.Name.Lexeme == "lastIndex")
        {
            EmitExpression(s.Object);
            EmitBoxIfNeeded(s.Object);
            EmitExpression(s.Value);
            EmitUnboxToDouble();
            // Dup value for expression result
            IL.Emit(OpCodes.Dup);
            var valueTemp = IL.DeclareLocal(_ctx.Types.Double);
            IL.Emit(OpCodes.Stloc, valueTemp);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpSetLastIndex);
            // Put value back on stack as boxed result
            IL.Emit(OpCodes.Ldloc, valueTemp);
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            return;
        }

        // Build stack for SetProperty(obj, name, value)
        EmitExpression(s.Object);
        EmitBoxIfNeeded(s.Object);
        IL.Emit(OpCodes.Ldstr, s.Name.Lexeme);
        EmitExpression(s.Value);
        EmitBoxIfNeeded(s.Value);

        // Stack: [obj, name, value]
        // Dup value for expression result, then store it
        IL.Emit(OpCodes.Dup);
        var resultTemp = IL.DeclareLocal(_ctx.Types.Object);
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
            _ctx.EnumReverse?.TryGetValue(_ctx.ResolveEnumName(enumVar.Name.Lexeme), out var reverse) == true)
        {
            // Check if index is a literal we can resolve at compile time
            if (gi.Index is Expr.Literal lit && lit.Value is double d && reverse.TryGetValue(d, out var memberName))
            {
                IL.Emit(OpCodes.Ldstr, memberName);
                SetStackType(StackType.String);
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
            IL.Emit(OpCodes.Newarr, _ctx.Types.Double);
            for (int i = 0; i < keys.Length; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                IL.Emit(OpCodes.Ldc_R8, keys[i]);
                IL.Emit(OpCodes.Stelem_R8);
            }

            // Arg 4: values array
            IL.Emit(OpCodes.Ldc_I4, values.Length);
            IL.Emit(OpCodes.Newarr, _ctx.Types.String);
            for (int i = 0; i < values.Length; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                IL.Emit(OpCodes.Ldstr, values[i]);
                IL.Emit(OpCodes.Stelem_Ref);
            }

            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetEnumMemberName);
            SetStackType(StackType.String);
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
        var valueLocal = IL.DeclareLocal(_ctx.Types.Object);
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
            IL.Emit(OpCodes.Newarr, _ctx.Types.Object);

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
            IL.Emit(OpCodes.Newarr, _ctx.Types.Object);

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
                    IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
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
            IL.Emit(OpCodes.Newobj, _ctx.Types.GetConstructor(_ctx.Types.DictionaryStringObject));

            foreach (var prop in o.Properties)
            {
                IL.Emit(OpCodes.Dup);
                EmitStaticPropertyKey(prop.Key!);
                EmitExpression(prop.Value);
                EmitBoxIfNeeded(prop.Value);
                IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(_ctx.Types.DictionaryStringObject, "set_Item", _ctx.Types.String, _ctx.Types.Object));
            }

            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateObject);
        }
        else
        {
            // Complex case: has spreads or computed keys, use Dictionary<string, object?> and SetIndex
            IL.Emit(OpCodes.Newobj, _ctx.Types.GetConstructor(_ctx.Types.DictionaryStringObject));

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
                    IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(_ctx.Types.DictionaryStringObject, "set_Item", _ctx.Types.String, _ctx.Types.Object));
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
        string? simpleClassName = instance.ClassType switch
        {
            TypeInfo.Class c => c.Name,
            _ => null
        };
        if (simpleClassName == null)
            return false;

        // Check if this is an external .NET type (@DotNetType)
        if (_ctx.TypeMapper.ExternalTypes.TryGetValue(simpleClassName, out var externalType))
        {
            EmitExternalPropertyGet(receiver, externalType, propertyName);
            return true;
        }

        // Resolve to qualified name for multi-module compilation
        string className = _ctx.ResolveClassName(simpleClassName);

        // Also check if the qualified name is an external type
        if (_ctx.TypeMapper.ExternalTypes.TryGetValue(className, out externalType))
        {
            EmitExternalPropertyGet(receiver, externalType, propertyName);
            return true;
        }

        // Convert TypeScript camelCase property name to .NET PascalCase for lookup
        string pascalPropertyName = NamingConventions.ToPascalCase(propertyName);

        // Look up the getter in the class hierarchy
        var getterBuilder = _ctx.ResolveInstanceGetter(className, pascalPropertyName);
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

        // Check the actual return type of the getter method
        // Field properties have typed getters, but explicit accessors return object
        var getterReturnType = getterBuilder.ReturnType;

        if (getterReturnType.IsValueType)
        {
            // Getter returns a native value type - box it for internal code that expects object
            IL.Emit(OpCodes.Box, getterReturnType);
            SetStackUnknown();
        }
        else if (_ctx.Types.IsString(getterReturnType))
        {
            SetStackType(StackType.String);
        }
        else
        {
            // Reference types (including object) don't need boxing
            SetStackUnknown();
        }

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
        string? simpleClassName = instance.ClassType switch
        {
            TypeInfo.Class c => c.Name,
            _ => null
        };
        if (simpleClassName == null)
            return false;

        // Check if this is an external .NET type (@DotNetType)
        if (_ctx.TypeMapper.ExternalTypes.TryGetValue(simpleClassName, out var externalType))
        {
            EmitExternalPropertySet(receiver, externalType, propertyName, value);
            return true;
        }

        // Resolve to qualified name for multi-module compilation
        string className = _ctx.ResolveClassName(simpleClassName);

        // Also check if the qualified name is an external type
        if (_ctx.TypeMapper.ExternalTypes.TryGetValue(className, out externalType))
        {
            EmitExternalPropertySet(receiver, externalType, propertyName, value);
            return true;
        }

        // Convert TypeScript camelCase property name to .NET PascalCase for lookup
        string pascalPropertyName = NamingConventions.ToPascalCase(propertyName);

        // Look up the setter in the class hierarchy
        var setterBuilder = _ctx.ResolveInstanceSetter(className, pascalPropertyName);
        if (setterBuilder == null)
            return false;

        // Get the class type builder to cast the receiver
        if (!_ctx.Classes.TryGetValue(className, out var classType))
            return false;

        // Get the actual parameter type of the setter method
        // Field properties have typed setters, but explicit accessors take object
        var setterParams = setterBuilder.GetParameters();
        var setterParamType = setterParams.Length > 0 ? setterParams[0].ParameterType : _ctx.Types.Object;

        // Emit: ((ClassName)receiver).set_PropertyName(value)
        // Also need to keep the value on the stack as the expression result
        EmitExpression(receiver);
        EmitBoxIfNeeded(receiver);
        IL.Emit(OpCodes.Castclass, classType);

        // Emit value and convert to setter parameter type
        EmitExpression(value);

        // Check if setter returns void (field properties) or object (explicit accessors)
        var setterReturnsVoid = _ctx.Types.IsVoid(setterBuilder.ReturnType);

        // Save a copy for expression result (need to box if value type for consistent handling)
        if (setterParamType.IsValueType)
        {
            // For value types: box first, then dup, then unbox for setter
            EmitBoxIfNeeded(value);
            IL.Emit(OpCodes.Dup);
            var resultTemp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, resultTemp);
            IL.Emit(OpCodes.Unbox_Any, setterParamType);
            IL.Emit(OpCodes.Callvirt, setterBuilder);
            // Pop setter return value if not void (explicit accessors return object)
            if (!setterReturnsVoid)
            {
                IL.Emit(OpCodes.Pop);
            }
            IL.Emit(OpCodes.Ldloc, resultTemp);
        }
        else
        {
            // For reference types (including object): dup, optionally cast, call setter
            EmitBoxIfNeeded(value);
            IL.Emit(OpCodes.Dup);
            var resultTemp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, resultTemp);
            if (!_ctx.Types.IsObject(setterParamType))
            {
                IL.Emit(OpCodes.Castclass, setterParamType);
            }
            IL.Emit(OpCodes.Callvirt, setterBuilder);
            // Pop setter return value if not void (explicit accessors return object)
            if (!setterReturnsVoid)
            {
                IL.Emit(OpCodes.Pop);
            }
            IL.Emit(OpCodes.Ldloc, resultTemp);
        }

        SetStackUnknown();  // Result is boxed object
        return true;
    }

    /// <summary>
    /// Tries to emit static property get access on an external .NET type (via @DotNetType).
    /// Returns true if successful, false if property not found.
    /// </summary>
    private bool TryEmitExternalStaticPropertyGet(Type externalType, string propertyName)
    {
        // Try PascalCase first (most .NET properties use PascalCase)
        string pascalPropertyName = NamingConventions.ToPascalCase(propertyName);

        // Try to find a static property (first PascalCase, then original name)
        var property = externalType.GetProperty(pascalPropertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    ?? externalType.GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        if (property != null)
        {
            var getter = property.GetGetMethod();
            if (getter != null)
            {
                IL.Emit(OpCodes.Call, getter);

                if (property.PropertyType.IsValueType)
                {
                    IL.Emit(OpCodes.Box, property.PropertyType);
                }
                SetStackUnknown();
                return true;
            }
        }

        // Try to find a static field
        var field = externalType.GetField(pascalPropertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                 ?? externalType.GetField(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        if (field != null)
        {
            IL.Emit(OpCodes.Ldsfld, field);

            if (field.FieldType.IsValueType)
            {
                IL.Emit(OpCodes.Box, field.FieldType);
            }
            SetStackUnknown();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Emits property get access on an external .NET type (via @DotNetType).
    /// </summary>
    private void EmitExternalPropertyGet(Expr receiver, Type externalType, string propertyName)
    {
        // Try PascalCase first (most .NET properties use PascalCase)
        string pascalPropertyName = NamingConventions.ToPascalCase(propertyName);

        // Try to find the property (first PascalCase, then original name)
        var property = externalType.GetProperty(pascalPropertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    ?? externalType.GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        if (property == null)
        {
            // Try to find a field instead
            var field = externalType.GetField(pascalPropertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                     ?? externalType.GetField(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            if (field != null)
            {
                // Emit field access
                EmitExpression(receiver);
                EmitBoxIfNeeded(receiver);

                if (externalType.IsValueType)
                {
                    // For value types, unbox to a local and load its address for ldfld
                    IL.Emit(OpCodes.Unbox_Any, externalType);
                    var valueLocal = IL.DeclareLocal(externalType);
                    IL.Emit(OpCodes.Stloc, valueLocal);
                    IL.Emit(OpCodes.Ldloca, valueLocal);
                    IL.Emit(OpCodes.Ldfld, field);
                }
                else
                {
                    IL.Emit(OpCodes.Castclass, externalType);
                    IL.Emit(OpCodes.Ldfld, field);
                }

                if (field.FieldType.IsValueType)
                {
                    IL.Emit(OpCodes.Box, field.FieldType);
                }
                SetStackUnknown();
                return;
            }

            throw new Exception($"Property or field '{propertyName}' not found on external type {externalType.FullName}");
        }

        var getter = property.GetGetMethod();
        if (getter == null)
        {
            throw new Exception($"Property '{property.Name}' on external type {externalType.FullName} has no getter");
        }

        // Emit property access
        EmitExpression(receiver);
        EmitBoxIfNeeded(receiver);

        if (externalType.IsValueType)
        {
            // For value types, we need to unbox to a local and load its address
            IL.Emit(OpCodes.Unbox_Any, externalType);
            var valueLocal = IL.DeclareLocal(externalType);
            IL.Emit(OpCodes.Stloc, valueLocal);
            IL.Emit(OpCodes.Ldloca, valueLocal);
            IL.Emit(OpCodes.Call, getter);
        }
        else
        {
            IL.Emit(OpCodes.Castclass, externalType);
            IL.Emit(OpCodes.Callvirt, getter);
        }

        if (property.PropertyType.IsValueType)
        {
            IL.Emit(OpCodes.Box, property.PropertyType);
        }
        SetStackUnknown();
    }

    /// <summary>
    /// Emits property set access on an external .NET type (via @DotNetType).
    /// </summary>
    private void EmitExternalPropertySet(Expr receiver, Type externalType, string propertyName, Expr value)
    {
        // Try PascalCase first (most .NET properties use PascalCase)
        string pascalPropertyName = NamingConventions.ToPascalCase(propertyName);

        // Try to find the property (first PascalCase, then original name)
        var property = externalType.GetProperty(pascalPropertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    ?? externalType.GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        if (property == null)
        {
            // Try to find a field instead
            var field = externalType.GetField(pascalPropertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                     ?? externalType.GetField(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            if (field != null)
            {
                // Emit field set
                EmitExpression(receiver);
                EmitBoxIfNeeded(receiver);
                IL.Emit(OpCodes.Castclass, externalType);
                EmitExpression(value);
                EmitExternalTypeConversion(field.FieldType);

                // Save value for expression result
                IL.Emit(OpCodes.Dup);
                var valueTemp = IL.DeclareLocal(field.FieldType);
                IL.Emit(OpCodes.Stloc, valueTemp);

                IL.Emit(OpCodes.Stfld, field);

                // Put value back on stack as boxed result
                IL.Emit(OpCodes.Ldloc, valueTemp);
                if (field.FieldType.IsValueType)
                {
                    IL.Emit(OpCodes.Box, field.FieldType);
                }
                SetStackUnknown();
                return;
            }

            throw new Exception($"Property or field '{propertyName}' not found on external type {externalType.FullName}");
        }

        var setter = property.GetSetMethod();
        if (setter == null)
        {
            throw new Exception($"Property '{property.Name}' on external type {externalType.FullName} has no setter");
        }

        // Emit property set
        EmitExpression(receiver);
        EmitBoxIfNeeded(receiver);
        IL.Emit(OpCodes.Castclass, externalType);
        EmitExpression(value);
        EmitExternalTypeConversion(property.PropertyType);

        // Save value for expression result
        IL.Emit(OpCodes.Dup);
        var propValueTemp = IL.DeclareLocal(property.PropertyType);
        IL.Emit(OpCodes.Stloc, propValueTemp);

        IL.Emit(OpCodes.Callvirt, setter);

        // Put value back on stack as boxed result
        IL.Emit(OpCodes.Ldloc, propValueTemp);
        if (property.PropertyType.IsValueType)
        {
            IL.Emit(OpCodes.Box, property.PropertyType);
        }
        SetStackUnknown();
    }
}
