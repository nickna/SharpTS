using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    /// <summary>
    /// Emits a Promise static method call (resolve, reject, all, race, allSettled, any).
    /// Returns Task&lt;object?&gt; on the stack - does NOT synchronously await.
    /// The await is handled by the async state machine's EmitAwait.
    /// </summary>
    private void EmitPromiseStaticCall(string methodName, List<Expr> arguments)
    {
        switch (methodName)
        {
            case "resolve":
                // Promise.resolve(value?)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseResolve);
                SetStackUnknown();
                return;

            case "reject":
                // Promise.reject(reason)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseReject);
                SetStackUnknown();
                return;

            case "all":
                // Promise.all(iterable)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseAll);
                SetStackUnknown();
                return;

            case "race":
                // Promise.race(iterable)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseRace);
                SetStackUnknown();
                return;

            case "allSettled":
                // Promise.allSettled(iterable)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseAllSettled);
                SetStackUnknown();
                return;

            case "any":
                // Promise.any(iterable)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseAny);
                SetStackUnknown();
                return;

            default:
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                return;
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
        EnsureBoxed();

        // Cast to Task<object?> if needed
        _il.Emit(OpCodes.Castclass, typeof(Task<object?>));

        switch (methodName)
        {
            case "then":
                // promise.then(onFulfilled?, onRejected?)
                // PromiseThen(Task<object?> promise, object? onFulfilled, object? onRejected)

                // onFulfilled callback (optional)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }

                // onRejected callback (optional)
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }

                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseThen);
                break;

            case "catch":
                // promise.catch(onRejected)
                // PromiseCatch(Task<object?> promise, object? onRejected)

                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }

                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseCatch);
                break;

            case "finally":
                // promise.finally(onFinally)
                // PromiseFinally(Task<object?> promise, object? onFinally)

                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }

                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseFinally);
                break;

            default:
                // Unknown method - just return the promise unchanged
                break;
        }

        SetStackUnknown();
    }
}
