using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // Stack contract is identical to Type.GetField(string, BindingFlags).
    // The ordinary emitter retains the exact original instruction.
    private void EmitStaticFieldLookup(ILGenerator il)
    {
        if (_nativeStaticFieldLookup is { } lookup) il.Emit(OpCodes.Call, lookup);
        else il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", _types.String, typeof(BindingFlags)));
    }
}
