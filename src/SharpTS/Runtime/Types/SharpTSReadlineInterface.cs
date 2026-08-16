using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible readline Interface object.
/// Extends SharpTSEventEmitter for event-driven patterns (line, close, pause, resume events).
/// </summary>
public class SharpTSReadlineInterface : SharpTSEventEmitter
{
    private bool _closed;
    private bool _paused;
    private string _prompt = "> ";
    private TextReader? _input;
    private TextWriter? _output;

    /// <summary>
    /// Creates a new readline interface with optional input/output streams.
    /// </summary>
    public SharpTSReadlineInterface(SharpTSObject? options = null)
    {
        _closed = false;
        _paused = false;
        _input = Console.In;
        _output = Console.Out;

        if (options != null)
        {
            if (options.GetProperty("prompt") is string p)
                _prompt = p;
        }
    }

    /// <summary>
    /// Gets a member of this interface object.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "question" => BuiltInMethod.CreateV2("question", 2, Question),
            "close" => BuiltInMethod.CreateV2("close", 0, CloseInterface),
            "prompt" => BuiltInMethod.CreateV2("prompt", 0, 1, PromptMethod),
            "pause" => BuiltInMethod.CreateV2("pause", 0, Pause),
            "resume" => BuiltInMethod.CreateV2("resume", 0, Resume),
            "write" => BuiltInMethod.CreateV2("write", 1, WriteMethod),
            "setPrompt" => BuiltInMethod.CreateV2("setPrompt", 1, SetPrompt),
            "getPrompt" => BuiltInMethod.CreateV2("getPrompt", 0, GetPrompt),
            // EventEmitter methods
            _ => base.GetMember(name)
        };
    }

    private RuntimeValue Question(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_closed || args.Length < 2)
            return RuntimeValue.Null;

        var query = args[0].ToObject()?.ToString() ?? "";
        var callback = args[1].ToObject();

        _output?.Write(query);
        var answer = _input?.ReadLine() ?? "";

        if (callback is ISharpTSCallable callable)
        {
            callable.Call(interpreter, [answer]);
        }

        return RuntimeValue.Null;
    }

    private RuntimeValue CloseInterface(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_closed) return RuntimeValue.FromObject(this);
        _closed = true;

        EmitEvent(interpreter, "close", []);
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue PromptMethod(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (!_closed)
        {
            _output?.Write(_prompt);
        }
        return RuntimeValue.Null;
    }

    private RuntimeValue Pause(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (!_paused)
        {
            _paused = true;
            EmitEvent(interpreter, "pause", []);
        }
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue Resume(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_paused)
        {
            _paused = false;
            EmitEvent(interpreter, "resume", []);
        }
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue WriteMethod(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (!_closed && args.Length > 0)
        {
            _output?.Write(args[0].ToObject()?.ToString() ?? "");
        }
        return RuntimeValue.Null;
    }

    private RuntimeValue SetPrompt(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length > 0)
        {
            _prompt = args[0].ToObject()?.ToString() ?? "";
        }
        return RuntimeValue.Null;
    }

    private RuntimeValue GetPrompt(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        return RuntimeValue.FromString(_prompt);
    }

    public override string ToString() => "Interface {}";
}
