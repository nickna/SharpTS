using System.Threading.Tasks;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Async iterator wrapper that makes a <see cref="SharpTSReadable"/> (and its
/// <see cref="SharpTSDuplex"/> / <see cref="SharpTSTransform"/> subclasses) usable with
/// <c>for await (const chunk of readable)</c>. Mirrors Node's
/// <c>Readable.prototype[Symbol.asyncIterator]</c> (#1024).
/// </summary>
/// <remarks>
/// Registered as the return value of <see cref="SharpTSReadable"/>'s
/// <c>Symbol.asyncIterator</c> lookup path; the interpreter's <c>TryGetAsyncIterator</c>
/// special-cases <see cref="SharpTSReadable"/> alongside its <see cref="SharpTSReadableStream"/>
/// branch.
///
/// Lifecycle:
/// <list type="bullet">
///   <item><c>[Symbol.asyncIterator]()</c> returns <c>this</c> (self-iterable).</item>
///   <item><c>next()</c> delegates to <see cref="SharpTSReadable.IterNextAsync"/>, yielding one
///     buffered chunk at a time and settling <c>done:true</c> on <c>end</c>, or rejecting on
///     <c>error</c>. A pull against an empty-but-live stream parks until the next push.</item>
///   <item><c>return()</c> destroys the stream — matches the spec "AsyncIteratorClose" step when
///     the loop exits via <c>break</c>/<c>return</c>/throw.</item>
/// </list>
/// </remarks>
public class SharpTSReadableAsyncIterator : SharpTSObject
{
    public override TypeCategory RuntimeCategory => TypeCategory.AsyncGenerator;

    private readonly SharpTSReadable _stream;
    private bool _done;

    public SharpTSReadableAsyncIterator(SharpTSReadable stream)
        : base(new Dictionary<string, object?>())
    {
        _stream = stream;

        // Self-iterable.
        SetBySymbol(SharpTSSymbol.AsyncIterator,
            BuiltInMethod.CreateV2("[Symbol.asyncIterator]", 0, (_, _, _) => RuntimeValue.FromObject(this)));

        SetProperty("next", BuiltInMethod.CreateV2("next", 0, (_, _, _) =>
        {
            if (_done)
            {
                return RuntimeValue.FromObject(Task.FromResult<object?>(new Dictionary<string, object?>
                {
                    ["value"] = SharpTSUndefined.Instance,
                    ["done"] = (object)true,
                }));
            }

            var pull = _stream.IterNextAsync();
            // Observe a done:true result so subsequent next() calls short-circuit.
            // No ConfigureAwait(false) on the parked pull: the resume must ride the
            // interpreter's SynchronizationContext so the producer's TrySetResult
            // posts it into the event loop's callback queue synchronously. A
            // thread-pool resume is invisible to the loop's exit check, so a
            // producer that settles the pull and drops its last timer handle in
            // the same tick (setInterval's final push(null)+clearInterval) let the
            // program exit before the loop body ran (#1211).
            async Task<object?> AwaitAndObserve()
            {
                bool parked = !pull.IsCompleted;
                var result = await pull;
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
            _stream.IterReturn();
            var returnValue = args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance;
            return RuntimeValue.FromObject(Task.FromResult<object?>(new Dictionary<string, object?>
            {
                ["value"] = returnValue,
                ["done"] = (object)true,
            }));
        }));
    }
}
