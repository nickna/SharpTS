using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private void EmitUninitializedParameterProperties(ILGenerator il, TypeBuilder owner, Stmt.Function? constructor)
    {
        foreach (var parameter in constructor?.Parameters.Where(parameter => parameter.IsParameterProperty) ?? [])
        {
            if (!GetClassRegistry().TryGetOwnGuestDataField(owner, parameter.Name.Lexeme, out var field)
                || (!field.IsPrimitive && field.NativeType != _types.Object && field.BackingField != null)) continue;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
            il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(field.GuestSetter));
        }
    }

    private void DefineGuestDataField(TypeBuilder owner, Stmt.Field source, Type nativeType,
        FieldBuilder? backing = null, MethodBuilder? getter = null, MethodBuilder? setter = null)
    {
        var attributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig;
        var guestGet = owner.DefineMethod("$GuestGet_" + source.Name.Lexeme, attributes, _types.Object, Type.EmptyTypes);
        var guestSet = owner.DefineMethod("$GuestSet_" + source.Name.Lexeme, attributes, _types.Void, [_types.Object]);
        var hasRaw = owner.DefineMethod("$HasRaw_" + source.Name.Lexeme, attributes, _types.Boolean, Type.EmptyTypes);
        GetClassRegistry().RegisterGuestDataField(new GuestDataField(source, owner, nativeType,
            backing, getter, setter, guestGet, guestSet, hasRaw));
    }

    private void EmitClearGuestFieldOverride(ILGenerator il, FieldInfo store, string name)
    {
        var absent = il.DefineLabel();
        var dictionary = il.DeclareLocal(_types.DictionaryStringObject);
        EmitLoadDynamicFieldsOrBranch(il, store, dictionary, absent);
        il.Emit(OpCodes.Ldloc, dictionary);
        il.Emit(OpCodes.Ldstr, name);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "Remove", [_types.String])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(absent);
    }

    private void EmitGuestDataFieldBodies(TypeBuilder owner, FieldInfo store, MethodInfo ensureFields)
    {
        foreach (var field in GetClassRegistry().GetOwnGuestDataFields(owner))
        {
            bool nativeOwn = _nativeOwnSourceDeclarations.ContainsKey(owner);
            var get = field.GuestGetter.GetILGenerator();
            var native = get.DefineLabel();
            if (field.IsPrimitive || field.BackingField == null || nativeOwn)
            {
                var dictionary = get.DeclareLocal(_types.DictionaryStringObject);
                var value = get.DeclareLocal(_types.Object);
                EmitLoadDynamicFieldsOrBranch(get, store, dictionary, native);
                get.Emit(OpCodes.Ldloc, dictionary);
                get.Emit(OpCodes.Ldstr, field.SourceName);
                get.Emit(OpCodes.Ldloca, value);
                get.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
                    [_types.String, _types.Object.MakeByRefType()])!);
                get.Emit(OpCodes.Brfalse, native);
                get.Emit(OpCodes.Ldloc, value);
                get.Emit(OpCodes.Ret);
            }
            get.MarkLabel(native);
            if (field.BackingField is { } backing)
            {
                get.Emit(OpCodes.Ldarg_0);
                get.Emit(OpCodes.Ldfld, EmitterTypeHelpers.SelfFieldReference(backing));
                if (field.NativeType.IsValueType || field.NativeType.IsGenericParameter)
                    get.Emit(OpCodes.Box, field.NativeType);
            }
            else get.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
            get.Emit(OpCodes.Ret);

            var set = field.GuestSetter.GetILGenerator();
            var raw = set.DefineLabel();
            if (nativeOwn && field.NativeType != _types.Object)
            {
                set.Emit(OpCodes.Ldarg_1); set.Emit(OpCodes.Isinst, field.NativeType); set.Emit(OpCodes.Brfalse, raw);
            }
            if (field.IsPrimitive)
            {
                set.Emit(OpCodes.Ldarg_1);
                set.Emit(OpCodes.Isinst, field.NativeType);
                set.Emit(OpCodes.Brfalse, raw);
                set.Emit(OpCodes.Ldarg_0);
                set.Emit(OpCodes.Ldarg_1);
                set.Emit(OpCodes.Unbox_Any, field.NativeType);
                set.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(field.NativeSetter!));
                set.Emit(OpCodes.Ret);
            }
            else if (field.BackingField is { } typedBacking)
            {
                set.Emit(OpCodes.Ldarg_0);
                set.Emit(OpCodes.Ldarg_1);
                if (field.NativeType.IsValueType || field.NativeType.IsGenericParameter)
                    set.Emit(OpCodes.Unbox_Any, field.NativeType);
                else if (field.NativeType != _types.Object) set.Emit(OpCodes.Castclass, field.NativeType);
                set.Emit(OpCodes.Stfld, EmitterTypeHelpers.SelfFieldReference(typedBacking));
                if (nativeOwn) EmitClearGuestFieldOverride(set, store, field.SourceName);
                set.Emit(OpCodes.Ret);
            }
            set.MarkLabel(raw);
            if (field.IsPrimitive || field.BackingField == null || nativeOwn)
            {
                set.Emit(OpCodes.Ldarg_0);
                set.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(ensureFields));
                set.Emit(OpCodes.Ldstr, field.SourceName);
                set.Emit(OpCodes.Ldarg_1);
                set.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
                set.Emit(OpCodes.Ret);
            }

            var has = field.HasRawOverride.GetILGenerator();
            if (!field.IsPrimitive)
                has.Emit(OpCodes.Ldc_I4_1);
            else
            {
                var missing = has.DefineLabel();
                var dictionary = has.DeclareLocal(_types.DictionaryStringObject);
                EmitLoadDynamicFieldsOrBranch(has, store, dictionary, missing);
                has.Emit(OpCodes.Ldloc, dictionary);
                has.Emit(OpCodes.Ldstr, field.SourceName);
                has.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", [_types.String])!);
                has.Emit(OpCodes.Ret);
                has.MarkLabel(missing);
                has.Emit(OpCodes.Ldc_I4_0);
            }
            has.Emit(OpCodes.Ret);
        }
    }

    private void EmitDictionaryGuestFieldBranches(ILGenerator il, TypeBuilder owner, bool write)
    {
        foreach (var field in GetClassRegistry().GetOwnGuestDataFields(owner).Where(field => field.BackingField == null))
            EmitGuestFieldBranch(il, owner, field, write);
    }

    private void EmitInheritedGuestFieldBranches(ILGenerator il, TypeBuilder owner, bool write)
    {
        var seen = GetClassRegistry().GetOwnGuestDataFields(owner).Select(field => field.SourceName).ToHashSet(StringComparer.Ordinal);
        for (Type? parent = owner.BaseType; parent != null; parent = parent.BaseType)
        {
            var definition = parent.IsConstructedGenericType ? parent.GetGenericTypeDefinition() : parent;
            if (definition is not TypeBuilder builder) break;
            foreach (var field in GetClassRegistry().GetOwnGuestDataFields(builder))
            {
                if (!seen.Add(field.SourceName)) continue;
                bool blocked = false;
                for (Type? nearer = owner; nearer != null && nearer != parent; nearer = nearer.BaseType)
                {
                    var nearerDefinition = nearer.IsConstructedGenericType ? nearer.GetGenericTypeDefinition() : nearer;
                    if (nearerDefinition is TypeBuilder nearerBuilder
                        && GetClassRegistry().HasOwnGuestAccessorBarrier(nearerBuilder, field.SourceName))
                    { blocked = true; break; }
                }
                if (!blocked) EmitGuestFieldBranch(il, owner, field, write);
            }
        }
    }

    private void EmitGuestFieldBranch(ILGenerator il, TypeBuilder owner, GuestDataField field, bool write)
    {
        var method = write ? field.GuestSetter : field.GuestGetter;
        if (!EmitterTypeHelpers.TryResolveInstanceDispatch(owner, method, owner, out _, out var target))
            throw new InvalidOperationException("Cannot resolve guest field owner.");
        var next = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, field.SourceName);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, next);
        il.Emit(OpCodes.Ldarg_0);
        if (write) il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, target);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(next);
    }
}
