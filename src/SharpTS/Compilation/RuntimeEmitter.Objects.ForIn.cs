using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct ForInInputs(EmittedObjectKeysRuntime Keys,
        EmittedObjectPrototypeRuntime Prototypes, EmittedObjectDescriptorRuntime Descriptors,
        EmittedObjectOwnPropertiesRuntime OwnProperties, MethodInfo GetProperty, MethodInfo IsTruthy,
        MethodInfo TypeOf, Type UndefinedType);

    private void EmitForInKeys(TypeBuilder owner, ForInInputs inputs)
    {
        inputs.Keys.ForIn = owner.DefineMethod("GetForInKeys", MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject, [_types.Object]);
        inputs.Keys.ForInEnumerable = owner.DefineMethod("IsForInEnumerable", MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean, [_types.Object, _types.Object]);

        var il = inputs.Keys.ForIn.GetILGenerator();
        var constructor = il.DefineLabel();
        EmitFunctionBranch(il, constructor);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.Keys.Keys);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(constructor);
        var result = il.DeclareLocal(_types.ListOfObject);
        var seen = il.DeclareLocal(_types.HashSetOfObject);
        var current = il.DeclareLocal(_types.Object);
        var names = il.DeclareLocal(_types.ListOfObject);
        var index = il.DeclareLocal(_types.Int32);
        var key = il.DeclareLocal(_types.Object);
        var loop = il.DefineLabel();
        var keyLoop = il.DefineLabel();
        var nextKey = il.DefineLabel();
        var nextPrototype = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.HashSetOfObject));
        il.Emit(OpCodes.Stloc, seen);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, current);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, current);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, current);
        il.Emit(OpCodes.Call, inputs.Keys.Names);
        il.Emit(OpCodes.Stloc, names);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);
        il.MarkLabel(keyLoop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, names);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, nextPrototype);
        il.Emit(OpCodes.Ldloc, names);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, key);
        il.Emit(OpCodes.Ldloc, seen);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.HashSetOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Brfalse, nextKey);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Call, inputs.Keys.ForInEnumerable);
        il.Emit(OpCodes.Brfalse, nextKey);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.MarkLabel(nextKey);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, keyLoop);
        il.MarkLabel(nextPrototype);
        il.Emit(OpCodes.Ldloc, current);
        il.Emit(OpCodes.Call, inputs.Prototypes.GetPrototypeOf);
        il.Emit(OpCodes.Stloc, current);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);

        il = inputs.Keys.ForInEnumerable.GetILGenerator();
        constructor = il.DefineLabel();
        EmitFunctionBranch(il, constructor);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, inputs.OwnProperties.IsEnumerable);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(constructor);
        current = il.DeclareLocal(_types.Object);
        var descriptor = il.DeclareLocal(_types.Object);
        loop = il.DefineLabel();
        nextPrototype = il.DefineLabel();
        done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, current);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, current);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, current);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, inputs.Descriptors.GetOwnPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descriptor);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Brfalse, nextPrototype);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, nextPrototype);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Ldstr, "enumerable");
        il.Emit(OpCodes.Call, inputs.GetProperty);
        il.Emit(OpCodes.Call, inputs.IsTruthy);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nextPrototype);
        il.Emit(OpCodes.Ldloc, current);
        il.Emit(OpCodes.Call, inputs.Prototypes.GetPrototypeOf);
        il.Emit(OpCodes.Stloc, current);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        void EmitFunctionBranch(ILGenerator body, Label target)
        {
            body.Emit(OpCodes.Ldarg_0);
            body.Emit(OpCodes.Call, inputs.TypeOf);
            body.Emit(OpCodes.Ldstr, "function");
            body.Emit(OpCodes.Call, _types.StringOpEquality);
            body.Emit(OpCodes.Brtrue, target);
        }
    }
}
