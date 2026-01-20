using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Property access and collection emission methods for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    protected override void EmitGet(Expr.Get g)
    {
        // Static type property dispatch via registry (Math.PI, Number.MAX_VALUE, Symbol.iterator, etc.)
        if (g.Object is Expr.Variable staticVar && _ctx.TypeEmitterRegistry != null)
        {
            var staticStrategy = _ctx.TypeEmitterRegistry.GetStaticStrategy(staticVar.Name.Lexeme);
            if (staticStrategy != null && staticStrategy.TryEmitStaticPropertyGet(this, g.Name.Lexeme))
            {
                SetStackUnknown();
                return;
            }
        }

        // Special case: process.stdin.isTTY, process.stdout.isTTY, process.stderr.isTTY
        if (TryEmitProcessStreamProperty(g))
        {
            return;
        }

        // Built-in module property access (path.sep, path.delimiter, os.EOL, etc.)
        if (g.Object is Expr.Variable builtInVar &&
            _ctx.BuiltInModuleNamespaces != null &&
            _ctx.BuiltInModuleNamespaces.TryGetValue(builtInVar.Name.Lexeme, out var builtInModuleName) &&
            _ctx.BuiltInModuleEmitterRegistry?.GetEmitter(builtInModuleName) is { } builtInEmitter)
        {
            if (builtInEmitter.TryEmitPropertyGet(this, g.Name.Lexeme))
            {
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

        // Handle static member access via class expression variable
        if (g.Object is Expr.Variable classExprVar &&
            _ctx.VarToClassExpr != null &&
            _ctx.VarToClassExpr.TryGetValue(classExprVar.Name.Lexeme, out var classExpr) &&
            _ctx.ClassExprStaticFields != null &&
            _ctx.ClassExprStaticFields.TryGetValue(classExpr, out var exprStaticFields) &&
            exprStaticFields.TryGetValue(g.Name.Lexeme, out var exprStaticField))
        {
            IL.Emit(OpCodes.Ldsfld, exprStaticField);
            SetStackUnknown();
            return;
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

        // Special case: Error properties (name, message, stack, errors for AggregateError)
        if (objType is TypeInfo.Error)
        {
            EmitExpression(g.Object);
            EmitBoxIfNeeded(g.Object);
            switch (g.Name.Lexeme)
            {
                case "name":
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.ErrorGetName);
                    SetStackType(StackType.String);
                    return;
                case "message":
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.ErrorGetMessage);
                    SetStackType(StackType.String);
                    return;
                case "stack":
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.ErrorGetStack);
                    SetStackType(StackType.String);
                    return;
                case "errors":
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.AggregateErrorGetErrors);
                    SetStackUnknown();
                    return;
            }
        }

        EmitExpression(g.Object);
        EmitBoxIfNeeded(g.Object);

        if (g.Optional)
        {
            var builder = _ctx.ILBuilder;
            var nullLabel = builder.DefineLabel("optional_null");
            var endLabel = builder.DefineLabel("optional_end");

            IL.Emit(OpCodes.Dup);
            builder.Emit_Brfalse(nullLabel);

            IL.Emit(OpCodes.Ldstr, g.Name.Lexeme);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);
            builder.Emit_Br(endLabel);

            builder.MarkLabel(nullLabel);
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Ldnull);

            builder.MarkLabel(endLabel);
        }
        else
        {
            IL.Emit(OpCodes.Ldstr, g.Name.Lexeme);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);
        }
    }

    protected override void EmitSet(Expr.Set s)
    {
        // Handle process.exitCode assignment
        if (s.Object is Expr.Variable processVar && processVar.Name.Lexeme == "process" && s.Name.Lexeme == "exitCode")
        {
            EmitExpression(s.Value);
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_I4);
            IL.Emit(OpCodes.Dup); // Keep value for expression result
            IL.Emit(OpCodes.Call, _ctx.Types.GetPropertySetter(_ctx.Types.Environment, "ExitCode"));
            IL.Emit(OpCodes.Conv_R8); // Convert back to double for JS number
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            return;
        }

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

        // Type-first dispatch: Use TypeEmitterRegistry for property setters
        if (objType != null && _ctx.TypeEmitterRegistry != null)
        {
            var strategy = _ctx.TypeEmitterRegistry.GetStrategy(objType);
            if (strategy != null && strategy.TryEmitPropertySet(this, s.Object, s.Name.Lexeme, s.Value))
            {
                SetStackUnknown();
                return;
            }
        }

        // Build stack for SetProperty(obj, name, value) or SetPropertyStrict(obj, name, value, strictMode)
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

        // Stack: [obj, name, value] - call SetProperty or SetPropertyStrict
        if (_ctx.IsStrictMode)
        {
            IL.Emit(OpCodes.Ldc_I4_1); // true for strict mode
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetPropertyStrict);
        }
        else
        {
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetProperty);
        }

        // Put result back on stack
        IL.Emit(OpCodes.Ldloc, resultTemp);
    }

    protected override void EmitGetIndex(Expr.GetIndex gi)
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

    protected override void EmitSetIndex(Expr.SetIndex si)
    {
        // Store value in a temp local so we can use it twice:
        // once for SetIndex, once for the expression result
        EmitExpression(si.Value);
        EmitBoxIfNeeded(si.Value);
        var valueLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, valueLocal);

        // Call SetIndex(object, index, value) or SetIndexStrict(object, index, value, strictMode)
        EmitExpression(si.Object);
        EmitBoxIfNeeded(si.Object);
        EmitExpression(si.Index);
        EmitBoxIfNeeded(si.Index);
        IL.Emit(OpCodes.Ldloc, valueLocal);

        if (_ctx.IsStrictMode)
        {
            IL.Emit(OpCodes.Ldc_I4_1); // true for strict mode
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndexStrict);
        }
        else
        {
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndex);
        }

        // Push value back for expression result
        IL.Emit(OpCodes.Ldloc, valueLocal);
    }

    protected override void EmitArrayLiteral(Expr.ArrayLiteral a)
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

            // Pass Symbol.iterator and runtimeType for iterator protocol support
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolIterator);
            IL.Emit(OpCodes.Ldtoken, _ctx.Runtime!.RuntimeType);
            IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.ConcatArrays);
        }
    }

    protected override void EmitObjectLiteral(Expr.ObjectLiteral o)
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
                PrepareReceiverForMemberAccess(externalType);
                IL.Emit(OpCodes.Ldfld, field);
                BoxResultIfValueType(field.FieldType);
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
        bool isValueType = PrepareReceiverForMemberAccess(externalType);
        IL.Emit(isValueType ? OpCodes.Call : OpCodes.Callvirt, getter);
        BoxResultIfValueType(property.PropertyType);
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

    /// <summary>
    /// Tries to emit IL for process.stdin.isTTY, process.stdout.isTTY, process.stderr.isTTY property access.
    /// Returns true if the property was handled.
    /// </summary>
    private bool TryEmitProcessStreamProperty(Expr.Get g)
    {
        // Pattern: process.stdin.isTTY, process.stdout.isTTY, process.stderr.isTTY
        // g.Object is Expr.Get { Object: Expr.Variable("process"), Name: "stdin/stdout/stderr" }
        // g.Name.Lexeme is "isTTY"

        if (g.Object is not Expr.Get streamGet)
            return false;

        if (streamGet.Object is not Expr.Variable processVar || processVar.Name.Lexeme != "process")
            return false;

        string streamName = streamGet.Name.Lexeme;
        string propertyName = g.Name.Lexeme;

        if (propertyName != "isTTY")
            return false;

        switch (streamName)
        {
            case "stdin":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StdinIsTTY);
                SetStackUnknown();
                return true;

            case "stdout":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StdoutIsTTY);
                SetStackUnknown();
                return true;

            case "stderr":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StderrIsTTY);
                SetStackUnknown();
                return true;

            default:
                return false;
        }
    }

    #region ES2022 Private Class Elements

    /// <summary>
    /// Emits IL for ES2022 private field access (obj.#field).
    /// Currently emits a runtime exception as private fields in compiled code
    /// require additional infrastructure for brand checking.
    /// </summary>
    protected override void EmitGetPrivate(Expr.GetPrivate gp)
    {
        // ES2022 private fields are not yet supported in compiled mode.
        // The interpreter supports them, but IL emission requires:
        // 1. Tracking which class is currently executing
        // 2. Emitting ConditionalWeakTable lookups with proper brand checking
        // For now, emit code that throws at runtime
        IL.Emit(OpCodes.Ldstr, $"Private field '{gp.Name.Lexeme}' access not yet supported in compiled mode. Use interpreter mode.");
        IL.Emit(OpCodes.Newobj, typeof(NotImplementedException).GetConstructor([typeof(string)])!);
        IL.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits IL for ES2022 private field assignment (obj.#field = value).
    /// Currently emits a runtime exception.
    /// </summary>
    protected override void EmitSetPrivate(Expr.SetPrivate sp)
    {
        IL.Emit(OpCodes.Ldstr, $"Private field '{sp.Name.Lexeme}' assignment not yet supported in compiled mode. Use interpreter mode.");
        IL.Emit(OpCodes.Newobj, typeof(NotImplementedException).GetConstructor([typeof(string)])!);
        IL.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits IL for ES2022 private method call (obj.#method()).
    /// Currently emits a runtime exception.
    /// </summary>
    protected override void EmitCallPrivate(Expr.CallPrivate cp)
    {
        IL.Emit(OpCodes.Ldstr, $"Private method '{cp.Name.Lexeme}' call not yet supported in compiled mode. Use interpreter mode.");
        IL.Emit(OpCodes.Newobj, typeof(NotImplementedException).GetConstructor([typeof(string)])!);
        IL.Emit(OpCodes.Throw);
    }

    #endregion
}
