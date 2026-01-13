using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    protected override void EmitWhile(Stmt.While w)
    {
        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        // Push labels for break/continue
        _loopLabels.Push((endLabel, continueLabel, null));

        _il.MarkLabel(startLabel);
        EmitExpression(w.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brfalse, endLabel);

        EmitStatement(w.Body);

        _il.MarkLabel(continueLabel);
        _il.Emit(OpCodes.Br, startLabel);

        _il.MarkLabel(endLabel);
        _loopLabels.Pop();
    }

    protected override void EmitForOf(Stmt.ForOf f)
    {
        if (f.IsAsync)
        {
            EmitForAwaitOf(f);
            return;
        }

        string varName = f.Variable.Lexeme;
        var varField = _builder.GetVariableField(varName);

        // Emit iterable
        EmitExpression(f.Iterable);
        EnsureBoxed();

        // Cast to IEnumerable and get enumerator
        var getEnumerator = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;

        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        _il.Emit(OpCodes.Callvirt, getEnumerator);

        var enumLocal = _il.DeclareLocal(typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Stloc, enumLocal);

        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        // Push labels for break/continue
        _loopLabels.Push((endLabel, continueLabel, null));

        _il.MarkLabel(startLabel);
        _il.Emit(OpCodes.Ldloc, enumLocal);
        _il.Emit(OpCodes.Callvirt, moveNext);
        _il.Emit(OpCodes.Brfalse, endLabel);

        // Set loop variable
        if (varField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, enumLocal);
            _il.Emit(OpCodes.Callvirt, current);
            _il.Emit(OpCodes.Stfld, varField);
        }
        else
        {
            var varLocal = _il.DeclareLocal(typeof(object));
            _ctx!.Locals.RegisterLocal(varName, varLocal);
            _il.Emit(OpCodes.Ldloc, enumLocal);
            _il.Emit(OpCodes.Callvirt, current);
            _il.Emit(OpCodes.Stloc, varLocal);
        }

        EmitStatement(f.Body);

        _il.MarkLabel(continueLabel);
        _il.Emit(OpCodes.Br, startLabel);

        _il.MarkLabel(endLabel);
        _loopLabels.Pop();
    }

    private void EmitForAwaitOf(Stmt.ForOf f)
    {
        // for await...of iterates over async iterables
        // First try Symbol.asyncIterator protocol, then fall back to $IAsyncGenerator
        // The result from next() is a promise/task with { value, done } properties

        string varName = f.Variable.Lexeme;
        var varField = _builder.GetVariableField(varName);

        // Emit the async iterable expression
        EmitExpression(f.Iterable);
        EnsureBoxed();

        // Store the iterable
        var iterableLocal = _il.DeclareLocal(_types.Object);
        _il.Emit(OpCodes.Stloc, iterableLocal);

        // Try async iterator protocol: GetIteratorFunction(iterable, Symbol.asyncIterator)
        var asyncIteratorFnLocal = _il.DeclareLocal(_types.Object);
        var asyncGenLabel = _il.DefineLabel();
        var afterLoopLabel = _il.DefineLabel();

        _il.Emit(OpCodes.Ldloc, iterableLocal);
        _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolAsyncIterator);
        _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorFunction);
        _il.Emit(OpCodes.Stloc, asyncIteratorFnLocal);

        // If async iterator function is null, fall back to $IAsyncGenerator
        _il.Emit(OpCodes.Ldloc, asyncIteratorFnLocal);
        _il.Emit(OpCodes.Brfalse, asyncGenLabel);

        // ===== Custom async iterator protocol path =====
        {
            // Call the async iterator function to get the async iterator object
            // Use InvokeMethodValue to properly bind 'this' to the iterable object
            _il.Emit(OpCodes.Ldloc, iterableLocal);           // receiver (this)
            _il.Emit(OpCodes.Ldloc, asyncIteratorFnLocal);    // method
            _il.Emit(OpCodes.Ldc_I4_0);
            _il.Emit(OpCodes.Newarr, _types.Object);          // args
            _il.Emit(OpCodes.Call, _ctx.Runtime.InvokeMethodValue);

            var asyncIteratorLocal = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, asyncIteratorLocal);

            var startLabel = _il.DefineLabel();
            var endLabel = _il.DefineLabel();
            var continueLabel = _il.DefineLabel();

            _loopLabels.Push((endLabel, continueLabel, null));

            _il.MarkLabel(startLabel);

            // Call InvokeIteratorNext(asyncIterator) which returns a Promise/Task
            _il.Emit(OpCodes.Ldloc, asyncIteratorLocal);
            _il.Emit(OpCodes.Call, _ctx.Runtime.InvokeIteratorNext);

            // The result should be a Task/Promise - await it
            // Store as object first, then check if it's a Task
            var nextResultLocal = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, nextResultLocal);

            // Check if result is a Task<object> and await it
            var isTaskLabel = _il.DefineLabel();
            var afterAwaitLabel = _il.DefineLabel();
            var resultLocal = _il.DeclareLocal(_types.Object);

            _il.Emit(OpCodes.Ldloc, nextResultLocal);
            _il.Emit(OpCodes.Isinst, _types.TaskOfObject);
            _il.Emit(OpCodes.Brtrue, isTaskLabel);

            // Not a task - use the result directly (might be a sync iterator result)
            _il.Emit(OpCodes.Ldloc, nextResultLocal);
            _il.Emit(OpCodes.Stloc, resultLocal);
            _il.Emit(OpCodes.Br, afterAwaitLabel);

            // Is a Task - await it
            _il.MarkLabel(isTaskLabel);
            _il.Emit(OpCodes.Ldloc, nextResultLocal);
            _il.Emit(OpCodes.Castclass, _types.TaskOfObject);
            var taskLocal = _il.DeclareLocal(_types.TaskOfObject);
            _il.Emit(OpCodes.Stloc, taskLocal);
            _il.Emit(OpCodes.Ldloc, taskLocal);
            var getAwaiter = _types.GetMethodNoParams(_types.TaskOfObject, "GetAwaiter");
            _il.Emit(OpCodes.Call, getAwaiter);
            var awaiterLocal = _il.DeclareLocal(_types.TaskAwaiterOfObject);
            _il.Emit(OpCodes.Stloc, awaiterLocal);
            _il.Emit(OpCodes.Ldloca, awaiterLocal);
            var getResult = _types.GetMethodNoParams(_types.TaskAwaiterOfObject, "GetResult");
            _il.Emit(OpCodes.Call, getResult);
            _il.Emit(OpCodes.Stloc, resultLocal);

            _il.MarkLabel(afterAwaitLabel);

            // Check if done: use GetIteratorDone
            _il.Emit(OpCodes.Ldloc, resultLocal);
            _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorDone);
            _il.Emit(OpCodes.Brtrue, endLabel);

            // Get value: use GetIteratorValue
            _il.Emit(OpCodes.Ldloc, resultLocal);
            _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorValue);

            // Assign to loop variable
            if (varField != null)
            {
                var valueTemp = _il.DeclareLocal(_types.Object);
                _il.Emit(OpCodes.Stloc, valueTemp);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, valueTemp);
                _il.Emit(OpCodes.Stfld, varField);
            }
            else
            {
                var varLocal = _il.DeclareLocal(_types.Object);
                _ctx.Locals.RegisterLocal(varName, varLocal);
                _il.Emit(OpCodes.Stloc, varLocal);
            }

            EmitStatement(f.Body);

            _il.MarkLabel(continueLabel);
            _il.Emit(OpCodes.Br, startLabel);

            _il.MarkLabel(endLabel);
            _loopLabels.Pop();
            _il.Emit(OpCodes.Br, afterLoopLabel); // Skip the fallback path
        }

        // ===== $IAsyncGenerator fallback path =====
        _il.MarkLabel(asyncGenLabel);
        {
            // Cast to $IAsyncGenerator interface
            var asyncGenInterface = _ctx.Runtime.AsyncGeneratorInterfaceType;
            _il.Emit(OpCodes.Ldloc, iterableLocal);
            _il.Emit(OpCodes.Castclass, asyncGenInterface);

            // Store the async generator in a local
            var asyncGenLocal = _il.DeclareLocal(asyncGenInterface);
            _il.Emit(OpCodes.Stloc, asyncGenLocal);

            var genStartLabel = _il.DefineLabel();
            var genEndLabel = _il.DefineLabel();
            var genContinueLabel = _il.DefineLabel();

            _loopLabels.Push((genEndLabel, genContinueLabel, null));

            _il.MarkLabel(genStartLabel);

            // Call next() which returns Task<object>
            _il.Emit(OpCodes.Ldloc, asyncGenLocal);
            _il.Emit(OpCodes.Callvirt, _ctx.Runtime.AsyncGeneratorNextMethod);

            // Await the Task<object>
            var genTaskLocal = _il.DeclareLocal(_types.TaskOfObject);
            _il.Emit(OpCodes.Stloc, genTaskLocal);
            _il.Emit(OpCodes.Ldloc, genTaskLocal);
            var genGetAwaiter = _types.GetMethodNoParams(_types.TaskOfObject, "GetAwaiter");
            _il.Emit(OpCodes.Call, genGetAwaiter);
            var genAwaiterLocal = _il.DeclareLocal(_types.TaskAwaiterOfObject);
            _il.Emit(OpCodes.Stloc, genAwaiterLocal);
            _il.Emit(OpCodes.Ldloca, genAwaiterLocal);
            var genGetResult = _types.GetMethodNoParams(_types.TaskAwaiterOfObject, "GetResult");
            _il.Emit(OpCodes.Call, genGetResult);

            // Result is a Dictionary<string, object> with { value, done }
            var genResultLocal = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, genResultLocal);

            // Check if done: GetProperty(result, "done")
            _il.Emit(OpCodes.Ldloc, genResultLocal);
            _il.Emit(OpCodes.Ldstr, "done");
            _il.Emit(OpCodes.Call, _ctx.Runtime.GetProperty);

            // Convert to bool and check
            _il.Emit(OpCodes.Call, _ctx.Runtime.IsTruthy);
            _il.Emit(OpCodes.Brtrue, genEndLabel);

            // Get value: GetProperty(result, "value")
            _il.Emit(OpCodes.Ldloc, genResultLocal);
            _il.Emit(OpCodes.Ldstr, "value");
            _il.Emit(OpCodes.Call, _ctx.Runtime.GetProperty);

            // Assign to loop variable
            if (varField != null)
            {
                var genValueTemp = _il.DeclareLocal(_types.Object);
                _il.Emit(OpCodes.Stloc, genValueTemp);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, genValueTemp);
                _il.Emit(OpCodes.Stfld, varField);
            }
            else
            {
                var genVarLocal = _il.DeclareLocal(_types.Object);
                _ctx.Locals.RegisterLocal(varName, genVarLocal);
                _il.Emit(OpCodes.Stloc, genVarLocal);
            }

            EmitStatement(f.Body);

            _il.MarkLabel(genContinueLabel);
            _il.Emit(OpCodes.Br, genStartLabel);

            _il.MarkLabel(genEndLabel);
            _loopLabels.Pop();
        }

        // Common exit point for both paths
        _il.MarkLabel(afterLoopLabel);
    }

    protected override void EmitDoWhile(Stmt.DoWhile dw)
    {
        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        // Push labels for break/continue
        _loopLabels.Push((endLabel, continueLabel, null));

        // Body executes at least once
        _il.MarkLabel(startLabel);
        EmitStatement(dw.Body);

        // Continue target is after the body, before condition check
        _il.MarkLabel(continueLabel);

        // Evaluate condition
        EmitExpression(dw.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brtrue, startLabel);

        _il.MarkLabel(endLabel);
        _loopLabels.Pop();
    }

    protected override void EmitForIn(Stmt.ForIn f)
    {
        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        string varName = f.Variable.Lexeme;
        var varField = _builder.GetVariableField(varName);

        // Evaluate object and get keys
        EmitExpression(f.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetKeys);
        var keysLocal = _il.DeclareLocal(typeof(List<object>));
        _il.Emit(OpCodes.Stloc, keysLocal);

        // Create index variable
        var indexLocal = _il.DeclareLocal(typeof(int));
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Stloc, indexLocal);

        // Loop variable (holds current key)
        LocalBuilder? loopVar = null;
        if (varField == null)
        {
            loopVar = _il.DeclareLocal(typeof(object));
            _ctx!.Locals.RegisterLocal(varName, loopVar);
        }

        // Push labels for break/continue
        _loopLabels.Push((endLabel, continueLabel, null));

        _il.MarkLabel(startLabel);

        // Check if index < keys.Count
        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Ldloc, keysLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetLength);
        _il.Emit(OpCodes.Clt);
        _il.Emit(OpCodes.Brfalse, endLabel);

        // Get current key: keys[index]
        _il.Emit(OpCodes.Ldloc, keysLocal);
        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetElement);

        // Store to loop variable
        if (varField != null)
        {
            var keyTemp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, keyTemp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, keyTemp);
            _il.Emit(OpCodes.Stfld, varField);
        }
        else
        {
            _il.Emit(OpCodes.Stloc, loopVar!);
        }

        // Emit body
        EmitStatement(f.Body);

        _il.MarkLabel(continueLabel);

        // Increment index
        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Stloc, indexLocal);

        _il.Emit(OpCodes.Br, startLabel);

        _il.MarkLabel(endLabel);
        _loopLabels.Pop();
    }
}
