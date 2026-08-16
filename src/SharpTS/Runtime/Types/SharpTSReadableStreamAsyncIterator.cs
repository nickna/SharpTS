using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Async iterator wrapper that makes a <see cref="SharpTSReadableStream"/>
/// usable with <c>for await (const chunk of rs)</c>. Matches Node 18+
/// behaviour where <c>ReadableStream</c> implements the async iterable
/// protocol by wrapping its default reader.
/// </summary>
/// <remarks>
/// Registered as the return value of <see cref="SharpTSReadableStream"/>'s
/// <c>Symbol.asyncIterator</c> lookup path; the interpreter's
/// <c>TryGetAsyncIterator</c> special-cases this shape alongside its
/// <see cref="SharpTSObject"/> / <see cref="SharpTSInstance"/> branches.
///
/// Lifecycle:
/// <list type="bullet">
///   <item><c>[Symbol.asyncIterator]()</c> returns <c>this</c> (self-iterable).</item>
///   <item><c>next()</c> delegates to the stream reader's <c>read()</c>,
///     returning the same <c>{value, done}</c> shape.</item>
///   <item><c>return()</c> releases the reader lock so the stream becomes
///     unlocked again — matches the spec "release reader" step when
///     for-await exits via <c>break</c>/<c>return</c>/throw.</item>
/// </list>
/// </remarks>
public class SharpTSReadableStreamAsyncIterator : SharpTSObject
{
    public override TypeCategory RuntimeCategory => TypeCategory.AsyncGenerator;

    private readonly SharpTSReadableStream _stream;
    private readonly SharpTSReadableStreamDefaultReader _reader;
    private bool _done;

    public SharpTSReadableStreamAsyncIterator(SharpTSReadableStream stream, SharpTSReadableStreamDefaultReader reader)
        : base(new Dictionary<string, object?>())
    {
        _stream = stream;
        _reader = reader;

        // Self-iterable.
        SetBySymbol(SharpTSSymbol.AsyncIterator,
            BuiltInMethod.CreateV2("[Symbol.asyncIterator]", 0, (_, _, _) => RuntimeValue.FromObject(this)));

        SetProperty("next", BuiltInMethod.CreateV2("next", 0, (interp, _, _) =>
        {
            if (_done)
            {
                return RuntimeValue.FromObject(Task.FromResult<object?>(new Dictionary<string, object?>
                {
                    ["value"] = SharpTSUndefined.Instance,
                    ["done"] = (object)true,
                }));
            }
            var readTask = _stream.ReadInternal();
            // Wrap in a continuation that flips _done when the result is done:true.
            // No ConfigureAwait(false) on the parked read: the resume must ride the
            // interpreter's SynchronizationContext so the producer's TrySetResult
            // posts it into the event loop's callback queue synchronously —
            // a thread-pool resume is invisible to the loop's exit check and the
            // program can exit before the loop body runs (#1211's web-stream twin).
            async Task<object?> AwaitAndObserve()
            {
                bool parked = !readTask.IsCompleted;
                var result = await readTask;
                if (parked) EventLoopTestHooks.ParkedResumeDelay();
                if (result is IDictionary<string, object?> dict &&
                    dict.TryGetValue("done", out var d) && d is bool db && db)
                {
                    _done = true;
                }
                return result;
            }
            return RuntimeValue.FromObject(AwaitAndObserve());
        }));

        SetProperty("return", BuiltInMethod.CreateV2("return", 0, 1, (_, _, args) =>
        {
            _done = true;
            if (_stream.Reader == _reader)
            {
                _stream.Reader = null;
            }
            var returnValue = args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance;
            return RuntimeValue.FromObject(Task.FromResult<object?>(new Dictionary<string, object?>
            {
                ["value"] = returnValue,
                ["done"] = (object)true,
            }));
        }));
    }
}
