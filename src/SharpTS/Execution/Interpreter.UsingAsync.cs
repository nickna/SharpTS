using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    private readonly Dictionary<RuntimeEnvironment, UsingTracker> _asyncUsingTrackers =
        new(System.Collections.Generic.ReferenceEqualityComparer.Instance);

    private UsingTracker? FindUsingTracker()
    {
        _usingTrackerStack.TryPeek(out var ordinary);
        for (RuntimeEnvironment? scope = _environment; scope is not null; scope = scope.Enclosing)
        {
            if (ordinary is not null && ReferenceEquals(ordinary.OwnerScope, scope)) return ordinary;
            if (_asyncUsingTrackers.TryGetValue(scope, out var tracker)) return tracker;
        }
        return ordinary;
    }

    private static bool IsGuestUsingFailure(Exception exception) => exception is
        ThrowException or SharpTSPromiseRejectedException or
        AggregateException { InnerException: SharpTSPromiseRejectedException };

    private ExecutionResult CombineUsingDisposalFailure(ExecutionResult completion, Exception exception)
    {
        bool guest = IsGuestUsingFailure(exception);
        object? error = TranslateException(exception);
        if (completion.Type != ExecutionResult.ResultType.Throw)
            return ExecutionResult.Throw(error, fromGuestThrow: guest);

        object? pending = completion.Value.ToObject();
        if (!completion.FromGuestThrow) pending = CoerceCaughtValueForBinding(pending);
        if (!guest) error = CoerceCaughtValueForBinding(error);
        return ExecutionResult.Throw(new SharpTSSuppressedError(error, pending));
    }

    private async ValueTask<ExecutionResult> ExecuteUsingHandlerBlockCore(
        IEvaluationContext context, List<Stmt> statements, RuntimeEnvironment environment)
    {
        var tracker = new UsingTracker(this, environment);
        _asyncUsingTrackers.TryGetValue(environment, out var previous);
        _asyncUsingTrackers[environment] = tracker;
        var completion = ExecutionResult.Success();
        try
        {
            using var scope = PushScope(environment);
            ReserveLexicalDeclarations(statements);
            HoistFunctionDeclarations(statements);
            foreach (var statement in statements)
            {
                ThrowIfExecutionCancelled();
                completion = (await context.ExecuteStmtAsync(statement)).UpdateEmpty(completion);
                if (completion.IsAbrupt) break;
            }
        }
        catch (GeneratorReturnException exception)
        {
            completion = ExecutionResult.Return(RuntimeValue.FromBoxed(exception.Value));
        }
        catch (WorkerTerminatedException) { throw; }
        catch (Exception exception)
        {
            completion = ExecutionResult.Throw(TranslateException(exception),
                fromGuestThrow: IsGuestUsingFailure(exception));
        }
        finally
        {
            if (previous is null) _asyncUsingTrackers.Remove(environment);
            else _asyncUsingTrackers[environment] = previous;
            completion = await tracker.DisposeAllAsync(completion);
        }
        return completion;
    }

    private async ValueTask DisposeResourceAsync(
        object? resource, ISharpTSCallable? method, bool isAsync, bool awaitResult)
    {
        var environment = _environment;
        var generator = CurrentAsyncGenerator;
        bool ownsHostTask = false;
        try
        {
            object? result = SharpTSUndefined.Instance;
            if (resource is not null and not SharpTSUndefined)
            {
                if (method is not null)
                    result = FunctionBuiltIns.CallWithThis(this, method, resource, []);
                else if (resource is IDisposable disposable)
                    disposable.Dispose();
                else if (isAsync && resource is IAsyncDisposable asyncDisposable)
                    result = asyncDisposable.DisposeAsync().AsTask();
            }
            _environment = environment;
            CurrentAsyncGenerator = generator;
            if (isAsync)
            {
                // An async-from-sync method's return value is ignored, but its
                // async disposal still has an Await checkpoint.
                if (method is not null && !awaitResult) result = SharpTSUndefined.Instance;
                if (result is Task { IsCompleted: false })
                {
                    Ref();
                    ownsHostTask = true;
                }
                Task<object?> task = result is Task hostTask && result is not Task<object?>
                    ? AwaitUsingHostTask(hostTask) : AwaitAsyncIterationValue(result);
                await AwaitPreservingEnvironment(QueueNativeAwait(task));
            }
        }
        finally
        {
            _environment = environment;
            CurrentAsyncGenerator = generator;
            if (ownsHostTask) Unref();
        }
    }

    private static async Task<object?> AwaitUsingHostTask(Task task)
    {
        await task;
        return SharpTSUndefined.Instance;
    }
}
