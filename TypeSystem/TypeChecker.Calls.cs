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
                throw new Exception("Type Error: Symbol() accepts at most one argument (description).");
            }
            if (call.Arguments.Count == 1)
            {
                var argType = CheckExpr(call.Arguments[0]);
                if (!IsString(argType) && argType is not TypeInfo.Any)
                {
                    throw new Exception($"Type Error: Symbol() description must be a string, got '{argType}'.");
                }
            }
            return new TypeInfo.Symbol();
        }

        // Handle BigInt() constructor - converts number or string to bigint
        if (call.Callee is Expr.Variable bigIntVar && bigIntVar.Name.Lexeme == "BigInt")
        {
            if (call.Arguments.Count != 1)
            {
                throw new Exception("Type Error: BigInt() requires exactly one argument.");
            }
            var argType = CheckExpr(call.Arguments[0]);
            if (!IsNumber(argType) && !IsString(argType) && argType is not TypeInfo.BigInt && argType is not TypeInfo.Any)
            {
                throw new Exception($"Type Error: BigInt() argument must be a number, string, or bigint, got '{argType}'.");
            }
            return new TypeInfo.BigInt();
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

        if (calleeType is TypeInfo.Function funcType)
        {
            // Count non-spread arguments and check for spreads
            bool hasSpread = call.Arguments.Any(a => a is Expr.Spread);
            int nonSpreadCount = call.Arguments.Count(a => a is not Expr.Spread);

            // Only check min arity if no spreads (spreads can expand to any count)
            if (!hasSpread && nonSpreadCount < funcType.MinArity)
            {
                throw new Exception($"Type Error: Expected at least {funcType.MinArity} arguments but got {nonSpreadCount}.");
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
                            throw new Exception($"Type Error: Spread element type '{arrType.ElementType}' not compatible with rest parameter type '{restElementType}'.");
                        }
                    }
                    else if (spreadType is not TypeInfo.Any)
                    {
                        throw new Exception($"Type Error: Spread argument must be an array.");
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
                                throw new Exception($"Type Error: Argument {argIndex + 1} expected type '{funcType.ParamTypes[paramIndex]}' but got '{argType}'.");
                            }
                        }
                        else if (restElementType != null)
                        {
                            // Check against rest parameter element type
                            if (!IsCompatible(restElementType, argType))
                            {
                                throw new Exception($"Type Error: Argument {argIndex + 1} expected type '{restElementType}' but got '{argType}'.");
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

        throw new Exception($"Type Error: Can only call functions.");
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
            throw new Exception($"Type Error: No overload matches call with arguments ({argTypesStr}).");
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
        if (specific is TypeInfo.StringLiteral && general is TypeInfo.Primitive { Type: TokenType.TYPE_STRING })
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
