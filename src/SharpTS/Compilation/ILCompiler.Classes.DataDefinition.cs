using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private void EmitSourceDataDefinition(TypeBuilder owner, MethodInfo ensureFields)
    {
        var contract = _runtime.ObjectFields.SourceDataDefinition
            ?? throw new InvalidOperationException("Source class data definition contract is absent.");
        EmitTypeDefinitions.AddInterfaceImplementation(owner, contract.Contract);
        var method = owner.DefineMethod("<>DefineOwnDataValue",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final
            | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void, [_types.String, _types.Object]);
        owner.DefineMethodOverride(method, contract.DefineValue);
        var il = method.GetILGenerator();
        var instanceData = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _runtime.ClassPrototypes.IsPrototype);
        il.Emit(OpCodes.Brfalse, instanceData);
        // Prototype definitions already own their values and creation order in
        // the descriptor store. They must not populate instance field storage.
        il.Emit(OpCodes.Ret);
        il.MarkLabel(instanceData);

        // These exact source declaration/setter pairs also carry the existing
        // native/raw representation transition. No guest accessor is invoked.
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (Type? current = owner; current is not null; current = current.BaseType)
        {
            var definition = current.IsConstructedGenericType ? current.GetGenericTypeDefinition() : current;
            if (definition is not TypeBuilder source) break;
            foreach (var field in GetClassRegistry().GetOwnGuestDataFields(source))
            {
                if (seen.Add(field.SourceName))
                    EmitGuestFieldBranch(il, owner, field, write: true);
            }
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(ensureFields));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
        il.Emit(OpCodes.Ret);
    }
}
