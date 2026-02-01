using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Generator function compilation for the IL compiler.
/// Handles the definition and emission of generator state machines.
/// </summary>
public partial class ILCompiler
{
    /// <summary>
    /// Defines a generator function and its state machine.
    /// </summary>
    private void DefineGeneratorFunction(Stmt.Function funcStmt)
    {
        string funcName = funcStmt.Name.Lexeme;

        // Analyze the generator function for yield points and hoisted variables
        var analysis = _generators.Analyzer.Analyze(funcStmt);

        // Create the state machine builder
        var smBuilder = new GeneratorStateMachineBuilder(_moduleBuilder, _types, _generators.StateMachineCounter++);
        smBuilder.DefineStateMachine(funcName, analysis, isInstanceMethod: false, runtime: _runtime);

        _generators.StateMachines[funcName] = smBuilder;
        _generators.Functions[funcName] = funcStmt;

        // Define the stub method that creates and returns the state machine
        var paramTypes = funcStmt.Parameters.Select(_ => _types.Object).ToArray();
        var methodBuilder = _programType.DefineMethod(
            funcName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.IEnumerableOfObject,  // Generator returns IEnumerable<object>
            paramTypes
        );

        _functions.Builders[funcName] = methodBuilder;

        // Track rest parameter info
        var restParam = funcStmt.Parameters.FirstOrDefault(p => p.IsRest);
        if (restParam != null)
        {
            int restIndex = funcStmt.Parameters.IndexOf(restParam);
            int regularCount = funcStmt.Parameters.Count(p => !p.IsRest);
            _functions.RestParams[funcName] = (restIndex, regularCount);
        }
    }

    /// <summary>
    /// Emits all generator state machine bodies.
    /// Called after all functions have been defined.
    /// </summary>
    private void EmitGeneratorStateMachineBodies()
    {
        foreach (var (funcName, smBuilder) in _generators.StateMachines)
        {
            var funcStmt = _generators.Functions[funcName];
            var methodBuilder = _functions.Builders[funcName];
            var analysis = _generators.Analyzer.Analyze(funcStmt);

            // Emit the stub method body (creates and returns the state machine)
            EmitGeneratorStubMethod(methodBuilder, smBuilder, funcStmt, analysis);

            // Emit the MoveNext method body
            EmitGeneratorMoveNextBody(smBuilder, funcStmt);

            // Finalize the state machine type
            smBuilder.CreateType();
        }
    }

    /// <summary>
    /// Emits the stub method that creates and initializes the generator state machine.
    /// </summary>
    private void EmitGeneratorStubMethod(
        MethodBuilder methodBuilder,
        GeneratorStateMachineBuilder smBuilder,
        Stmt.Function funcStmt,
        GeneratorStateAnalyzer.GeneratorFunctionAnalysis analysis)
    {
        var il = methodBuilder.GetILGenerator();

        // Create new instance of the state machine using the constructor builder
        il.Emit(OpCodes.Newobj, smBuilder.Constructor);

        // Copy parameters to state machine fields
        for (int i = 0; i < funcStmt.Parameters.Count; i++)
        {
            var paramName = funcStmt.Parameters[i].Name.Lexeme;
            var field = smBuilder.GetVariableField(paramName);
            if (field != null)
            {
                il.Emit(OpCodes.Dup);  // Keep state machine reference on stack
                il.Emit(OpCodes.Ldarg, i);
                il.Emit(OpCodes.Stfld, field);
            }
        }

        // Copy captured outer scope variables to state machine fields
        foreach (var capturedVar in analysis.CapturedVariables)
        {
            var capturedField = smBuilder.CapturedVariables.GetValueOrDefault(capturedVar);
            if (capturedField == null) continue;

            // Try to load from entry-point display class (captured top-level variables)
            if (_closures.CapturedTopLevelVars.Contains(capturedVar) &&
                _closures.EntryPointDisplayClassFields.TryGetValue(capturedVar, out var entryPointField))
            {
                il.Emit(OpCodes.Dup);  // Keep state machine reference on stack
                if (_closures.EntryPointDisplayClassStaticField != null)
                {
                    il.Emit(OpCodes.Ldsfld, _closures.EntryPointDisplayClassStaticField);
                    il.Emit(OpCodes.Ldfld, entryPointField);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Stfld, capturedField);
            }
            // Try to load from top-level static vars (non-captured module-level variables)
            else if (_topLevelStaticVars.TryGetValue(capturedVar, out var staticField))
            {
                il.Emit(OpCodes.Dup);  // Keep state machine reference on stack
                il.Emit(OpCodes.Ldsfld, staticField);
                il.Emit(OpCodes.Stfld, capturedField);
            }
        }

        // Return the state machine (which implements IEnumerable<object>)
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the MoveNext method body for a generator state machine.
    /// Uses GeneratorMoveNextEmitter to handle full generator body with yield expressions.
    /// </summary>
    private void EmitGeneratorMoveNextBody(GeneratorStateMachineBuilder smBuilder, Stmt.Function funcStmt)
    {
        var analysis = _generators.Analyzer.Analyze(funcStmt);

        // Create a compilation context for the state machine
        var il = smBuilder.MoveNextMethod.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functions.Builders, _classes.Builders, _types)
        {
            ClosureAnalyzer = _closures.Analyzer,
            ArrowMethods = _closures.ArrowMethods,
            DisplayClasses = _closures.DisplayClasses,
            DisplayClassFields = _closures.DisplayClassFields,
            DisplayClassConstructors = _closures.DisplayClassConstructors,
            FunctionRestParams = _functions.RestParams,
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            Runtime = _runtime,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            AsyncMethods = null,
            // Module support for multi-module compilation
            CurrentModulePath = _modules.CurrentPath,
            ClassToModule = _modules.ClassToModule,
            FunctionToModule = _modules.FunctionToModule,
            EnumToModule = _modules.EnumToModule,
            DotNetNamespace = _modules.CurrentDotNetNamespace,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            ClassExprBuilders = _classExprs.Builders,
            // Check for function-level "use strict" directive
            IsStrictMode = _isStrictMode || CheckForUseStrict(funcStmt.Body),
            // Registry services
            ClassRegistry = GetClassRegistry()
        };

        // Use the new emitter for full generator body emission
        var emitter = new GeneratorMoveNextEmitter(smBuilder, analysis, _types);
        emitter.EmitMoveNext(funcStmt.Body, ctx);
    }

    /// <summary>
    /// Emits the body of an instance generator method using a state machine.
    /// Called for class methods marked with IsGenerator = true.
    /// </summary>
    private void EmitGeneratorMethodBody(MethodBuilder methodBuilder, Stmt.Function method, FieldInfo fieldsField)
    {
        // Analyze generator function to determine yield points and hoisted variables
        var analysis = _generators.Analyzer.Analyze(method);

        // Build state machine type for instance method
        var smBuilder = new GeneratorStateMachineBuilder(_moduleBuilder, _types, _generators.StateMachineCounter++);
        smBuilder.DefineStateMachine(
            $"{methodBuilder.DeclaringType!.Name}_{method.Name.Lexeme}",
            analysis,
            isInstanceMethod: true,  // This is an instance method
            runtime: _runtime
        );

        // Emit stub method body (creates state machine and returns it)
        EmitGeneratorInstanceStubMethod(methodBuilder, smBuilder, method.Parameters, analysis);

        // Create context for MoveNext emission
        var il = smBuilder.MoveNextMethod.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functions.Builders, _classes.Builders, _types)
        {
            FieldsField = fieldsField,
            IsInstanceMethod = true,
            ClosureAnalyzer = _closures.Analyzer,
            ArrowMethods = _closures.ArrowMethods,
            DisplayClasses = _closures.DisplayClasses,
            DisplayClassFields = _closures.DisplayClassFields,
            DisplayClassConstructors = _closures.DisplayClassConstructors,
            FunctionRestParams = _functions.RestParams,
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            Runtime = _runtime,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            AsyncMethods = null,
            // Module support for multi-module compilation
            CurrentModulePath = _modules.CurrentPath,
            ClassToModule = _modules.ClassToModule,
            FunctionToModule = _modules.FunctionToModule,
            EnumToModule = _modules.EnumToModule,
            DotNetNamespace = _modules.CurrentDotNetNamespace,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            ClassExprBuilders = _classExprs.Builders,
            IsStrictMode = _isStrictMode || CheckForUseStrict(method.Body),
            // ES2022 Private Class Elements support for generator methods
            CurrentClassName = methodBuilder.DeclaringType?.Name,
            CurrentClassBuilder = methodBuilder.DeclaringType as TypeBuilder,
            // Registry services
            ClassRegistry = GetClassRegistry(),
            // Entry-point display class for captured top-level variables
            EntryPointDisplayClassFields = _closures.EntryPointDisplayClassFields.Count > 0 ? _closures.EntryPointDisplayClassFields : null,
            CapturedTopLevelVars = _closures.CapturedTopLevelVars.Count > 0 ? _closures.CapturedTopLevelVars : null,
            EntryPointDisplayClassStaticField = _closures.EntryPointDisplayClassStaticField
        };

        // Emit MoveNext body
        var moveNextEmitter = new GeneratorMoveNextEmitter(smBuilder, analysis, _types);
        moveNextEmitter.EmitMoveNext(method.Body, ctx);

        // Finalize the state machine type
        smBuilder.CreateType();
    }

    /// <summary>
    /// Emits the stub method that creates and initializes the generator state machine for an instance method.
    /// The stub copies 'this' and parameters to the state machine, then returns it.
    /// </summary>
    private void EmitGeneratorInstanceStubMethod(
        MethodBuilder methodBuilder,
        GeneratorStateMachineBuilder smBuilder,
        List<Stmt.Parameter> parameters,
        GeneratorStateAnalyzer.GeneratorFunctionAnalysis analysis)
    {
        var il = methodBuilder.GetILGenerator();

        // Create new instance of the state machine
        il.Emit(OpCodes.Newobj, smBuilder.Constructor);

        // Copy 'this' to state machine's ThisField if it exists
        if (smBuilder.ThisField != null)
        {
            il.Emit(OpCodes.Dup);  // Keep state machine reference on stack
            il.Emit(OpCodes.Ldarg_0);  // Load 'this' (instance methods have 'this' at arg 0)
            il.Emit(OpCodes.Stfld, smBuilder.ThisField);
        }

        // Get the typed parameter types for the method
        // We need to box value types since state machine fields are object-typed
        string? className = methodBuilder.DeclaringType?.Name;
        string methodName = methodBuilder.Name;
        Type[] paramTypes = className != null
            ? ParameterTypeResolver.ResolveMethodParameters(className, methodName, parameters, _typeMapper, _typeMap)
            : parameters.Select(_ => typeof(object)).ToArray();

        // Copy parameters to state machine fields (instance methods start params at index 1)
        for (int i = 0; i < parameters.Count; i++)
        {
            var paramName = parameters[i].Name.Lexeme;
            var field = smBuilder.GetVariableField(paramName);
            if (field != null)
            {
                il.Emit(OpCodes.Dup);  // Keep state machine reference on stack
                il.Emit(OpCodes.Ldarg, i + 1);  // +1 because 'this' is at index 0

                // Box value types since state machine fields are object-typed
                if (i < paramTypes.Length && paramTypes[i].IsValueType)
                {
                    il.Emit(OpCodes.Box, paramTypes[i]);
                }

                il.Emit(OpCodes.Stfld, field);
            }
        }

        // Copy captured outer scope variables to state machine fields
        foreach (var capturedVar in analysis.CapturedVariables)
        {
            var capturedField = smBuilder.CapturedVariables.GetValueOrDefault(capturedVar);
            if (capturedField == null) continue;

            // Try to load from entry-point display class (captured top-level variables)
            if (_closures.CapturedTopLevelVars.Contains(capturedVar) &&
                _closures.EntryPointDisplayClassFields.TryGetValue(capturedVar, out var entryPointField))
            {
                il.Emit(OpCodes.Dup);  // Keep state machine reference on stack
                if (_closures.EntryPointDisplayClassStaticField != null)
                {
                    il.Emit(OpCodes.Ldsfld, _closures.EntryPointDisplayClassStaticField);
                    il.Emit(OpCodes.Ldfld, entryPointField);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Stfld, capturedField);
            }
            // Try to load from top-level static vars (non-captured module-level variables)
            else if (_topLevelStaticVars.TryGetValue(capturedVar, out var staticField))
            {
                il.Emit(OpCodes.Dup);  // Keep state machine reference on stack
                il.Emit(OpCodes.Ldsfld, staticField);
                il.Emit(OpCodes.Stfld, capturedField);
            }
        }

        // Return the state machine (which implements IEnumerable<object>)
        il.Emit(OpCodes.Ret);
    }
}
