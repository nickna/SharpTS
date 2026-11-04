using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    private Dictionary<string, SharpTSObject>? _typedArrayPrototypes;
    private SharpTSObject? _typedArrayPrototype;

    internal SharpTSObject GetTypedArrayPrototype(string typeName)
    {
        _typedArrayPrototypes ??= new(StringComparer.Ordinal);
        if (_typedArrayPrototypes.TryGetValue(typeName, out var prototype)) return prototype;
        _typedArrayPrototype ??= new SharpTSObject([]) { Prototype = GetObjectPrototype() };
        prototype = new SharpTSObject([]) { Prototype = _typedArrayPrototype };
        prototype.DefineProperty("constructor", new SharpTSPropertyDescriptor(
            _globalConstants[typeName], writable: true, enumerable: false, configurable: true));
        _typedArrayPrototypes.Add(typeName, prototype);
        return prototype;
    }

    private bool TryGetTypedArrayConstructorPrototype(object constructor, out SharpTSObject prototype)
    {
        foreach (var name in BuiltInNames.TypedArrayNames)
        {
            if (!ReferenceEquals(_globalConstants[name], constructor)) continue;
            prototype = GetTypedArrayPrototype(name);
            return true;
        }
        prototype = null!;
        return false;
    }
}
