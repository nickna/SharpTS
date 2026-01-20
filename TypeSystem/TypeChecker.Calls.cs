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
        if (call.Callee is Expr.Variable v && v.Name.Lexeme == "console.log")
        {
             foreach(var arg in call.Arguments) CheckExpr(arg);
             return new TypeInfo.Void();
        }

        // Handle Symbol() constructor - creates unique symbols
        if (call.Callee is Expr.Variable symVar && symVar.Name.Lexeme == "Symbol")
        {
            if (call.Arguments.Count > 1)
            {
                throw new TypeCheckException(" Symbol() accepts at most one argument (description).");
            }
            if (call.Arguments.Count == 1)
            {
                var argType = CheckExpr(call.Arguments[0]);
                if (!IsString(argType) && argType is not TypeInfo.Any)
                {
                    throw new TypeCheckException($" Symbol() description must be a string, got '{argType}'.");
                }
            }
            return new TypeInfo.Symbol();
        }

        // Handle BigInt() constructor - converts number or string to bigint
        if (call.Callee is Expr.Variable bigIntVar && bigIntVar.Name.Lexeme == "BigInt")
        {
            if (call.Arguments.Count != 1)
            {
                throw new TypeCheckException(" BigInt() requires exactly one argument.");
            }
            var argType = CheckExpr(call.Arguments[0]);
            if (!IsNumber(argType) && !IsString(argType) && argType is not TypeInfo.BigInt && argType is not TypeInfo.Any)
            {
                throw new TypeCheckException($" BigInt() argument must be a number, string, or bigint, got '{argType}'.");
            }
            return new TypeInfo.BigInt();
        }

        // Handle Date() function call - returns current date as string (without 'new')
        if (call.Callee is Expr.Variable dateVar && dateVar.Name.Lexeme == "Date")
        {
            // Date() called as a function (not with new) ignores arguments and returns a string
            foreach (var arg in call.Arguments) CheckExpr(arg);
            return new TypeInfo.String();
        }

        // Handle Error() and error subtypes called without 'new' - still creates error objects
        if (call.Callee is Expr.Variable errorVar && IsErrorTypeName(errorVar.Name.Lexeme))
        {
            // Error constructors accept 0-1 argument (optional message)
            // AggregateError accepts 0-2 arguments (errors array, optional message)
            int maxArgs = errorVar.Name.Lexeme == "AggregateError" ? 2 : 1;
            if (call.Arguments.Count > maxArgs)
            {
                throw new TypeCheckException($" {errorVar.Name.Lexeme}() accepts at most {maxArgs} argument(s).");
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
                        throw new TypeCheckException($" AggregateError first argument must be an array, got '{firstArgType}'.");
                    }
                }
                else
                {
                    // For other error types, first argument should be a string message
                    if (!IsString(firstArgType) && firstArgType is not TypeInfo.Any)
                    {
                        throw new TypeCheckException($" {errorVar.Name.Lexeme}() message must be a string, got '{firstArgType}'.");
                    }
                }
            }

            if (call.Arguments.Count == 2)
            {
                var secondArgType = CheckExpr(call.Arguments[1]);
                if (!IsString(secondArgType) && secondArgType is not TypeInfo.Any)
                {
                    throw new TypeCheckException($" AggregateError message must be a string, got '{secondArgType}'.");
                }
            }

            return new TypeInfo.Error(errorVar.Name.Lexeme);
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
                return objMethodType.ReturnType;
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
                return arrMethodType.ReturnType;
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
                return jsonMethodType.ReturnType;
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
                return numMethodType.ReturnType;
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
                return dateMethodType.ReturnType;
            }
        }

        // Handle global parseInt()
        if (call.Callee is Expr.Variable parseIntVar && parseIntVar.Name.Lexeme == "parseInt")
        {
            foreach (var arg in call.Arguments) CheckExpr(arg);
            return new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER);
        }

        // Handle global parseFloat()
        if (call.Callee is Expr.Variable parseFloatVar && parseFloatVar.Name.Lexeme == "parseFloat")
        {
            foreach (var arg in call.Arguments) CheckExpr(arg);
            return new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER);
        }

        // Handle global isNaN()
        if (call.Callee is Expr.Variable isNaNVar && isNaNVar.Name.Lexeme == "isNaN")
        {
            foreach (var arg in call.Arguments) CheckExpr(arg);
            return new TypeInfo.Primitive(Parsing.TokenType.TYPE_BOOLEAN);
        }

        // Handle global isFinite()
        if (call.Callee is Expr.Variable isFiniteVar && isFiniteVar.Name.Lexeme == "isFinite")
        {
            foreach (var arg in call.Arguments) CheckExpr(arg);
            return new TypeInfo.Primitive(Parsing.TokenType.TYPE_BOOLEAN);
        }

        // Handle setTimeout(callback, delay?, ...args)
        if (call.Callee is Expr.Variable setTimeoutVar && setTimeoutVar.Name.Lexeme == "setTimeout")
        {
            if (call.Arguments.Count < 1)
            {
                throw new TypeCheckException(" setTimeout() requires at least one argument (callback).");
            }

            // First argument must be a function
            var callbackType = CheckExpr(call.Arguments[0]);
            if (callbackType is not TypeInfo.Function && callbackType is not TypeInfo.Any)
            {
                throw new TypeCheckException($" setTimeout() callback must be a function, got '{callbackType}'.");
            }

            // Second argument (delay) must be a number if provided
            if (call.Arguments.Count >= 2)
            {
                var delayType = CheckExpr(call.Arguments[1]);
                if (!IsNumber(delayType) && delayType is not TypeInfo.Any && delayType is not TypeInfo.Undefined)
                {
                    throw new TypeCheckException($" setTimeout() delay must be a number, got '{delayType}'.");
                }
            }

            // Additional arguments are passed to the callback (any type allowed)
            for (int i = 2; i < call.Arguments.Count; i++)
            {
                CheckExpr(call.Arguments[i]);
            }

            return new TypeInfo.Timeout();
        }

        // Handle clearTimeout(handle?)
        if (call.Callee is Expr.Variable clearTimeoutVar && clearTimeoutVar.Name.Lexeme == "clearTimeout")
        {
            // clearTimeout accepts 0 or 1 argument
            if (call.Arguments.Count > 1)
            {
                throw new TypeCheckException(" clearTimeout() accepts at most one argument.");
            }

            // If argument provided, it should be Timeout, null, undefined, or any
            if (call.Arguments.Count == 1)
            {
                var handleType = CheckExpr(call.Arguments[0]);
                if (handleType is not TypeInfo.Timeout &&
                    handleType is not TypeInfo.Null &&
                    handleType is not TypeInfo.Undefined &&
                    handleType is not TypeInfo.Any)
                {
                    // Also allow union types containing Timeout
                    if (handleType is TypeInfo.Union union)
                    {
                        bool hasTimeout = union.FlattenedTypes.Any(t => t is TypeInfo.Timeout);
                        if (!hasTimeout)
                        {
                            throw new TypeCheckException($" clearTimeout() argument must be a Timeout, got '{handleType}'.");
                        }
                    }
                    else
                    {
                        throw new TypeCheckException($" clearTimeout() argument must be a Timeout, got '{handleType}'.");
                    }
                }
            }

            return new TypeInfo.Void();
        }

        // Handle setInterval(callback, delay?, ...args)
        if (call.Callee is Expr.Variable setIntervalVar && setIntervalVar.Name.Lexeme == "setInterval")
        {
            if (call.Arguments.Count < 1)
            {
                throw new TypeCheckException(" setInterval() requires at least one argument (callback).");
            }

            // First argument must be a function
            var callbackType = CheckExpr(call.Arguments[0]);
            if (callbackType is not TypeInfo.Function && callbackType is not TypeInfo.Any)
            {
                throw new TypeCheckException($" setInterval() callback must be a function, got '{callbackType}'.");
            }

            // Second argument (delay) must be a number if provided
            if (call.Arguments.Count >= 2)
            {
                var delayType = CheckExpr(call.Arguments[1]);
                if (!IsNumber(delayType) && delayType is not TypeInfo.Any && delayType is not TypeInfo.Undefined)
                {
                    throw new TypeCheckException($" setInterval() delay must be a number, got '{delayType}'.");
                }
            }

            // Additional arguments are passed to the callback (any type allowed)
            for (int i = 2; i < call.Arguments.Count; i++)
            {
                CheckExpr(call.Arguments[i]);
            }

            return new TypeInfo.Timeout();
        }

        // Handle clearInterval(handle?)
        if (call.Callee is Expr.Variable clearIntervalVar && clearIntervalVar.Name.Lexeme == "clearInterval")
        {
            // clearInterval accepts 0 or 1 argument
            if (call.Arguments.Count > 1)
            {
                throw new TypeCheckException(" clearInterval() accepts at most one argument.");
            }

            // If argument provided, it should be Timeout, null, undefined, or any
            if (call.Arguments.Count == 1)
            {
                var handleType = CheckExpr(call.Arguments[0]);
                if (handleType is not TypeInfo.Timeout &&
                    handleType is not TypeInfo.Null &&
                    handleType is not TypeInfo.Undefined &&
                    handleType is not TypeInfo.Any)
                {
                    // Also allow union types containing Timeout
                    if (handleType is TypeInfo.Union union)
                    {
                        bool hasTimeout = union.FlattenedTypes.Any(t => t is TypeInfo.Timeout);
                        if (!hasTimeout)
                        {
                            throw new TypeCheckException($" clearInterval() argument must be a Timeout, got '{handleType}'.");
                        }
                    }
                    else
                    {
                        throw new TypeCheckException($" clearInterval() argument must be a Timeout, got '{handleType}'.");
                    }
                }
            }

            return new TypeInfo.Void();
        }

        // Handle __objectRest (internal helper for object rest patterns)
        if (call.Callee is Expr.Variable restVar && restVar.Name.Lexeme == "__objectRest")
        {
            foreach (var arg in call.Arguments) CheckExpr(arg);
            return new TypeInfo.Any(); // Returns an object with remaining properties
        }

        TypeInfo calleeType = CheckExpr(call.Callee);

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
                typeArgs = call.TypeArgs.Select(ToTypeInfo).ToList();
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

            // Only check min arity if no spreads (spreads can expand to any count)
            if (!hasSpread && nonSpreadCount < funcType.MinArity)
            {
                throw new TypeCheckException($" Expected at least {funcType.MinArity} arguments but got {nonSpreadCount}.");
            }

            // Check for too many arguments (when there's no rest parameter)
            if (!hasSpread && !funcType.HasRestParam && nonSpreadCount > funcType.ParamTypes.Count)
            {
                throw new TypeCheckException($" Expected {funcType.ParamTypes.Count} arguments but got {nonSpreadCount}.");
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
                            throw new TypeCheckException($" Spread element type '{arrType.ElementType}' not compatible with rest parameter type '{restElementType}'.");
                        }
                    }
                    else if (spreadType is not TypeInfo.Any)
                    {
                        throw new TypeCheckException($" Spread argument must be an array.");
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
                        TypeInfo argType = CheckExpr(arg);
                        if (paramIndex < regularParamCount)
                        {
                            // Check against regular parameter
                            if (!IsCompatible(funcType.ParamTypes[paramIndex], argType))
                            {
                                throw new TypeCheckException($" Argument {argIndex + 1} expected type '{funcType.ParamTypes[paramIndex]}' but got '{argType}'.");
                            }
                        }
                        else if (restElementType != null)
                        {
                            // Check against rest parameter element type
                            if (!IsCompatible(restElementType, argType))
                            {
                                throw new TypeCheckException($" Argument {argIndex + 1} expected type '{restElementType}' but got '{argType}'.");
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
            return CheckCallableInterfaceCall(itf, call.TypeArgs, call.Arguments);
        }
        if (calleeType is TypeInfo.GenericInterface gi && gi.IsCallable)
        {
            return CheckGenericCallableInterfaceCall(gi, call.TypeArgs, call.Arguments);
        }

        throw new TypeCheckException($" Can only call functions.");
    }

    /// <summary>
    /// Checks a call on an interface with call signatures.
    /// Returns the return type of the matching call signature.
    /// </summary>
    private TypeInfo CheckCallableInterfaceCall(
        TypeInfo.Interface itf,
        List<string>? typeArgs,
        List<Expr> arguments)
    {
        if (itf.CallSignatures == null || itf.CallSignatures.Count == 0)
        {
            throw new TypeCheckException($" Interface '{itf.Name}' is not callable.");
        }

        List<TypeInfo> argTypes = arguments.Select(CheckExpr).ToList();

        // Try each call signature
        foreach (var callSig in itf.CallSignatures)
        {
            if (callSig.IsGeneric)
            {
                // Generic call signature - try to instantiate
                var result = TryMatchGenericCallSignature(callSig, typeArgs, argTypes);
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

        throw new TypeCheckException($" No call signature matches the call for interface '{itf.Name}'.");
    }

    /// <summary>
    /// Checks a call on a generic interface with call signatures.
    /// </summary>
    private TypeInfo CheckGenericCallableInterfaceCall(
        TypeInfo.GenericInterface gi,
        List<string>? typeArgs,
        List<Expr> arguments)
    {
        if (gi.CallSignatures == null || gi.CallSignatures.Count == 0)
        {
            throw new TypeCheckException($" Generic interface '{gi.Name}' is not callable.");
        }

        // If type args provided, substitute and check
        if (typeArgs != null && typeArgs.Count > 0)
        {
            var instantiatedTypeArgs = typeArgs.Select(ToTypeInfo).ToList();
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

        throw new TypeCheckException($" No call signature matches the call for generic interface '{gi.Name}'.");
    }

    /// <summary>
    /// Tries to match a generic call signature by inferring type arguments.
    /// </summary>
    private TypeInfo? TryMatchGenericCallSignature(
        TypeInfo.CallSignature callSig,
        List<string>? explicitTypeArgs,
        List<TypeInfo> argTypes)
    {
        if (callSig.TypeParams == null || callSig.TypeParams.Count == 0)
            return null;

        Dictionary<string, TypeInfo> inferred = [];

        if (explicitTypeArgs != null && explicitTypeArgs.Count > 0)
        {
            // Use explicit type arguments
            for (int i = 0; i < callSig.TypeParams.Count && i < explicitTypeArgs.Count; i++)
            {
                inferred[callSig.TypeParams[i].Name] = ToTypeInfo(explicitTypeArgs[i]);
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
            throw new TypeCheckException($" No overload matches call with arguments ({argTypesStr}).");
        }

        // If multiple signatures match, select the most specific one
        TypeInfo.Function bestMatch = SelectMostSpecificOverload(matchingSignatures, argTypes);

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
            typeArgs = call.TypeArgs.Select(ToTypeInfo).ToList();
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
            throw new TypeCheckException($" No overload matches call with arguments ({argTypesStr}).");
        }

        // If multiple signatures match, select the most specific one
        TypeInfo.Function bestMatch = SelectMostSpecificOverload(matchingSignatures, argTypes);

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
}
