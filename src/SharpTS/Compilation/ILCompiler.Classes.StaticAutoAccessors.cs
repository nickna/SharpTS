using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private readonly Dictionary<Stmt.AutoAccessor, (TypeBuilder Type, ConstructorBuilder Constructor, MethodBuilder Getter, MethodBuilder? Setter)>
        _staticAutoAccessorValues = new(ReferenceEqualityComparer.Instance);

    private void DefineStaticAutoAccessorValue(TypeBuilder owner, Stmt.AutoAccessor source, FieldBuilder backing)
    {
        bool evaluated = _classExprs.Names.ContainsValue(owner.FullName!);
        var type = _moduleBuilder.DefineType("$StaticAutoAccessor_" + _staticAutoAccessorValues.Count,
            TypeAttributes.Public | TypeAttributes.Sealed, _types.Object);
        var home = type.DefineField("Owner", _types.Object, FieldAttributes.Private | FieldAttributes.InitOnly);
        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [_types.Object]);
        var init = constructor.GetILGenerator();
        init.Emit(OpCodes.Ldarg_0); init.Emit(OpCodes.Call, _types.ObjectDefaultCtor);
        init.Emit(OpCodes.Ldarg_0); init.Emit(OpCodes.Ldarg_1); init.Emit(OpCodes.Stfld, home); init.Emit(OpCodes.Ret);
        MethodBuilder Define(bool setter)
        {
            var method = type.DefineMethod(setter ? "Set" : "Get", MethodAttributes.Public, _types.Object,
                setter ? [_types.Object, _types.Object] : [_types.Object]);
            MarkExpectsThis(method); MarkNonConstructible(method); MarkPadsUndefined(method);
            MarkFunctionName(method, $"{(setter ? "set" : "get")} {source.Name.Lexeme}");
            var il = method.GetILGenerator();
            var valid = il.DefineLabel();
            if (!source.IsStatic)
            {
                var metadata = _receiverAutoAccessors[source];
                var slots = il.DeclareLocal(_types.DictionaryStringObject);
                var invalid = il.DefineLabel();
                il.Emit(OpCodes.Ldsfld, metadata.Storage); il.Emit(OpCodes.Brfalse, invalid);
                il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Brfalse, invalid);
                il.Emit(OpCodes.Ldsfld, metadata.Storage); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldloca, slots);
                il.Emit(OpCodes.Callvirt, _types.GetMethod(metadata.Storage.FieldType, "TryGetValue", _types.Object, _types.DictionaryStringObject.MakeByRefType()));
                il.Emit(OpCodes.Brfalse, invalid);
                il.Emit(OpCodes.Ldloc, slots); il.Emit(OpCodes.Ldstr, "'owner");
                il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item", _types.String));
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, home); il.Emit(OpCodes.Beq, valid);
                il.MarkLabel(invalid);
                GuestErrorEmitter.ThrowTypeError(il, _runtime, "Cannot access auto-accessor on an object whose class did not declare it");
                il.MarkLabel(valid);
                il.Emit(OpCodes.Ldarg_1);
                if (setter) il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, setter ? metadata.Setter! : metadata.Getter);
                il.Emit(OpCodes.Ret);
                return method;
            }
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, home); il.Emit(OpCodes.Beq, valid);
            GuestErrorEmitter.ThrowTypeError(il, _runtime, "Cannot access static auto-accessor on an object whose class did not declare it");
            il.MarkLabel(valid);
            if (evaluated)
            {
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, home); il.Emit(OpCodes.Castclass, _runtime.ClassDefinitions.Type);
                il.Emit(OpCodes.Ldfld, _runtime.ClassDefinitions.PrivateMembers); il.Emit(OpCodes.Ldstr, StaticAutoAccessorKey(source));
                if (setter)
                {
                    il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
                    il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
                }
                else il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item", _types.String));
            }
            else
            {
                var original = setter ? _classes.StaticSetters[owner.FullName!][source.Name.Lexeme]
                    : _classes.StaticGetters[owner.FullName!][source.Name.Lexeme];
                MethodInfo target = owner.IsGenericTypeDefinition
                    ? EmitterTypeHelpers.ResolveMethod(CanonicalGenericClassOwner(owner), original) : original;
                if (setter)
                {
                    il.Emit(OpCodes.Ldarg_2); EmitBoxedFieldConversion(il, backing.FieldType); il.Emit(OpCodes.Call, target);
                    il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
                }
                else
                {
                    il.Emit(OpCodes.Call, target);
                    if (backing.FieldType.IsValueType || backing.FieldType.IsGenericParameter) il.Emit(OpCodes.Box, backing.FieldType);
                }
            }
            il.Emit(OpCodes.Ret);
            return method;
        }
        var getter = Define(false);
        var setter = source.IsReadonly ? null : Define(true);
        _staticAutoAccessorValues.Add(source, (type, constructor, getter, setter));
        type.CreateType();
    }

    private static string StaticAutoAccessorKey(Stmt.AutoAccessor source) => "'auto:" + source.Name.SourceOrderOffset;

    private void EmitStaticAutoAccessorDescriptor(ILGenerator il, TypeBuilder owner, Stmt.AutoAccessor source, Action? emitOwner = null)
    {
        var metadata = _staticAutoAccessorValues[source];
        var home = il.DeclareLocal(_types.Object);
        if (emitOwner == null) EmitCanonicalClassConstructorValue(il, owner);
        else emitOwner();
        il.Emit(OpCodes.Stloc, home);
        il.Emit(OpCodes.Ldloc, home);
        if (!source.IsStatic)
        {
            il.Emit(OpCodes.Castclass, _runtime.ClassDefinitions.Type);
            il.Emit(OpCodes.Ldfld, _runtime.ClassDefinitions.Prototype);
        }
        il.Emit(OpCodes.Ldstr, source.Name.Lexeme);
        il.Emit(OpCodes.Newobj, _runtime.DescriptorStorage.DescriptorConstructor);
        var target = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, home); il.Emit(OpCodes.Newobj, metadata.Constructor); il.Emit(OpCodes.Stloc, target);
        void Callable(MethodBuilder method, PropertyInfo property)
        {
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldloc, target); EmitMethodInfoLiteral(il, method, metadata.Type);
            il.Emit(OpCodes.Newobj, _runtime.FunctionConstruction.Constructor); il.Emit(OpCodes.Callvirt, property.GetSetMethod()!);
        }
        Callable(metadata.Getter, _runtime.DescriptorStorage.DescriptorGetter);
        if (metadata.Setter != null) Callable(metadata.Setter, _runtime.DescriptorStorage.DescriptorSetter);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorConfigurable.GetSetMethod()!);
        il.Emit(OpCodes.Call, _runtime.DescriptorStorage.DefineProperty); il.Emit(OpCodes.Pop);
    }
}
