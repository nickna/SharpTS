using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Async generator function compilation for the IL compiler.
/// Handles the definition and emission of async generator state machines.
/// </summary>
public partial class ILCompiler
{
    /// <summary>
    /// Defines an async generator function and its state machine.
    /// </summary>
    private void DefineAsyncGeneratorFunction(Stmt.Function funcStmt)
    {
        string funcName = funcStmt.Name.Lexeme;

        // Analyze the async generator function for yield/await points and hoisted variables
        var analysis = _asyncGeneratorAnalyzer.Analyze(funcStmt);

        // Create the state machine builder
        var smBuilder = new AsyncGeneratorStateMachineBuilder(_moduleBuilder, _types, _asyncGeneratorStateMachineCounter++);
        smBuilder.DefineStateMachine(funcName, analysis, isInstanceMethod: false, runtime: _runtime);

        _asyncGeneratorStateMachines[funcName] = smBuilder;
        _asyncGeneratorFunctions[funcName] = funcStmt;

        // Define the stub method that creates and returns the state machine
        var paramTypes = funcStmt.Parameters.Select(_ => _types.Object).ToArray();
        var methodBuilder = _programType.DefineMethod(
            funcName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.IAsyncEnumerableOfObject,  // Async generator returns IAsyncEnumerable<object>
            paramTypes
        );

        _functionBuilders[funcName] = methodBuilder;

        // Track rest parameter info
        var restParam = funcStmt.Parameters.FirstOrDefault(p => p.IsRest);
        if (restParam != null)
        {
            int restIndex = funcStmt.Parameters.IndexOf(restParam);
            int regularCount = funcStmt.Parameters.Count(p => !p.IsRest);
            _functionRestParams[funcName] = (restIndex, regularCount);
        }
    }

    /// <summary>
    /// Emits all async generator state machine bodies.
    /// Called after all functions have been defined.
    /// </summary>
    private void EmitAsyncGeneratorStateMachineBodies()
    {
        foreach (var (funcName, smBuilder) in _asyncGeneratorStateMachines)
        {
            var funcStmt = _asyncGeneratorFunctions[funcName];
            var methodBuilder = _functionBuilders[funcName];

            // Emit the stub method body (creates and returns the state machine)
            EmitAsyncGeneratorStubMethod(methodBuilder, smBuilder, funcStmt);

            // Emit the MoveNextAsync method body
            EmitAsyncGeneratorMoveNextAsyncBody(smBuilder, funcStmt);

            // Finalize the state machine type
            smBuilder.CreateType();
        }
    }

    /// <summary>
    /// Emits the stub method that creates and initializes the async generator state machine.
    /// </summary>
    private void EmitAsyncGeneratorStubMethod(MethodBuilder methodBuilder, AsyncGeneratorStateMachineBuilder smBuilder, Stmt.Function funcStmt)
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

        // Return the state machine (which implements IAsyncEnumerable<object>)
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the MoveNextAsync method body for an async generator state machine.
    /// Uses AsyncGeneratorMoveNextEmitter to handle full generator body with yield and await expressions.
    /// </summary>
    private void EmitAsyncGeneratorMoveNextAsyncBody(AsyncGeneratorStateMachineBuilder smBuilder, Stmt.Function funcStmt)
    {
        var analysis = _asyncGeneratorAnalyzer.Analyze(funcStmt);

        // Create a compilation context for the state machine
        var il = smBuilder.MoveNextAsyncMethod.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders, _types)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null,
            // Module support for multi-module compilation
            CurrentModulePath = _currentModulePath,
            ClassToModule = _classToModule,
            FunctionToModule = _functionToModule,
            EnumToModule = _enumToModule,
            DotNetNamespace = _currentDotNetNamespace,
            TypeEmitterRegistry = _typeEmitterRegistry
        };

        // Use the new emitter for full async generator body emission
        var emitter = new AsyncGeneratorMoveNextEmitter(smBuilder, analysis, _types);
        emitter.EmitMoveNextAsync(funcStmt.Body, ctx);
    }
}
