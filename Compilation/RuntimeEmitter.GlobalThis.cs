using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits globalThis helper methods (GetProperty, SetProperty).
    /// </summary>
    private void EmitGlobalThisMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitGlobalThisGetProperty(typeBuilder, runtime);
        EmitGlobalThisSetProperty(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static object GlobalThisGetProperty(string name)
    /// Gets a property from globalThis, checking user-assigned properties first,
    /// then delegating to built-ins.
    /// </summary>
    private void EmitGlobalThisGetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GlobalThisGetProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String]
        );
        runtime.GlobalThisGetProperty = method;

        var il = method.GetILGenerator();

        // For now, a simple implementation that returns undefined for unknown properties
        // In a full implementation, this would:
        // 1. Check user-assigned properties stored in a dictionary
        // 2. Check for built-in globals (Math, JSON, etc.)
        // 3. Return undefined if not found

        var selfRefLabel = il.DefineLabel();
        var mathLabel = il.DefineLabel();
        var consoleLabel = il.DefineLabel();
        var processLabel = il.DefineLabel();
        var undefinedPropLabel = il.DefineLabel();
        var nanLabel = il.DefineLabel();
        var infinityLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();

        // Check for "globalThis" (self-reference)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "globalThis");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, selfRefLabel);

        // Check for "undefined"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "undefined");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, undefinedPropLabel);

        // Check for "NaN"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "NaN");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, nanLabel);

        // Check for "Infinity"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "Infinity");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, infinityLabel);

        // Default: return undefined
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Br, returnLabel);

        // Self-reference: return null (marker for globalThis in property access chains)
        il.MarkLabel(selfRefLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, returnLabel);

        // undefined property
        il.MarkLabel(undefinedPropLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Br, returnLabel);

        // NaN property
        il.MarkLabel(nanLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, returnLabel);

        // Infinity property
        il.MarkLabel(infinityLabel);
        il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, returnLabel);

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void GlobalThisSetProperty(string name, object value)
    /// Sets a property on globalThis (user-assigned properties).
    /// </summary>
    private void EmitGlobalThisSetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GlobalThisSetProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.String, _types.Object]
        );
        runtime.GlobalThisSetProperty = method;

        var il = method.GetILGenerator();

        // For now, a no-op implementation
        // In a full implementation, this would store the property in a static dictionary
        // Note: This is intentionally a no-op for compiled code since there's no persistent
        // globalThis object in compiled assemblies. User-assigned properties would be
        // lost between executions anyway.
        il.Emit(OpCodes.Ret);
    }
}
