using SharpTS.Execution;
using SharpTS.Parsing;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Array-like storage for a function's arguments object.
/// </summary>
/// <remarks>
/// Reuses <see cref="SharpTSArray"/>'s indexed-property implementation, while
/// retaining the distinct ECMAScript identity needed by Object.prototype.toString,
/// Array.isArray, and instanceof Array.
/// </remarks>
public sealed class SharpTSArguments : SharpTSArray
{
    private RuntimeEnvironment? _parameterEnvironment;
    private Dictionary<long, Token>? _parameterMap;

    public SharpTSArguments(IEnumerable<object?> arguments) : base(arguments)
    {
        // Arguments length is an ordinary own property, independent of indexed storage.
        DefineProperty("length", new SharpTSPropertyDescriptor(
            (double)LongLength, writable: true, enumerable: false, configurable: true));
    }

    internal static SharpTSArguments Create(IEnumerable<object?> arguments, Interpreter interpreter,
        RuntimeEnvironment environment, List<Stmt.Parameter> parameters, List<Stmt>? body)
    {
        var result = new SharpTSArguments(arguments);
        result.DefineProperty(SharpTSSymbol.Iterator,
            SharpTSPropertyDescriptor.ForMethod(interpreter.GetArrayPrototype().GetIntrinsicValuesMethod()));
        if (!environment.IsStrictMode && !environment.RequiresUnmappedArguments
            && body is not null && !ParameterBinder.HasUseStrict(body))
        {
            foreach (var (index, name) in ParameterBinder.MappedArgumentParameters(parameters, body))
            {
                if (index >= result.LongLength) continue;
                (result._parameterMap ??= [])[index] = name;
                result._parameterEnvironment = environment;
            }
        }
        return result;
    }

    internal bool TryGetMappedArgument(long index, out object? value)
    {
        if (_parameterMap?.TryGetValue(index, out var name) == true)
        {
            value = _parameterEnvironment!.Get(name).ToObject();
            return true;
        }
        value = null;
        return false;
    }

    internal void SetMappedArgument(long index, object? value)
    {
        if (_parameterMap?.TryGetValue(index, out var name) == true)
            _parameterEnvironment!.Assign(name, value);
    }

    internal void UnmapArgument(long index)
    {
        if (_parameterMap?.Remove(index) == true && _parameterMap.Count == 0)
        {
            _parameterMap = null;
            _parameterEnvironment = null;
        }
    }

    internal void SetMappedArgumentsIntegrity(bool freeze)
    {
        if (_parameterMap is null) return;
        foreach (long index in _parameterMap.Keys.ToArray())
        {
            var descriptor = new SharpTSPropertyDescriptor
            {
                HasConfigurable = true,
                Configurable = false,
                HasWritable = freeze,
                Writable = false
            };
            DefineProperty(index.ToString(System.Globalization.CultureInfo.InvariantCulture), descriptor);
        }
    }
}
