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

        // Special case: globalThis.Math.PI, globalThis.JSON.parse, etc.
        if (TryEmitGlobalThisChainedProperty(g))
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

        // Handle static member access via 'this' in static context (static blocks, static methods)
        // In static blocks, 'this' refers to the class constructor, so this.property accesses static members
        if (g.Object is Expr.This && !_ctx.IsInstanceMethod && _ctx.CurrentClassBuilder != null)
        {
            // Find the class name for the current class builder
            string? currentClassName = null;
            foreach (var (name, builder) in _ctx.Classes)
            {
                if (builder == _ctx.CurrentClassBuilder)
                {
                    currentClassName = name;
                    break;
                }
            }

            if (currentClassName != null)
            {
                // Emit as static field access on the current class
                if (EmitStaticMemberAccess(currentClassName, _ctx.CurrentClassBuilder, g.Name.Lexeme))
                {
                    return;
                }
            }
        }

        // Handle static member access via class name
        if (g.Object is Expr.Variable classVar && _ctx.Classes.TryGetValue(_ctx.ResolveClassName(classVar.Name.Lexeme), out var classBuilder))
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);

            // Try static getter first (for auto-accessors and explicit static accessors)
            if (_ctx.ClassRegistry!.TryGetStaticGetter(resolvedClassName, g.Name.Lexeme, out var staticGetter))
            {
                IL.Emit(OpCodes.Call, staticGetter!);

                // The getter returns the typed value (e.g., double for number).
                // Track the stack type so EmitBoxIfNeeded can box only when necessary.
                // This avoids unnecessary boxing in numeric contexts like `Counter.count + 1`.
                string pascalPropName = NamingConventions.ToPascalCase(g.Name.Lexeme);
                if (_ctx.PropertyTypes != null &&
                    _ctx.PropertyTypes.TryGetValue(resolvedClassName, out var propTypes) &&
                    propTypes.TryGetValue(pascalPropName, out var propType))
                {
                    if (propType == _ctx.Types.Double)
                    {
                        SetStackType(StackType.Double);
                    }
                    else if (propType == _ctx.Types.Boolean)
                    {
                        SetStackType(StackType.Boolean);
                    }
                    else if (propType == _ctx.Types.String)
                    {
                        SetStackType(StackType.String);
                    }
                    else
                    {
                        // Other reference types
                        SetStackUnknown();
                    }
                }
                else
                {
                    // Fallback: assume object return (legacy behavior)
                    SetStackUnknown();
                }
                return;
            }

            // Try to find static field using stored FieldBuilders
            // Use TryGetCallableStaticField to handle generic classes properly
            if (_ctx.ClassRegistry!.TryGetCallableStaticField(resolvedClassName, g.Name.Lexeme, classBuilder, out var callableStaticField))
            {
                IL.Emit(OpCodes.Ldsfld, callableStaticField!);
                SetStackUnknown();
                return;
            }

            // Static methods are handled in EmitCall, so just fall through for now
            // If we get here for a method reference (not call), we'll use the generic path
        }

        // Handle static member access via imported class alias (import X = require('./module') where module exports a class)
        if (g.Object is Expr.Variable importedClassVar &&
            _ctx.ImportedClassAliases?.TryGetValue(importedClassVar.Name.Lexeme, out var importedQualifiedClassName) == true &&
            _ctx.Classes.TryGetValue(importedQualifiedClassName, out var importedClassBuilder))
        {
            // Try static getter first
            if (_ctx.ClassRegistry!.TryGetStaticGetter(importedQualifiedClassName, g.Name.Lexeme, out var importedStaticGetter))
            {
                IL.Emit(OpCodes.Call, importedStaticGetter!);
                SetStackUnknown();
                return;
            }

            // Try static field
            if (_ctx.ClassRegistry!.TryGetCallableStaticField(importedQualifiedClassName, g.Name.Lexeme, importedClassBuilder, out var importedStaticField))
            {
                IL.Emit(OpCodes.Ldsfld, importedStaticField!);
                SetStackUnknown();
                return;
            }
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

        // Category-based built-in type property dispatch
        if (objType != null && TryEmitBuiltInTypePropertyGet(g, objType))
            return;

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

        // Handle static property assignment via 'this' in static context (static blocks, static methods)
        if (s.Object is Expr.This && !_ctx.IsInstanceMethod && _ctx.CurrentClassBuilder != null)
        {
            // Find the class name for the current class builder
            string? currentClassName = null;
            foreach (var (name, builder) in _ctx.Classes)
            {
                if (builder == _ctx.CurrentClassBuilder)
                {
                    currentClassName = name;
                    break;
                }
            }

            if (currentClassName != null)
            {
                // Emit as static field assignment on the current class
                if (EmitStaticMemberSet(currentClassName, _ctx.CurrentClassBuilder, s.Name.Lexeme, s.Value))
                {
                    return;
                }
            }
        }

        // Handle static property assignment via class name
        if (s.Object is Expr.Variable classVar && _ctx.Classes.TryGetValue(_ctx.ResolveClassName(classVar.Name.Lexeme), out var classBuilder))
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);

            // Try static setter first (for auto-accessors and explicit static accessors)
            if (_ctx.ClassRegistry!.TryGetStaticSetter(resolvedClassName, s.Name.Lexeme, out var staticSetter))
            {
                // Get the property type from PropertyTypes dictionary
                Type propertyType = _ctx.Types.Object;
                string pascalPropName = NamingConventions.ToPascalCase(s.Name.Lexeme);

                // Try to get the type from PropertyTypes dictionary
                if (_ctx.PropertyTypes != null &&
                    _ctx.PropertyTypes.TryGetValue(resolvedClassName, out var propTypes) &&
                    propTypes.TryGetValue(pascalPropName, out var propType))
                {
                    propertyType = propType;
                }
                else
                {
                    // Fallback: infer from value expression type via TypeMap
                    TypeInfo? valueType = _ctx.TypeMap?.Get(s.Value);
                    if (valueType is TypeInfo.Primitive prim)
                    {
                        propertyType = prim.Type switch
                        {
                            TokenType.TYPE_NUMBER => _ctx.Types.Double,
                            TokenType.TYPE_BOOLEAN => _ctx.Types.Boolean,
                            TokenType.TYPE_STRING => _ctx.Types.String,
                            _ => _ctx.Types.Object
                        };
                    }
                }

                EmitExpression(s.Value);
                EmitBoxIfNeeded(s.Value);
                IL.Emit(OpCodes.Dup); // Keep value for expression result
                var staticSetterResultTemp = IL.DeclareLocal(_ctx.Types.Object);
                IL.Emit(OpCodes.Stloc, staticSetterResultTemp);

                // If setter expects a value type, unbox the value
                if (propertyType.IsValueType)
                {
                    IL.Emit(OpCodes.Unbox_Any, propertyType);
                }
                else if (!_ctx.Types.IsObject(propertyType))
                {
                    IL.Emit(OpCodes.Castclass, propertyType);
                }

                IL.Emit(OpCodes.Call, staticSetter!);

                // Restore result for expression value
                IL.Emit(OpCodes.Ldloc, staticSetterResultTemp);
                return;
            }

            if (_ctx.ClassRegistry!.TryGetStaticField(resolvedClassName, s.Name.Lexeme, out var staticField))
            {
                EmitExpression(s.Value);
                EmitBoxIfNeeded(s.Value);
                IL.Emit(OpCodes.Dup); // Keep value for expression result
                IL.Emit(OpCodes.Stsfld, staticField!);
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
    /// Emits static member access on a class (used for both ClassName.property and this.property in static context).
    /// Returns true if successful, false if property not found.
    /// </summary>
    private bool EmitStaticMemberAccess(string className, System.Reflection.Emit.TypeBuilder classBuilder, string propertyName)
    {
        // Try static getter first (for auto-accessors and explicit static accessors)
        if (_ctx.ClassRegistry!.TryGetStaticGetter(className, propertyName, out var staticGetter))
        {
            IL.Emit(OpCodes.Call, staticGetter!);

            // Track the stack type for proper boxing behavior
            string pascalPropName = NamingConventions.ToPascalCase(propertyName);
            if (_ctx.PropertyTypes != null &&
                _ctx.PropertyTypes.TryGetValue(className, out var propTypes) &&
                propTypes.TryGetValue(pascalPropName, out var propType))
            {
                if (propType == _ctx.Types.Double)
                {
                    SetStackType(StackType.Double);
                }
                else if (propType == _ctx.Types.Boolean)
                {
                    SetStackType(StackType.Boolean);
                }
                else if (propType == _ctx.Types.String)
                {
                    SetStackType(StackType.String);
                }
                else
                {
                    SetStackUnknown();
                }
            }
            else
            {
                SetStackUnknown();
            }
            return true;
        }

        // Try to find static field using stored FieldBuilders
        if (_ctx.ClassRegistry!.TryGetStaticField(className, propertyName, out var staticField))
        {
            IL.Emit(OpCodes.Ldsfld, staticField!);
            SetStackUnknown();
            return true;
        }

        // Try static private fields - strip leading # if present
        string privateName = propertyName.StartsWith('#') ? propertyName[1..] : propertyName;
        if (_ctx.ClassRegistry!.TryGetStaticPrivateField(className, privateName, out var staticPrivateField))
        {
            IL.Emit(OpCodes.Ldsfld, staticPrivateField!);
            SetStackUnknown();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Emits static member set on a class (used for both ClassName.property = value and this.property = value in static context).
    /// Returns true if successful, false if property not found.
    /// </summary>
    private bool EmitStaticMemberSet(string className, System.Reflection.Emit.TypeBuilder classBuilder, string propertyName, Expr value)
    {
        // Try static setter first (for auto-accessors and explicit static accessors)
        if (_ctx.ClassRegistry!.TryGetStaticSetter(className, propertyName, out var staticSetter))
        {
            // Get the property type from PropertyTypes dictionary
            Type propertyType = _ctx.Types.Object;
            string pascalPropName = NamingConventions.ToPascalCase(propertyName);

            if (_ctx.PropertyTypes != null &&
                _ctx.PropertyTypes.TryGetValue(className, out var propTypes) &&
                propTypes.TryGetValue(pascalPropName, out var propType))
            {
                propertyType = propType;
            }
            else
            {
                TypeInfo? valueType = _ctx.TypeMap?.Get(value);
                if (valueType is TypeInfo.Primitive prim)
                {
                    propertyType = prim.Type switch
                    {
                        TokenType.TYPE_NUMBER => _ctx.Types.Double,
                        TokenType.TYPE_BOOLEAN => _ctx.Types.Boolean,
                        TokenType.TYPE_STRING => _ctx.Types.String,
                        _ => _ctx.Types.Object
                    };
                }
            }

            EmitExpression(value);
            EmitBoxIfNeeded(value);
            IL.Emit(OpCodes.Dup); // Keep value for expression result
            var staticSetterResultTemp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, staticSetterResultTemp);

            if (propertyType.IsValueType)
            {
                IL.Emit(OpCodes.Unbox_Any, propertyType);
            }
            else if (!_ctx.Types.IsObject(propertyType))
            {
                IL.Emit(OpCodes.Castclass, propertyType);
            }

            IL.Emit(OpCodes.Call, staticSetter!);

            IL.Emit(OpCodes.Ldloc, staticSetterResultTemp);
            return true;
        }

        // Try static fields - use TryGetCallableStaticField to handle generic classes properly
        if (_ctx.ClassRegistry!.TryGetCallableStaticField(className, propertyName, classBuilder, out var callableStaticField))
        {
            EmitExpression(value);
            EmitBoxIfNeeded(value);
            IL.Emit(OpCodes.Dup); // Keep value for expression result
            IL.Emit(OpCodes.Stsfld, callableStaticField!);
            return true;
        }

        // Try static private fields
        string privateFieldName = propertyName.StartsWith('#') ? propertyName[1..] : propertyName;
        if (_ctx.ClassRegistry!.TryGetStaticPrivateField(className, privateFieldName, out var staticPrivateField))
        {
            EmitExpression(value);
            EmitBoxIfNeeded(value);
            IL.Emit(OpCodes.Dup); // Keep value for expression result
            IL.Emit(OpCodes.Stsfld, staticPrivateField!);
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

    /// <summary>
    /// Tries to emit IL for globalThis chained property access like globalThis.Math.PI, globalThis.console.log, etc.
    /// Returns true if the property was handled.
    /// </summary>
    private bool TryEmitGlobalThisChainedProperty(Expr.Get g)
    {
        // Pattern: globalThis.Math.PI, globalThis.console.log, etc.
        // g.Object is Expr.Get { Object: Expr.Variable("globalThis"), Name: "Math/JSON/console/etc" }
        // g.Name.Lexeme is "PI/parse/log/etc"

        if (g.Object is not Expr.Get innerGet)
            return false;

        if (innerGet.Object is not Expr.Variable globalThisVar || globalThisVar.Name.Lexeme != "globalThis")
            return false;

        string namespaceName = innerGet.Name.Lexeme;
        string propertyName = g.Name.Lexeme;

        // Try to use the static emitter for the inner namespace
        var staticStrategy = _ctx.TypeEmitterRegistry?.GetStaticStrategy(namespaceName);
        if (staticStrategy != null && staticStrategy.TryEmitStaticPropertyGet(this, propertyName))
        {
            SetStackUnknown();
            return true;
        }

        // Handle globalThis.globalThis.X case (self-reference)
        if (namespaceName == "globalThis")
        {
            // Treat globalThis.globalThis as just globalThis
            var selfStrategy = _ctx.TypeEmitterRegistry?.GetStaticStrategy("globalThis");
            if (selfStrategy != null && selfStrategy.TryEmitStaticPropertyGet(this, propertyName))
            {
                SetStackUnknown();
                return true;
            }
        }

        return false;
    }

    #region ES2022 Private Class Elements

    /// <summary>
    /// Emits IL for ES2022 private field access (obj.#field).
    /// Supports both instance private fields (via ConditionalWeakTable) and static private fields.
    /// </summary>
    protected override void EmitGetPrivate(Expr.GetPrivate gp)
    {
        // Get the field name without the # prefix
        string fieldName = gp.Name.Lexeme;
        if (fieldName.StartsWith('#'))
            fieldName = fieldName[1..];

        // Check if this is accessing the current class (this.#field or ClassName.#field)
        string? className = _ctx.CurrentClassName;
        if (className == null)
        {
            // Fallback: throw runtime error if context isn't set up
            IL.Emit(OpCodes.Ldstr, $"Cannot access private field '#{fieldName}' - class context not available");
            IL.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
            IL.Emit(OpCodes.Throw);
            return;
        }

        // Check if it's a static private field access (ClassName.#field)
        if (gp.Object is Expr.Variable classVar && classVar.Name.Lexeme == _ctx.CurrentClassName?.Split('.').Last()?.Split('_').Last())
        {
            // Try static private field
            if (_ctx.ClassRegistry!.TryGetStaticPrivateField(className, fieldName, out var staticField))
            {
                IL.Emit(OpCodes.Ldsfld, staticField!);
                SetStackUnknown();
                return;
            }
        }

        // Instance private field access (this.#field or other.#field)
        var storageField = _ctx.ClassRegistry!.GetPrivateFieldStorage(className);
        if (storageField != null)
        {
            var cwtType = typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>)
                .MakeGenericType(typeof(object), typeof(Dictionary<string, object?>));
            var dictType = typeof(Dictionary<string, object?>);

            // Declare local for the dictionary result
            var dictLocal = IL.DeclareLocal(dictType);

            // Load __privateFields static field
            IL.Emit(OpCodes.Ldsfld, storageField);

            // Emit the object expression
            EmitExpression(gp.Object);
            EmitBoxIfNeeded(gp.Object);

            // Load address of dictLocal for out parameter
            IL.Emit(OpCodes.Ldloca, dictLocal);

            // Call TryGetValue(object key, out TValue value)
            var tryGetValueMethod = cwtType.GetMethod("TryGetValue", [typeof(object), dictType.MakeByRefType()])!;
            IL.Emit(OpCodes.Callvirt, tryGetValueMethod);

            // If false, throw TypeError (brand check failed)
            var successLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Brtrue, successLabel);

            // Brand check failed - throw TypeError
            IL.Emit(OpCodes.Ldstr, $"TypeError: Cannot read private member #{fieldName} from an object whose class did not declare it");
            IL.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
            IL.Emit(OpCodes.Throw);

            IL.MarkLabel(successLabel);

            // Access dictionary: dictLocal[fieldName]
            IL.Emit(OpCodes.Ldloc, dictLocal);
            IL.Emit(OpCodes.Ldstr, fieldName);
            IL.Emit(OpCodes.Callvirt, dictType.GetMethod("get_Item", [typeof(string)])!);

            SetStackUnknown();
            return;
        }

        // Fallback: check for static private field (covers ClassName.#staticField case)
        if (_ctx.ClassRegistry!.TryGetStaticPrivateField(className, fieldName, out var fallbackStaticField))
        {
            IL.Emit(OpCodes.Ldsfld, fallbackStaticField!);
            SetStackUnknown();
            return;
        }

        // No private field found
        IL.Emit(OpCodes.Ldstr, $"Private field '#{fieldName}' not found in class '{className}'");
        IL.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        IL.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits IL for ES2022 private field assignment (obj.#field = value).
    /// </summary>
    protected override void EmitSetPrivate(Expr.SetPrivate sp)
    {
        // Get the field name without the # prefix
        string fieldName = sp.Name.Lexeme;
        if (fieldName.StartsWith('#'))
            fieldName = fieldName[1..];

        string? className = _ctx.CurrentClassName;
        if (className == null)
        {
            IL.Emit(OpCodes.Ldstr, $"Cannot write private field '#{fieldName}' - class context not available");
            IL.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
            IL.Emit(OpCodes.Throw);
            return;
        }

        // Check if it's a static private field
        if (sp.Object is Expr.Variable classVar && classVar.Name.Lexeme == _ctx.CurrentClassName?.Split('.').Last()?.Split('_').Last())
        {
            if (_ctx.ClassRegistry!.TryGetStaticPrivateField(className, fieldName, out var staticField))
            {
                // Emit value, box, store, but also leave value on stack for expression result
                EmitExpression(sp.Value);
                EmitBoxIfNeeded(sp.Value);
                IL.Emit(OpCodes.Dup);  // Keep copy on stack for expression result
                IL.Emit(OpCodes.Stsfld, staticField!);
                SetStackUnknown();
                return;
            }
        }

        // Instance private field assignment
        var setStorageField = _ctx.ClassRegistry!.GetPrivateFieldStorage(className);
        if (setStorageField != null)
        {
            var cwtType = typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>)
                .MakeGenericType(typeof(object), typeof(Dictionary<string, object?>));
            var dictType = typeof(Dictionary<string, object?>);

            // Declare local for dictionary and value
            var dictLocal = IL.DeclareLocal(dictType);
            var valueLocal = IL.DeclareLocal(typeof(object));

            // Load __privateFields static field
            IL.Emit(OpCodes.Ldsfld, setStorageField);

            // Emit the object expression
            EmitExpression(sp.Object);
            EmitBoxIfNeeded(sp.Object);

            // Load address of dictLocal for out parameter
            IL.Emit(OpCodes.Ldloca, dictLocal);

            // Call TryGetValue
            var tryGetValueMethod = cwtType.GetMethod("TryGetValue", [typeof(object), dictType.MakeByRefType()])!;
            IL.Emit(OpCodes.Callvirt, tryGetValueMethod);

            // If false, throw TypeError
            var successLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Brtrue, successLabel);

            // Brand check failed
            IL.Emit(OpCodes.Ldstr, $"TypeError: Cannot write private member #{fieldName} to an object whose class did not declare it");
            IL.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
            IL.Emit(OpCodes.Throw);

            IL.MarkLabel(successLabel);

            // Emit value and store in local (for expression result)
            EmitExpression(sp.Value);
            EmitBoxIfNeeded(sp.Value);
            IL.Emit(OpCodes.Stloc, valueLocal);

            // Set dictionary: dictLocal[fieldName] = value
            IL.Emit(OpCodes.Ldloc, dictLocal);
            IL.Emit(OpCodes.Ldstr, fieldName);
            IL.Emit(OpCodes.Ldloc, valueLocal);
            IL.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item", [typeof(string), typeof(object)])!);

            // Leave value on stack for expression result
            IL.Emit(OpCodes.Ldloc, valueLocal);
            SetStackUnknown();
            return;
        }

        // Fallback for static private field
        if (_ctx.ClassRegistry!.TryGetStaticPrivateField(className, fieldName, out var setFallbackStaticField))
        {
            EmitExpression(sp.Value);
            EmitBoxIfNeeded(sp.Value);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stsfld, setFallbackStaticField!);
            SetStackUnknown();
            return;
        }

        // No private field found
        IL.Emit(OpCodes.Ldstr, $"Private field '#{fieldName}' not found in class '{className}'");
        IL.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        IL.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits IL for ES2022 private method call (obj.#method()).
    /// </summary>
    protected override void EmitCallPrivate(Expr.CallPrivate cp)
    {
        // Get the method name without the # prefix
        string methodName = cp.Name.Lexeme;
        if (methodName.StartsWith('#'))
            methodName = methodName[1..];

        string? className = _ctx.CurrentClassName;
        if (className == null)
        {
            IL.Emit(OpCodes.Ldstr, $"Cannot call private method '#{methodName}' - class context not available");
            IL.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
            IL.Emit(OpCodes.Throw);
            return;
        }

        // Check for static private method (ClassName.#method())
        if (cp.Object is Expr.Variable classVar && classVar.Name.Lexeme == _ctx.CurrentClassName?.Split('.').Last()?.Split('_').Last())
        {
            if (_ctx.ClassRegistry!.TryGetStaticPrivateMethod(className, methodName, out var staticMethod))
            {
                // Emit arguments
                foreach (var arg in cp.Arguments)
                {
                    EmitExpression(arg);
                    EmitBoxIfNeeded(arg);
                }

                // Call static method
                IL.Emit(OpCodes.Call, staticMethod!);
                SetStackUnknown();
                return;
            }
        }

        // Instance private method call (this.#method() or other.#method())
        if (_ctx.ClassRegistry!.TryGetPrivateMethod(className, methodName, out var instanceMethod))
        {
            // For instance methods, we need to verify the brand (that the object has this class's private slots)
            // We can check via the ConditionalWeakTable
            var callStorageField = _ctx.ClassRegistry!.GetPrivateFieldStorage(className);
            if (callStorageField != null)
            {
                var cwtType = typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>)
                    .MakeGenericType(typeof(object), typeof(Dictionary<string, object?>));
                var dictType = typeof(Dictionary<string, object?>);

                // Emit object and keep a copy for method call
                EmitExpression(cp.Object);
                EmitBoxIfNeeded(cp.Object);

                // Store object in local for reuse
                var objLocal = IL.DeclareLocal(typeof(object));
                IL.Emit(OpCodes.Stloc, objLocal);

                // Brand check: verify object is in the ConditionalWeakTable
                var dictLocal = IL.DeclareLocal(dictType);
                IL.Emit(OpCodes.Ldsfld, callStorageField);
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Ldloca, dictLocal);
                var tryGetValueMethod = cwtType.GetMethod("TryGetValue", [typeof(object), dictType.MakeByRefType()])!;
                IL.Emit(OpCodes.Callvirt, tryGetValueMethod);

                var validLabel = IL.DefineLabel();
                IL.Emit(OpCodes.Brtrue, validLabel);

                // Brand check failed
                IL.Emit(OpCodes.Ldstr, $"TypeError: Cannot call private method #{methodName} on an object whose class did not declare it");
                IL.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
                IL.Emit(OpCodes.Throw);

                IL.MarkLabel(validLabel);

                // Load receiver (cast to class type)
                IL.Emit(OpCodes.Ldloc, objLocal);
                if (_ctx.CurrentClassBuilder != null)
                {
                    IL.Emit(OpCodes.Castclass, _ctx.CurrentClassBuilder);
                }

                // Emit arguments
                foreach (var arg in cp.Arguments)
                {
                    EmitExpression(arg);
                    EmitBoxIfNeeded(arg);
                }

                // Call instance method
                IL.Emit(OpCodes.Callvirt, instanceMethod!);
                SetStackUnknown();
                return;
            }
            else
            {
                // No private fields, so no brand check needed (class has only private methods)
                EmitExpression(cp.Object);
                EmitBoxIfNeeded(cp.Object);
                if (_ctx.CurrentClassBuilder != null)
                {
                    IL.Emit(OpCodes.Castclass, _ctx.CurrentClassBuilder);
                }

                foreach (var arg in cp.Arguments)
                {
                    EmitExpression(arg);
                    EmitBoxIfNeeded(arg);
                }

                IL.Emit(OpCodes.Callvirt, instanceMethod!);
                SetStackUnknown();
                return;
            }
        }

        // Fallback: check for static private method
        if (_ctx.ClassRegistry!.TryGetStaticPrivateMethod(className, methodName, out var fallbackStaticMethod))
        {
            foreach (var arg in cp.Arguments)
            {
                EmitExpression(arg);
                EmitBoxIfNeeded(arg);
            }

            IL.Emit(OpCodes.Call, fallbackStaticMethod!);
            SetStackUnknown();
            return;
        }

        // No private method found
        IL.Emit(OpCodes.Ldstr, $"Private method '#{methodName}' not found in class '{className}'");
        IL.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        IL.Emit(OpCodes.Throw);
    }

    #endregion

    #region Category-Based Built-in Type Property Access

    /// <summary>
    /// Attempts to emit IL for built-in type property access using TypeCategoryResolver.
    /// Returns true if the property was handled, false to fall back to runtime dispatch.
    /// </summary>
    private bool TryEmitBuiltInTypePropertyGet(Expr.Get g, TypeInfo objType)
    {
        var category = TypeCategoryResolver.Classify(objType);
        string propName = g.Name.Lexeme;

        return category switch
        {
            TypeCategory.Map => TryEmitMapPropertyGet(g, propName),
            TypeCategory.Set => TryEmitSetPropertyGet(g, propName),
            TypeCategory.RegExp => TryEmitRegExpPropertyGet(g, propName),
            TypeCategory.Error => TryEmitErrorPropertyGet(g, propName),
            TypeCategory.Timeout => TryEmitTimeoutPropertyGet(g, propName),
            _ => false
        };
    }

    private bool TryEmitMapPropertyGet(Expr.Get g, string propName)
    {
        if (propName != "size") return false;

        EmitExpression(g.Object);
        EmitBoxIfNeeded(g.Object);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.MapSize);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        SetStackUnknown();
        return true;
    }

    private bool TryEmitSetPropertyGet(Expr.Get g, string propName)
    {
        if (propName != "size") return false;

        EmitExpression(g.Object);
        EmitBoxIfNeeded(g.Object);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.SetSize);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        SetStackUnknown();
        return true;
    }

    private bool TryEmitRegExpPropertyGet(Expr.Get g, string propName)
    {
        switch (propName)
        {
            case "source":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetSource);
                SetStackType(StackType.String);
                return true;
            case "flags":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetFlags);
                SetStackType(StackType.String);
                return true;
            case "global":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetGlobal);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                SetStackUnknown();
                return true;
            case "ignoreCase":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetIgnoreCase);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                SetStackUnknown();
                return true;
            case "multiline":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetMultiline);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                SetStackUnknown();
                return true;
            case "lastIndex":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpGetLastIndex);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                SetStackUnknown();
                return true;
            default:
                return false;
        }
    }

    private bool TryEmitErrorPropertyGet(Expr.Get g, string propName)
    {
        switch (propName)
        {
            case "name":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ErrorGetName);
                SetStackType(StackType.String);
                return true;
            case "message":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ErrorGetMessage);
                SetStackType(StackType.String);
                return true;
            case "stack":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ErrorGetStack);
                SetStackType(StackType.String);
                return true;
            case "errors":
                EmitExpression(g.Object);
                EmitBoxIfNeeded(g.Object);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.AggregateErrorGetErrors);
                SetStackUnknown();
                return true;
            default:
                return false;
        }
    }

    private bool TryEmitTimeoutPropertyGet(Expr.Get g, string propName)
    {
        if (propName != "hasRef") return false;

        EmitExpression(g.Object);
        EmitBoxIfNeeded(g.Object);
        IL.Emit(OpCodes.Castclass, _ctx.Runtime!.TSTimeoutType);
        IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSTimeoutHasRefGetter);
        IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
        SetStackUnknown();
        return true;
    }

    #endregion
}
