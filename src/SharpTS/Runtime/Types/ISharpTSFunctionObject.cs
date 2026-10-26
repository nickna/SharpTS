using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SharpTS.Runtime.Types;

/// <summary>Own property storage for interpreter callable objects.</summary>
internal interface ISharpTSFunctionObject : ISharpTSCallable
{
    SharpTSObject OwnProperties { get; }
}

internal static class FunctionObjectProperties
{
    private sealed class PrototypeState
    {
        internal object? Prototype;
    }

    // Receiver views share their original function's own-property storage.
    // Keep internal prototype state separate from that object's visible keys.
    private static readonly ConditionalWeakTable<SharpTSObject, PrototypeState> Prototypes = new();

    internal static bool TryGetStorage([NotNullWhen(true)] object? function, Interpreter? interpreter, out SharpTSObject storage)
    {
        storage = function switch
        {
            SharpTSFunction declaration => declaration.OwnPropertyStorage,
            SharpTSArrowFunction expression => expression.OwnPropertyStorage,
            BoundFunction bound => bound.OwnPropertyStorage,
            ISharpTSFunctionObject value => interpreter is null ? value.OwnProperties : GetProperties(value, interpreter),
            _ => null!
        };
        return storage is not null;
    }

    internal static bool TryGetExplicitPrototype(object? function, Interpreter? interpreter, out object? prototype)
    {
        if (TryGetStorage(function, interpreter, out var storage)
            && Prototypes.TryGetValue(storage, out var state))
        {
            prototype = state.Prototype;
            return true;
        }
        prototype = null;
        return false;
    }

    internal static void SetExplicitPrototype(SharpTSObject storage, object? prototype)
        => Prototypes.GetValue(storage, static _ => new PrototypeState()).Prototype = prototype;

    internal static SharpTSObject GetProperties(ISharpTSFunctionObject function, Interpreter interpreter)
        => function is ISharpTSGeneratorFunctionValue generator
            ? generator.FunctionProperties.GetProperties(interpreter)
            : function.OwnProperties;

    internal static SharpTSObject Create(string name, double length)
    {
        var properties = new SharpTSObject([]);
        properties.DefineProperty("length", new SharpTSPropertyDescriptor(
            (double)length, writable: false, enumerable: false, configurable: true));
        properties.DefineProperty("name", new SharpTSPropertyDescriptor(
            name, writable: false, enumerable: false, configurable: true));
        return properties;
    }
}
