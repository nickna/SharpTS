using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $PassThrough class for standalone stream support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSPassThrough
/// </summary>
public partial class RuntimeEmitter
{
    private void EmitTSPassThroughClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public class $PassThrough : $Transform
        var typeBuilder = moduleBuilder.DefineType(
            "$PassThrough",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            runtime.TSTransformType  // Extends $Transform
        );
        runtime.TSPassThroughType = typeBuilder;

        // Constructor
        EmitTSPassThroughCtor(typeBuilder, runtime);

        // PassThrough inherits Transform behavior and just passes data unchanged
        // Override Write to push directly to readable side
        EmitTSPassThroughWrite(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    private void EmitTSPassThroughCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        runtime.TSPassThroughCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor ($Transform)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.TSTransformCtor);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSPassThroughWrite(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public override bool Write(object? chunk, object? encoding, object? callback)
        // PassThrough just pushes to readable side unchanged
        // Remove NewSlot to properly override $Duplex.Write (via $Transform)
        var method = typeBuilder.DefineMethod(
            "Write",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Boolean,
            [_types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var returnFalseLabel = il.DefineLabel();
        var callCallbackLabel = il.DefineLabel();

        // if (_destroyed || _writeEnded) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableDestroyedField);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDuplexWriteEndedField);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);

        // Push chunk to readable side
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePush);
        il.Emit(OpCodes.Pop);

        // Call callback if provided
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, callCallbackLabel);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, callCallbackLabel);

        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(callCallbackLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }
}
