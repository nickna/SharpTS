using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    protected override void EmitCall(Expr.Call c)
    {
        // Handle console.log specially (handles both Variable and Get patterns)
        if (_helpers.TryEmitConsoleLog(c,
            arg => { EmitExpression(arg); EnsureBoxed(); },
            _ctx!.Runtime!.ConsoleLog,
            _ctx!.Runtime!.ConsoleLogMultiple))
        {
            return;
        }

        // Direct function call
        if (c.Callee is Expr.Variable funcVar && _ctx!.Functions.TryGetValue(_ctx.ResolveFunctionName(funcVar.Name.Lexeme), out var funcMethod))
        {
            var parameters = funcMethod.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i < c.Arguments.Count)
                {
                    EmitExpression(c.Arguments[i]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
            }
            _il.Emit(OpCodes.Call, funcMethod);
            SetStackUnknown();
            return;
        }

        // Method call: obj.method(args)
        if (c.Callee is Expr.Get methodGet)
        {
            var methodName = methodGet.Name.Lexeme;
            var arguments = c.Arguments;

            // Check compile-time type to use optimized emitters
            var objType = _ctx!.TypeMap?.Get(methodGet.Object);

            // Handle Map methods
            if (objType is TypeSystem.TypeInfo.Map)
            {
                EmitMapMethodCall(methodGet.Object, methodName, arguments);
                return;
            }

            // Handle Set methods
            if (objType is TypeSystem.TypeInfo.Set)
            {
                EmitSetMethodCall(methodGet.Object, methodName, arguments);
                return;
            }

            // Fallback for Map/Set methods when type isn't known at compile time
            if (methodName is "get" or "set" or "has" or "delete" or "clear" or "keys" or "values" or "entries" or "forEach" or "add")
            {
                EmitMapSetMethodCall(methodGet.Object, methodName, arguments);
                return;
            }
        }

        // Generic call through runtime
        EmitExpression(c.Callee);
        EnsureBoxed();

        _il.Emit(OpCodes.Ldc_I4, c.Arguments.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < c.Arguments.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(c.Arguments[i]);
            EnsureBoxed();
            _il.Emit(OpCodes.Stelem_Ref);
        }

        _il.Emit(OpCodes.Call, _ctx!.Runtime!.InvokeValue);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits a Map method call when the receiver is known to be a Map at compile time.
    /// </summary>
    private void EmitMapMethodCall(Expr receiver, string methodName, List<Expr> arguments)
    {
        EmitExpression(receiver);
        EnsureBoxed();

        switch (methodName)
        {
            case "set":
                if (arguments.Count >= 2)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    EmitExpression(arguments[1]);
                    EnsureBoxed();
                }
                else if (arguments.Count == 1)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Ldnull);
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapSet);
                SetStackUnknown();
                break;

            case "get":
                if (arguments.Count >= 1)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapGet);
                SetStackUnknown();
                break;

            case "has":
                if (arguments.Count >= 1)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapHas);
                _il.Emit(OpCodes.Box, _types.Boolean);
                SetStackUnknown();
                break;

            case "delete":
                if (arguments.Count >= 1)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapDelete);
                _il.Emit(OpCodes.Box, _types.Boolean);
                SetStackUnknown();
                break;

            case "clear":
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapClear);
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;

            case "keys":
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapKeys);
                SetStackUnknown();
                break;

            case "values":
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapValues);
                SetStackUnknown();
                break;

            case "entries":
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapEntries);
                SetStackUnknown();
                break;

            case "forEach":
                if (arguments.Count >= 1)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapForEach);
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;

            default:
                // Unknown method - use generic call
                _il.Emit(OpCodes.Pop); // Pop receiver
                EmitGenericCall(receiver, methodName, arguments);
                break;
        }
    }

    /// <summary>
    /// Emits a Set method call when the receiver is known to be a Set at compile time.
    /// </summary>
    private void EmitSetMethodCall(Expr receiver, string methodName, List<Expr> arguments)
    {
        EmitExpression(receiver);
        EnsureBoxed();

        switch (methodName)
        {
            case "add":
                if (arguments.Count >= 1)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetAdd);
                SetStackUnknown();
                break;

            case "has":
                if (arguments.Count >= 1)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetHas);
                _il.Emit(OpCodes.Box, _types.Boolean);
                SetStackUnknown();
                break;

            case "delete":
                if (arguments.Count >= 1)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetDelete);
                _il.Emit(OpCodes.Box, _types.Boolean);
                SetStackUnknown();
                break;

            case "clear":
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetClear);
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;

            case "keys":
            case "values":
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetValues);
                SetStackUnknown();
                break;

            case "entries":
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetEntries);
                SetStackUnknown();
                break;

            case "forEach":
                if (arguments.Count >= 1)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetForEach);
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;

            default:
                // Unknown method - use generic call
                _il.Emit(OpCodes.Pop); // Pop receiver
                EmitGenericCall(receiver, methodName, arguments);
                break;
        }
    }

    /// <summary>
    /// Emits a generic method call through the runtime.
    /// </summary>
    private void EmitGenericCall(Expr receiver, string methodName, List<Expr> arguments)
    {
        EmitExpression(receiver);
        EnsureBoxed();
        _il.Emit(OpCodes.Ldstr, methodName);
        _il.Emit(OpCodes.Ldc_I4, arguments.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < arguments.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(arguments[i]);
            EnsureBoxed();
            _il.Emit(OpCodes.Stelem_Ref);
        }
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.InvokeMethodValue);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits a Map or Set method call using runtime dispatch.
    /// </summary>
    private void EmitMapSetMethodCall(Expr receiver, string methodName, List<Expr> arguments)
    {
        EmitExpression(receiver);
        EnsureBoxed();

        switch (methodName)
        {
            case "set":
                // Map.set(key, value)
                if (arguments.Count >= 2)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    EmitExpression(arguments[1]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapSet);
                }
                else if (arguments.Count == 1)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Ldnull);
                    _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapSet);
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                    _il.Emit(OpCodes.Ldnull);
                    _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapSet);
                }
                SetStackUnknown();
                break;

            case "get":
                // Map.get(key)
                if (arguments.Count >= 1)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapGet);
                SetStackUnknown();
                break;

            case "has":
                // Map/Set.has(key)
                if (arguments.Count >= 1)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapHas);
                _il.Emit(OpCodes.Box, _types.Boolean);
                SetStackUnknown();
                break;

            case "delete":
                // Map/Set.delete(key)
                if (arguments.Count >= 1)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapDelete);
                _il.Emit(OpCodes.Box, _types.Boolean);
                SetStackUnknown();
                break;

            case "clear":
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapClear);
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;

            case "keys":
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapKeys);
                SetStackUnknown();
                break;

            case "values":
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapValues);
                SetStackUnknown();
                break;

            case "entries":
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapEntries);
                SetStackUnknown();
                break;

            case "forEach":
                if (arguments.Count >= 1)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapForEach);
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;

            case "add":
                // Set.add(value)
                if (arguments.Count >= 1)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetAdd);
                SetStackUnknown();
                break;

            default:
                // Fallback to generic call
                _il.Emit(OpCodes.Ldstr, methodName);
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.InvokeMethodValue);
                SetStackUnknown();
                break;
        }
    }

    protected override void EmitGet(Expr.Get g)
    {
        // Special case: Symbol well-known symbols
        if (g.Object is Expr.Variable symV && symV.Name.Lexeme == "Symbol")
        {
            switch (g.Name.Lexeme)
            {
                case "iterator":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolIterator);
                    SetStackUnknown();
                    return;
                case "asyncIterator":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolAsyncIterator);
                    SetStackUnknown();
                    return;
                case "toStringTag":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolToStringTag);
                    SetStackUnknown();
                    return;
                case "hasInstance":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolHasInstance);
                    SetStackUnknown();
                    return;
                case "isConcatSpreadable":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolIsConcatSpreadable);
                    SetStackUnknown();
                    return;
                case "toPrimitive":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolToPrimitive);
                    SetStackUnknown();
                    return;
                case "species":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolSpecies);
                    SetStackUnknown();
                    return;
                case "unscopables":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolUnscopables);
                    SetStackUnknown();
                    return;
            }
        }

        EmitExpression(g.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Ldstr, g.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);
        SetStackUnknown();
    }

    protected override void EmitSet(Expr.Set s)
    {
        EmitExpression(s.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Ldstr, s.Name.Lexeme);
        EmitExpression(s.Value);
        EnsureBoxed();

        _il.Emit(OpCodes.Dup);
        var resultTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, resultTemp);

        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetProperty);
        _il.Emit(OpCodes.Ldloc, resultTemp);
        SetStackUnknown();
    }

    protected override void EmitGetPrivate(Expr.GetPrivate gp)
    {
        // Get the field name without the # prefix
        string fieldName = gp.Name.Lexeme;
        if (fieldName.StartsWith('#'))
            fieldName = fieldName[1..];

        // Check if this is static private field access (ClassName.#field)
        if (gp.Object is Expr.Variable classVar &&
            _ctx?.CurrentClassName != null &&
            classVar.Name.Lexeme == _ctx.CurrentClassName.Split('.').Last()?.Split('_').Last())
        {
            // In generator state machines, we can't directly access private static fields
            // from another class, so always use the runtime helper for static private fields
            EmitDeclaringClassType();
            _il.Emit(OpCodes.Ldstr, fieldName);
            _il.Emit(OpCodes.Call, _ctx.Runtime!.GetStaticPrivateField);
            SetStackUnknown();
            return;
        }

        // Instance private field access - use runtime helper
        // Stack: GetPrivateField(instance, declaringClass, fieldName)
        EmitExpression(gp.Object);
        EnsureBoxed();
        EmitDeclaringClassType();
        _il.Emit(OpCodes.Ldstr, fieldName);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetPrivateField);
        SetStackUnknown();
    }

    protected override void EmitSetPrivate(Expr.SetPrivate sp)
    {
        // Get the field name without the # prefix
        string fieldName = sp.Name.Lexeme;
        if (fieldName.StartsWith('#'))
            fieldName = fieldName[1..];

        // Check if this is static private field access (ClassName.#field)
        if (sp.Object is Expr.Variable classVar &&
            _ctx?.CurrentClassName != null &&
            classVar.Name.Lexeme == _ctx.CurrentClassName.Split('.').Last()?.Split('_').Last())
        {
            // In generator state machines, we can't directly access private static fields
            // from another class, so always use the runtime helper for static private fields
            EmitExpression(sp.Value);
            EnsureBoxed();
            var valueTemp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, valueTemp);

            EmitDeclaringClassType();
            _il.Emit(OpCodes.Ldstr, fieldName);
            _il.Emit(OpCodes.Ldloc, valueTemp);
            _il.Emit(OpCodes.Call, _ctx.Runtime!.SetStaticPrivateField);

            // Leave value on stack for expression result
            _il.Emit(OpCodes.Ldloc, valueTemp);
            SetStackUnknown();
            return;
        }

        // Instance private field assignment - use runtime helper
        EmitExpression(sp.Value);
        EnsureBoxed();
        var valueLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueLocal);

        // SetPrivateField(instance, declaringClass, fieldName, value)
        EmitExpression(sp.Object);
        EnsureBoxed();
        EmitDeclaringClassType();
        _il.Emit(OpCodes.Ldstr, fieldName);
        _il.Emit(OpCodes.Ldloc, valueLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetPrivateField);

        // Leave value on stack for expression result
        _il.Emit(OpCodes.Ldloc, valueLocal);
        SetStackUnknown();
    }

    protected override void EmitCallPrivate(Expr.CallPrivate cp)
    {
        // Get the method name without the # prefix
        string methodName = cp.Name.Lexeme;
        if (methodName.StartsWith('#'))
            methodName = methodName[1..];

        // Check if this is static private method call (ClassName.#method())
        if (cp.Object is Expr.Variable classVar &&
            _ctx?.CurrentClassName != null &&
            classVar.Name.Lexeme == _ctx.CurrentClassName.Split('.').Last()?.Split('_').Last())
        {
            // In generator state machines, we can't directly call private static methods
            // from another class, so always use the runtime helper for static private methods
            EmitDeclaringClassType();
            _il.Emit(OpCodes.Ldstr, methodName);
            EmitArgumentArray(cp.Arguments);
            _il.Emit(OpCodes.Call, _ctx.Runtime!.CallStaticPrivateMethod);
            SetStackUnknown();
            return;
        }

        // Instance private method call - use runtime helper
        // Emit arguments first
        var argTemps = new List<LocalBuilder>();
        foreach (var arg in cp.Arguments)
        {
            EmitExpression(arg);
            EnsureBoxed();
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            argTemps.Add(temp);
        }

        // CallPrivateMethod(instance, declaringClass, methodName, args[])
        EmitExpression(cp.Object);
        EnsureBoxed();
        EmitDeclaringClassType();
        _il.Emit(OpCodes.Ldstr, methodName);

        // Build args array from temps
        _il.Emit(OpCodes.Ldc_I4, argTemps.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < argTemps.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldloc, argTemps[i]);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CallPrivateMethod);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits the typeof() for the declaring class containing the private member.
    /// </summary>
    private void EmitDeclaringClassType()
    {
        if (_ctx?.CurrentClassBuilder != null)
        {
            _il.Emit(OpCodes.Ldtoken, _ctx.CurrentClassBuilder);
            _il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
        }
        else
        {
            // Should not happen if called correctly - throw at runtime
            _il.Emit(OpCodes.Ldstr, "Cannot access private members outside of class context");
            _il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
            _il.Emit(OpCodes.Throw);
        }
    }

    /// <summary>
    /// Emits an object[] array from argument expressions.
    /// </summary>
    private void EmitArgumentArray(List<Expr> arguments)
    {
        _il.Emit(OpCodes.Ldc_I4, arguments.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < arguments.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(arguments[i]);
            EnsureBoxed();
            _il.Emit(OpCodes.Stelem_Ref);
        }
    }

    protected override void EmitNew(Expr.New n)
    {
        // Extract qualified name from callee expression
        var (namespaceParts, className) = ExtractQualifiedNameFromCallee(n.Callee);

        // Handle built-in types first (Set, Map, Date, Error, etc.)
        if (namespaceParts.Count == 0 && TryEmitBuiltInConstructor(className, n.Arguments))
        {
            return;
        }

        // Resolve class name (may be qualified for namespace classes or multi-module compilation)
        string resolvedClassName;
        if (namespaceParts.Count > 0)
        {
            // Build qualified name for namespace classes: Namespace_SubNs_ClassName
            string nsPath = string.Join("_", namespaceParts);
            resolvedClassName = $"{nsPath}_{className}";
        }
        else
        {
            resolvedClassName = _ctx!.ResolveClassName(className);
        }

        var ctorBuilder = _ctx!.ClassRegistry?.GetConstructorByQualifiedName(resolvedClassName);
        if (_ctx.Classes.TryGetValue(resolvedClassName, out var typeBuilder) && ctorBuilder != null)
        {
            int expectedParamCount = ctorBuilder.GetParameters().Length;

            foreach (var arg in n.Arguments)
            {
                EmitExpression(arg);
                EnsureBoxed();
            }

            for (int i = n.Arguments.Count; i < expectedParamCount; i++)
            {
                _il.Emit(OpCodes.Ldnull);
            }

            _il.Emit(OpCodes.Newobj, ctorBuilder);
            SetStackUnknown();
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackType(StackType.Null);
        }
    }

    /// <summary>
    /// Attempts to emit code for built-in constructors like Set, Map, etc.
    /// Returns true if the constructor was handled.
    /// </summary>
    private bool TryEmitBuiltInConstructor(string className, List<Expr> arguments)
    {
        switch (className)
        {
            case "Set":
                EmitNewSet(arguments);
                return true;
            case "Map":
                EmitNewMap(arguments);
                return true;
            default:
                return false;
        }
    }

    private void EmitNewSet(List<Expr> arguments)
    {
        if (arguments.Count == 0)
        {
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateSet);
        }
        else
        {
            EmitExpression(arguments[0]);
            EnsureBoxed();
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateSetFromArray);
        }
        SetStackUnknown();
    }

    private void EmitNewMap(List<Expr> arguments)
    {
        if (arguments.Count == 0)
        {
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateMap);
        }
        else
        {
            EmitExpression(arguments[0]);
            EnsureBoxed();
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateMapFromEntries);
        }
        SetStackUnknown();
    }

    /// <summary>
    /// Extracts a qualified class name from a callee expression.
    /// </summary>
    private static (List<string> namespaceParts, string className) ExtractQualifiedNameFromCallee(Expr callee)
    {
        List<string> parts = [];
        CollectGetChainParts(callee, parts);

        if (parts.Count == 0)
            return ([], "");

        var namespaceParts = parts.Count > 1 ? parts.Take(parts.Count - 1).ToList() : [];
        var className = parts[^1];
        return (namespaceParts, className);
    }

    private static void CollectGetChainParts(Expr expr, List<string> parts)
    {
        switch (expr)
        {
            case Expr.Variable v:
                parts.Add(v.Name.Lexeme);
                break;
            case Expr.Get g:
                CollectGetChainParts(g.Object, parts);
                parts.Add(g.Name.Lexeme);
                break;
        }
    }

    protected override void EmitThis()
    {
        if (_builder.ThisField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.ThisField);
            SetStackUnknown();
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackType(StackType.Null);
        }
    }

    protected override void EmitArrayLiteral(Expr.ArrayLiteral a)
    {
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
        SetStackUnknown();
    }

    protected override void EmitObjectLiteral(Expr.ObjectLiteral o)
    {
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
        SetStackUnknown();
    }

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
                _il.Emit(OpCodes.Ldstr, lk.Literal.Literal!.ToString()!);
                break;
            default:
                _il.Emit(OpCodes.Ldstr, "");
                break;
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

    protected override void EmitTemplateLiteral(Expr.TemplateLiteral tl)
    {
        if (tl.Strings.Count == 0 && tl.Expressions.Count == 0)
        {
            _il.Emit(OpCodes.Ldstr, "");
            SetStackType(StackType.String);
            return;
        }

        _il.Emit(OpCodes.Ldstr, tl.Strings[0]);

        for (int i = 0; i < tl.Expressions.Count; i++)
        {
            EmitExpression(tl.Expressions[i]);
            EnsureBoxed();
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.Stringify);
            _il.Emit(OpCodes.Call, Types.StringConcat2);

            if (i + 1 < tl.Strings.Count)
            {
                _il.Emit(OpCodes.Ldstr, tl.Strings[i + 1]);
                _il.Emit(OpCodes.Call, Types.StringConcat2);
            }
        }
        SetStackType(StackType.String);
    }

    protected override void EmitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral ttl)
    {
        EmitExpression(ttl.Tag);
        EnsureBoxed();

        _il.Emit(OpCodes.Ldc_I4, ttl.CookedStrings.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < ttl.CookedStrings.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            if (ttl.CookedStrings[i] != null)
                _il.Emit(OpCodes.Ldstr, ttl.CookedStrings[i]!);
            else
                _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        _il.Emit(OpCodes.Ldc_I4, ttl.RawStrings.Count);
        _il.Emit(OpCodes.Newarr, typeof(string));
        for (int i = 0; i < ttl.RawStrings.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldstr, ttl.RawStrings[i]);
            _il.Emit(OpCodes.Stelem_Ref);
        }

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

        _il.Emit(OpCodes.Call, _ctx!.Runtime!.InvokeTaggedTemplate);
        SetStackUnknown();
    }

    protected override void EmitCompoundAssign(Expr.CompoundAssign ca)
    {
        string name = ca.Name.Lexeme;

        EmitVariable(new Expr.Variable(ca.Name));
        EnsureBoxed();
        EmitExpression(ca.Value);
        EnsureBoxed();

        var op = ca.Operator.Type;
        if (op == TokenType.PLUS_EQUAL)
        {
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.Add);
        }
        else
        {
            var rightLocal = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, rightLocal);
            _il.Emit(OpCodes.Call, Types.ConvertToDoubleFromObject);
            _il.Emit(OpCodes.Ldloc, rightLocal);
            _il.Emit(OpCodes.Call, Types.ConvertToDoubleFromObject);

            switch (op)
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
                default:
                    _il.Emit(OpCodes.Add);
                    break;
            }
            _il.Emit(OpCodes.Box, typeof(double));
        }

        _il.Emit(OpCodes.Dup);
        var field = _builder.GetVariableField(name);
        if (field != null)
        {
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, field);
        }
        else if (_ctx!.Locals.TryGetLocal(name, out var local))
        {
            _il.Emit(OpCodes.Stloc, local);
        }

        SetStackUnknown();
    }

    protected override void EmitLogicalAssign(Expr.LogicalAssign la)
    {
        string name = la.Name.Lexeme;
        var endLabel = _il.DefineLabel();

        EmitVariable(new Expr.Variable(la.Name));
        EnsureBoxed();
        _il.Emit(OpCodes.Dup);

        switch (la.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brfalse, endLabel);
                break;
            case TokenType.OR_OR_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brtrue, endLabel);
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                var assignLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Brfalse, assignLabel);
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Isinst, _ctx!.Runtime!.UndefinedType);
                _il.Emit(OpCodes.Brtrue, assignLabel);
                // Not nullish - pop extra value and keep current
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Br, endLabel);
                _il.MarkLabel(assignLabel);
                // At assignLabel we have [value, value], pop one to match other cases
                _il.Emit(OpCodes.Pop);
                break;
        }

        _il.Emit(OpCodes.Pop);
        EmitExpression(la.Value);
        EnsureBoxed();
        _il.Emit(OpCodes.Dup);

        var field = _builder.GetVariableField(name);
        if (field != null)
        {
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, field);
        }
        else if (_ctx!.Locals.TryGetLocal(name, out var local))
        {
            _il.Emit(OpCodes.Stloc, local);
        }

        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    protected override void EmitLogicalSet(Expr.LogicalSet ls)
    {
        var skipLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        EmitExpression(ls.Object);
        EnsureBoxed();
        var objLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objLocal);

        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Ldstr, ls.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);
        _il.Emit(OpCodes.Dup);

        switch (ls.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brfalse, skipLabel);
                break;
            case TokenType.OR_OR_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brtrue, skipLabel);
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                var assignLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Brfalse, assignLabel);
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Isinst, _ctx!.Runtime!.UndefinedType);
                _il.Emit(OpCodes.Brtrue, assignLabel);
                // Not nullish - pop extra value and skip assignment
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Br, skipLabel);
                _il.MarkLabel(assignLabel);
                // At assignLabel we have [value, value], pop one to match other cases
                _il.Emit(OpCodes.Pop);
                break;
        }

        _il.Emit(OpCodes.Pop);
        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Ldstr, ls.Name.Lexeme);
        EmitExpression(ls.Value);
        EnsureBoxed();
        var resultLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Stloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetProperty);
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(skipLabel);
        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    protected override void EmitLogicalSetIndex(Expr.LogicalSetIndex lsi)
    {
        var skipLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        EmitExpression(lsi.Object);
        EnsureBoxed();
        var objLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objLocal);

        EmitExpression(lsi.Index);
        EnsureBoxed();
        var indexLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, indexLocal);

        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetIndex);
        _il.Emit(OpCodes.Dup);

        switch (lsi.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brfalse, skipLabel);
                break;
            case TokenType.OR_OR_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brtrue, skipLabel);
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                var assignLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Brfalse, assignLabel);
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Isinst, _ctx!.Runtime!.UndefinedType);
                _il.Emit(OpCodes.Brtrue, assignLabel);
                // Not nullish - pop extra value and skip assignment
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Br, skipLabel);
                _il.MarkLabel(assignLabel);
                // At assignLabel we have [value, value], pop one to match other cases
                _il.Emit(OpCodes.Pop);
                break;
        }

        _il.Emit(OpCodes.Pop);
        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Ldloc, indexLocal);
        EmitExpression(lsi.Value);
        EnsureBoxed();
        var resultLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Stloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetIndex);
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(skipLabel);
        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    protected override void EmitPrefixIncrement(Expr.PrefixIncrement pi)
    {
        if (pi.Operand is Expr.Variable v)
        {
            string name = v.Name.Lexeme;
            double delta = pi.Operator.Type == TokenType.PLUS_PLUS ? 1.0 : -1.0;

            EmitVariable(v);
            EnsureBoxed();
            _il.Emit(OpCodes.Call, Types.ConvertToDoubleFromObject);
            _il.Emit(OpCodes.Ldc_R8, delta);
            _il.Emit(OpCodes.Add);
            _il.Emit(OpCodes.Box, typeof(double));

            _il.Emit(OpCodes.Dup);
            var field = _builder.GetVariableField(name);
            if (field != null)
            {
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, temp);
                _il.Emit(OpCodes.Stfld, field);
            }
            else if (_ctx!.Locals.TryGetLocal(name, out var local))
            {
                _il.Emit(OpCodes.Stloc, local);
            }
        }
        SetStackUnknown();
    }

    protected override void EmitPostfixIncrement(Expr.PostfixIncrement poi)
    {
        if (poi.Operand is Expr.Variable v)
        {
            string name = v.Name.Lexeme;
            double delta = poi.Operator.Type == TokenType.PLUS_PLUS ? 1.0 : -1.0;

            EmitVariable(v);
            EnsureBoxed();
            _il.Emit(OpCodes.Dup);

            _il.Emit(OpCodes.Call, Types.ConvertToDoubleFromObject);
            _il.Emit(OpCodes.Ldc_R8, delta);
            _il.Emit(OpCodes.Add);
            _il.Emit(OpCodes.Box, typeof(double));

            var field = _builder.GetVariableField(name);
            if (field != null)
            {
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, temp);
                _il.Emit(OpCodes.Stfld, field);
            }
            else if (_ctx!.Locals.TryGetLocal(name, out var local))
            {
                _il.Emit(OpCodes.Stloc, local);
            }
        }
        SetStackUnknown();
    }

    protected override void EmitCompoundSet(Expr.CompoundSet cs)
    {
        EmitExpression(cs.Object);
        EnsureBoxed();
        var objTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objTemp);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);

        EmitExpression(cs.Value);
        EnsureBoxed();
        EmitCompoundOperation(cs.Operator.Type);

        var resultLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, resultLocal);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetProperty);

        _il.Emit(OpCodes.Ldloc, resultLocal);
        SetStackUnknown();
    }

    protected override void EmitCompoundSetIndex(Expr.CompoundSetIndex csi)
    {
        EmitExpression(csi.Object);
        EnsureBoxed();
        var objTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objTemp);

        EmitExpression(csi.Index);
        EnsureBoxed();
        var indexTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, indexTemp);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldloc, indexTemp);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetIndex);

        EmitExpression(csi.Value);
        EnsureBoxed();
        EmitCompoundOperation(csi.Operator.Type);

        var resultLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, resultLocal);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldloc, indexTemp);
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetIndex);

        _il.Emit(OpCodes.Ldloc, resultLocal);
        SetStackUnknown();
    }
}
