using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Callback for Promise resolve function passed to the executor.
/// Implements ISharpTSCallable so it can be called from TypeScript code.
/// </summary>
public class PromiseResolveCallback : ISharpTSFunctionObject, IBuiltInFunctionMetadata
{
    private readonly Action<object?> _resolve;
    private SharpTSObject? _properties;
    private SharpTSObject OwnPropertyStorage => _properties ??= FunctionObjectProperties.Create(FunctionName, Arity());
    SharpTSObject ISharpTSFunctionObject.OwnProperties => OwnPropertyStorage;

    public PromiseResolveCallback(Action<object?> resolve)
    {
        _resolve = resolve;
    }

    public int Arity() => 1;
    public string FunctionName => "";
    public bool HasMetadataProperty(string name)
        => name is "name" or "length"
            && OwnPropertyStorage.GetOwnPropertyDescriptor(name) is not null;

    public bool DeleteMetadataProperty(string name)
    {
        if (name is not ("name" or "length")) return true;
        return OwnPropertyStorage.DeletePropertyStrict(name, false);
    }

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        var value = arguments.Count > 0 ? arguments[0] : null;
        _resolve(value);
        return SharpTSUndefined.Instance;
    }
}

/// <summary>
/// Callback for Promise reject function passed to the executor.
/// Implements ISharpTSCallable so it can be called from TypeScript code.
/// </summary>
public class PromiseRejectCallback : ISharpTSFunctionObject, IBuiltInFunctionMetadata
{
    private readonly Action<object?> _reject;
    private SharpTSObject? _properties;
    private SharpTSObject OwnPropertyStorage => _properties ??= FunctionObjectProperties.Create(FunctionName, Arity());
    SharpTSObject ISharpTSFunctionObject.OwnProperties => OwnPropertyStorage;

    public PromiseRejectCallback(Action<object?> reject)
    {
        _reject = reject;
    }

    public int Arity() => 1;
    public string FunctionName => "";
    public bool HasMetadataProperty(string name)
        => name is "name" or "length"
            && OwnPropertyStorage.GetOwnPropertyDescriptor(name) is not null;

    public bool DeleteMetadataProperty(string name)
    {
        if (name is not ("name" or "length")) return true;
        return OwnPropertyStorage.DeletePropertyStrict(name, false);
    }

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        var reason = arguments.Count > 0 ? arguments[0] : null;
        _reject(reason);
        return SharpTSUndefined.Instance;
    }
}
