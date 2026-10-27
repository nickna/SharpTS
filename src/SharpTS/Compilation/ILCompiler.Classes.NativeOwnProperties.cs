using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private readonly Dictionary<TypeBuilder, Stmt.Class> _nativeOwnSourceDeclarations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypeBuilder, (MethodBuilder Read, MethodBuilder Keys, MethodBuilder Prototype, FieldBuilder InitialPrototype)>
        _nativeOwnAdapters = new(ReferenceEqualityComparer.Instance);

    private static string NativeOwnFieldIdentity(GuestDataField field) => field.DeclaringType.FullName + "\0" + field.SourceName;

    private Label EmitNativeOwnReceiverGuard(ILGenerator il)
    {
        var legacy = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _runtime.GuestOwnProperties!.IsReceiver);
        il.Emit(OpCodes.Brfalse, legacy); return legacy;
    }

    private void DefineNativeOwnPropertyAdapter(TypeBuilder owner, string className)
    {
        var own = _runtime.GuestOwnProperties!;
        var read = owner.DefineMethod("$ReadOwnDescriptor", MethodAttributes.Private, own.DescriptorType, [_types.Object]);
        var keys = owner.DefineMethod("$OwnPropertyKeys", MethodAttributes.Private, _types.ListOfObject, Type.EmptyTypes);
        var prototype = owner.DefineMethod("$InitialOwnPrototype", MethodAttributes.Private, _types.Object, Type.EmptyTypes);
        var initial = owner.DefineField("$InitialOwnPrototypeValue", _types.Object, FieldAttributes.Private);
        _nativeOwnAdapters.Add(owner, (read, keys, prototype, initial));
        GetClassRegistry().NativeOwnProperties!.Register(owner, read, keys, prototype);

        var il = keys.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, own.SnapshotKeys); il.Emit(OpCodes.Ret);
        il = prototype.GetILGenerator(); il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, EmitterTypeHelpers.SelfFieldReference(initial)); il.Emit(OpCodes.Ret);
        EmitNativeOwnDescriptorBody(read, className);
    }

    private void EmitNativeOwnDescriptorBody(MethodBuilder method, string className)
    {
        var own = _runtime.GuestOwnProperties!; var storage = _runtime.DescriptorStorage;
        var owner = (TypeBuilder)method.DeclaringType!;
        var il = method.GetILGenerator(); var absent = il.DefineLabel(); var named = il.DefineLabel();
        var descriptor = il.DeclareLocal(storage.DescriptorType); var key = il.DeclareLocal(_types.String);
        var value = il.DeclareLocal(_types.Object); var active = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Isinst, _types.String); il.Emit(OpCodes.Stloc, key);
        il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Brtrue, named);
        // Symbols use their current full descriptor, without invoking its accessor.
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, _runtime.Symbols.GetStorage);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloca, value);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
        il.Emit(OpCodes.Brfalse, absent);
        il.Emit(OpCodes.Ldloc, value); il.Emit(OpCodes.Isinst, storage.DescriptorType); il.Emit(OpCodes.Stloc, descriptor);
        var synthesize = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Brfalse, synthesize);
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Ret);
        il.MarkLabel(named);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Call, storage.GetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descriptor); var noPds = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Brfalse, noPds);
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Ret);
        il.MarkLabel(noPds);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Call, own.HasOwn); il.Emit(OpCodes.Brfalse, absent);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Call, own.ActiveSourceField); il.Emit(OpCodes.Stloc, active);
        foreach (var field in GetClassRegistry().GetOwnGuestDataFields(owner))
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, active); il.Emit(OpCodes.Ldstr, NativeOwnFieldIdentity(field));
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", _types.Object, _types.Object)); il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(field.GuestGetter));
            il.Emit(OpCodes.Stloc, value); il.Emit(OpCodes.Br, synthesize); il.MarkLabel(next);
        }
        var fields = il.DeclareLocal(_types.DictionaryStringObject);
        EmitLoadDynamicFieldsOrBranch(il, _classes.PropertyDispatch.Require(className).FieldsField, fields, absent);
        il.Emit(OpCodes.Ldloc, fields); il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Ldloca, value);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue")); il.Emit(OpCodes.Brfalse, absent);
        il.MarkLabel(synthesize);
        il.Emit(OpCodes.Newobj, storage.DescriptorConstructor); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Callvirt, storage.DescriptorValue.GetSetMethod()!); il.Emit(OpCodes.Ret);
        il.MarkLabel(absent); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
    }

    private void EmitNativeOwnPrototypeCapture(CompilationContext context)
    {
        if (context.CurrentClassBuilder is not TypeBuilder owner || !_nativeOwnAdapters.TryGetValue(owner, out var adapter)) return;
        var il = context.IL; var prototype = il.DeclareLocal(_types.Object); var nominal = il.DefineLabel(); var ready = il.DefineLabel();
        var legacy = EmitNativeOwnReceiverGuard(il);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, _runtime.DescriptorStorage.HasPrototypeEntry); il.Emit(OpCodes.Brfalse, nominal);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, _runtime.DescriptorStorage.GetPrototype); il.Emit(OpCodes.Stloc, prototype); il.Emit(OpCodes.Br, ready);
        il.MarkLabel(nominal);
        // Type arguments do not create another guest constructor/prototype.
        // Field adapters above still execute on their actual closed T owner.
        il.Emit(OpCodes.Ldtoken, CanonicalGenericClassOwner(owner)); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Call, _runtime.ClassPrototypes.LookupRegistered!); il.Emit(OpCodes.Stloc, prototype);
        il.MarkLabel(ready); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, prototype);
        il.Emit(OpCodes.Stfld, EmitterTypeHelpers.SelfFieldReference(adapter.InitialPrototype));
        il.MarkLabel(legacy);
    }

    private void EmitNativeOwnPropertyRead(MethodBuilder method, TypeBuilder owner)
    {
        var il = method.GetILGenerator(); var storage = _runtime.DescriptorStorage;
        var descriptor = il.DeclareLocal(storage.DescriptorType); var getter = il.DeclareLocal(_types.Object);
        var inherited = il.DefineLabel(); var data = il.DefineLabel(); var undefined = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(_nativeOwnAdapters[owner].Read)); il.Emit(OpCodes.Stloc, descriptor);
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Brfalse, inherited);
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Callvirt, storage.DescriptorGetter.GetGetMethod()!); il.Emit(OpCodes.Stloc, getter);
        il.Emit(OpCodes.Ldloc, getter); il.Emit(OpCodes.Brfalse, data);
        il.Emit(OpCodes.Ldloc, getter); il.Emit(OpCodes.Isinst, _runtime.Sentinels.UndefinedType); il.Emit(OpCodes.Brtrue, undefined);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, getter); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, _runtime.Invocation.Method); il.Emit(OpCodes.Ret);
        il.MarkLabel(data);
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Callvirt, storage.DescriptorSetter.GetGetMethod()!); il.Emit(OpCodes.Brtrue, undefined);
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Callvirt, storage.DescriptorValue.GetGetMethod()!); il.Emit(OpCodes.Ret);
        il.MarkLabel(inherited);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, _runtime.ObjectPrototypes.GetPrototypeOf);
        var prototype = il.DeclareLocal(_types.Object); il.Emit(OpCodes.Stloc, prototype); il.Emit(OpCodes.Ldloc, prototype); il.Emit(OpCodes.Brfalse, undefined);
        il.Emit(OpCodes.Ldloc, prototype); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, _runtime.Reflect.Get); il.Emit(OpCodes.Ret);
        il.MarkLabel(undefined); il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance); il.Emit(OpCodes.Ret);
    }

    private void EmitNativeOwnPropertyWrite(MethodBuilder method, string className, MethodInfo ensureFields)
    {
        var owner = (TypeBuilder)method.DeclaringType!; var il = method.GetILGenerator(); var own = _runtime.GuestOwnProperties!;
        var active = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, own.ActiveSourceField); il.Emit(OpCodes.Stloc, active);
        foreach (var field in GetClassRegistry().GetOwnGuestDataFields(owner))
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, active); il.Emit(OpCodes.Ldstr, NativeOwnFieldIdentity(field));
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", _types.Object, _types.Object)); il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(field.GuestSetter));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Call, own.CommitUpdate); il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(ensureFields));
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, own.CommitDefinition); il.Emit(OpCodes.Ret);
    }
}
