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
                IL.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", Type.EmptyTypes)!);
            }
            else
            {
                // Multiple arguments - use RuntimeTypes.ConsoleLogMultiple
                IL.Emit(OpCodes.Ldc_I4, c.Arguments.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
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
                IL.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", Type.EmptyTypes)!);
            }
            else
            {
                IL.Emit(OpCodes.Ldc_I4, c.Arguments.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
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
                IL.Emit(OpCodes.Castclass, typeof(List<object>));

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
            _stackType = StackType.Unknown;
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
            IL.Emit(OpCodes.Box, typeof(double));
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

        // Special case: Static method call on class (e.g., Counter.increment())
        if (c.Callee is Expr.Get staticGet &&
            staticGet.Object is Expr.Variable classVar &&
            _ctx.Classes.TryGetValue(classVar.Name.Lexeme, out var classBuilder))
        {
            if (_ctx.StaticMethods != null &&
                _ctx.StaticMethods.TryGetValue(classVar.Name.Lexeme, out var classMethods) &&
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

        if (c.Callee is Expr.Variable funcVar && _ctx.Functions.TryGetValue(funcVar.Name.Lexeme, out var methodBuilder))
        {
            // Determine target method (may be generic instantiation)
            MethodInfo targetMethod = methodBuilder;

            // Handle generic function call (e.g., identity<number>(42))
            if (_ctx.IsGenericFunction?.TryGetValue(funcVar.Name.Lexeme, out var isGeneric) == true && isGeneric)
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
                    var genericParams = _ctx.FunctionGenericParams![funcVar.Name.Lexeme];
                    Type[] inferredArgs = new Type[genericParams.Length];
                    for (int i = 0; i < genericParams.Length; i++)
                    {
                        // Use the base type constraint if available, otherwise object
                        var baseConstraint = genericParams[i].BaseType;
                        inferredArgs[i] = (baseConstraint != null && baseConstraint != typeof(object))
                            ? baseConstraint
                            : typeof(object);
                    }
                    targetMethod = methodBuilder.MakeGenericMethod(inferredArgs);
                }
            }

            var paramCount = targetMethod.GetParameters().Length;

            // Check if this function has a rest parameter
            (int RestParamIndex, int RegularParamCount) restInfo = default;
            bool hasRestParam = _ctx.FunctionRestParams?.TryGetValue(funcVar.Name.Lexeme, out restInfo) == true;
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
                    IL.Emit(OpCodes.Newarr, typeof(object));
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
                    IL.Emit(OpCodes.Newarr, typeof(bool));
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
                    IL.Emit(OpCodes.Newarr, typeof(object));
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
                    IL.Emit(OpCodes.Newarr, typeof(object));
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
            _stackType = StackType.Unknown; // Function returns boxed object
            return;
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
            IL.Emit(OpCodes.Newarr, typeof(object));

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
            IL.Emit(OpCodes.Newarr, typeof(object));

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
            IL.Emit(OpCodes.Newarr, typeof(bool));

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
            "number" => typeof(double),
            "string" => typeof(string),
            "boolean" => typeof(bool),
            _ when _ctx.GenericTypeParameters.TryGetValue(typeArg, out var gp) => gp,
            _ when _ctx.Classes.TryGetValue(typeArg, out var tb) => tb,
            _ => typeof(object)
        };
    }

    private void EmitMathCall(string methodName, List<Expr> arguments)
    {
        if (methodName == "random")
        {
            IL.Emit(OpCodes.Call, _ctx.Runtime!.Random);
            IL.Emit(OpCodes.Box, typeof(double));
            _stackType = StackType.Unknown;
            return;
        }

        // Handle variadic min/max (JavaScript allows any number of arguments)
        if (methodName is "min" or "max")
        {
            var minMaxMethod = methodName == "min"
                ? typeof(Math).GetMethod("Min", [typeof(double), typeof(double)])!
                : typeof(Math).GetMethod("Max", [typeof(double), typeof(double)])!;

            if (arguments.Count == 0)
            {
                // No args: min() returns Infinity, max() returns -Infinity
                IL.Emit(OpCodes.Ldc_R8, methodName == "min" ? double.PositiveInfinity : double.NegativeInfinity);
            }
            else
            {
                // Emit first argument
                EmitExpressionAsDouble(arguments[0]);
                // Chain remaining arguments with min/max calls
                for (int i = 1; i < arguments.Count; i++)
                {
                    EmitExpressionAsDouble(arguments[i]);
                    IL.Emit(OpCodes.Call, minMaxMethod);
                }
            }
            IL.Emit(OpCodes.Box, typeof(double));
            _stackType = StackType.Unknown;
            return;
        }

        // Emit all arguments as doubles
        foreach (var arg in arguments)
        {
            EmitExpressionAsDouble(arg);
        }

        if (methodName == "round")
        {
            // Use MidpointRounding.AwayFromZero to match JavaScript behavior
            IL.Emit(OpCodes.Ldc_I4, (int)MidpointRounding.AwayFromZero);
            var roundMethod = typeof(Math).GetMethod("Round", [typeof(double), typeof(MidpointRounding)])!;
            IL.Emit(OpCodes.Call, roundMethod);
            IL.Emit(OpCodes.Box, typeof(double));
            _stackType = StackType.Unknown;
            return;
        }

        if (methodName == "sign")
        {
            // Math.Sign returns int, need to convert to double
            var signMethod = typeof(Math).GetMethod("Sign", [typeof(double)])!;
            IL.Emit(OpCodes.Call, signMethod);
            IL.Emit(OpCodes.Conv_R8); // Convert int to double
            IL.Emit(OpCodes.Box, typeof(double));
            _stackType = StackType.Unknown;
            return;
        }

        MethodInfo? mathMethod = methodName switch
        {
            "abs" => typeof(Math).GetMethod("Abs", [typeof(double)]),
            "floor" => typeof(Math).GetMethod("Floor", [typeof(double)]),
            "ceil" => typeof(Math).GetMethod("Ceiling", [typeof(double)]),
            "sqrt" => typeof(Math).GetMethod("Sqrt", [typeof(double)]),
            "sin" => typeof(Math).GetMethod("Sin", [typeof(double)]),
            "cos" => typeof(Math).GetMethod("Cos", [typeof(double)]),
            "tan" => typeof(Math).GetMethod("Tan", [typeof(double)]),
            "log" => typeof(Math).GetMethod("Log", [typeof(double)]),
            "exp" => typeof(Math).GetMethod("Exp", [typeof(double)]),
            "trunc" => typeof(Math).GetMethod("Truncate", [typeof(double)]),
            "pow" => typeof(Math).GetMethod("Pow", [typeof(double), typeof(double)]),
            _ => null
        };

        if (mathMethod != null)
        {
            IL.Emit(OpCodes.Call, mathMethod);
            IL.Emit(OpCodes.Box, typeof(double));
            _stackType = StackType.Unknown;
        }
        else
        {
            throw new Exception($"Compile Error: Unknown Math method '{methodName}'.");
        }
    }

    private void EmitMethodCall(Expr.Get methodGet, List<Expr> arguments)
    {
        string methodName = methodGet.Name.Lexeme;

        // Try direct dispatch for known class instance methods
        TypeSystem.TypeInfo? objType = _ctx.TypeMap?.Get(methodGet.Object);
        if (TryEmitDirectMethodCall(methodGet.Object, objType, methodName, arguments))
            return;

        // Methods that exist on both strings and arrays - use TypeMap for static dispatch
        if (methodName is "slice" or "concat" or "includes" or "indexOf")
        {
            // Use static type information if available (already fetched above)
            if (objType is TypeSystem.TypeInfo.Primitive { Type: TokenType.TYPE_STRING })
            {
                EmitStringMethodCall(methodGet.Object, methodName, arguments);
                return;
            }
            if (objType is TypeSystem.TypeInfo.Array)
            {
                EmitArrayMethodCall(methodGet.Object, methodName, arguments);
                return;
            }

            // Fallback: runtime dispatch for any/unknown/union types
            EmitAmbiguousMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

        // Special case: String-only method calls
        if (methodName is "charAt" or "substring" or "toUpperCase" or "toLowerCase"
            or "trim" or "replace" or "split" or "startsWith" or "endsWith"
            or "repeat" or "padStart" or "padEnd" or "charCodeAt" or "lastIndexOf"
            or "trimStart" or "trimEnd" or "replaceAll" or "at" or "match" or "search")
        {
            EmitStringMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

        // Special case: Array-only method calls
        if (methodName is "pop" or "shift" or "unshift" or "map" or "filter" or "forEach"
            or "push" or "find" or "findIndex" or "some" or "every" or "reduce" or "join"
            or "reverse")
        {
            EmitArrayMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

        // Special case: Date instance method calls
        if (methodName is "getTime" or "getFullYear" or "getMonth" or "getDate" or "getDay"
            or "getHours" or "getMinutes" or "getSeconds" or "getMilliseconds" or "getTimezoneOffset"
            or "setTime" or "setFullYear" or "setMonth" or "setDate"
            or "setHours" or "setMinutes" or "setSeconds" or "setMilliseconds"
            or "toISOString" or "toDateString" or "toTimeString" or "valueOf"
            or "toString")
        {
            EmitDateMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

        // Special case: Map method calls (based on type info)
        if (objType is TypeSystem.TypeInfo.Map)
        {
            EmitMapMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

        // Special case: Set method calls (based on type info)
        if (objType is TypeSystem.TypeInfo.Set)
        {
            EmitSetMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

        // Special case: RegExp method calls
        if (methodName is "test" or "exec")
        {
            EmitRegExpMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

        // Get the method/function value from the object
        EmitExpression(methodGet);

        // Create args array
        IL.Emit(OpCodes.Ldc_I4, arguments.Count);
        IL.Emit(OpCodes.Newarr, typeof(object));

        for (int i = 0; i < arguments.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(arguments[i]);
            EmitBoxIfNeeded(arguments[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        // Call TSFunction.Invoke or use runtime dispatch
        IL.Emit(OpCodes.Call, _ctx.Runtime!.InvokeValue);
    }

    private void EmitStringMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the string object
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);
        IL.Emit(OpCodes.Castclass, typeof(string));

        switch (methodName)
        {
            case "charAt":
                // str.charAt(index) -> str[index].ToString() or "" if out of range
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringCharAt);
                return;

            case "substring":
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringSubstring);
                return;

            case "indexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringIndexOf);
                IL.Emit(OpCodes.Box, typeof(double));
                return;

            case "toUpperCase":
                IL.Emit(OpCodes.Callvirt, typeof(string).GetMethod("ToUpper", Type.EmptyTypes)!);
                return;

            case "toLowerCase":
                IL.Emit(OpCodes.Callvirt, typeof(string).GetMethod("ToLower", Type.EmptyTypes)!);
                return;

            case "trim":
                IL.Emit(OpCodes.Callvirt, typeof(string).GetMethod("Trim", Type.EmptyTypes)!);
                return;

            case "replace":
                // str.replace(searchValue, replacement) - searchValue can be string or RegExp
                if (arguments.Count >= 2)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    // Don't cast - let runtime handle string or RegExp
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify); // replacement is always string
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringReplaceRegExp);
                return;

            case "split":
                // str.split(separator) - separator can be string or RegExp
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    // Don't cast - let runtime handle string or RegExp
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringSplitRegExp);
                return;

            case "match":
                // str.match(pattern) - pattern can be string or RegExp
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringMatchRegExp);
                return;

            case "search":
                // str.search(pattern) - pattern can be string or RegExp
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringSearchRegExp);
                IL.Emit(OpCodes.Box, typeof(double));
                return;

            case "includes":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringIncludes);
                IL.Emit(OpCodes.Box, typeof(bool));
                return;

            case "startsWith":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringStartsWith);
                IL.Emit(OpCodes.Box, typeof(bool));
                return;

            case "endsWith":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringEndsWith);
                IL.Emit(OpCodes.Box, typeof(bool));
                return;

            case "slice":
                // str.slice(start, end?) - with negative index support
                // StringSlice(string str, int argCount, object[] args)
                // Stack already has: [string]
                // Need to push: argCount, then args array
                IL.Emit(OpCodes.Ldc_I4, arguments.Count); // [string, argCount]
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, typeof(object)); // [string, argCount, array]
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringSlice);
                return;

            case "repeat":
                // str.repeat(count)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Unbox_Any, typeof(double));
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringRepeat);
                return;

            case "padStart":
                // str.padStart(targetLength, padString?)
                // StringPadStart(string str, int argCount, object[] args)
                // Stack already has: [string]
                // Need to push: argCount, then args array
                IL.Emit(OpCodes.Ldc_I4, arguments.Count); // [string, argCount]
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, typeof(object)); // [string, argCount, array]
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringPadStart);
                return;

            case "padEnd":
                // str.padEnd(targetLength, padString?)
                // StringPadEnd(string str, int argCount, object[] args)
                // Stack already has: [string]
                // Need to push: argCount, then args array
                IL.Emit(OpCodes.Ldc_I4, arguments.Count); // [string, argCount]
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, typeof(object)); // [string, argCount, array]
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringPadEnd);
                return;

            case "charCodeAt":
                // str.charCodeAt(index) -> number
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Unbox_Any, typeof(double));
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringCharCodeAt);
                IL.Emit(OpCodes.Box, typeof(double));
                return;

            case "concat":
                // str.concat(...strings)
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringConcat);
                return;

            case "lastIndexOf":
                // str.lastIndexOf(search) -> number
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringLastIndexOf);
                IL.Emit(OpCodes.Box, typeof(double));
                return;

            case "trimStart":
                // str.trimStart() -> string
                IL.Emit(OpCodes.Callvirt, typeof(string).GetMethod("TrimStart", Type.EmptyTypes)!);
                return;

            case "trimEnd":
                // str.trimEnd() -> string
                IL.Emit(OpCodes.Callvirt, typeof(string).GetMethod("TrimEnd", Type.EmptyTypes)!);
                return;

            case "replaceAll":
                // str.replaceAll(search, replacement) -> string
                if (arguments.Count >= 2)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, typeof(string));
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                    IL.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringReplaceAll);
                return;

            case "at":
                // str.at(index) -> string | null
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Unbox_Any, typeof(double));
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringAt);
                return;
        }
    }

    private void EmitArrayMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the array object
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        // Cast to List<object>
        IL.Emit(OpCodes.Castclass, typeof(List<object>));

        switch (methodName)
        {
            case "pop":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayPop);
                return;

            case "shift":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayShift);
                return;

            case "unshift":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayUnshift);
                IL.Emit(OpCodes.Box, typeof(double));
                return;

            case "push":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayPush);
                IL.Emit(OpCodes.Box, typeof(double));
                return;

            case "slice":
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArraySlice);
                return;

            case "map":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayMap);
                return;

            case "filter":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayFilter);
                return;

            case "forEach":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayForEach);
                IL.Emit(OpCodes.Ldnull); // forEach returns undefined
                return;

            case "find":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayFind);
                return;

            case "findIndex":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayFindIndex);
                IL.Emit(OpCodes.Box, typeof(double));
                return;

            case "some":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArraySome);
                // Method now returns boxed bool (object), no additional boxing needed
                return;

            case "every":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayEvery);
                // Method now returns boxed bool (object), no additional boxing needed
                return;

            case "reduce":
                // reduce(callback, initialValue?)
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayReduce);
                return;

            case "join":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayJoin);
                return;

            case "concat":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayConcat);
                return;

            case "reverse":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayReverse);
                return;

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
                // Method now returns boxed bool (object), no additional boxing needed
                return;

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
                IL.Emit(OpCodes.Box, typeof(double));
                return;
        }
    }

    private void EmitAmbiguousMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the object
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        var objLocal = IL.DeclareLocal(typeof(object));
        IL.Emit(OpCodes.Stloc, objLocal);

        // Check if it's a string
        var isStringLabel = IL.DefineLabel();
        var isListLabel = IL.DefineLabel();
        var doneLabel = IL.DefineLabel();

        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Isinst, typeof(string));
        IL.Emit(OpCodes.Brtrue, isStringLabel);

        // Assume it's a list if not a string
        IL.Emit(OpCodes.Br, isListLabel);

        // String path
        IL.MarkLabel(isStringLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Castclass, typeof(string));

        switch (methodName)
        {
            case "includes":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringIncludes);
                IL.Emit(OpCodes.Box, typeof(bool));
                break;

            case "indexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringIndexOf);
                IL.Emit(OpCodes.Box, typeof(double));
                break;

            case "slice":
                // str.slice(start, end?) - with negative index support
                // StringSlice(string str, int argCount, object[] args)
                IL.Emit(OpCodes.Ldc_I4, arguments.Count); // argCount
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
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
                IL.Emit(OpCodes.Newarr, typeof(object));
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
        IL.Emit(OpCodes.Castclass, typeof(List<object>));

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
                IL.Emit(OpCodes.Box, typeof(bool));
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
                IL.Emit(OpCodes.Box, typeof(double));
                break;

            case "slice":
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
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
                    IL.Emit(OpCodes.Castclass, typeof(List<object>));
                }
                else
                {
                    IL.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayConcat);
                break;
        }

        IL.MarkLabel(doneLabel);
    }

    private void EmitDateMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the Date object
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        switch (methodName)
        {
            // Getters (no arguments, return double)
            case "getTime":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetTime);
                IL.Emit(OpCodes.Box, typeof(double));
                return;
            case "getFullYear":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetFullYear);
                IL.Emit(OpCodes.Box, typeof(double));
                return;
            case "getMonth":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetMonth);
                IL.Emit(OpCodes.Box, typeof(double));
                return;
            case "getDate":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetDate);
                IL.Emit(OpCodes.Box, typeof(double));
                return;
            case "getDay":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetDay);
                IL.Emit(OpCodes.Box, typeof(double));
                return;
            case "getHours":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetHours);
                IL.Emit(OpCodes.Box, typeof(double));
                return;
            case "getMinutes":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetMinutes);
                IL.Emit(OpCodes.Box, typeof(double));
                return;
            case "getSeconds":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetSeconds);
                IL.Emit(OpCodes.Box, typeof(double));
                return;
            case "getMilliseconds":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetMilliseconds);
                IL.Emit(OpCodes.Box, typeof(double));
                return;
            case "getTimezoneOffset":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetTimezoneOffset);
                IL.Emit(OpCodes.Box, typeof(double));
                return;

            // Simple setters (single argument, return double)
            case "setTime":
                if (arguments.Count > 0)
                {
                    EmitExpressionAsDouble(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, double.NaN);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateSetTime);
                IL.Emit(OpCodes.Box, typeof(double));
                return;
            case "setDate":
                if (arguments.Count > 0)
                {
                    EmitExpressionAsDouble(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, double.NaN);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateSetDate);
                IL.Emit(OpCodes.Box, typeof(double));
                return;
            case "setMilliseconds":
                if (arguments.Count > 0)
                {
                    EmitExpressionAsDouble(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, double.NaN);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateSetMilliseconds);
                IL.Emit(OpCodes.Box, typeof(double));
                return;

            // Multi-argument setters (variadic, packaged as object[])
            case "setFullYear":
            case "setMonth":
            case "setHours":
            case "setMinutes":
            case "setSeconds":
                // Create args array
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                // Call appropriate runtime method
                var setMethod = methodName switch
                {
                    "setFullYear" => _ctx.Runtime!.DateSetFullYear,
                    "setMonth" => _ctx.Runtime!.DateSetMonth,
                    "setHours" => _ctx.Runtime!.DateSetHours,
                    "setMinutes" => _ctx.Runtime!.DateSetMinutes,
                    "setSeconds" => _ctx.Runtime!.DateSetSeconds,
                    _ => throw new Exception($"Unknown Date method: {methodName}")
                };
                IL.Emit(OpCodes.Call, setMethod);
                IL.Emit(OpCodes.Box, typeof(double));
                return;

            // Conversion methods (no arguments, return string)
            case "toISOString":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateToISOString);
                return;
            case "toDateString":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateToDateString);
                return;
            case "toTimeString":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateToTimeString);
                return;

            // valueOf (no arguments, returns double)
            case "valueOf":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateValueOf);
                IL.Emit(OpCodes.Box, typeof(double));
                return;

            // toString (no arguments, returns string)
            case "toString":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateToString);
                return;
        }
    }

    /// <summary>
    /// Emits code for RegExp method calls (test, exec).
    /// </summary>
    private void EmitRegExpMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the RegExp object
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        switch (methodName)
        {
            case "test":
                // regex.test(str) -> bool
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpTest);
                IL.Emit(OpCodes.Box, typeof(bool));
                _stackType = StackType.Unknown;
                break;

            case "exec":
                // regex.exec(str) -> array|null
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpExec);
                _stackType = StackType.Unknown;
                break;
        }
    }

    private void EmitNew(Expr.New n)
    {
        // Special case: new Date(...) constructor
        if (n.ClassName.Lexeme == "Date")
        {
            EmitNewDate(n.Arguments);
            return;
        }

        // Special case: new Map(...) constructor
        if (n.ClassName.Lexeme == "Map")
        {
            EmitNewMap(n.Arguments);
            return;
        }

        // Special case: new Set(...) constructor
        if (n.ClassName.Lexeme == "Set")
        {
            EmitNewSet(n.Arguments);
            return;
        }

        // Special case: new RegExp(...) constructor
        if (n.ClassName.Lexeme == "RegExp")
        {
            EmitNewRegExp(n.Arguments);
            return;
        }

        if (_ctx.Classes.TryGetValue(n.ClassName.Lexeme, out var typeBuilder) &&
            _ctx.ClassConstructors != null &&
            _ctx.ClassConstructors.TryGetValue(n.ClassName.Lexeme, out var ctorBuilder))
        {
            Type targetType = typeBuilder;
            ConstructorInfo targetCtor = ctorBuilder;

            // Handle generic class instantiation (e.g., new Box<number>(42))
            if (n.TypeArgs != null && n.TypeArgs.Count > 0 &&
                _ctx.ClassGenericParams?.TryGetValue(n.ClassName.Lexeme, out var _) == true)
            {
                // Resolve type arguments
                Type[] typeArgs = n.TypeArgs.Select(ResolveTypeArg).ToArray();

                // Create the constructed generic type
                targetType = typeBuilder.MakeGenericType(typeArgs);

                // Get the constructor on the constructed type
                targetCtor = TypeBuilder.GetConstructor(targetType, ctorBuilder);
            }

            // Get expected parameter count from constructor definition
            int expectedParamCount = ctorBuilder.GetParameters().Length;

            // Emit arguments directly onto the stack (all typed as object)
            foreach (var arg in n.Arguments)
            {
                EmitExpression(arg);
                EmitBoxIfNeeded(arg);
            }

            // Pad missing optional arguments with null
            for (int i = n.Arguments.Count; i < expectedParamCount; i++)
            {
                IL.Emit(OpCodes.Ldnull);
            }

            // Call the constructor directly using newobj
            IL.Emit(OpCodes.Newobj, targetCtor);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }
    }

    private void EmitArrowFunction(Expr.ArrowFunction af)
    {
        // Get the method for this arrow function
        if (!_ctx.ArrowMethods.TryGetValue(af, out var method))
        {
            // Fallback if not found (shouldn't happen with proper collection)
            IL.Emit(OpCodes.Ldnull);
            return;
        }

        // Check if this is a capturing arrow (has display class)
        if (_ctx.DisplayClasses.TryGetValue(af, out var displayClass))
        {
            // Capturing arrow: create display class instance and populate fields
            EmitCapturingArrowFunction(af, method, displayClass);
        }
        else
        {
            // Non-capturing arrow: create TSFunction wrapping static method
            EmitNonCapturingArrowFunction(af, method);
        }
    }

    private void EmitNonCapturingArrowFunction(Expr.ArrowFunction af, MethodBuilder method)
    {
        // Create TSFunction for static method:
        // new TSFunction(null, method)

        // Push null (no target)
        IL.Emit(OpCodes.Ldnull);

        // Get MethodInfo from the method builder using reflection
        // We need to load the method as a MethodInfo at runtime
        // Use Type.GetMethod or RuntimeMethodHandle

        // Load the method as a runtime handle and convert to MethodInfo
        IL.Emit(OpCodes.Ldtoken, method);

        // For static methods on a non-generic type:
        IL.Emit(OpCodes.Call, typeof(System.Reflection.MethodBase).GetMethod(
            "GetMethodFromHandle",
            [typeof(RuntimeMethodHandle)])!);
        IL.Emit(OpCodes.Castclass, typeof(System.Reflection.MethodInfo));

        // Call $TSFunction constructor
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
    }

    private void EmitCapturingArrowFunction(Expr.ArrowFunction af, MethodBuilder method, TypeBuilder displayClass)
    {
        // Get the pre-tracked constructor (we can't call GetConstructors() on TypeBuilder before CreateType)
        if (!_ctx.DisplayClassConstructors.TryGetValue(af, out var displayCtor))
        {
            // Fallback
            IL.Emit(OpCodes.Ldnull);
            return;
        }

        IL.Emit(OpCodes.Newobj, displayCtor);

        // Get captured variables for this arrow using the stored field mapping
        if (!_ctx.DisplayClassFields.TryGetValue(af, out var fieldMap))
        {
            // No fields to populate, just create TSFunction
            IL.Emit(OpCodes.Ldtoken, method);
            IL.Emit(OpCodes.Call, typeof(System.Reflection.MethodBase).GetMethod(
                "GetMethodFromHandle",
                [typeof(RuntimeMethodHandle)])!);
            IL.Emit(OpCodes.Castclass, typeof(System.Reflection.MethodInfo));
            IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            return;
        }

        // Populate captured fields
        foreach (var (capturedVar, field) in fieldMap)
        {
            IL.Emit(OpCodes.Dup); // Keep display class on stack

            // Load the captured variable's current value
            if (capturedVar == "this")
            {
                // 'this' is captured - load the enclosing instance
                if (_ctx.IsInstanceMethod)
                {
                    IL.Emit(OpCodes.Ldarg_0);  // Load 'this' from enclosing method
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
            }
            else if (_ctx.TryGetParameter(capturedVar, out var argIndex))
            {
                IL.Emit(OpCodes.Ldarg, argIndex);
            }
            else if (_ctx.CapturedFields != null && _ctx.CapturedFields.TryGetValue(capturedVar, out var capturedField))
            {
                // Variable is captured from outer closure
                IL.Emit(OpCodes.Ldarg_0); // this (display class)
                IL.Emit(OpCodes.Ldfld, capturedField);
            }
            else
            {
                var local = _ctx.Locals.GetLocal(capturedVar);
                if (local != null)
                {
                    IL.Emit(OpCodes.Ldloc, local);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull); // Variable not found
                }
            }

            IL.Emit(OpCodes.Stfld, field);
        }

        // Create TSFunction: new TSFunction(displayInstance, method)
        // Stack has: displayInstance

        // Load method info
        IL.Emit(OpCodes.Ldtoken, method);
        IL.Emit(OpCodes.Call, typeof(System.Reflection.MethodBase).GetMethod(
            "GetMethodFromHandle",
            [typeof(RuntimeMethodHandle)])!);
        IL.Emit(OpCodes.Castclass, typeof(System.Reflection.MethodInfo));

        // Call $TSFunction constructor
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
    }

    private void EmitObjectStaticCall(string methodName, List<Expr> arguments)
    {
        // Object methods take exactly one argument
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        switch (methodName)
        {
            case "keys":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.GetKeys);
                break;
            case "values":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.GetValues);
                break;
            case "entries":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.GetEntries);
                break;
            default:
                IL.Emit(OpCodes.Pop);
                IL.Emit(OpCodes.Ldnull);
                break;
        }
    }

    private void EmitArrayStaticCall(string methodName, List<Expr> arguments)
    {
        switch (methodName)
        {
            case "isArray":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.IsArray);
                IL.Emit(OpCodes.Box, typeof(bool));
                break;
            default:
                IL.Emit(OpCodes.Ldnull);
                break;
        }
    }

    private void EmitJSONCall(string methodName, List<Expr> arguments)
    {
        switch (methodName)
        {
            case "parse":
                // Arg 0: text (required)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "null");
                }

                // Arg 1: reviver (optional)
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.JsonParseWithReviver);
                }
                else
                {
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.JsonParse);
                }
                break;

            case "stringify":
                // Arg 0: value (required)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }

                // Arg 1: replacer (optional), Arg 2: space (optional)
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);

                    if (arguments.Count > 2)
                    {
                        EmitExpression(arguments[2]);
                        EmitBoxIfNeeded(arguments[2]);
                    }
                    else
                    {
                        IL.Emit(OpCodes.Ldnull);
                    }
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.JsonStringifyFull);
                }
                else
                {
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.JsonStringify);
                }
                break;

            default:
                IL.Emit(OpCodes.Ldnull);
                break;
        }
    }

    private void EmitNumberStaticCall(string methodName, List<Expr> arguments)
    {
        switch (methodName)
        {
            case "parseInt":
                EmitGlobalParseInt(arguments);
                break;
            case "parseFloat":
                EmitGlobalParseFloat(arguments);
                break;
            case "isNaN":
                // Number.isNaN is stricter than global isNaN - only returns true for actual NaN
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberIsNaN);
                IL.Emit(OpCodes.Box, typeof(bool));
                break;
            case "isFinite":
                // Number.isFinite is stricter than global isFinite
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberIsFinite);
                IL.Emit(OpCodes.Box, typeof(bool));
                break;
            case "isInteger":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberIsInteger);
                IL.Emit(OpCodes.Box, typeof(bool));
                break;
            case "isSafeInteger":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberIsSafeInteger);
                IL.Emit(OpCodes.Box, typeof(bool));
                break;
            default:
                IL.Emit(OpCodes.Ldnull);
                break;
        }
    }

    private void EmitGlobalParseInt(List<Expr> arguments)
    {
        // Emit string argument
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        // Emit radix (default 10)
        if (arguments.Count > 1)
        {
            EmitExpression(arguments[1]);
            EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            IL.Emit(OpCodes.Ldc_I4, 10);
            IL.Emit(OpCodes.Box, typeof(int));
        }

        IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberParseInt);
        IL.Emit(OpCodes.Box, typeof(double));
    }

    private void EmitGlobalParseFloat(List<Expr> arguments)
    {
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberParseFloat);
        IL.Emit(OpCodes.Box, typeof(double));
    }

    private void EmitGlobalIsNaN(List<Expr> arguments)
    {
        // Global isNaN coerces to number first
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GlobalIsNaN);
        IL.Emit(OpCodes.Box, typeof(bool));
    }

    private void EmitGlobalIsFinite(List<Expr> arguments)
    {
        // Global isFinite coerces to number first
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GlobalIsFinite);
        IL.Emit(OpCodes.Box, typeof(bool));
    }

    /// <summary>
    /// Emits a call to an async function.
    /// The async method returns Task&lt;object?&gt; which we synchronously await.
    /// </summary>
    private void EmitAsyncFunctionCall(MethodInfo asyncMethod, List<Expr> arguments)
    {
        var paramCount = asyncMethod.GetParameters().Length;

        // Emit provided arguments
        foreach (var arg in arguments)
        {
            EmitExpression(arg);
            EmitBoxIfNeeded(arg);
        }

        // Pad with nulls for missing arguments (for default parameters)
        for (int i = arguments.Count; i < paramCount; i++)
        {
            IL.Emit(OpCodes.Ldnull);
        }

        // Call the async method (returns Task<object?> or Task)
        IL.Emit(OpCodes.Call, asyncMethod);

        // Synchronously wait for the result: task.GetAwaiter().GetResult()
        Type returnType = asyncMethod.ReturnType;

        if (returnType == typeof(Task))
        {
            // Task (no return value) - just wait for completion
            var getAwaiter = typeof(Task).GetMethod("GetAwaiter")!;
            var awaiterType = getAwaiter.ReturnType;
            var getResult = awaiterType.GetMethod("GetResult")!;

            // Store task in local to call methods on it
            var taskLocal = IL.DeclareLocal(typeof(Task));
            IL.Emit(OpCodes.Stloc, taskLocal);
            IL.Emit(OpCodes.Ldloca, taskLocal);
            IL.Emit(OpCodes.Call, getAwaiter);

            var awaiterLocal = IL.DeclareLocal(awaiterType);
            IL.Emit(OpCodes.Stloc, awaiterLocal);
            IL.Emit(OpCodes.Ldloca, awaiterLocal);
            IL.Emit(OpCodes.Call, getResult);

            // void async functions return null
            IL.Emit(OpCodes.Ldnull);
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            // Task<T> - wait and get result
            var getAwaiter = returnType.GetMethod("GetAwaiter")!;
            var awaiterType = getAwaiter.ReturnType;
            var getResult = awaiterType.GetMethod("GetResult")!;

            // Store task in local to call methods on it
            var taskLocal = IL.DeclareLocal(returnType);
            IL.Emit(OpCodes.Stloc, taskLocal);
            IL.Emit(OpCodes.Ldloca, taskLocal);
            IL.Emit(OpCodes.Call, getAwaiter);

            var awaiterLocal = IL.DeclareLocal(awaiterType);
            IL.Emit(OpCodes.Stloc, awaiterLocal);
            IL.Emit(OpCodes.Ldloca, awaiterLocal);
            IL.Emit(OpCodes.Call, getResult);

            // Box if necessary (result might be value type)
            Type resultType = returnType.GetGenericArguments()[0];
            if (resultType.IsValueType)
            {
                IL.Emit(OpCodes.Box, resultType);
            }
        }
        else
        {
            // Non-task return type (shouldn't happen for async methods)
            // Just leave the result on the stack
        }
    }

    private void EmitPromiseStaticCall(string methodName, List<Expr> arguments)
    {
        switch (methodName)
        {
            case "resolve":
                // Promise.resolve(value?)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.PromiseResolve);
                // Result is Task<object?>, need to await it
                EmitAwaitTask();
                return;

            case "reject":
                // Promise.reject(reason)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.PromiseReject);
                // Result is Task<object?>, need to await it
                EmitAwaitTask();
                return;

            case "all":
                // Promise.all(iterable)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.PromiseAll);
                // Result is Task<object?>, need to await it
                EmitAwaitTask();
                return;

            case "race":
                // Promise.race(iterable)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.PromiseRace);
                // Result is Task<object?>, need to await it
                EmitAwaitTask();
                return;

            case "allSettled":
                // Promise.allSettled(iterable)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.PromiseAllSettled);
                // Result is Task<object?>, need to await it
                EmitAwaitTask();
                return;

            case "any":
                // Promise.any(iterable)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.PromiseAny);
                // Result is Task<object?>, need to await it
                EmitAwaitTask();
                return;

            default:
                IL.Emit(OpCodes.Ldnull);
                return;
        }
    }

    /// <summary>
    /// Emits code to await a Task<object?> and get its result.
    /// </summary>
    private void EmitAwaitTask()
    {
        // Store task in local
        var taskLocal = IL.DeclareLocal(typeof(Task<object?>));
        IL.Emit(OpCodes.Stloc, taskLocal);
        IL.Emit(OpCodes.Ldloca, taskLocal);

        // Call GetAwaiter()
        var getAwaiter = typeof(Task<object?>).GetMethod("GetAwaiter")!;
        IL.Emit(OpCodes.Call, getAwaiter);

        // Store awaiter and call GetResult()
        var awaiterType = getAwaiter.ReturnType;
        var awaiterLocal = IL.DeclareLocal(awaiterType);
        IL.Emit(OpCodes.Stloc, awaiterLocal);
        IL.Emit(OpCodes.Ldloca, awaiterLocal);
        var getResult = awaiterType.GetMethod("GetResult")!;
        IL.Emit(OpCodes.Call, getResult);
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
        string? className = instance.ClassType switch
        {
            TypeSystem.TypeInfo.Class c => c.Name,
            _ => null
        };
        if (className == null)
            return false;

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
        _stackType = StackType.Unknown; // Method returns boxed object
        return true;
    }

    /// <summary>
    /// Emits code for new Date(...) construction with various argument forms.
    /// </summary>
    private void EmitNewDate(List<Expr> arguments)
    {
        switch (arguments.Count)
        {
            case 0:
                // new Date() - current date/time
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateDateNoArgs);
                break;

            case 1:
                // new Date(value) - milliseconds or ISO string
                EmitExpression(arguments[0]);
                EmitBoxIfNeeded(arguments[0]);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateDateFromValue);
                break;

            default:
                // new Date(year, month, day?, hours?, minutes?, seconds?, ms?)
                // Emit all 7 arguments, using 0 for missing ones
                for (int i = 0; i < 7; i++)
                {
                    if (i < arguments.Count)
                    {
                        EmitExpressionAsDouble(arguments[i]);
                    }
                    else
                    {
                        // Default values: day=1, others=0
                        IL.Emit(OpCodes.Ldc_R8, i == 2 ? 1.0 : 0.0);
                    }
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateDateFromComponents);
                break;
        }
        // All Date constructors return an object, reset stack type
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits code for new Map(...) construction.
    /// </summary>
    private void EmitNewMap(List<Expr> arguments)
    {
        if (arguments.Count == 0)
        {
            // new Map() - empty map
            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateMap);
        }
        else
        {
            // new Map(entries) - map from entries
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateMapFromEntries);
        }
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits code for new Set(...) construction.
    /// </summary>
    private void EmitNewSet(List<Expr> arguments)
    {
        if (arguments.Count == 0)
        {
            // new Set() - empty set
            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateSet);
        }
        else
        {
            // new Set(values) - set from array
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateSetFromArray);
        }
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits code for new RegExp(...) construction.
    /// </summary>
    private void EmitNewRegExp(List<Expr> arguments)
    {
        switch (arguments.Count)
        {
            case 0:
                // new RegExp() - empty pattern
                IL.Emit(OpCodes.Ldstr, "");
                IL.Emit(OpCodes.Ldstr, "");
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateRegExpWithFlags);
                break;

            case 1:
                // new RegExp(pattern) - pattern only
                EmitExpression(arguments[0]);
                EmitBoxIfNeeded(arguments[0]);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify); // Ensure pattern is a string
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateRegExp);
                break;

            default:
                // new RegExp(pattern, flags) - pattern and flags
                EmitExpression(arguments[0]);
                EmitBoxIfNeeded(arguments[0]);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify); // Ensure pattern is a string
                EmitExpression(arguments[1]);
                EmitBoxIfNeeded(arguments[1]);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify); // Ensure flags is a string
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateRegExpWithFlags);
                break;
        }
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits code for Map method calls.
    /// </summary>
    private void EmitMapMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the Map object
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        switch (methodName)
        {
            case "get":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapGet);
                return;

            case "set":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapSet);
                return;

            case "has":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapHas);
                IL.Emit(OpCodes.Box, typeof(bool));
                return;

            case "delete":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapDelete);
                IL.Emit(OpCodes.Box, typeof(bool));
                return;

            case "clear":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapClear);
                IL.Emit(OpCodes.Ldnull); // clear returns undefined
                return;

            case "keys":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapKeys);
                return;

            case "values":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapValues);
                return;

            case "entries":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapEntries);
                return;

            case "forEach":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapForEach);
                IL.Emit(OpCodes.Ldnull); // forEach returns undefined
                return;
        }
    }

    /// <summary>
    /// Emits code for Set method calls.
    /// </summary>
    private void EmitSetMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the Set object
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        switch (methodName)
        {
            case "add":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetAdd);
                return;

            case "has":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetHas);
                IL.Emit(OpCodes.Box, typeof(bool));
                return;

            case "delete":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetDelete);
                IL.Emit(OpCodes.Box, typeof(bool));
                return;

            case "clear":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetClear);
                IL.Emit(OpCodes.Ldnull); // clear returns undefined
                return;

            case "keys":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetKeys);
                return;

            case "values":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetValues);
                return;

            case "entries":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetEntries);
                return;

            case "forEach":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetForEach);
                IL.Emit(OpCodes.Ldnull); // forEach returns undefined
                return;
        }
    }
}
