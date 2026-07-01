using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton Readable stream for process.stdin.
/// Reads from Console.In on a background thread and emits 'data'/'end' events.
/// </summary>
public class SharpTSStdin : SharpTSReadable
{
    public static readonly SharpTSStdin Instance = new();

    private Thread? _readerThread;
    private volatile bool _reading;
    private readonly object _startLock = new();

    private SharpTSStdin() { }

    /// <summary>
    /// Returns true if stdin is connected to a terminal (not redirected).
    /// Exposed as a C# property for compiled mode PascalCase property lookup.
    /// </summary>
    public bool IsTTY => !Console.IsInputRedirected;

    /// <summary>
    /// Gets a member by name, adding stdin-specific properties on top of Readable.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "isTTY" => IsTTY,
            // Override 'resume' to start the background reader
            "resume" => BuiltInMethod.CreateV2("resume", 0, ResumeWithReader),
            // Override 'on'/'addListener' to start the reader when 'data' listener is added
            "on" or "addListener" => BuiltInMethod.CreateV2(name, 2, OnWithReader),
            "once" => BuiltInMethod.CreateV2("once", 2, OnceWithReader),
            // process.stdin never destroys — no-op to protect singleton state
            "destroy" => BuiltInMethod.CreateV2("destroy", 0, 1, (_, _, _) => RuntimeValue.FromObject(this)),
            _ => base.GetMember(name)
        };
    }

    private RuntimeValue OnWithReader(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var eventName = args.Length > 0 ? args[0].ToObject()?.ToString() : null;

        // Delegate to base Readable's on (which handles flowing mode)
        var baseOn = base.GetMember("on") as BuiltInMethod;
        baseOn?.Bind(this).CallV2(interpreter, args);

        if (eventName == "data" || eventName == "readable")
        {
            StartReaderThread(interpreter);
        }

        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue OnceWithReader(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var eventName = args.Length > 0 ? args[0].ToObject()?.ToString() : null;

        var baseOnce = base.GetMember("once") as BuiltInMethod;
        baseOnce?.Bind(this).CallV2(interpreter, args);

        if (eventName == "data" || eventName == "readable")
        {
            StartReaderThread(interpreter);
        }

        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue ResumeWithReader(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var baseResume = base.GetMember("resume") as BuiltInMethod;
        baseResume?.Bind(this).CallV2(interpreter, args);
        StartReaderThread(interpreter);
        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Starts the background thread that reads from Console.In and pushes data into the stream.
    /// </summary>
    private void StartReaderThread(Interp interpreter)
    {
        lock (_startLock)
        {
            if (_reading) return;
            _reading = true;
        }

        _readerThread = new Thread(() => ReadLoop(interpreter))
        {
            IsBackground = true,
            Name = "SharpTS-stdin-reader"
        };
        _readerThread.Start();
    }

    private void ReadLoop(Interp interpreter)
    {
        try
        {
            string? line;
            while ((line = Console.ReadLine()) != null)
            {
                // Push data with newline (Console.ReadLine strips it, but data events include it)
                PushFromExternal(interpreter, line + "\n");
            }
            // EOF reached — push null to signal end
            PushFromExternal(interpreter, null);
        }
        catch (Exception)
        {
            // Stream destroyed or interrupted — signal end
            PushFromExternal(interpreter, null);
        }
    }

    /// <summary>
    /// Pushes data from the background reader thread into the Readable stream.
    /// </summary>
    internal void PushFromExternal(Interp interpreter, object? chunk)
    {
        var pushMethod = base.GetMember("push") as BuiltInMethod;
        pushMethod?.Bind(this).Call(interpreter, [chunk]);
    }

    /// <summary>
    /// Resets the stdin singleton state, including the background reader thread flag.
    /// Called from ResetReadableState (via Interpreter.Dispose).
    /// </summary>
    internal new void ResetReadableState()
    {
        _reading = false;
        // Don't abort the reader thread — it's a background thread and will die with the process.
        // Just reset the flag so a new interpreter run can start a fresh reader.
        base.ResetReadableState();
    }

    public override string ToString() => "[object stdin]";
}
