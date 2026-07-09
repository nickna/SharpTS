using SharpTS.TypeSystem.Exceptions;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.TypeSystem;

/// <summary>
/// Function call type checking and overload resolution.
/// </summary>
/// <remarks>
/// Contains CheckCall and overload resolution helpers:
/// GetCallableFunction, TryMatchConstructorArgs, ResolveOverloadedCall,
/// TryMatchSignature, SelectMostSpecificOverload, CompareSpecificity, IsMoreSpecific.
/// </remarks>
public partial class TypeChecker
{
    private TypeInfo CheckCall(Expr.Call call)
    {
        if (TryCheckBuiltinCall(call, out var builtinCallResult))
            return builtinCallResult;

        // Invalidate property narrowings for method calls on objects
        // e.g., obj.mutate() should invalidate narrowings on obj's properties
        if (call.Callee is Expr.Get methodGet)
        {
            var receiverPath = GetNarrowingPath(methodGet.Object);
            if (receiverPath != null)
            {
                InvalidatePropertiesForFunctionArg(receiverPath);
            }
        }

        TypeInfo calleeType = CheckExpr(call.Callee);

        // Optional call: if callee could be nullish, strip null/undefined and check the rest.
        // The result type will be unioned with undefined at the end.
        if (call.Optional && calleeType is TypeInfo.Union optUnion)
        {
            var nonNullish = optUnion.FlattenedTypes
                .Where(t => t is not (TypeInfo.Null or TypeInfo.Undefined))
                .ToList();
            if (nonNullish.Count == 0)
            {
                // All members are nullish — result is always undefined
                foreach (var arg in call.Arguments) CheckExpr(arg);
                return new TypeInfo.Undefined();
            }
            calleeType = nonNullish.Count == 1 ? nonNullish[0] : new TypeInfo.Union(nonNullish);
        }

        // Non-optional call on a genuinely nullable callee: tsc rejects this outright (TS2721/2722/
        // 2723) rather than silently allowing it — unlike the union-of-function-members branch further
        // below (which permits reading a possibly-missing PROPERTY that happens to be a function),
        // actually INVOKING a possibly-null/undefined value is always an error.
        if (!call.Optional && calleeType is TypeInfo.Union nullableCallee
            && (nullableCallee.ContainsNull || nullableCallee.ContainsUndefined))
        {
            throw CannotInvokeNullishError(nullableCallee, call.Paren.Line);
        }

        if (calleeType is TypeInfo.Class classType)
        {
             return new TypeInfo.Instance(classType);
        }

        // Handle generic function calls
        if (calleeType is TypeInfo.GenericFunction genericFunc)
        {
            // Check each argument and collect their types
            List<TypeInfo> argTypes = [];
            foreach (var arg in call.Arguments)
            {
                if (arg is Expr.Spread spread)
                {
                    argTypes.Add(CheckExpr(spread.Expression));
                }
                else
                {
                    argTypes.Add(CheckExpr(arg));
                }
            }

            // Determine type arguments (explicit or inferred)
            List<TypeInfo> typeArgs;
            if (call.TypeArgs != null && call.TypeArgs.Count > 0)
            {
                // Explicit type arguments provided
                typeArgs = call.TypeArgs.Select((_, i) => ResolveTypeArg(call.TypeArgs, call.TypeArgNodes, i)).ToList();
            }
            else
            {
                // Infer type arguments from call arguments
                typeArgs = InferTypeArguments(genericFunc, argTypes);
            }

            // Instantiate the function with the type arguments
            var instantiatedFunc = InstantiateGenericFunction(genericFunc, typeArgs);
            if (instantiatedFunc is TypeInfo.Function instFunc)
            {
                // Excess-property check for fresh object-literal args against the instantiated
                // parameter types (the generic path otherwise skips argument validation).
                for (int ai = 0; ai < call.Arguments.Count && ai < instFunc.ParamTypes.Count; ai++)
                {
                    if (call.Arguments[ai] is Expr.ObjectLiteral && argTypes[ai] is TypeInfo.Record argRec)
                        CheckExcessProperties(argRec, instFunc.ParamTypes[ai], call.Arguments[ai]);
                }
                return instFunc.ReturnType;
            }
            return new TypeInfo.Any();
        }

        // Handle overloaded function calls
        if (calleeType is TypeInfo.OverloadedFunction overloadedFunc)
        {
            return ResolveOverloadedCall(call, overloadedFunc);
        }

        // Handle generic overloaded function calls
        if (calleeType is TypeInfo.GenericOverloadedFunction genericOverloadedFunc)
        {
            return ResolveGenericOverloadedCall(call, genericOverloadedFunc);
        }

        if (calleeType is TypeInfo.Function funcType)
        {
            // Count non-spread arguments and check for spreads
            bool hasSpread = call.Arguments.Any(a => a is Expr.Spread);
            int nonSpreadCount = call.Arguments.Count(a => a is not Expr.Spread);

            // If all declared params are `any`, treat as a loose JS function
            // and skip min-arity checks — JS calls are always variadic by
            // spec (missing args become `undefined`), and untyped CJS
            // functions shouldn't be held to stricter TS rules. Zero-parameter
            // untyped functions also count (the `.All` on an empty list is true
            // but the old `Count > 0` guard excluded them; lodash `function
            // shortOut() { return func.apply(undefined, arguments); }` falls here).
            bool allParamsAny = funcType.ParamTypes.Count == 0
                || funcType.ParamTypes.All(p => p is TypeInfo.Any);

            // Only check min arity if no spreads (spreads can expand to any count)
            if (!hasSpread && !allParamsAny && nonSpreadCount < funcType.MinArity)
            {
                throw new TypeCheckException($"Expected at least {funcType.MinArity} arguments but got {nonSpreadCount}.", tsCode: "TS2554");
            }

            // Check for too many arguments (when there's no rest parameter).
            // Skip when allParamsAny — JS never rejects extra args; they're reachable
            // through the `arguments` object, which any untyped function body may use
            // (lodash passes through `...arguments` in many places).
            if (!hasSpread && !funcType.HasRestParam && !allParamsAny
                && nonSpreadCount > funcType.ParamTypes.Count)
            {
                throw new TypeCheckException($"Expected {funcType.ParamTypes.Count} arguments but got {nonSpreadCount}.", tsCode: "TS2554");
            }

            // Get rest param element type if function has rest parameter
            TypeInfo? restElementType = null;
            if (funcType.HasRestParam && funcType.ParamTypes.Count > 0)
            {
                var lastParamType = funcType.ParamTypes[^1];
                if (lastParamType is TypeInfo.Array arrType)
                {
                    restElementType = arrType.ElementType;
                }
            }

            // Check types for provided arguments
            int argIndex = 0;
            int paramIndex = 0;
            int regularParamCount = funcType.HasRestParam ? funcType.ParamTypes.Count - 1 : funcType.ParamTypes.Count;

            foreach (var arg in call.Arguments)
            {
                if (arg is Expr.Spread spread)
                {
                    // Spread argument - check that it's an array
                    TypeInfo spreadType = CheckExpr(spread.Expression);
                    if (spreadType is TypeInfo.Array arrType)
                    {
                        // Check element type compatibility with rest param or remaining regular params
                        if (restElementType != null && !IsCompatible(restElementType, arrType.ElementType))
                        {
                            throw new TypeCheckException($"Spread element type '{arrType.ElementType}' not compatible with rest parameter type '{restElementType}'.", tsCode: "TS2345");
                        }
                    }
                    else if (spreadType is not TypeInfo.Any)
                    {
                        throw new TypeCheckException($"Spread argument must be an array.", tsCode: "TS2488");
                    }
                    // After spread, we can't reliably match params
                    break;
                }
                else
                {
                    TypeInfo expectedParamType = paramIndex < regularParamCount
                        ? funcType.ParamTypes[paramIndex]
                        : restElementType ?? new TypeInfo.Any();

                    // Apply contextual typing for array literals with tuple parameter types
                    if (expectedParamType is TypeInfo.Tuple tupleParamType && arg is Expr.ArrayLiteral argArrayLit)
                    {
                        CheckArrayLiteralAgainstTuple(argArrayLit, tupleParamType, $"argument {argIndex + 1}");
                    }
                    else
                    {
                        // Contextual typing: when an arrow is passed where a function
                        // type is expected, flow the expected signature into the arrow
                        // so its untyped parameters get the proper types (e.g.
                        // `arr.sort((a, b) => ...)` infers a, b as number).
                        TypeInfo argType;
                        if (arg is Expr.ArrowFunction arrowArg &&
                            (expectedParamType is TypeInfo.Function or TypeInfo.GenericFunction))
                        {
                            argType = CheckArrowFunction(arrowArg, expectedParamType);
                            _typeMap.Set(arg, argType);
                        }
                        else
                        {
                            argType = CheckExpr(arg);
                        }
                        // Excess-property check for a FRESH object-literal argument (tsc's fresh-literal
                        // rule) against its parameter type — mirrors the assignment path.
                        if (arg is Expr.ObjectLiteral && argType is TypeInfo.Record argExcessRecord
                            && paramIndex < regularParamCount)
                        {
                            CheckExcessProperties(argExcessRecord, funcType.ParamTypes[paramIndex], arg);
                        }
                        if (paramIndex < regularParamCount)
                        {
                            // Check against regular parameter. An optional/default-valued parameter
                            // (position >= MinArity) also accepts an explicit `undefined` (#668).
                            bool optional = paramIndex >= funcType.MinArity;
                            if (!IsArgumentCompatible(funcType.ParamTypes[paramIndex], argType, optional))
                            {
                                throw new TypeCheckException($"Argument {argIndex + 1} expected type '{funcType.ParamTypes[paramIndex]}' but got '{argType}'.", tsCode: "TS2345");
                            }
                        }
                        else if (restElementType != null)
                        {
                            // Check against rest parameter element type
                            if (!IsCompatible(restElementType, argType))
                            {
                                throw new TypeCheckException($"Argument {argIndex + 1} expected type '{restElementType}' but got '{argType}'.", tsCode: "TS2345");
                            }
                        }

                        // Invalidate property narrowings for object arguments
                        // If the argument is a variable/property path referencing an object,
                        // the function might mutate its properties
                        if (IsObjectType(argType))
                        {
                            var argPath = GetNarrowingPath(arg);
                            if (argPath != null)
                            {
                                InvalidatePropertiesForFunctionArg(argPath);
                                // Mark variable as escaped for inter-procedural analysis
                                // The function might store a reference to this object
                                if (argPath is Narrowing.NarrowingPath.Variable escapedVar)
                                {
                                    _escapeAnalyzer.MarkEscaped(escapedVar.Name);
                                }
                            }
                        }
                    }

                    if (paramIndex < regularParamCount) paramIndex++;
                    argIndex++;
                }
            }
            return funcType.ReturnType;
        }
        else if (calleeType is TypeInfo.Any)
        {
             foreach(var arg in call.Arguments) CheckExpr(arg);
             return new TypeInfo.Any();
        }

        // Handle interfaces with call signatures (callable interfaces)
        if (calleeType is TypeInfo.Interface itf && itf.IsCallable)
        {
            return CheckCallableInterfaceCall(itf, call.TypeArgs, call.Arguments, call.TypeArgNodes);
        }
        if (calleeType is TypeInfo.GenericInterface gi && gi.IsCallable)
        {
            return CheckGenericCallableInterfaceCall(gi, call.TypeArgs, call.Arguments, call.TypeArgNodes);
        }
        // Handle callable inline object types: `{ (x): T }`
        if (calleeType is TypeInfo.Record callableRec && callableRec.IsCallable)
        {
            return CheckCallSignaturesCall(callableRec.CallSignatures!, "object type", call.TypeArgs, call.Arguments, call.TypeArgNodes);
        }

        // Handle union types containing functions (e.g., from property access on union type)
        if (calleeType is TypeInfo.Union unionCallee)
        {
            var functionMembers = unionCallee.FlattenedTypes
                .Where(t => t is TypeInfo.Function or TypeInfo.OverloadedFunction or TypeInfo.GenericFunction)
                .ToList();

            if (functionMembers.Count > 0)
            {
                // Check arguments - they must be compatible with all function signatures
                foreach (var arg in call.Arguments)
                {
                    CheckExpr(arg);
                }

                // Collect return types from all function members
                List<TypeInfo> returnTypes = [];
                foreach (var funcMember in functionMembers)
                {
                    if (funcMember is TypeInfo.Function func)
                    {
                        returnTypes.Add(func.ReturnType);
                    }
                    else if (funcMember is TypeInfo.OverloadedFunction of)
                    {
                        returnTypes.Add(of.Implementation.ReturnType);
                    }
                    else if (funcMember is TypeInfo.GenericFunction gf)
                    {
                        returnTypes.Add(gf.ReturnType);
                    }
                }

                // No undefined/null member can survive to here: a non-optional call on a nullable
                // union already threw above, and an optional call already stripped them.
                var unique = returnTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
                return unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique);
            }
        }

        throw new TypeCheckException($"Can only call functions.", tsCode: "TS2349");
    }

    /// <summary>
    /// Builds the "cannot invoke" error for a non-optional call on a nullable union callee,
    /// picking the same code tsc would (TS2721 null-only, TS2722 undefined-only, TS2723 both) —
    /// unlike member access, calls have no separate bare-identifier code family.
    /// </summary>
    private static TypeCheckException CannotInvokeNullishError(TypeInfo.Union union, int line)
    {
        (string code, string subject) = (union.ContainsNull, union.ContainsUndefined) switch
        {
            (true, false) => ("TS2721", "'null'"),
            (false, true) => ("TS2722", "'undefined'"),
            _ => ("TS2723", "'null' or 'undefined'"),
        };
        return new TypeCheckException($"Cannot invoke an object which is possibly {subject}.", line, tsCode: code);
    }

    /// <summary>
    /// Checks a call on an interface with call signatures.
    /// Returns the return type of the matching call signature.
    /// </summary>
    /// <summary>
    /// Handles the special-cased builtin call forms that short-circuit ordinary
    /// callable resolution: bare-identifier globals (console.*, Symbol, BigInt, Date,
    /// Error family, parseInt/parseFloat/isNaN/isFinite/eval, timers, queueMicrotask,
    /// the __objectRest/__arrayDestructure desugaring helpers) and namespace statics
    /// (Object/Array/Map/JSON/Number/Date/Buffer/AbortSignal/Response/String/Iterator).
    /// Returns <c>true</c> with <paramref name="result"/> set when the call is one of
    /// these; <c>false</c> (leaving the general path to run) otherwise. Extracted from
    /// CheckCall (#1140); no behaviour change.
    /// </summary>
    private bool TryCheckBuiltinCall(Expr.Call call, out TypeInfo result)
    {
        // Handle all console.* methods
        if (call.Callee is Expr.Variable v && v.Name.Lexeme.StartsWith("console."))
        {
            var methodName = v.Name.Lexeme["console.".Length..];
            var methodType = BuiltInTypes.GetConsoleStaticMethodType(methodName);
            if (methodType is TypeInfo.Function)
            {
                foreach (var arg in call.Arguments) CheckExpr(arg);
                { result = new TypeInfo.Void(); return true; }
            }
        }

        // Handle Symbol() constructor - creates unique symbols
        if (call.Callee is Expr.Variable symVar && symVar.Name.Lexeme == "Symbol")
        {
            if (call.Arguments.Count > 1)
            {
                throw new TypeCheckException("Symbol() accepts at most one argument (description).", tsCode: "TS2554");
            }
            if (call.Arguments.Count == 1)
            {
                var argType = CheckExpr(call.Arguments[0]);
                if (!IsString(argType) && argType is not TypeInfo.Any)
                {
                    throw new TypeCheckException($"Symbol() description must be a string, got '{argType}'.", tsCode: "TS2345");
                }
            }
            { result = new TypeInfo.Symbol(); return true; }
        }

        // Handle Symbol.for(key) - returns a shared symbol from the global registry. `Symbol` bare
        // resolves to Any (LookupVariable), so without this the call fell through and stayed Any too
        // — silently bypassing every arithmetic/comparison operand check a real `symbol` would trip.
        if (call.Callee is Expr.Get { Object: Expr.Variable { Name.Lexeme: "Symbol" }, Name.Lexeme: "for" })
        {
            if (call.Arguments.Count != 1)
            {
                throw new TypeCheckException("Symbol.for() requires exactly one argument.", tsCode: "TS2554");
            }
            var argType = CheckExpr(call.Arguments[0]);
            if (!IsString(argType) && argType is not TypeInfo.Any)
            {
                throw new TypeCheckException($"Symbol.for() argument must be a string, got '{argType}'.", tsCode: "TS2345");
            }
            { result = new TypeInfo.Symbol(); return true; }
        }

        // Handle Symbol.keyFor(sym) - returns the key registered for a symbol, or undefined if none.
        if (call.Callee is Expr.Get { Object: Expr.Variable { Name.Lexeme: "Symbol" }, Name.Lexeme: "keyFor" })
        {
            if (call.Arguments.Count != 1)
            {
                throw new TypeCheckException("Symbol.keyFor() requires exactly one argument.", tsCode: "TS2554");
            }
            CheckExpr(call.Arguments[0]);
            { result = new TypeInfo.Union([new TypeInfo.String(), new TypeInfo.Undefined()]); return true; }
        }

        // Handle BigInt() constructor - converts number or string to bigint
        if (call.Callee is Expr.Variable bigIntVar && bigIntVar.Name.Lexeme == "BigInt")
        {
            if (call.Arguments.Count != 1)
            {
                throw new TypeCheckException("BigInt() requires exactly one argument.", tsCode: "TS2554");
            }
            var argType = CheckExpr(call.Arguments[0]);
            // BigInt's parameter type is `string | number | bigint | boolean` (a boolean coerces to 0n/1n).
            bool isBoolean = argType is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } or TypeInfo.BooleanLiteral;
            if (!IsNumber(argType) && !IsString(argType) && !IsBigInt(argType) && !isBoolean && argType is not TypeInfo.Any)
            {
                throw new TypeCheckException($"BigInt() argument must be a number, string, bigint, or boolean, got '{argType}'.", tsCode: "TS2345");
            }
            { result = new TypeInfo.BigInt(); return true; }
        }

        // Handle Date() function call - returns current date as string (without 'new')
        if (call.Callee is Expr.Variable dateVar && dateVar.Name.Lexeme == "Date")
        {
            // Date() called as a function (not with new) ignores arguments and returns a string
            foreach (var arg in call.Arguments) CheckExpr(arg);
            { result = new TypeInfo.String(); return true; }
        }

        // Handle Error() and error subtypes called without 'new' - still creates error objects
        if (call.Callee is Expr.Variable errorVar && BuiltInNames.IsErrorTypeName(errorVar.Name.Lexeme))
        {
            // Error constructors accept 0-2 arguments (message, options)
            // AggregateError accepts 0-3 arguments (errors array, message, options)
            int maxArgs = errorVar.Name.Lexeme == "AggregateError" ? 3 : 2;
            if (call.Arguments.Count > maxArgs)
            {
                throw new TypeCheckException($"{errorVar.Name.Lexeme}() accepts at most {maxArgs} argument(s).", tsCode: "TS2554");
            }

            // Validate argument types
            if (call.Arguments.Count >= 1)
            {
                var firstArgType = CheckExpr(call.Arguments[0]);
                if (errorVar.Name.Lexeme == "AggregateError")
                {
                    // First argument should be an array of errors
                    if (firstArgType is not TypeInfo.Array && firstArgType is not TypeInfo.Any)
                    {
                        throw new TypeCheckException($"AggregateError first argument must be an array, got '{firstArgType}'.", tsCode: "TS2345");
                    }
                }
                else
                {
                    // For other error types, first argument should be a string message
                    if (!IsString(firstArgType) && firstArgType is not TypeInfo.Any)
                    {
                        throw new TypeCheckException($"{errorVar.Name.Lexeme}() message must be a string, got '{firstArgType}'.", tsCode: "TS2345");
                    }
                }
            }

            // Validate remaining arguments
            for (int i = 1; i < call.Arguments.Count; i++)
            {
                var argType = CheckExpr(call.Arguments[i]);
                if (errorVar.Name.Lexeme == "AggregateError" && i == 1)
                {
                    // AggregateError second arg is message (string)
                    if (!IsString(argType) && argType is not TypeInfo.Any)
                    {
                        throw new TypeCheckException($"AggregateError message must be a string, got '{argType}'.", tsCode: "TS2345");
                    }
                }
                // Options argument (last arg) is an object - accept any type
            }

            { result = new TypeInfo.Error(errorVar.Name.Lexeme); return true; }
        }

        // Handle Object.keys(), Object.values(), Object.entries()
        if (call.Callee is Expr.Get get &&
            get.Object is Expr.Variable objVar &&
            objVar.Name.Lexeme == "Object")
        {
            var methodType = BuiltInTypes.GetObjectStaticMethodType(get.Name.Lexeme);
            if (methodType is TypeInfo.Function objMethodType)
            {
                foreach (var arg in call.Arguments) CheckExpr(arg);
                { result = objMethodType.ReturnType; return true; }
            }
        }

        // Handle Array.isArray()
        if (call.Callee is Expr.Get arrGet &&
            arrGet.Object is Expr.Variable arrVar &&
            arrVar.Name.Lexeme == "Array")
        {
            var methodType = BuiltInTypes.GetArrayStaticMethodType(arrGet.Name.Lexeme);
            if (methodType is TypeInfo.Function arrMethodType)
            {
                foreach (var arg in call.Arguments) CheckExpr(arg);
                { result = arrMethodType.ReturnType; return true; }
            }
        }

        // Handle Map.groupBy()
        if (call.Callee is Expr.Get mapGet &&
            mapGet.Object is Expr.Variable mapVar &&
            mapVar.Name.Lexeme == "Map")
        {
            var methodType = BuiltInTypes.GetMapStaticMethodType(mapGet.Name.Lexeme);
            if (methodType is TypeInfo.Function mapMethodType)
            {
                foreach (var arg in call.Arguments) CheckExpr(arg);
                { result = mapMethodType.ReturnType; return true; }
            }
        }

        // Handle JSON.parse(), JSON.stringify()
        if (call.Callee is Expr.Get jsonGet &&
            jsonGet.Object is Expr.Variable jsonVar &&
            jsonVar.Name.Lexeme == "JSON")
        {
            var methodType = BuiltInTypes.GetJSONStaticMethodType(jsonGet.Name.Lexeme);
            if (methodType is TypeInfo.Function jsonMethodType)
            {
                foreach (var arg in call.Arguments) CheckExpr(arg);
                { result = jsonMethodType.ReturnType; return true; }
            }
        }

        // Handle Number.parseInt(), Number.parseFloat(), Number.isNaN(), etc.
        if (call.Callee is Expr.Get numGet &&
            numGet.Object is Expr.Variable numVar &&
            numVar.Name.Lexeme == "Number")
        {
            var methodType = BuiltInTypes.GetNumberStaticMemberType(numGet.Name.Lexeme);
            if (methodType is TypeInfo.Function numMethodType)
            {
                foreach (var arg in call.Arguments) CheckExpr(arg);
                { result = numMethodType.ReturnType; return true; }
            }
        }

        // Handle Date.now()
        if (call.Callee is Expr.Get dateGet &&
            dateGet.Object is Expr.Variable dateStaticVar &&
            dateStaticVar.Name.Lexeme == "Date")
        {
            var methodType = BuiltInTypes.GetDateStaticMemberType(dateGet.Name.Lexeme);
            if (methodType is TypeInfo.Function dateMethodType)
            {
                foreach (var arg in call.Arguments) CheckExpr(arg);
                { result = dateMethodType.ReturnType; return true; }
            }
        }

        // Handle Buffer.from(), Buffer.alloc(), Buffer.isBuffer(), etc.
        if (call.Callee is Expr.Get bufferGet &&
            bufferGet.Object is Expr.Variable bufferVar &&
            bufferVar.Name.Lexeme == "Buffer")
        {
            var methodType = BuiltInTypes.GetBufferStaticMethodType(bufferGet.Name.Lexeme);
            if (methodType is TypeInfo.Function bufferMethodType)
            {
                foreach (var arg in call.Arguments) CheckExpr(arg);
                { result = bufferMethodType.ReturnType; return true; }
            }
        }

        // Handle AbortSignal.abort(), AbortSignal.timeout(), AbortSignal.any()
        if (call.Callee is Expr.Get abortSignalGet &&
            abortSignalGet.Object is Expr.Variable abortSignalVar &&
            abortSignalVar.Name.Lexeme == "AbortSignal")
        {
            var methodType = BuiltInTypes.GetAbortSignalStaticMethodType(abortSignalGet.Name.Lexeme);
            if (methodType is TypeInfo.Function abortSignalMethodType)
            {
                foreach (var arg in call.Arguments) CheckExpr(arg);
                { result = abortSignalMethodType.ReturnType; return true; }
            }
        }

        // Handle Response.json(), Response.redirect(), Response.error()
        if (call.Callee is Expr.Get responseGet &&
            responseGet.Object is Expr.Variable responseVar &&
            responseVar.Name.Lexeme == "Response" &&
            responseGet.Name.Lexeme is "json" or "redirect" or "error")
        {
            foreach (var arg in call.Arguments) CheckExpr(arg);
            { result = new TypeInfo.Any(); return true; }
        }

        // Handle String.fromCharCode(), String.raw()
        if (call.Callee is Expr.Get stringGet &&
            stringGet.Object is Expr.Variable stringVar &&
            stringVar.Name.Lexeme == "String")
        {
            var methodType = BuiltInTypes.GetStringStaticMethodType(stringGet.Name.Lexeme);
            if (methodType is TypeInfo.Function stringMethodType)
            {
                foreach (var arg in call.Arguments) CheckExpr(arg);
                { result = stringMethodType.ReturnType; return true; }
            }
        }

        // Handle Iterator.from()
        if (call.Callee is Expr.Get iterGet &&
            iterGet.Object is Expr.Variable iterVar &&
            iterVar.Name.Lexeme == "Iterator")
        {
            var methodType = BuiltInTypes.GetIteratorStaticMethodType(iterGet.Name.Lexeme);
            if (methodType is TypeInfo.Function iterMethodType)
            {
                foreach (var arg in call.Arguments) CheckExpr(arg);
                { result = iterMethodType.ReturnType; return true; }
            }
        }

        // Handle global parseInt()
        if (call.Callee is Expr.Variable parseIntVar && parseIntVar.Name.Lexeme == "parseInt")
        {
            foreach (var arg in call.Arguments) CheckExpr(arg);
            { result = new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER); return true; }
        }

        // Handle global parseFloat()
        if (call.Callee is Expr.Variable parseFloatVar && parseFloatVar.Name.Lexeme == "parseFloat")
        {
            foreach (var arg in call.Arguments) CheckExpr(arg);
            { result = new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER); return true; }
        }

        // Handle global isNaN()
        if (call.Callee is Expr.Variable isNaNVar && isNaNVar.Name.Lexeme == "isNaN")
        {
            foreach (var arg in call.Arguments) CheckExpr(arg);
            { result = new TypeInfo.Primitive(Parsing.TokenType.TYPE_BOOLEAN); return true; }
        }

        // Handle global isFinite()
        if (call.Callee is Expr.Variable isFiniteVar && isFiniteVar.Name.Lexeme == "isFinite")
        {
            foreach (var arg in call.Arguments) CheckExpr(arg);
            { result = new TypeInfo.Primitive(Parsing.TokenType.TYPE_BOOLEAN); return true; }
        }

        // Handle global eval() — typed as (s: string) => any. The argument string is
        // not statically analyzed (matching tsc's `eval` lib typing), so the result is Any.
        if (call.Callee is Expr.Variable evalVar && evalVar.Name.Lexeme == "eval")
        {
            foreach (var arg in call.Arguments) CheckExpr(arg);
            { result = new TypeInfo.Any(); return true; }
        }

        // Timer functions (setTimeout / clearTimeout / setInterval / clearInterval)
        // have two resolutions: the JS globals (untyped, `_environment.Get` returns
        // null — handled here) and imports from stdlib/node/timers{,/promises}.ts
        // (which return a proper Function with a concrete signature — generic
        // function-call validation handles them, so this block is skipped).
        if (call.Callee is Expr.Variable setTimeoutVar && setTimeoutVar.Name.Lexeme == "setTimeout"
            && _environment.Get(setTimeoutVar.Name.Lexeme) is null or TypeInfo.Any)
        {
            if (call.Arguments.Count < 1)
                throw new TypeCheckException("setTimeout() requires at least one argument (callback).", tsCode: "TS2554");

            var callbackType = CheckExpr(call.Arguments[0]);
            if (callbackType is not TypeInfo.Function && callbackType is not TypeInfo.Any)
                throw new TypeCheckException($"setTimeout() callback must be a function, got '{callbackType}'.", tsCode: "TS2345");

            if (call.Arguments.Count >= 2)
            {
                var delayType = CheckExpr(call.Arguments[1]);
                if (!IsNumber(delayType) && delayType is not TypeInfo.Any && delayType is not TypeInfo.Undefined)
                    throw new TypeCheckException($"setTimeout() delay must be a number, got '{delayType}'.", tsCode: "TS2345");
            }

            for (int i = 2; i < call.Arguments.Count; i++) CheckExpr(call.Arguments[i]);
            { result = new TypeInfo.Timeout(); return true; }
        }

        if (call.Callee is Expr.Variable clearTimeoutVar && clearTimeoutVar.Name.Lexeme == "clearTimeout"
            && _environment.Get(clearTimeoutVar.Name.Lexeme) is null or TypeInfo.Any)
            { result = CheckClearTimerCall(call, "clearTimeout"); return true; }

        if (call.Callee is Expr.Variable setIntervalVar && setIntervalVar.Name.Lexeme == "setInterval"
            && _environment.Get(setIntervalVar.Name.Lexeme) is null or TypeInfo.Any)
        {
            if (call.Arguments.Count < 1)
                throw new TypeCheckException("setInterval() requires at least one argument (callback).", tsCode: "TS2554");

            var callbackType = CheckExpr(call.Arguments[0]);
            if (callbackType is not TypeInfo.Function && callbackType is not TypeInfo.Any)
                throw new TypeCheckException($"setInterval() callback must be a function, got '{callbackType}'.", tsCode: "TS2345");

            if (call.Arguments.Count >= 2)
            {
                var delayType = CheckExpr(call.Arguments[1]);
                if (!IsNumber(delayType) && delayType is not TypeInfo.Any && delayType is not TypeInfo.Undefined)
                    throw new TypeCheckException($"setInterval() delay must be a number, got '{delayType}'.", tsCode: "TS2345");
            }

            for (int i = 2; i < call.Arguments.Count; i++) CheckExpr(call.Arguments[i]);
            { result = new TypeInfo.Timeout(); return true; }
        }

        if (call.Callee is Expr.Variable clearIntervalVar && clearIntervalVar.Name.Lexeme == "clearInterval"
            && _environment.Get(clearIntervalVar.Name.Lexeme) is null or TypeInfo.Any)
            { result = CheckClearTimerCall(call, "clearInterval"); return true; }

        // Handle queueMicrotask(callback)
        if (call.Callee is Expr.Variable queueMicrotaskVar && queueMicrotaskVar.Name.Lexeme == "queueMicrotask")
        {
            if (call.Arguments.Count != 1)
            {
                throw new TypeCheckException("queueMicrotask() requires exactly one argument (callback).", tsCode: "TS2554");
            }

            // Argument must be a function
            var callbackType = CheckExpr(call.Arguments[0]);
            if (callbackType is not TypeInfo.Function && callbackType is not TypeInfo.Any)
            {
                throw new TypeCheckException($"queueMicrotask() callback must be a function, got '{callbackType}'.", tsCode: "TS2345");
            }

            { result = new TypeInfo.Void(); return true; } // queueMicrotask returns undefined
        }

        // Handle __objectRest (internal helper for object rest patterns)
        if (call.Callee is Expr.Variable restVar && restVar.Name.Lexeme == "__objectRest")
        {
            foreach (var arg in call.Arguments) CheckExpr(arg);
            { result = new TypeInfo.Any(); return true; } // Returns an object with remaining properties
        }

        // Handle __arrayDestructure (internal helper for array binding patterns, #685).
        // Normalizes the destructuring source so the desugared positional index
        // access types correctly for non-indexable iterables.
        if (call.Callee is Expr.Variable arrDestVar && arrDestVar.Name.Lexeme == BuiltInNames.ArrayDestructure)
        {
            var sourceType = call.Arguments.Count == 1 ? CheckExpr(call.Arguments[0]) : new TypeInfo.Any();
            { result = NormalizeArrayDestructureSourceType(sourceType); return true; }
        }
        result = null!;
        return false;
    }

    private TypeInfo CheckCallableInterfaceCall(
        TypeInfo.Interface itf,
        List<string>? typeArgs,
        List<Expr> arguments,
        List<TypeNode?>? typeArgNodes = null)
    {
        if (itf.CallSignatures == null || itf.CallSignatures.Count == 0)
        {
            throw new TypeCheckException($"Interface '{itf.Name}' is not callable.", tsCode: "TS2349");
        }

        return CheckCallSignaturesCall(itf.CallSignatures, $"interface '{itf.Name}'", typeArgs, arguments, typeArgNodes);
    }

    /// <summary>
    /// Resolves a call against a set of call signatures (shared by callable interfaces and callable
    /// inline object types). Returns the matching signature's return type.
    /// </summary>
    private TypeInfo CheckCallSignaturesCall(
        List<TypeInfo.CallSignature> callSignatures,
        string calleeDescription,
        List<string>? typeArgs,
        List<Expr> arguments,
        List<TypeNode?>? typeArgNodes = null)
    {
        List<TypeInfo> argTypes = arguments.Select(CheckExpr).ToList();

        // Try each call signature
        foreach (var callSig in callSignatures)
        {
            if (callSig.IsGeneric)
            {
                // Generic call signature - try to instantiate
                var result = TryMatchGenericCallSignature(callSig, typeArgs, argTypes, typeArgNodes);
                if (result != null)
                    return result;
            }
            else
            {
                // Non-generic - direct matching
                if (TryMatchSignature(new TypeInfo.Function(callSig.ParamTypes, callSig.ReturnType, callSig.MinArity, callSig.HasRestParam), argTypes))
                {
                    return callSig.ReturnType;
                }
            }
        }

        throw new TypeCheckException($"No call signature matches the call for {calleeDescription}.", tsCode: "TS2769");
    }

    /// <summary>
    /// Checks a call on a generic interface with call signatures.
    /// </summary>
    private TypeInfo CheckGenericCallableInterfaceCall(
        TypeInfo.GenericInterface gi,
        List<string>? typeArgs,
        List<Expr> arguments,
        List<TypeNode?>? typeArgNodes = null)
    {
        if (gi.CallSignatures == null || gi.CallSignatures.Count == 0)
        {
            throw new TypeCheckException($"Generic interface '{gi.Name}' is not callable.", tsCode: "TS2349");
        }

        // If type args provided, substitute and check
        if (typeArgs != null && typeArgs.Count > 0)
        {
            var instantiatedTypeArgs = typeArgs.Select((_, i) => ResolveTypeArg(typeArgs, typeArgNodes, i)).ToList();
            Dictionary<string, TypeInfo> subs = [];
            for (int i = 0; i < gi.TypeParams.Count && i < instantiatedTypeArgs.Count; i++)
            {
                subs[gi.TypeParams[i].Name] = instantiatedTypeArgs[i];
            }

            List<TypeInfo> argTypes = arguments.Select(CheckExpr).ToList();
            foreach (var callSig in gi.CallSignatures)
            {
                var substitutedParamTypes = callSig.ParamTypes.Select(p => Substitute(p, subs)).ToList();
                if (TryMatchSignature(new TypeInfo.Function(substitutedParamTypes, Substitute(callSig.ReturnType, subs), callSig.MinArity, callSig.HasRestParam), argTypes))
                {
                    return Substitute(callSig.ReturnType, subs);
                }
            }
        }

        throw new TypeCheckException($"No call signature matches the call for generic interface '{gi.Name}'.", tsCode: "TS2769");
    }

    /// <summary>
    /// Tries to match a generic call signature by inferring type arguments.
    /// </summary>
    private TypeInfo? TryMatchGenericCallSignature(
        TypeInfo.CallSignature callSig,
        List<string>? explicitTypeArgs,
        List<TypeInfo> argTypes,
        List<TypeNode?>? explicitTypeArgNodes = null)
    {
        if (callSig.TypeParams == null || callSig.TypeParams.Count == 0)
            return null;

        Dictionary<string, TypeInfo> inferred = [];

        if (explicitTypeArgs != null && explicitTypeArgs.Count > 0)
        {
            // Use explicit type arguments
            for (int i = 0; i < callSig.TypeParams.Count && i < explicitTypeArgs.Count; i++)
            {
                inferred[callSig.TypeParams[i].Name] = ResolveTypeArg(explicitTypeArgs, explicitTypeArgNodes, i);
            }
        }
        else
        {
            // Try to infer from argument types
            for (int i = 0; i < callSig.ParamTypes.Count && i < argTypes.Count; i++)
            {
                InferFromType(callSig.ParamTypes[i], argTypes[i], inferred);
            }
        }

        // Check if all type parameters were inferred
        foreach (var tp in callSig.TypeParams)
        {
            if (!inferred.ContainsKey(tp.Name))
            {
                if (tp.Default != null)
                    inferred[tp.Name] = tp.Default;
                else
                    return null; // Cannot infer
            }
        }

        // Substitute and check argument compatibility
        var substitutedParamTypes = callSig.ParamTypes.Select(p => Substitute(p, inferred)).ToList();
        if (!TryMatchSignature(new TypeInfo.Function(substitutedParamTypes, Substitute(callSig.ReturnType, inferred), callSig.MinArity, callSig.HasRestParam), argTypes))
            return null;

        return Substitute(callSig.ReturnType, inferred);
    }

    /// <summary>
    /// Extracts the callable function type from a TypeInfo that could be Function or OverloadedFunction.
    /// For OverloadedFunction, returns the implementation's type.
    /// </summary>
    private TypeInfo.Function? GetCallableFunction(TypeInfo? methodType)
    {
        return methodType switch
        {
            TypeInfo.Function f => f,
            TypeInfo.OverloadedFunction of => of.Implementation,
            _ => null
        };
    }

    /// <summary>
    /// Checks if constructor arguments match a constructor signature.
    /// </summary>
    private bool TryMatchConstructorArgs(List<TypeInfo> argTypes, List<TypeInfo> paramTypes, int minArity, bool hasRestParam)
    {
        if (argTypes.Count < minArity)
            return false;
        if (!hasRestParam && argTypes.Count > paramTypes.Count)
            return false;

        int regularParamCount = hasRestParam ? paramTypes.Count - 1 : paramTypes.Count;

        for (int i = 0; i < argTypes.Count && i < regularParamCount; i++)
        {
            if (!IsCompatible(paramTypes[i], argTypes[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Resolve an overloaded function call by finding the best matching signature.
    /// </summary>
    /// <summary>
    /// True when every argument SUBTYPE-matches its parameter — the stricter first-pass relation
    /// of tsc's two-pass overload resolution: identical to assignability except that an
    /// <c>any</c> argument only subtype-matches an <c>any</c>/<c>unknown</c> parameter.
    /// </summary>
    private static bool ArgsSubtypeMatch(TypeInfo.Function signature, List<TypeInfo> argTypes)
    {
        for (int i = 0; i < argTypes.Count && i < signature.ParamTypes.Count; i++)
        {
            if (argTypes[i] is TypeInfo.Any &&
                signature.ParamTypes[i] is not (TypeInfo.Any or TypeInfo.Unknown))
            {
                return false;
            }
        }
        return true;
    }

    private TypeInfo ResolveOverloadedCall(Expr.Call call, TypeInfo.OverloadedFunction overloadedFunc)
    {
        // Collect argument types
        List<TypeInfo> argTypes = [];
        foreach (var arg in call.Arguments)
        {
            if (arg is Expr.Spread spread)
            {
                argTypes.Add(CheckExpr(spread.Expression));
            }
            else
            {
                argTypes.Add(CheckExpr(arg));
            }
        }

        // Find matching signatures
        List<TypeInfo.Function> matchingSignatures = [];

        foreach (var signature in overloadedFunc.Signatures)
        {
            if (TryMatchSignature(signature, argTypes))
            {
                matchingSignatures.Add(signature);
            }
        }

        if (matchingSignatures.Count == 0)
        {
            string argTypesStr = string.Join(", ", argTypes);
            throw new TypeCheckException($"No overload matches call with arguments ({argTypesStr}).", tsCode: "TS2769");
        }

        // tsc resolves overloads in two passes: SUBTYPE matching first, then assignability. The
        // practical difference here: an `any` argument is assignable to every parameter but is a
        // subtype only of any/unknown — so `foo(a)` with `a: any` picks a later `(x: any)`
        // overload over an earlier `(x: number)` one.
        var subtypeMatches = matchingSignatures.Where(sig => ArgsSubtypeMatch(sig, argTypes)).ToList();
        if (subtypeMatches.Count > 0)
        {
            matchingSignatures = subtypeMatches;
        }

        // tsc takes the FIRST matching overload in declaration order (that's why overloads are
        // conventionally ordered most-specific first) — `foo16(e)` against `(x: Object)` then
        // `(x: E)` resolves to Object, not the more specific E.
        TypeInfo.Function bestMatch = matchingSignatures[0];

        return bestMatch.ReturnType;
    }

    /// <summary>
    /// Resolve a generic overloaded function call by inferring type arguments and finding the best matching signature.
    /// </summary>
    private TypeInfo ResolveGenericOverloadedCall(Expr.Call call, TypeInfo.GenericOverloadedFunction genericOverloadedFunc)
    {
        // Collect argument types
        List<TypeInfo> argTypes = [];
        foreach (var arg in call.Arguments)
        {
            if (arg is Expr.Spread spread)
            {
                argTypes.Add(CheckExpr(spread.Expression));
            }
            else
            {
                argTypes.Add(CheckExpr(arg));
            }
        }

        // Determine type arguments (explicit or inferred)
        List<TypeInfo> typeArgs;
        if (call.TypeArgs != null && call.TypeArgs.Count > 0)
        {
            // Explicit type arguments provided
            typeArgs = call.TypeArgs.Select((_, i) => ResolveTypeArg(call.TypeArgs, call.TypeArgNodes, i)).ToList();
        }
        else
        {
            // Infer type arguments from call arguments
            // Create a temporary GenericFunction to use the existing inference logic
            // We use the implementation signature as a base for inference
            var tempGenericFunc = new TypeInfo.GenericFunction(
                genericOverloadedFunc.TypeParams,
                genericOverloadedFunc.Implementation.ParamTypes,
                genericOverloadedFunc.Implementation.ReturnType,
                genericOverloadedFunc.Implementation.RequiredParams,
                genericOverloadedFunc.Implementation.HasRestParam,
                genericOverloadedFunc.Implementation.ThisType);
            typeArgs = InferTypeArguments(tempGenericFunc, argTypes);
        }

        // Create substitution map
        Dictionary<string, TypeInfo> substitutions = [];
        for (int i = 0; i < typeArgs.Count && i < genericOverloadedFunc.TypeParams.Count; i++)
        {
            substitutions[genericOverloadedFunc.TypeParams[i].Name] = typeArgs[i];
        }

        // Instantiate each signature with the inferred type arguments and find matches
        List<TypeInfo.Function> matchingSignatures = [];

        foreach (var signature in genericOverloadedFunc.Signatures)
        {
            // Substitute type parameters in the signature
            var instantiatedParams = signature.ParamTypes.Select(p => Substitute(p, substitutions)).ToList();
            var instantiatedReturn = Substitute(signature.ReturnType, substitutions);
            var instantiatedSig = new TypeInfo.Function(
                instantiatedParams,
                instantiatedReturn,
                signature.RequiredParams,
                signature.HasRestParam,
                signature.ThisType);

            if (TryMatchSignature(instantiatedSig, argTypes))
            {
                matchingSignatures.Add(instantiatedSig);
            }
        }

        if (matchingSignatures.Count == 0)
        {
            string argTypesStr = string.Join(", ", argTypes);
            throw new TypeCheckException($"No overload matches call with arguments ({argTypesStr}).", tsCode: "TS2769");
        }

        // tsc resolves overloads in two passes: SUBTYPE matching first, then assignability. The
        // practical difference here: an `any` argument is assignable to every parameter but is a
        // subtype only of any/unknown — so `foo(a)` with `a: any` picks a later `(x: any)`
        // overload over an earlier `(x: number)` one.
        var subtypeMatches = matchingSignatures.Where(sig => ArgsSubtypeMatch(sig, argTypes)).ToList();
        if (subtypeMatches.Count > 0)
        {
            matchingSignatures = subtypeMatches;
        }

        // tsc takes the FIRST matching overload in declaration order (that's why overloads are
        // conventionally ordered most-specific first) — `foo16(e)` against `(x: Object)` then
        // `(x: E)` resolves to Object, not the more specific E.
        TypeInfo.Function bestMatch = matchingSignatures[0];

        return bestMatch.ReturnType;
    }

    /// <summary>
    /// Check if a signature matches the given argument types.
    /// </summary>
    private bool TryMatchSignature(TypeInfo.Function signature, List<TypeInfo> argTypes)
    {
        // Check argument count
        if (argTypes.Count < signature.MinArity)
            return false;

        if (!signature.HasRestParam && argTypes.Count > signature.ParamTypes.Count)
            return false;

        // Check each argument type
        int regularParamCount = signature.HasRestParam ? signature.ParamTypes.Count - 1 : signature.ParamTypes.Count;

        for (int i = 0; i < argTypes.Count; i++)
        {
            TypeInfo expectedType;
            if (i < regularParamCount)
            {
                expectedType = signature.ParamTypes[i];
            }
            else if (signature.HasRestParam && signature.ParamTypes.Count > 0)
            {
                // Rest parameter - check against element type
                var restType = signature.ParamTypes[^1];
                if (restType is TypeInfo.Array arrType)
                {
                    expectedType = arrType.ElementType;
                }
                else
                {
                    expectedType = new TypeInfo.Any();
                }
            }
            else
            {
                break; // No more parameters to check
            }

            if (!IsCompatible(expectedType, argTypes[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Select the most specific signature from a list of matching signatures.
    /// Uses "most specific match" rules: prefer more specific types over general ones.
    /// </summary>
    private TypeInfo.Function SelectMostSpecificOverload(List<TypeInfo.Function> candidates, List<TypeInfo> argTypes)
    {
        if (candidates.Count == 1)
            return candidates[0];

        TypeInfo.Function mostSpecific = candidates[0];

        for (int i = 1; i < candidates.Count; i++)
        {
            int comparison = CompareSpecificity(mostSpecific, candidates[i], argTypes);
            if (comparison < 0)
            {
                // candidates[i] is more specific
                mostSpecific = candidates[i];
            }
            // If comparison == 0 (equally specific), keep the first one (declaration order)
        }

        return mostSpecific;
    }

    /// <summary>
    /// Compare two signatures for specificity.
    /// Returns: &gt;0 if sig1 is more specific, &lt;0 if sig2 is more specific, 0 if equally specific.
    /// </summary>
    private int CompareSpecificity(TypeInfo.Function sig1, TypeInfo.Function sig2, List<TypeInfo> argTypes)
    {
        int score = 0;
        int paramCount = Math.Min(Math.Min(sig1.ParamTypes.Count, sig2.ParamTypes.Count), argTypes.Count);

        for (int i = 0; i < paramCount; i++)
        {
            var p1 = sig1.ParamTypes[i];
            var p2 = sig2.ParamTypes[i];

            if (IsMoreSpecific(p1, p2))
                score++;
            else if (IsMoreSpecific(p2, p1))
                score--;
        }

        return score;
    }

    /// <summary>
    /// Returns true if 'specific' is a more specific type than 'general'.
    /// Specificity rules:
    /// - Literal types are more specific than primitives
    /// - Primitives are more specific than unions containing them
    /// - Derived classes are more specific than base classes
    /// - Non-nullable types are more specific than nullable types
    /// </summary>
    private bool IsMoreSpecific(TypeInfo specific, TypeInfo general)
    {
        // Literal type > Primitive type
        if (specific is TypeInfo.StringLiteral && general is TypeInfo.String)
            return true;
        if (specific is TypeInfo.NumberLiteral && general is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER })
            return true;
        if (specific is TypeInfo.BooleanLiteral && general is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN })
            return true;

        // Primitive > Union containing it
        if (general is TypeInfo.Union union)
        {
            if (specific is TypeInfo.Primitive || specific is TypeInfo.StringLiteral ||
                specific is TypeInfo.NumberLiteral || specific is TypeInfo.BooleanLiteral)
            {
                // Check if the specific type is one of the union members
                if (union.FlattenedTypes.Any(t => IsCompatible(t, specific)))
                    return true;
            }
        }

        // Non-nullable > Nullable (union with null)
        if (general is TypeInfo.Union nullableUnion && nullableUnion.ContainsNull)
        {
            if (specific is not TypeInfo.Null && specific is not TypeInfo.Union)
                return true;
        }

        // Derived class > Base class
        if (specific is TypeInfo.Instance i1 && general is TypeInfo.Instance i2)
        {
            if (i1.ClassType is TypeInfo.Class specificClass && i2.ClassType is TypeInfo.Class generalClass)
            {
                return IsSubclassOf(specificClass, generalClass);
            }
        }

        return false;
    }

    private TypeInfo CheckClearTimerCall(Expr.Call call, string functionName)
    {
        if (call.Arguments.Count > 1)
        {
            throw new TypeCheckException($"{functionName}() accepts at most one argument.", tsCode: "TS2554");
        }

        if (call.Arguments.Count == 1)
        {
            var handleType = CheckExpr(call.Arguments[0]);
            if (handleType is not TypeInfo.Timeout &&
                handleType is not TypeInfo.Null &&
                handleType is not TypeInfo.Undefined &&
                handleType is not TypeInfo.Any)
            {
                if (handleType is TypeInfo.Union union)
                {
                    bool hasTimeout = union.FlattenedTypes.Any(t => t is TypeInfo.Timeout);
                    if (!hasTimeout)
                    {
                        throw new TypeCheckException($"{functionName}() argument must be a Timeout, got '{handleType}'.", tsCode: "TS2345");
                    }
                }
                else
                {
                    throw new TypeCheckException($"{functionName}() argument must be a Timeout, got '{handleType}'.", tsCode: "TS2345");
                }
            }
        }

        return new TypeInfo.Void();
    }
}
