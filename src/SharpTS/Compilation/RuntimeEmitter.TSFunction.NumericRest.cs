using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitNumericRest4Attribute(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var type = moduleBuilder.DefineType("$NumericRest4",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, typeof(Attribute));
        var name = type.DefineField("Entry", _types.String, FieldAttributes.Public | FieldAttributes.InitOnly);
        var ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [_types.String]);
        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Attribute).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, name);
        il.Emit(OpCodes.Ret);
        runtime.NumericRest4AttrType = type;
        runtime.NumericRest4AttrCtor = ctor;
        runtime.NumericRest4AttrValueField = name;
        type.CreateType();
    }

    // Both callable constructors bind the capability to their actual MethodInfo.
    // Ordinary wrappers keep a null field and allocate no delegate or attribute.
    private void EmitComputeNumericRest4(ILGenerator il, FieldBuilder entry, EmittedRuntime runtime)
    {
        var done = il.DefineLabel();
        var name = il.DeclareLocal(_types.String);
        var companion = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldarg_1); // Captured/instance target: never eligible.
        il.Emit(OpCodes.Brtrue, done);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldtoken, runtime.NumericRest4AttrType);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "IsDefined", _types.Type, _types.Boolean));
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldtoken, runtime.NumericRest4AttrType);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "GetCustomAttributes", _types.Type, _types.Boolean));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, runtime.NumericRest4AttrType);
        il.Emit(OpCodes.Ldfld, runtime.NumericRest4AttrValueField);
        il.Emit(OpCodes.Stloc, name);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.MethodInfo, "DeclaringType"));
        il.Emit(OpCodes.Ldloc, name);
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Static | BindingFlags.NonPublic));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags)));
        il.Emit(OpCodes.Stloc, companion);
        il.Emit(OpCodes.Ldloc, companion);
        il.Emit(OpCodes.Brfalse, done); // Metadata-rewritten output can omit it.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, companion);
        il.Emit(OpCodes.Ldtoken, entry.FieldType);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        // Bind the already allocated wrapper to the unused first parameter.
        // This avoids the open-static floating-point shuffle thunk and needs
        // no extra target object. The entry forwards to the scalar companion.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "CreateDelegate", _types.Type, _types.Object));
        il.Emit(OpCodes.Castclass, entry.FieldType);
        il.Emit(OpCodes.Stfld, entry);
        il.MarkLabel(done);
    }
}
