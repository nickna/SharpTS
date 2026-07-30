using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the <c>$DataCloneError</c> exception type: a standalone (pure-IL, no
/// SharpTS.dll dependency) marker exception thrown by the emitted structured-clone
/// core (<see cref="RuntimeEmitter.EmitStructuredCloneHelper"/>) for values the HTML
/// structured clone algorithm cannot clone (functions, symbols, class instances,
/// Promises, and any value nested inside an object/array/map/set), mirroring
/// <see cref="SharpTS.Runtime.Types.StructuredClone.DataCloneError"/> (#1255).
/// </summary>
public partial class RuntimeEmitter
{
    private void EmitTSDataCloneErrorType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$DataCloneError",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Exception
        );
        runtime.TSDataCloneErrorType = typeBuilder;

        // public $DataCloneError(string message) : base("DataCloneError: " + message)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        runtime.TSDataCloneErrorCtor = ctor;

        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "DataCloneError: ");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.StringConcat2);
        il.Emit(OpCodes.Call, _types.GetConstructor(_types.Exception, [_types.String])!);
        il.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }
}
