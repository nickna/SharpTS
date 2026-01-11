using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Function call and closure emission methods for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    private void EmitCall(Expr.Call c)
    {
        // Special case: super() or super.constructor() call in derived class
        if (c.Callee is Expr.Super superExpr && (superExpr.Method == null || superExpr.Method.Lexeme == "constructor"))
        {
            if (_ctx.CurrentSuperclassName != null &&
                _ctx.ClassConstructors != null &&
                _ctx.ClassConstructors.TryGetValue(_ctx.CurrentSuperclassName, out var parentCtor))
            {
                // Load this
                IL.Emit(OpCodes.Ldarg_0);

                // Load arguments
                foreach (var arg in c.Arguments)
                {
                    EmitExpression(arg);
                    EmitBoxIfNeeded(arg);
                }

                // Call parent constructor
                IL.Emit(OpCodes.Call, parentCtor);
                IL.Emit(OpCodes.Ldnull); // constructor call returns undefined
                return;
            }
        }

        // Special case: console.log (parser transforms it to Variable "console.log")
        if (c.Callee is Expr.Variable consoleVar && consoleVar.Name.Lexeme == "console.log")
        {
            if (c.Arguments.Count == 1)
            {
                // Single argument - use RuntimeTypes.ConsoleLog
                EmitExpression(c.Arguments[0]);
                EmitBoxIfNeeded(c.Arguments[0]);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ConsoleLog);
            }
            else if (c.Arguments.Count == 0)
            {
                // No arguments - just print newline
                IL.Emit(OpCodes.Call, _ctx.Types.GetMethodNoParams(_ctx.Types.Console, "WriteLine"));
            }
            else
            {
                // Multiple arguments - use RuntimeTypes.ConsoleLogMultiple
                IL.Emit(OpCodes.Ldc_I4, c.Arguments.Count);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
                for (int i = 0; i < c.Arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(c.Arguments[i]);
                    EmitBoxIfNeeded(c.Arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ConsoleLogMultiple);
            }
            IL.Emit(OpCodes.Ldnull); // console.log returns undefined
            return;
        }

        // Alternative: console.log kept as Get expression (fallback)
        if (c.Callee is Expr.Get get && get.Object is Expr.Variable v && v.Name.Lexeme == "console" && get.Name.Lexeme == "log")
        {
            if (c.Arguments.Count == 1)
            {
                EmitExpression(c.Arguments[0]);
                EmitBoxIfNeeded(c.Arguments[0]);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ConsoleLog);
            }
            else if (c.Arguments.Count == 0)
            {
                IL.Emit(OpCodes.Call, _ctx.Types.GetMethodNoParams(_ctx.Types.Console, "WriteLine"));
            }
            else
            {
                IL.Emit(OpCodes.Ldc_I4, c.Arguments.Count);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
                for (int i = 0; i < c.Arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(c.Arguments[i]);
                    EmitBoxIfNeeded(c.Arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ConsoleLogMultiple);
            }
            IL.Emit(OpCodes.Ldnull);
            return;
        }

        // Special case: Math methods
        if (c.Callee is Expr.Get mathGet && mathGet.Object is Expr.Variable mathVar && mathVar.Name.Lexeme == "Math")
        {
            EmitMathCall(mathGet.Name.Lexeme, c.Arguments);
            return;
        }

        // Special case: Object.keys(), Object.values(), Object.entries()
        if (c.Callee is Expr.Get objGet &&
            objGet.Object is Expr.Variable objVar &&
            objVar.Name.Lexeme == "Object")
        {
            EmitObjectStaticCall(objGet.Name.Lexeme, c.Arguments);
            return;
        }

        // Special case: Array.isArray()
        if (c.Callee is Expr.Get arrGet &&
            arrGet.Object is Expr.Variable arrVar &&
            arrVar.Name.Lexeme == "Array")
        {
            EmitArrayStaticCall(arrGet.Name.Lexeme, c.Arguments);
            return;
        }

        // Special case: JSON.parse(), JSON.stringify()
        if (c.Callee is Expr.Get jsonGet &&
            jsonGet.Object is Expr.Variable jsonVar &&
            jsonVar.Name.Lexeme == "JSON")
        {
            EmitJSONCall(jsonGet.Name.Lexeme, c.Arguments);
            return;
        }

        // Special case: Number.parseInt(), Number.parseFloat(), Number.isNaN(), etc.
        if (c.Callee is Expr.Get numGet &&
            numGet.Object is Expr.Variable numVar &&
            numVar.Name.Lexeme == "Number")
        {
            EmitNumberStaticCall(numGet.Name.Lexeme, c.Arguments);
            return;
        }

        // Special case: Promise.resolve(), Promise.reject(), Promise.all(), Promise.race()
        if (c.Callee is Expr.Get promiseGet &&
            promiseGet.Object is Expr.Variable promiseVar &&
            promiseVar.Name.Lexeme == "Promise")
        {
            EmitPromiseStaticCall(promiseGet.Name.Lexeme, c.Arguments);
            return;
        }

        // Special case: __objectRest (internal helper for object rest patterns)
        if (c.Callee is Expr.Variable restVar && restVar.Name.Lexeme == "__objectRest")
        {
            if (c.Arguments.Count >= 2)
            {
                // Emit source object (now accepts object to support both dictionaries and class instances)
                EmitExpression(c.Arguments[0]);
                EmitBoxIfNeeded(c.Arguments[0]);

                // Emit exclude keys (List<object>)
                EmitExpression(c.Arguments[1]);
                EmitBoxIfNeeded(c.Arguments[1]);
                IL.Emit(OpCodes.Castclass, _ctx.Types.ListOfObject);

                IL.Emit(OpCodes.Call, _ctx.Runtime!.ObjectRest);
                return;
            }
        }

        // Special case: Symbol() constructor - creates unique symbols
        if (c.Callee is Expr.Variable symVar && symVar.Name.Lexeme == "Symbol")
        {
            if (c.Arguments.Count == 0)
            {
                // Symbol() with no description
                IL.Emit(OpCodes.Ldnull);
            }
            else
            {
                // Symbol(description) - emit the description argument
                EmitExpression(c.Arguments[0]);
                // Convert to string if needed
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify);
            }
            // Create new $TSSymbol instance
            IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSSymbolCtor);
            return;
        }

        // Special case: BigInt() constructor - converts number/string to bigint
        if (c.Callee is Expr.Variable bigIntVar && bigIntVar.Name.Lexeme == "BigInt")
        {
            if (c.Arguments.Count != 1)
                throw new Exception("BigInt() requires exactly one argument.");

            EmitExpression(c.Arguments[0]);
            EmitBoxIfNeeded(c.Arguments[0]);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateBigInt);
            SetStackUnknown();
            return;
        }

        // Special case: Date() function call - returns current date as string
        if (c.Callee is Expr.Variable dateVar && dateVar.Name.Lexeme == "Date")
        {
            // Date() without 'new' returns current date as string
            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateDateNoArgs);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.DateToString);
            return;
        }

        // Special case: Date.now() static method
        if (c.Callee is Expr.Get dateGet &&
            dateGet.Object is Expr.Variable dateStaticVar &&
            dateStaticVar.Name.Lexeme == "Date" &&
            dateGet.Name.Lexeme == "now")
        {
            IL.Emit(OpCodes.Call, _ctx.Runtime!.DateNow);
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            return;
        }

        // Special case: Global parseInt()
        if (c.Callee is Expr.Variable parseIntVar && parseIntVar.Name.Lexeme == "parseInt")
        {
            EmitGlobalParseInt(c.Arguments);
            return;
        }

        // Special case: Global parseFloat()
        if (c.Callee is Expr.Variable parseFloatVar && parseFloatVar.Name.Lexeme == "parseFloat")
        {
            EmitGlobalParseFloat(c.Arguments);
            return;
        }

        // Special case: Global isNaN()
        if (c.Callee is Expr.Variable isNaNVar && isNaNVar.Name.Lexeme == "isNaN")
        {
            EmitGlobalIsNaN(c.Arguments);
            return;
        }

        // Special case: Global isFinite()
        if (c.Callee is Expr.Variable isFiniteVar && isFiniteVar.Name.Lexeme == "isFinite")
        {
            EmitGlobalIsFinite(c.Arguments);
            return;
        }

        // Special case: Static method call on external .NET type (e.g., Console.WriteLine())
        if (c.Callee is Expr.Get externalStaticGet &&
            externalStaticGet.Object is Expr.Variable externalClassVar &&
            _ctx.TypeMapper?.ExternalTypes.TryGetValue(externalClassVar.Name.Lexeme, out var externalType) == true)
        {
            EmitExternalStaticMethodCall(externalType, externalStaticGet.Name.Lexeme, c.Arguments);
            return;
        }

        // Special case: Static method call on class (e.g., Counter.increment())
        if (c.Callee is Expr.Get staticGet &&
            staticGet.Object is Expr.Variable classVar &&
            _ctx.Classes.TryGetValue(_ctx.ResolveClassName(classVar.Name.Lexeme), out var classBuilder))
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);
            if (_ctx.StaticMethods != null &&
                _ctx.StaticMethods.TryGetValue(resolvedClassName, out var classMethods) &&
                classMethods.TryGetValue(staticGet.Name.Lexeme, out var staticMethod))
            {
                var paramCount = staticMethod.GetParameters().Length;

                // Emit provided arguments
                foreach (var arg in c.Arguments)
                {
                    EmitExpression(arg);
                    EmitBoxIfNeeded(arg);
                }

                // Pad with nulls for missing arguments (for default parameters)
                for (int i = c.Arguments.Count; i < paramCount; i++)
                {
                    IL.Emit(OpCodes.Ldnull);
                }

                IL.Emit(OpCodes.Call, staticMethod);
                return;
            }
        }

        // Special case: Array/String methods
        if (c.Callee is Expr.Get methodGet)
        {
            EmitMethodCall(methodGet, c.Arguments);
            return;
        }

        // Regular function call (named top-level function)
        // First check for async functions
        if (c.Callee is Expr.Variable asyncVar && _ctx.AsyncMethods?.TryGetValue(asyncVar.Name.Lexeme, out var asyncMethod) == true)
        {
            EmitAsyncFunctionCall(asyncMethod, c.Arguments);
            return;
        }

        if (c.Callee is Expr.Variable funcVar)
        {
            // Resolve function name (may be module-qualified in multi-module compilation)
            string resolvedFuncName = _ctx.ResolveFunctionName(funcVar.Name.Lexeme);

            if (_ctx.Functions.TryGetValue(resolvedFuncName, out var methodBuilder))
            {
                // Determine target method (may be generic instantiation)
                MethodInfo targetMethod = methodBuilder;

                // Handle generic function call (e.g., identity<number>(42))
                if (_ctx.IsGenericFunction?.TryGetValue(resolvedFuncName, out var isGeneric) == true && isGeneric)
                {
                    if (c.TypeArgs != null && c.TypeArgs.Count > 0)
                    {
                        // Explicit type arguments
                        Type[] typeArgs = c.TypeArgs.Select(ResolveTypeArg).ToArray();
                        targetMethod = methodBuilder.MakeGenericMethod(typeArgs);
                    }
                    else
                    {
                        // Type inference fallback - use constraint type or object
                        var genericParams = _ctx.FunctionGenericParams![resolvedFuncName];
                        Type[] inferredArgs = new Type[genericParams.Length];
                        for (int i = 0; i < genericParams.Length; i++)
                        {
                            // Use the base type constraint if available, otherwise object
                            var baseConstraint = genericParams[i].BaseType;
                            inferredArgs[i] = (baseConstraint != null && !_ctx.Types.IsObject(baseConstraint))
                                ? baseConstraint
                                : _ctx.Types.Object;
                        }
                        targetMethod = methodBuilder.MakeGenericMethod(inferredArgs);
                    }
                }

                var paramCount = targetMethod.GetParameters().Length;

                // Check if this function has a rest parameter
                (int RestParamIndex, int RegularParamCount) restInfo = default;
                bool hasRestParam = _ctx.FunctionRestParams?.TryGetValue(resolvedFuncName, out restInfo) == true;
                bool hasSpreads = c.Arguments.Any(a => a is Expr.Spread);

                if (hasRestParam)
                {
                    int regularCount = restInfo.RegularParamCount;
                    int restIndex = restInfo.RestParamIndex;

                    // Emit regular arguments (up to rest param index)
                    for (int i = 0; i < Math.Min(regularCount, c.Arguments.Count); i++)
                    {
                        if (c.Arguments[i] is Expr.Spread spread)
                        {
                            // Spread in regular position - just emit the expression
                            EmitExpression(spread.Expression);
                            EmitBoxIfNeeded(spread.Expression);
                        }
                        else
                        {
                            EmitExpression(c.Arguments[i]);
                            EmitBoxIfNeeded(c.Arguments[i]);
                        }
                    }

                    // Pad regular args with nulls if needed
                    for (int i = c.Arguments.Count; i < regularCount; i++)
                    {
                        IL.Emit(OpCodes.Ldnull);
                    }

                    // Create array for rest parameter from remaining arguments
                    int restArgsCount = Math.Max(0, c.Arguments.Count - regularCount);
                    if (hasSpreads && restArgsCount > 0)
                    {
                        // Has spreads in rest args - use ExpandCallArgs helper
                        IL.Emit(OpCodes.Ldc_I4, restArgsCount);
                        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
                        for (int i = 0; i < restArgsCount; i++)
                        {
                            IL.Emit(OpCodes.Dup);
                            IL.Emit(OpCodes.Ldc_I4, i);
                            var arg = c.Arguments[regularCount + i];
                            if (arg is Expr.Spread spread)
                            {
                                EmitExpression(spread.Expression);
                                EmitBoxIfNeeded(spread.Expression);
                            }
                            else
                            {
                                EmitExpression(arg);
                                EmitBoxIfNeeded(arg);
                            }
                            IL.Emit(OpCodes.Stelem_Ref);
                        }

                        // Emit isSpread array
                        IL.Emit(OpCodes.Ldc_I4, restArgsCount);
                        IL.Emit(OpCodes.Newarr, _ctx.Types.Boolean);
                        for (int i = 0; i < restArgsCount; i++)
                        {
                            if (c.Arguments[regularCount + i] is Expr.Spread)
                            {
                                IL.Emit(OpCodes.Dup);
                                IL.Emit(OpCodes.Ldc_I4, i);
                                IL.Emit(OpCodes.Ldc_I4_1);
                                IL.Emit(OpCodes.Stelem_I1);
                            }
                        }
                        IL.Emit(OpCodes.Call, _ctx.Runtime!.ExpandCallArgs);
                        IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateArray);
                    }
                    else if (restArgsCount > 0)
                    {
                        // No spreads - simple array creation
                        IL.Emit(OpCodes.Ldc_I4, restArgsCount);
                        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
                        for (int i = 0; i < restArgsCount; i++)
                        {
                            IL.Emit(OpCodes.Dup);
                            IL.Emit(OpCodes.Ldc_I4, i);
                            EmitExpression(c.Arguments[regularCount + i]);
                            EmitBoxIfNeeded(c.Arguments[regularCount + i]);
                            IL.Emit(OpCodes.Stelem_Ref);
                        }
                        IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateArray);
                    }
                    else
                    {
                        // No rest args - empty array
                        IL.Emit(OpCodes.Ldc_I4, 0);
                        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
                        IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateArray);
                    }
                }
                else
                {
                    // No rest param - regular argument emission
                    foreach (var arg in c.Arguments)
                    {
                        if (arg is Expr.Spread spread)
                        {
                            // Spread in non-rest function - just emit the expression
                            EmitExpression(spread.Expression);
                            EmitBoxIfNeeded(spread.Expression);
                        }
                        else
                        {
                            EmitExpression(arg);
                            EmitBoxIfNeeded(arg);
                        }
                    }

                    // Pad with nulls for missing arguments (for default parameters)
                    for (int i = c.Arguments.Count; i < paramCount; i++)
                    {
                        IL.Emit(OpCodes.Ldnull);
                    }
                }

                IL.Emit(OpCodes.Call, targetMethod);
                SetStackUnknown(); // Function returns boxed object
                return;
            }
        }

        // Function value call (variable holding TSFunction, or direct arrow call)
        EmitFunctionValueCall(c);
    }

    private void EmitFunctionValueCall(Expr.Call c)
    {
        // Emit the callee (should produce a TSFunction on the stack)
        EmitExpression(c.Callee);

        // Check if any argument is a spread
        bool hasSpreads = c.Arguments.Any(a => a is Expr.Spread);

        if (!hasSpreads)
        {
            // Simple case: no spreads
            IL.Emit(OpCodes.Ldc_I4, c.Arguments.Count);
            IL.Emit(OpCodes.Newarr, _ctx.Types.Object);

            for (int i = 0; i < c.Arguments.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                EmitExpression(c.Arguments[i]);
                EmitBoxIfNeeded(c.Arguments[i]);
                IL.Emit(OpCodes.Stelem_Ref);
            }
        }
        else
        {
            // Complex case: has spreads, use ExpandCallArgs
            // First emit args array
            IL.Emit(OpCodes.Ldc_I4, c.Arguments.Count);
            IL.Emit(OpCodes.Newarr, _ctx.Types.Object);

            for (int i = 0; i < c.Arguments.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                if (c.Arguments[i] is Expr.Spread spread)
                {
                    EmitExpression(spread.Expression);
                    EmitBoxIfNeeded(spread.Expression);
                }
                else
                {
                    EmitExpression(c.Arguments[i]);
                    EmitBoxIfNeeded(c.Arguments[i]);
                }
                IL.Emit(OpCodes.Stelem_Ref);
            }

            // Now emit isSpread bool array
            IL.Emit(OpCodes.Ldc_I4, c.Arguments.Count);
            IL.Emit(OpCodes.Newarr, _ctx.Types.Boolean);

            for (int i = 0; i < c.Arguments.Count; i++)
            {
                if (c.Arguments[i] is Expr.Spread)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    IL.Emit(OpCodes.Ldc_I4_1); // true
                    IL.Emit(OpCodes.Stelem_I1);
                }
            }

            // Call ExpandCallArgs
            IL.Emit(OpCodes.Call, _ctx.Runtime!.ExpandCallArgs);
        }

        // Call $TSFunction.Invoke(object[] args)
        IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSFunctionInvoke);
    }

    /// <summary>
    /// Resolves a type argument string to a .NET Type for generic instantiation.
    /// </summary>
    private Type ResolveTypeArg(string typeArg)
    {
        return typeArg switch
        {
            "number" => _ctx.Types.Double,
            "string" => _ctx.Types.String,
            "boolean" => _ctx.Types.Boolean,
            _ when _ctx.GenericTypeParameters.TryGetValue(typeArg, out var gp) => gp,
            _ when _ctx.Classes.TryGetValue(_ctx.ResolveClassName(typeArg), out var tb) => tb,
            _ => _ctx.Types.Object
        };
    }

    private void EmitMethodCall(Expr.Get methodGet, List<Expr> arguments)
    {
        string methodName = methodGet.Name.Lexeme;

        // Try direct dispatch for known class instance methods
        TypeSystem.TypeInfo? objType = _ctx.TypeMap?.Get(methodGet.Object);
        if (TryEmitDirectMethodCall(methodGet.Object, objType, methodName, arguments))
            return;

        // Type-first dispatch: Use TypeEmitterRegistry if we have type information
        if (objType != null && _ctx.TypeEmitterRegistry != null)
        {
            var strategy = _ctx.TypeEmitterRegistry.GetStrategy(objType);
            if (strategy != null && strategy.TryEmitMethodCall(this, methodGet.Object, methodName, arguments))
                return;

            // Handle union types - try emitters for member types
            if (objType is TypeSystem.TypeInfo.Union union)
            {
                // Try string emitter if union contains string
                bool hasStringMember = union.Types.Any(t => t is TypeSystem.TypeInfo.String or TypeSystem.TypeInfo.StringLiteral);
                if (hasStringMember)
                {
                    var stringStrategy = _ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.String());
                    if (stringStrategy != null && stringStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, arguments))
                        return;
                }

                // Try array emitter if union contains array
                bool hasArrayMember = union.Types.Any(t => t is TypeSystem.TypeInfo.Array);
                if (hasArrayMember)
                {
                    var arrayStrategy = _ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.Array(new TypeSystem.TypeInfo.Any()));
                    if (arrayStrategy != null && arrayStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, arguments))
                        return;
                }
            }
        }

        // Methods that exist on both strings and arrays - runtime dispatch for any/unknown/union types
        // Note: String and Array types are handled by TypeEmitterRegistry above
        if (methodName is "slice" or "concat" or "includes" or "indexOf")
        {
            EmitAmbiguousMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

        // Note: Date, Map, Set, WeakMap, WeakSet, RegExp methods are handled by TypeEmitterRegistry above

        // For object method calls, we need to pass the receiver as 'this'
        // Stack order: receiver, function, args

        // Emit receiver object once and store in a local to avoid double evaluation
        EmitExpression(methodGet.Object);
        EmitBoxIfNeeded(methodGet.Object);
        var receiverLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, receiverLocal);

        // Load receiver for InvokeMethodValue's first argument
        IL.Emit(OpCodes.Ldloc, receiverLocal);

        // Get the method/function value from the object using same receiver
        IL.Emit(OpCodes.Ldloc, receiverLocal);
        IL.Emit(OpCodes.Ldstr, methodName);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);

        // Create args array
        IL.Emit(OpCodes.Ldc_I4, arguments.Count);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);

        for (int i = 0; i < arguments.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(arguments[i]);
            EmitBoxIfNeeded(arguments[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        // Call InvokeMethodValue(receiver, function, args) to bind 'this'
        IL.Emit(OpCodes.Call, _ctx.Runtime!.InvokeMethodValue);
    }

    private void EmitAmbiguousMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the object
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        var objLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, objLocal);

        // Check if it's a string
        var isStringLabel = IL.DefineLabel();
        var isListLabel = IL.DefineLabel();
        var doneLabel = IL.DefineLabel();

        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Isinst, _ctx.Types.String);
        IL.Emit(OpCodes.Brtrue, isStringLabel);

        // Assume it's a list if not a string
        IL.Emit(OpCodes.Br, isListLabel);

        // String path
        IL.MarkLabel(isStringLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Castclass, _ctx.Types.String);

        switch (methodName)
        {
            case "includes":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, _ctx.Types.String);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringIncludes);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;

            case "indexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, _ctx.Types.String);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringIndexOf);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                break;

            case "slice":
                // str.slice(start, end?) - with negative index support
                // StringSlice(string str, int argCount, object[] args)
                IL.Emit(OpCodes.Ldc_I4, arguments.Count); // argCount
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringSlice);
                break;

            case "concat":
                // str.concat(...strings)
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringConcat);
                break;
        }
        IL.Emit(OpCodes.Br, doneLabel);

        // List path
        IL.MarkLabel(isListLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Castclass, _ctx.Types.ListOfObject);

        switch (methodName)
        {
            case "includes":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayIncludes);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;

            case "indexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayIndexOf);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                break;

            case "slice":
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArraySlice);
                break;

            case "concat":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, _ctx.Types.ListOfObject);
                }
                else
                {
                    IL.Emit(OpCodes.Newobj, _ctx.Types.GetConstructor(_ctx.Types.ListOfObject));
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayConcat);
                break;
        }

        IL.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Try to emit a direct method call for known class instance types.
    /// Returns true if direct dispatch was emitted, false to fall back to runtime dispatch.
    /// </summary>
    private bool TryEmitDirectMethodCall(Expr receiver, TypeSystem.TypeInfo? receiverType,
        string methodName, List<Expr> arguments)
    {
        // Only handle Instance types (e.g., let p: Person = ...)
        if (receiverType is not TypeSystem.TypeInfo.Instance instance)
            return false;

        // Extract the class name from the instance's class type
        string? simpleClassName = instance.ClassType switch
        {
            TypeSystem.TypeInfo.Class c => c.Name,
            _ => null
        };
        if (simpleClassName == null)
            return false;

        // Check if this is an external .NET type (@DotNetType)
        if (_ctx.TypeMapper.ExternalTypes.TryGetValue(simpleClassName, out var externalType))
        {
            EmitExternalInstanceMethodCall(receiver, externalType, methodName, arguments);
            return true;
        }

        // Resolve to qualified name for multi-module compilation
        string className = _ctx.ResolveClassName(simpleClassName);

        // Also check if the qualified name is an external type
        if (_ctx.TypeMapper.ExternalTypes.TryGetValue(className, out externalType))
        {
            EmitExternalInstanceMethodCall(receiver, externalType, methodName, arguments);
            return true;
        }

        // Look up the method in the class hierarchy
        var methodBuilder = _ctx.ResolveInstanceMethod(className, methodName);
        if (methodBuilder == null)
            return false;

        // Get the class type builder to cast the receiver
        if (!_ctx.Classes.TryGetValue(className, out var classType))
            return false;

        // Get expected parameter count from method definition
        int expectedParamCount = methodBuilder.GetParameters().Length;

        // Emit: ((ClassName)receiver).method(args)
        EmitExpression(receiver);
        EmitBoxIfNeeded(receiver);
        IL.Emit(OpCodes.Castclass, classType);

        // Emit all provided arguments
        foreach (var arg in arguments)
        {
            EmitExpression(arg);
            EmitBoxIfNeeded(arg);
        }

        // Pad missing optional arguments with null
        for (int i = arguments.Count; i < expectedParamCount; i++)
        {
            IL.Emit(OpCodes.Ldnull);
        }

        // Emit the virtual call
        IL.Emit(OpCodes.Callvirt, methodBuilder);
        SetStackUnknown();
        return true;
    }

    /// <summary>
    /// Emits an instance method call on an external .NET type (via @DotNetType).
    /// </summary>
    private void EmitExternalInstanceMethodCall(Expr receiver, Type externalType, string methodName, List<Expr> arguments)
    {
        // Try to find the instance method - first with original name, then with PascalCase
        string pascalMethodName = NamingConventions.ToPascalCase(methodName);
        var methods = externalType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == methodName || m.Name == pascalMethodName)
            .ToArray();

        if (methods.Length == 0)
        {
            throw new Exception($"Instance method '{methodName}' (or '{pascalMethodName}') not found on external type {externalType.FullName}");
        }

        // Find matching method by argument count (simple overload resolution for MVP)
        var method = methods.FirstOrDefault(m => m.GetParameters().Length == arguments.Count)
                  ?? methods.First(); // Fallback to first if no exact match

        // Emit receiver and prepare for member access
        EmitExpression(receiver);
        EmitBoxIfNeeded(receiver);
        bool isValueType = PrepareReceiverForMemberAccess(externalType);

        // Emit arguments with type conversion
        var parameters = method.GetParameters();
        for (int i = 0; i < arguments.Count && i < parameters.Length; i++)
        {
            EmitExpression(arguments[i]);
            EmitExternalTypeConversion(parameters[i].ParameterType);
        }

        // Emit the call - use Call for value types (with address), Callvirt for reference types
        IL.Emit(isValueType ? OpCodes.Call : OpCodes.Callvirt, method);

        // Handle return value
        if (method.ReturnType == typeof(void))
        {
            IL.Emit(OpCodes.Ldnull); // void returns undefined
        }
        else
        {
            BoxResultIfValueType(method.ReturnType);
        }
        SetStackUnknown();
    }

    /// <summary>
    /// Emits construction of an external .NET type (via @DotNetType).
    /// </summary>
    private void EmitExternalTypeConstruction(Type externalType, List<Expr> arguments)
    {
        // Find a constructor matching the argument count
        var ctors = externalType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        if (ctors.Length == 0)
        {
            throw new Exception($"No public constructors found on external type {externalType.FullName}");
        }

        // Find matching constructor by argument count (simple overload resolution for MVP)
        var ctor = ctors.FirstOrDefault(c => c.GetParameters().Length == arguments.Count)
                ?? ctors.OrderBy(c => c.GetParameters().Length).First(); // Fallback to shortest

        // Emit arguments with type conversion
        var parameters = ctor.GetParameters();
        for (int i = 0; i < arguments.Count && i < parameters.Length; i++)
        {
            EmitExpression(arguments[i]);
            EmitExternalTypeConversion(parameters[i].ParameterType);
        }

        // Emit newobj instruction
        IL.Emit(OpCodes.Newobj, ctor);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits a static method call on an external .NET type (via @DotNetType).
    /// </summary>
    private void EmitExternalStaticMethodCall(Type externalType, string methodName, List<Expr> arguments)
    {
        // Try to find the static method - first with original name, then with PascalCase
        string pascalMethodName = NamingConventions.ToPascalCase(methodName);
        var methods = externalType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == methodName || m.Name == pascalMethodName)
            .ToArray();

        if (methods.Length == 0)
        {
            throw new Exception($"Static method '{methodName}' (or '{pascalMethodName}') not found on external type {externalType.FullName}");
        }

        // Find matching method by argument count (simple overload resolution for MVP)
        var method = methods.FirstOrDefault(m => m.GetParameters().Length == arguments.Count)
                  ?? methods.First(); // Fallback to first if no exact match

        // Emit arguments with type conversion
        var parameters = method.GetParameters();
        for (int i = 0; i < arguments.Count && i < parameters.Length; i++)
        {
            EmitExpression(arguments[i]);
            EmitExternalTypeConversion(parameters[i].ParameterType);
        }

        // Emit the static call
        IL.Emit(OpCodes.Call, method);

        // Handle return value
        if (method.ReturnType == typeof(void))
        {
            IL.Emit(OpCodes.Ldnull); // void returns undefined
        }
        else if (method.ReturnType.IsValueType)
        {
            IL.Emit(OpCodes.Box, method.ReturnType);
        }
        SetStackUnknown();
    }

    /// <summary>
    /// Emits type conversion for passing arguments to external .NET methods.
    /// </summary>
    private void EmitExternalTypeConversion(Type targetType)
    {
        if (targetType == _ctx.Types.Double || targetType == typeof(double))
        {
            // If we already have a native double on the stack, no conversion needed
            if (_stackType == StackType.Double)
                return;
            EmitUnboxToDouble();
        }
        else if (targetType == _ctx.Types.Boolean || targetType == typeof(bool))
        {
            // If we already have a native boolean on the stack, no conversion needed
            if (_stackType == StackType.Boolean)
                return;
            IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Boolean);
        }
        else if (targetType == _ctx.Types.Int32 || targetType == typeof(int))
        {
            // If we already have a native double, just convert to int
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_I4);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_I4);
        }
        else if (targetType == _ctx.Types.Int64 || targetType == typeof(long))
        {
            // If we already have a native double, just convert to long
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_I8);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_I8);
        }
        else if (targetType == _ctx.Types.String || targetType == typeof(string))
        {
            // If we already have a string on the stack, no conversion needed
            if (_stackType == StackType.String)
                return;
            IL.Emit(OpCodes.Castclass, _ctx.Types.String);
        }
        else if (targetType.IsValueType)
        {
            IL.Emit(OpCodes.Unbox_Any, targetType);
        }
        else if (!_ctx.Types.IsObject(targetType))
        {
            IL.Emit(OpCodes.Castclass, targetType);
        }
        // For object type, no conversion needed
    }
}
