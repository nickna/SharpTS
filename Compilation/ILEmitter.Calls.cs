using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation.CallHandlers;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Function call and closure emission methods for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    /// <summary>
    /// Registry of call handlers for Chain of Responsibility dispatch.
    /// </summary>
    private static readonly CallHandlerRegistry _callHandlers = new();

    protected override void EmitCall(Expr.Call c)
    {
        // Try handler chain first (handles simple cases)
        if (_callHandlers.TryHandle(this, c))
            return;

        // Special case: super() or super.constructor() call in derived class
        if (c.Callee is Expr.Super superExpr && (superExpr.Method == null || superExpr.Method.Lexeme == "constructor"))
        {
            // Try class declaration constructors first
            // Use GetConstructor (not GetConstructorByQualifiedName) to resolve simple names like "Animal"
            // to qualified names like "$M_base_Animal" for cross-module inheritance
            var parentCtor = _ctx.CurrentSuperclassName != null
                ? _ctx.ClassRegistry?.GetConstructor(_ctx.CurrentSuperclassName)
                : null;
            if (parentCtor != null)
            {
                EmitSuperConstructorCall(parentCtor, c.Arguments);
                return;
            }

            // Try class expression constructors (for class expression inheritance)
            if (_ctx.CurrentClassExpr != null &&
                _ctx.ClassExprSuperclass?.TryGetValue(_ctx.CurrentClassExpr, out var superclassName) == true &&
                superclassName != null)
            {
                // Find parent constructor by superclass name (using variable name mapping)
                ConstructorBuilder? parentExprCtor = null;

                // Check class expression constructors using VarToClassExpr mapping
                if (_ctx.VarToClassExpr != null &&
                    _ctx.VarToClassExpr.TryGetValue(superclassName, out var parentClassExpr) &&
                    _ctx.ClassExprConstructors != null &&
                    _ctx.ClassExprConstructors.TryGetValue(parentClassExpr, out var exprCtor))
                {
                    parentExprCtor = exprCtor;
                }

                // If not found in class expressions, try class declarations
                if (parentExprCtor == null)
                {
                    parentExprCtor = _ctx.ClassRegistry?.GetConstructorByQualifiedName(superclassName);
                }

                if (parentExprCtor != null)
                {
                    EmitSuperConstructorCall(parentExprCtor, c.Arguments);
                    return;
                }
            }
        }

        // Special case: console methods (log, error, warn, info, debug, clear, time, timeEnd, timeLog)
        if (_helpers.TryEmitConsoleMethod(c,
            arg => { EmitExpression(arg); EmitBoxIfNeeded(arg); },
            _ctx.Runtime!))
        {
            return;
        }

        // Static type dispatch via registry (Math, JSON, Object, Array, Number, Promise)
        if (c.Callee is Expr.Get staticGet &&
            staticGet.Object is Expr.Variable staticVar &&
            _ctx.TypeEmitterRegistry != null)
        {
            var staticStrategy = _ctx.TypeEmitterRegistry.GetStaticStrategy(staticVar.Name.Lexeme);
            if (staticStrategy != null && staticStrategy.TryEmitStaticCall(this, staticGet.Name.Lexeme, c.Arguments))
            {
                SetStackUnknown();
                return;
            }
        }

        // Special case: globalThis.Math.floor(), globalThis.console.log(), etc.
        if (c.Callee is Expr.Get chainedGet &&
            chainedGet.Object is Expr.Get innerGet &&
            innerGet.Object is Expr.Variable globalThisVar &&
            globalThisVar.Name.Lexeme == "globalThis" &&
            _ctx.TypeEmitterRegistry != null)
        {
            string namespaceName = innerGet.Name.Lexeme;
            string methodName = chainedGet.Name.Lexeme;

            // Handle globalThis.console.log() specially
            if (namespaceName == "console")
            {
                var fakeCall = new Expr.Call(
                    new Expr.Get(new Expr.Variable(innerGet.Name), chainedGet.Name, false),
                    chainedGet.Name,
                    null, // TypeArgs
                    c.Arguments
                );
                if (_helpers.TryEmitConsoleMethod(fakeCall,
                    arg => { EmitExpression(arg); EmitBoxIfNeeded(arg); },
                    _ctx.Runtime!))
                {
                    return;
                }
            }

            // Use the static emitter for the inner namespace
            var staticStrategy = _ctx.TypeEmitterRegistry.GetStaticStrategy(namespaceName);
            if (staticStrategy != null && staticStrategy.TryEmitStaticCall(this, methodName, c.Arguments))
            {
                SetStackUnknown();
                return;
            }
        }

        // Special case: process.stdin.read(), process.stdout.write(), process.stderr.write()
        if (TryEmitProcessStreamCall(c))
        {
            return;
        }

        // Built-in module method calls (path.join, fs.readFileSync, etc.)
        if (c.Callee is Expr.Get builtInGet &&
            builtInGet.Object is Expr.Variable builtInVar &&
            _ctx.BuiltInModuleNamespaces != null &&
            _ctx.BuiltInModuleNamespaces.TryGetValue(builtInVar.Name.Lexeme, out var builtInModuleName) &&
            _ctx.BuiltInModuleEmitterRegistry?.GetEmitter(builtInModuleName) is { } builtInEmitter)
        {
            if (builtInEmitter.TryEmitMethodCall(this, builtInGet.Name.Lexeme, c.Arguments))
            {
                SetStackUnknown();
                return;
            }
        }

        // Special case: fs.promises.methodName() - emit direct method call instead of going through TSFunction
        // Pattern: c.Callee is Get(Get(Variable("fs"), "promises"), "methodName")
        if (c.Callee is Expr.Get fsPromisesMethodGet &&
            fsPromisesMethodGet.Object is Expr.Get fsPromisesGet &&
            fsPromisesGet.Name.Lexeme == "promises" &&
            fsPromisesGet.Object is Expr.Variable fsVar &&
            _ctx.BuiltInModuleNamespaces != null &&
            _ctx.BuiltInModuleNamespaces.TryGetValue(fsVar.Name.Lexeme, out var fsModuleName) &&
            fsModuleName == "fs" &&
            _ctx.BuiltInModuleEmitterRegistry?.GetEmitter("fs/promises") is { } fsPromisesEmitter)
        {
            if (fsPromisesEmitter.TryEmitMethodCall(this, fsPromisesMethodGet.Name.Lexeme, c.Arguments))
            {
                SetStackUnknown();
                return;
            }
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
                throw new CompileException("BigInt() requires exactly one argument.");

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

        // Special case: Global structuredClone()
        if (c.Callee is Expr.Variable structuredCloneVar && structuredCloneVar.Name.Lexeme == "structuredClone")
        {
            EmitStructuredClone(c.Arguments);
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
        if (c.Callee is Expr.Get classStaticGet &&
            classStaticGet.Object is Expr.Variable classVar &&
            _ctx.Classes.TryGetValue(_ctx.ResolveClassName(classVar.Name.Lexeme), out var classBuilder))
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);
            // Use TryGetCallableStaticMethod to handle generic classes properly
            if (_ctx.ClassRegistry!.TryGetCallableStaticMethod(resolvedClassName, classStaticGet.Name.Lexeme, classBuilder, out var callableMethod))
            {
                var staticMethodParams = callableMethod!.GetParameters();
                var paramCount = staticMethodParams.Length;

                // Emit provided arguments with proper type conversions
                for (int i = 0; i < c.Arguments.Count; i++)
                {
                    EmitExpression(c.Arguments[i]);
                    if (i < staticMethodParams.Length)
                    {
                        EmitConversionForParameter(c.Arguments[i], staticMethodParams[i].ParameterType);
                    }
                    else
                    {
                        EmitBoxIfNeeded(c.Arguments[i]);
                    }
                }

                // Pad missing optional arguments with appropriate default values
                for (int i = c.Arguments.Count; i < paramCount; i++)
                {
                    EmitDefaultForType(staticMethodParams[i].ParameterType);
                }

                IL.Emit(OpCodes.Call, callableMethod);
                SetStackUnknown();
                return;
            }
        }

        // Special case: Static method call on imported class (import X = require('./module') where module exports a class)
        if (c.Callee is Expr.Get importedClassStaticGet &&
            importedClassStaticGet.Object is Expr.Variable importedClassVar &&
            _ctx.ImportedClassAliases?.TryGetValue(importedClassVar.Name.Lexeme, out var importedQualifiedClassName) == true &&
            _ctx.Classes.TryGetValue(importedQualifiedClassName, out var importedClassBuilder))
        {
            if (_ctx.ClassRegistry!.TryGetCallableStaticMethod(importedQualifiedClassName, importedClassStaticGet.Name.Lexeme, importedClassBuilder, out var importedCallableMethod))
            {
                var importedMethodParams = importedCallableMethod!.GetParameters();
                var paramCount = importedMethodParams.Length;

                // Emit provided arguments with proper type conversions
                for (int i = 0; i < c.Arguments.Count; i++)
                {
                    EmitExpression(c.Arguments[i]);
                    if (i < importedMethodParams.Length)
                    {
                        EmitConversionForParameter(c.Arguments[i], importedMethodParams[i].ParameterType);
                    }
                    else
                    {
                        EmitBoxIfNeeded(c.Arguments[i]);
                    }
                }

                // Pad missing optional arguments
                for (int i = c.Arguments.Count; i < paramCount; i++)
                {
                    EmitDefaultForType(importedMethodParams[i].ParameterType);
                }

                IL.Emit(OpCodes.Call, importedCallableMethod);
                SetStackUnknown();
                return;
            }
        }

        // Special case: Static method call on class expression (const Factory = class { static create() { } }; Factory.create())
        if (c.Callee is Expr.Get classExprStaticGet &&
            classExprStaticGet.Object is Expr.Variable classExprVar &&
            _ctx.VarToClassExpr != null &&
            _ctx.VarToClassExpr.TryGetValue(classExprVar.Name.Lexeme, out var classExpr) &&
            _ctx.ClassExprStaticMethods != null &&
            _ctx.ClassExprStaticMethods.TryGetValue(classExpr, out var exprStaticMethods) &&
            exprStaticMethods.TryGetValue(classExprStaticGet.Name.Lexeme, out var exprStaticMethod))
        {
            var exprStaticMethodParams = exprStaticMethod.GetParameters();
            var paramCount = exprStaticMethodParams.Length;

            // Emit provided arguments with proper type conversions
            for (int i = 0; i < c.Arguments.Count; i++)
            {
                EmitExpression(c.Arguments[i]);
                if (i < exprStaticMethodParams.Length)
                {
                    EmitConversionForParameter(c.Arguments[i], exprStaticMethodParams[i].ParameterType);
                }
                else
                {
                    EmitBoxIfNeeded(c.Arguments[i]);
                }
            }

            // Pad missing optional arguments with appropriate default values
            for (int i = c.Arguments.Count; i < paramCount; i++)
            {
                EmitDefaultForType(exprStaticMethodParams[i].ParameterType);
            }

            IL.Emit(OpCodes.Call, exprStaticMethod);
            SetStackUnknown();
            return;
        }

        // Special case: this.method() in static context (static blocks, static methods)
        // In static blocks, 'this' refers to the class constructor, so this.method() calls static methods
        if (c.Callee is Expr.Get thisStaticGet &&
            thisStaticGet.Object is Expr.This &&
            !_ctx.IsInstanceMethod &&
            _ctx.CurrentClassBuilder != null)
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

            if (currentClassName != null &&
                _ctx.ClassRegistry!.TryGetCallableStaticMethod(currentClassName, thisStaticGet.Name.Lexeme, _ctx.CurrentClassBuilder, out var thisStaticMethod))
            {
                var staticMethodParams = thisStaticMethod!.GetParameters();
                var paramCount = staticMethodParams.Length;

                // Emit provided arguments with proper type conversions
                for (int i = 0; i < c.Arguments.Count; i++)
                {
                    EmitExpression(c.Arguments[i]);
                    if (i < staticMethodParams.Length)
                    {
                        EmitConversionForParameter(c.Arguments[i], staticMethodParams[i].ParameterType);
                    }
                    else
                    {
                        EmitBoxIfNeeded(c.Arguments[i]);
                    }
                }

                // Pad missing optional arguments with appropriate default values
                for (int i = c.Arguments.Count; i < paramCount; i++)
                {
                    EmitDefaultForType(staticMethodParams[i].ParameterType);
                }

                IL.Emit(OpCodes.Call, thisStaticMethod);
                SetStackUnknown();
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
        // First check for built-in module method bindings (e.g., import { readFile } from 'fs/promises')
        // This handles direct calls like readFile(...) by emitting direct method calls instead of TSFunction
        if (c.Callee is Expr.Variable builtInMethodVar &&
            _ctx.BuiltInModuleMethodBindings?.TryGetValue(builtInMethodVar.Name.Lexeme, out var binding) == true)
        {
            var methodBindingEmitter = _ctx.BuiltInModuleEmitterRegistry?.GetEmitter(binding.ModuleName);
            if (methodBindingEmitter != null && methodBindingEmitter.TryEmitMethodCall(this, binding.MethodName, c.Arguments))
            {
                return;
            }
        }

        // Check for async functions
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
                        // Pass Symbol.iterator and runtimeType for iterator protocol support
                        IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolIterator);
                        IL.Emit(OpCodes.Ldtoken, _ctx.Runtime!.RuntimeType);
                        IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
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
                    // No rest param - select overload based on argument count
                    // Check if we need to use an overload (fewer args than params)
                    if (c.Arguments.Count < paramCount &&
                        _ctx.FunctionOverloads != null &&
                        _ctx.FunctionOverloads.TryGetValue(resolvedFuncName, out var overloads))
                    {
                        // Find the overload matching our argument count
                        var matchingOverload = overloads.FirstOrDefault(o =>
                            o.GetParameters().Length == c.Arguments.Count);
                        if (matchingOverload != null)
                        {
                            targetMethod = matchingOverload;
                            paramCount = c.Arguments.Count; // Update param count for typed emission
                        }
                    }

                    // Get target parameter types for proper conversion
                    var targetParams = targetMethod.GetParameters();

                    // Emit arguments with proper type conversions
                    for (int i = 0; i < c.Arguments.Count; i++)
                    {
                        var arg = c.Arguments[i];
                        if (arg is Expr.Spread spread)
                        {
                            // Spread in non-rest function - just emit the expression
                            EmitExpression(spread.Expression);
                            EmitBoxIfNeeded(spread.Expression);
                        }
                        else
                        {
                            EmitExpression(arg);
                            // Convert to target parameter type
                            if (i < targetParams.Length)
                            {
                                EmitConversionForParameter(arg, targetParams[i].ParameterType);
                            }
                            else
                            {
                                EmitBoxIfNeeded(arg);
                            }
                        }
                    }

                    // Only pad with nulls if no matching overload was found
                    // and we still need more arguments
                    for (int i = c.Arguments.Count; i < paramCount; i++)
                    {
                        var paramType = targetParams[i].ParameterType;
                        EmitDefaultForType(paramType);
                    }
                }

                IL.Emit(OpCodes.Call, targetMethod);
                // Handle typed return values - box value types so callers receive object
                BoxReturnValueIfNeeded(targetMethod.ReturnType);
                return;
            }
        }

        // Function value call (variable holding TSFunction, or direct arrow call)
        EmitFunctionValueCall(c);
    }

    private void EmitFunctionValueCall(Expr.Call c)
    {
        // Emit the callee and store in a local (may be $TSFunction, $BoundTSFunction, or other callable)
        var calleeLocal = IL.DeclareLocal(_ctx.Types.Object);
        EmitExpression(c.Callee);
        EmitBoxIfNeeded(c.Callee);
        IL.Emit(OpCodes.Stloc, calleeLocal);

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

            // Pass Symbol.iterator and runtimeType for iterator protocol support
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolIterator);
            IL.Emit(OpCodes.Ldtoken, _ctx.Runtime!.RuntimeType);
            IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);

            // Call ExpandCallArgs
            IL.Emit(OpCodes.Call, _ctx.Runtime!.ExpandCallArgs);
        }

        // Call InvokeMethodValue(receiver, function, args)
        // This handles $TSFunction directly and falls back to InvokeValue for other callables
        // (like $BoundTSFunction which also has an Invoke method)
        var argsLocal = IL.DeclareLocal(_ctx.Types.ObjectArray);
        IL.Emit(OpCodes.Stloc, argsLocal);
        IL.Emit(OpCodes.Ldnull);  // receiver = null for non-method calls
        IL.Emit(OpCodes.Ldloc, calleeLocal);  // function
        IL.Emit(OpCodes.Ldloc, argsLocal);  // args
        IL.Emit(OpCodes.Call, _ctx.Runtime!.InvokeMethodValue);
        SetStackUnknown();
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

        // Timeout instance methods: ref(), unref()
        if (objType is TypeSystem.TypeInfo.Timeout && methodName is "ref" or "unref")
        {
            EmitExpression(methodGet.Object);
            EmitBoxIfNeeded(methodGet.Object);
            IL.Emit(OpCodes.Castclass, _ctx.Runtime!.TSTimeoutType);
            IL.Emit(OpCodes.Callvirt, methodName == "ref" ? _ctx.Runtime!.TSTimeoutRef : _ctx.Runtime!.TSTimeoutUnref);
            SetStackUnknown();
            return;
        }

        // Promise instance methods: then(), catch(), finally()
        // These work on Task<object?> returned by async functions
        if (methodName is "then" or "catch" or "finally")
        {
            // Check if we know it's a Promise at compile time
            if (objType is TypeSystem.TypeInfo.Promise)
            {
                EmitPromiseInstanceMethodCall(methodGet.Object, methodName, arguments);
                return;
            }
        }

        // Type-first dispatch: Use TypeEmitterRegistry if we have type information
        if (objType != null && _ctx.TypeEmitterRegistry != null)
        {
            var strategy = _ctx.TypeEmitterRegistry.GetStrategy(objType);
            if (strategy != null && strategy.TryEmitMethodCall(this, methodGet.Object, methodName, arguments))
                return;

            // Handle union types - try emitters for member types
            // BUT for methods that exist on multiple types, use runtime dispatch instead
            if (objType is TypeSystem.TypeInfo.Union union)
            {
                bool hasBufferMember = union.Types.Any(t => t is TypeSystem.TypeInfo.Buffer);
                bool hasStringMember = union.Types.Any(t => t is TypeSystem.TypeInfo.String or TypeSystem.TypeInfo.StringLiteral);
                bool hasArrayMember = union.Types.Any(t => t is TypeSystem.TypeInfo.Array);

                // Count how many types in the union could have this method
                // Methods like includes, indexOf, slice exist on multiple types
                bool isAmbiguousMethod = methodName is "slice" or "concat" or "includes" or "indexOf"
                    or "toString" or "valueOf";
                int typesWithMethod = 0;
                if (hasBufferMember && isAmbiguousMethod) typesWithMethod++;
                if (hasStringMember && isAmbiguousMethod) typesWithMethod++;
                if (hasArrayMember && isAmbiguousMethod) typesWithMethod++;

                // If multiple types could have this method, skip type-specific emitters
                // and let runtime dispatch handle it below
                if (typesWithMethod <= 1)
                {
                    // Try buffer emitter if union contains buffer
                    if (hasBufferMember)
                    {
                        var bufferStrategy = _ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.Buffer());
                        if (bufferStrategy != null && bufferStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, arguments))
                            return;
                    }

                    // Try string emitter if union contains string
                    if (hasStringMember)
                    {
                        var stringStrategy = _ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.String());
                        if (stringStrategy != null && stringStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, arguments))
                            return;
                    }

                    // Try array emitter if union contains array
                    if (hasArrayMember)
                    {
                        var arrayStrategy = _ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.Array(new TypeSystem.TypeInfo.Any()));
                        if (arrayStrategy != null && arrayStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, arguments))
                            return;
                    }
                }
            }
        }

        // Methods that exist on both strings and arrays - runtime dispatch for any/unknown/union types
        // Note: String and Array types are handled by TypeEmitterRegistry above
        // startsWith/endsWith are string-only but included here for Any-typed values
        // substring/charAt are string-only but need runtime dispatch for union types (string | undefined)
        if (methodName is "slice" or "concat" or "includes" or "indexOf" or "startsWith" or "endsWith"
            or "substring" or "charAt")
        {
            EmitAmbiguousMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

        // Number instance methods - runtime dispatch for any/unknown types
        if (methodName is "toFixed" or "toPrecision" or "toExponential" or "valueOf" or "toString")
        {
            // Check if we know it's a number at compile time
            if (objType is TypeSystem.TypeInfo.Primitive { Type: Parsing.TokenType.TYPE_NUMBER } or TypeSystem.TypeInfo.NumberLiteral)
            {
                EmitNumberMethodCallDirect(methodGet.Object, methodName, arguments);
                return;
            }
            // For unknown types, use runtime dispatch
            EmitNumberMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

        // Note: Date, Map, Set, WeakMap, WeakSet, RegExp methods are handled by TypeEmitterRegistry above
        // For Any types, we need runtime dispatch for Map methods since TypeEmitterRegistry won't catch them
        if (methodName is "get" or "set" or "has" or "delete" or "clear" or "keys" or "values" or "entries" or "forEach")
        {
            EmitMapMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

        // String-only methods - runtime dispatch for any/unknown types
        // When type is known as string, TypeEmitterRegistry's StringEmitter handles these
        // For Any types, we need to check at runtime if the receiver is a string
        if (methodName is "padEnd" or "padStart" or "trim" or "trimStart" or "trimEnd"
            or "toUpperCase" or "toLowerCase" or "replace" or "replaceAll" or "split"
            or "match" or "search" or "repeat" or "charCodeAt" or "at" or "lastIndexOf")
        {
            EmitStringOnlyMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

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
        var builder = _ctx.ILBuilder;
        var isStringLabel = builder.DefineLabel("ambiguous_string");
        var isListLabel = builder.DefineLabel("ambiguous_list");
        var doneLabel = builder.DefineLabel("ambiguous_done");

        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Isinst, _ctx.Types.String);
        builder.Emit_Brtrue(isStringLabel);

        // Assume it's a list if not a string
        builder.Emit_Br(isListLabel);

        // String path
        builder.MarkLabel(isStringLabel);
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

            case "startsWith":
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
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringStartsWith);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;

            case "endsWith":
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
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringEndsWith);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;

            case "substring":
                // str.substring(start, end?)
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
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringSubstring);
                break;

            case "charAt":
                // str.charAt(index)
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
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringCharAt);
                break;
        }
        builder.Emit_Br(doneLabel);

        // List path
        builder.MarkLabel(isListLabel);
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

            case "startsWith":
            case "endsWith":
                // Arrays don't have startsWith/endsWith - pop the list and return false
                IL.Emit(OpCodes.Pop);
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;
        }

        builder.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Emits a number method call when we know the receiver is a number at compile time.
    /// </summary>
    private void EmitNumberMethodCallDirect(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the number value
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        // Emit appropriate runtime method call based on method name
        switch (methodName)
        {
            case "toFixed":
                // Emit digits argument (default 0)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(OpCodes.Box, _ctx.Types.Int32);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberToFixed);
                break;

            case "toPrecision":
                // Emit precision argument (default null for default behavior)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberToPrecision);
                break;

            case "toExponential":
                // Emit digits argument (default null for default behavior)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberToExponential);
                break;

            case "valueOf":
                // valueOf just returns the number itself (already on stack, boxed)
                break;

            case "toString":
                // Emit radix argument (default 10)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_I4, 10);
                    IL.Emit(OpCodes.Box, _ctx.Types.Int32);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberToStringRadix);
                break;
        }
    }

    /// <summary>
    /// Emits a number method call with runtime type checking for any/unknown types.
    /// Checks if the receiver is a Double at runtime and dispatches accordingly.
    /// </summary>
    private void EmitNumberMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the object and store in local
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        var objLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, objLocal);

        var builder = _ctx.ILBuilder;
        var isDoubleLabel = builder.DefineLabel("number_method_double");
        var fallbackLabel = builder.DefineLabel("number_method_fallback");
        var doneLabel = builder.DefineLabel("number_method_done");

        // Check if it's a Double
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Isinst, _ctx.Types.Double);
        builder.Emit_Brtrue(isDoubleLabel);

        // Fall through to dynamic dispatch
        builder.Emit_Br(fallbackLabel);

        // Double path - call the appropriate number method
        builder.MarkLabel(isDoubleLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);

        switch (methodName)
        {
            case "toFixed":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(OpCodes.Box, _ctx.Types.Int32);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberToFixed);
                break;

            case "toPrecision":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberToPrecision);
                break;

            case "toExponential":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberToExponential);
                break;

            case "valueOf":
                // valueOf just returns the number (already loaded)
                break;

            case "toString":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_I4, 10);
                    IL.Emit(OpCodes.Box, _ctx.Types.Int32);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberToStringRadix);
                break;
        }
        builder.Emit_Br(doneLabel);

        // Fallback path - use dynamic dispatch via GetProperty/InvokeMethodValue
        builder.MarkLabel(fallbackLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);  // receiver
        IL.Emit(OpCodes.Ldloc, objLocal);
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

        IL.Emit(OpCodes.Call, _ctx.Runtime!.InvokeMethodValue);

        builder.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Emits a string-only method call with runtime type checking for any/unknown types.
    /// Checks if the receiver is a string at runtime and dispatches accordingly.
    /// For padEnd, padStart, trim, replace, split, etc.
    /// </summary>
    private void EmitStringOnlyMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the object and store in local
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        var objLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, objLocal);

        var builder = _ctx.ILBuilder;
        var isStringLabel = builder.DefineLabel("string_method_string");
        var fallbackLabel = builder.DefineLabel("string_method_fallback");
        var doneLabel = builder.DefineLabel("string_method_done");

        // Check if it's a string
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Isinst, _ctx.Types.String);
        builder.Emit_Brtrue(isStringLabel);

        // Fall through to dynamic dispatch
        builder.Emit_Br(fallbackLabel);

        // String path - call the appropriate string method
        builder.MarkLabel(isStringLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Castclass, _ctx.Types.String);

        switch (methodName)
        {
            case "padEnd":
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
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
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringPadEnd);
                break;

            case "padStart":
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
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
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringPadStart);
                break;

            case "trim":
                IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethodNoParams(_ctx.Types.String, "Trim"));
                break;

            case "trimStart":
                IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethodNoParams(_ctx.Types.String, "TrimStart"));
                break;

            case "trimEnd":
                IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethodNoParams(_ctx.Types.String, "TrimEnd"));
                break;

            case "toUpperCase":
                IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethodNoParams(_ctx.Types.String, "ToUpper"));
                break;

            case "toLowerCase":
                IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethodNoParams(_ctx.Types.String, "ToLower"));
                break;

            case "replace":
                if (arguments.Count >= 2)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringReplaceRegExp);
                break;

            case "replaceAll":
                if (arguments.Count >= 2)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Castclass, _ctx.Types.String);
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                    IL.Emit(OpCodes.Castclass, _ctx.Types.String);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringReplaceAll);
                break;

            case "split":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringSplitRegExp);
                break;

            case "match":
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
                break;

            case "search":
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
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                break;

            case "repeat":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Double);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringRepeat);
                break;

            case "charCodeAt":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Double);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringCharCodeAt);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                break;

            case "at":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Double);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringAt);
                break;

            case "lastIndexOf":
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
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringLastIndexOf);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                break;
        }
        builder.Emit_Br(doneLabel);

        // Fallback path - use dynamic dispatch via GetProperty/InvokeMethodValue
        builder.MarkLabel(fallbackLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);  // receiver
        IL.Emit(OpCodes.Ldloc, objLocal);
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

        IL.Emit(OpCodes.Call, _ctx.Runtime!.InvokeMethodValue);

        builder.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Emits a Map method call with runtime type checking.
    /// For Any-typed values that might be Maps (Dictionary&lt;object, object?&gt;).
    /// </summary>
    private void EmitMapMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the object and store in local
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        var objLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, objLocal);

        var builder = _ctx.ILBuilder;
        var isMapLabel = builder.DefineLabel("map_method_map");
        var fallbackLabel = builder.DefineLabel("map_method_fallback");
        var doneLabel = builder.DefineLabel("map_method_done");

        // Check if it's a Map (Dictionary<object, object?>)
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Isinst, _ctx.Types.DictionaryObjectObject);
        builder.Emit_Brtrue(isMapLabel);

        // Fall through to dynamic dispatch
        builder.Emit_Br(fallbackLabel);

        // Map path - call the appropriate Map method from RuntimeTypes
        builder.MarkLabel(isMapLabel);

        switch (methodName)
        {
            case "get":
                IL.Emit(OpCodes.Ldloc, objLocal);
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
                break;

            case "set":
                IL.Emit(OpCodes.Ldloc, objLocal);
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
                break;

            case "has":
                IL.Emit(OpCodes.Ldloc, objLocal);
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
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;

            case "delete":
                IL.Emit(OpCodes.Ldloc, objLocal);
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
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;

            case "clear":
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapClear);
                IL.Emit(OpCodes.Ldnull); // clear returns undefined
                break;

            case "keys":
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapKeys);
                break;

            case "values":
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapValues);
                break;

            case "entries":
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MapEntries);
                break;

            case "forEach":
                IL.Emit(OpCodes.Ldloc, objLocal);
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
                break;

            default:
                // Should not reach here
                IL.Emit(OpCodes.Ldnull);
                break;
        }
        builder.Emit_Br(doneLabel);

        // Fallback path - use dynamic dispatch via GetProperty/InvokeMethodValue
        builder.MarkLabel(fallbackLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);  // receiver
        IL.Emit(OpCodes.Ldloc, objLocal);
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

        IL.Emit(OpCodes.Call, _ctx.Runtime!.InvokeMethodValue);

        builder.MarkLabel(doneLabel);
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

        // Get target parameter types for proper conversion
        var targetParams = methodBuilder.GetParameters();
        int expectedParamCount = targetParams.Length;

        // Emit: ((ClassName)receiver).method(args)
        EmitExpression(receiver);
        EmitBoxIfNeeded(receiver);
        IL.Emit(OpCodes.Castclass, classType);

        // Emit arguments with proper type conversions
        for (int i = 0; i < arguments.Count; i++)
        {
            var arg = arguments[i];
            EmitExpression(arg);
            // Convert to target parameter type
            if (i < targetParams.Length)
            {
                EmitConversionForParameter(arg, targetParams[i].ParameterType);
            }
            else
            {
                EmitBoxIfNeeded(arg);
            }
        }

        // Pad missing optional arguments with appropriate default values
        for (int i = arguments.Count; i < expectedParamCount; i++)
        {
            var paramType = targetParams[i].ParameterType;
            EmitDefaultForType(paramType);
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
            throw new CompileException($"Instance method '{methodName}' (or '{pascalMethodName}') not found on external type {externalType.FullName}");
        }

        // Use type-aware overload resolution
        var resolver = new ExternalMethodResolver(_ctx.TypeMap, _ctx.Types);
        var candidate = resolver.ResolveMethod(methods, arguments);
        var method = (MethodInfo)candidate.Method;

        // Emit receiver and prepare for member access
        EmitExpression(receiver);
        EmitBoxIfNeeded(receiver);
        bool isValueType = PrepareReceiverForMemberAccess(externalType);

        // Emit arguments with type conversion (handles params arrays)
        EmitExternalCallArguments(arguments, method, candidate);

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
            throw new CompileException($"No public constructors found on external type {externalType.FullName}");
        }

        // Use type-aware overload resolution
        var resolver = new ExternalMethodResolver(_ctx.TypeMap, _ctx.Types);
        var candidate = resolver.ResolveConstructor(ctors, arguments);
        var ctor = (ConstructorInfo)candidate.Method;

        // Emit arguments with type conversion (handles params arrays)
        EmitExternalCallArguments(arguments, ctor, candidate);

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
            throw new CompileException($"Static method '{methodName}' (or '{pascalMethodName}') not found on external type {externalType.FullName}");
        }

        // Use type-aware overload resolution
        var resolver = new ExternalMethodResolver(_ctx.TypeMap, _ctx.Types);
        var candidate = resolver.ResolveMethod(methods, arguments);
        var method = (MethodInfo)candidate.Method;

        // Emit arguments with type conversion (handles params arrays)
        EmitExternalCallArguments(arguments, method, candidate);

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
    /// Emits arguments for an external method call, handling params arrays if present.
    /// </summary>
    private void EmitExternalCallArguments(List<Expr> arguments, MethodBase method, MethodCandidate candidate)
    {
        var parameters = method.GetParameters();

        if (candidate.ParamsStartIndex < 0)
        {
            // No params array - emit arguments normally
            for (int i = 0; i < arguments.Count; i++)
            {
                EmitExpression(arguments[i]);
                EmitExternalTypeConversion(parameters[i].ParameterType);
            }
        }
        else
        {
            // Emit regular (non-params) arguments first
            for (int i = 0; i < candidate.ParamsStartIndex; i++)
            {
                EmitExpression(arguments[i]);
                EmitExternalTypeConversion(parameters[i].ParameterType);
            }

            // Create and fill the params array
            var paramsParam = parameters[candidate.ParamsStartIndex];
            var elementType = paramsParam.ParameterType.GetElementType()!;
            int paramsCount = arguments.Count - candidate.ParamsStartIndex;

            // Emit array creation: new T[paramsCount]
            IL.Emit(OpCodes.Ldc_I4, paramsCount);
            IL.Emit(OpCodes.Newarr, elementType);

            // Fill array elements
            bool isObjectArray = elementType == _ctx.Types.Object || elementType == typeof(object);
            for (int i = 0; i < paramsCount; i++)
            {
                IL.Emit(OpCodes.Dup);                    // Duplicate array reference
                IL.Emit(OpCodes.Ldc_I4, i);              // Push index
                EmitExpression(arguments[candidate.ParamsStartIndex + i]);

                // For object[], box value types but leave reference types as-is
                if (isObjectArray)
                {
                    // Box unboxed value types on the stack (numbers, booleans)
                    if (_stackType == StackType.Double)
                    {
                        IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    }
                    else if (_stackType == StackType.Boolean)
                    {
                        IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                    }
                    // Reference types (strings, objects) are already boxed, no action needed
                }
                else
                {
                    EmitExternalTypeConversion(elementType);
                    if (elementType.IsValueType)
                        IL.Emit(OpCodes.Box, elementType);
                }

                IL.Emit(OpCodes.Stelem_Ref);             // Store in array
            }
            SetStackUnknown();
        }
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
        else if (targetType == _ctx.Types.Single || targetType == typeof(float))
        {
            // Float (single precision)
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_R4);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_R4);
        }
        else if (targetType == _ctx.Types.Int16 || targetType == typeof(short))
        {
            // Short (16-bit signed)
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_I4);
                IL.Emit(OpCodes.Conv_I2);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_I4);
            IL.Emit(OpCodes.Conv_I2);
        }
        else if (targetType == _ctx.Types.Byte || targetType == typeof(byte))
        {
            // Byte (8-bit unsigned)
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_I4);
                IL.Emit(OpCodes.Conv_U1);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_I4);
            IL.Emit(OpCodes.Conv_U1);
        }
        else if (targetType == _ctx.Types.SByte || targetType == typeof(sbyte))
        {
            // SByte (8-bit signed)
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_I4);
                IL.Emit(OpCodes.Conv_I1);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_I4);
            IL.Emit(OpCodes.Conv_I1);
        }
        else if (targetType == _ctx.Types.UInt16 || targetType == typeof(ushort))
        {
            // UInt16 (16-bit unsigned)
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_I4);
                IL.Emit(OpCodes.Conv_U2);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_I4);
            IL.Emit(OpCodes.Conv_U2);
        }
        else if (targetType == _ctx.Types.UInt32 || targetType == typeof(uint))
        {
            // UInt32 (32-bit unsigned)
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_U4);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_U4);
        }
        else if (targetType == _ctx.Types.UInt64 || targetType == typeof(ulong))
        {
            // UInt64 (64-bit unsigned)
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_U8);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_U8);
        }
        else if (targetType == _ctx.Types.Char || targetType == typeof(char))
        {
            // Char (16-bit Unicode character, treated as unsigned)
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_I4);
                IL.Emit(OpCodes.Conv_U2);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_I4);
            IL.Emit(OpCodes.Conv_U2);
        }
        else if (targetType == _ctx.Types.Decimal || targetType == typeof(decimal))
        {
            // Decimal requires calling the explicit conversion operator
            if (_stackType != StackType.Double)
                EmitUnboxToDouble();
            var opExplicit = _ctx.Types.Decimal.GetMethod("op_Explicit",
                BindingFlags.Public | BindingFlags.Static, [_ctx.Types.Double]);
            IL.Emit(OpCodes.Call, opExplicit!);
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
        else
        {
            // For object type, box unboxed value types
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                SetStackUnknown();
            }
            else if (_stackType == StackType.Boolean)
            {
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                SetStackUnknown();
            }
            // Reference types are already objects, no conversion needed
        }
    }

    /// <summary>
    /// Tries to emit IL for process.stdin.read(), process.stdout.write(), process.stderr.write() calls.
    /// Returns true if the call was handled.
    /// </summary>
    private bool TryEmitProcessStreamCall(Expr.Call c)
    {
        // Pattern: process.stdin.read(), process.stdout.write("data"), process.stderr.write("data")
        // c.Callee is Expr.Get { Object: Expr.Get { Object: Expr.Variable("process"), Name: "stdin/stdout/stderr" }, Name: "read/write" }

        if (c.Callee is not Expr.Get methodGet)
            return false;

        if (methodGet.Object is not Expr.Get streamGet)
            return false;

        if (streamGet.Object is not Expr.Variable processVar || processVar.Name.Lexeme != "process")
            return false;

        string streamName = streamGet.Name.Lexeme;
        string methodName = methodGet.Name.Lexeme;

        switch (streamName)
        {
            case "stdin" when methodName == "read":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StdinRead);
                SetStackUnknown();
                return true;

            case "stdout" when methodName == "write":
                if (c.Arguments.Count > 0)
                {
                    EmitExpression(c.Arguments[0]);
                    EmitBoxIfNeeded(c.Arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StdoutWrite);
                SetStackUnknown();
                return true;

            case "stderr" when methodName == "write":
                if (c.Arguments.Count > 0)
                {
                    EmitExpression(c.Arguments[0]);
                    EmitBoxIfNeeded(c.Arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StderrWrite);
                SetStackUnknown();
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Emits a Promise instance method call (.then, .catch, .finally).
    /// These methods take callbacks and return a new Promise (Task).
    /// </summary>
    private void EmitPromiseInstanceMethodCall(Expr promise, string methodName, List<Expr> arguments)
    {
        // Emit the promise (should be Task<object?>)
        EmitExpression(promise);
        EmitBoxIfNeeded(promise);

        // Cast to Task<object?> if needed
        IL.Emit(OpCodes.Castclass, typeof(Task<object?>));

        switch (methodName)
        {
            case "then":
                // promise.then(onFulfilled?, onRejected?)
                // PromiseThen(Task<object?> promise, object? onFulfilled, object? onRejected)

                // onFulfilled callback (optional)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }

                // onRejected callback (optional)
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }

                IL.Emit(OpCodes.Call, _ctx.Runtime!.PromiseThen);
                break;

            case "catch":
                // promise.catch(onRejected)
                // PromiseCatch(Task<object?> promise, object? onRejected)

                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }

                IL.Emit(OpCodes.Call, _ctx.Runtime!.PromiseCatch);
                break;

            case "finally":
                // promise.finally(onFinally)
                // PromiseFinally(Task<object?> promise, object? onFinally)

                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }

                IL.Emit(OpCodes.Call, _ctx.Runtime!.PromiseFinally);
                break;

            default:
                // Unknown method - just return the promise unchanged
                break;
        }

        SetStackUnknown();
    }

    /// <summary>
    /// Emits a super() constructor call with proper argument handling and generic type support.
    /// </summary>
    /// <param name="parentCtor">The parent class constructor to call.</param>
    /// <param name="arguments">The arguments to pass to the constructor.</param>
    private void EmitSuperConstructorCall(ConstructorBuilder parentCtor, List<Expr> arguments)
    {
        // Load this
        IL.Emit(OpCodes.Ldarg_0);

        // Load arguments with proper type conversions
        var ctorParams = parentCtor.GetParameters();
        for (int i = 0; i < arguments.Count; i++)
        {
            EmitExpression(arguments[i]);
            if (i < ctorParams.Length)
            {
                EmitConversionForParameter(arguments[i], ctorParams[i].ParameterType);
            }
            else
            {
                EmitBoxIfNeeded(arguments[i]);
            }
        }

        // Pad missing optional arguments with appropriate default values
        for (int i = arguments.Count; i < ctorParams.Length; i++)
        {
            EmitDefaultForType(ctorParams[i].ParameterType);
        }

        // Call parent constructor
        // Handle generic superclass with type arguments (e.g., extends Box<string>)
        ConstructorInfo ctorToCall = parentCtor;
        Type? baseType = _ctx.CurrentClassBuilder?.BaseType;
        if (baseType != null && baseType.IsGenericType && baseType.IsConstructedGenericType)
        {
            // Get the constructor for the closed generic type
            ctorToCall = TypeBuilder.GetConstructor(baseType, parentCtor);
        }
        IL.Emit(OpCodes.Call, ctorToCall);
        IL.Emit(OpCodes.Ldnull); // constructor call returns undefined
        SetStackUnknown();
    }
}
