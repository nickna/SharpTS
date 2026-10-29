using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Exact-owner static presence queries completed from emitted metadata.</summary>
internal sealed class NativeStaticMemberPresence
{
    internal const string UnsupportedOwnerMessage = "Reflective static-member presence on an unproved CLR owner is not supported in this native guest profile. Use --target dll or --target exe.";
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private readonly TypeProvider _types;
    private readonly TypeBuilder _owner;
    private sealed record Rows(TypeBuilder Owner, FieldInfo[] Fields, string[] Properties, MethodInfo[] Methods);

    internal NativeStaticMemberPresence(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativeStaticMemberPresence", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        HasMember = _owner.DefineMethod("HasMember", MethodAttributes.Assembly | MethodAttributes.Static,
            types.Boolean, [types.Type, types.String]);
    }

    internal MethodBuilder HasMember { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy { get; private set; }

    internal void CompleteEmission(NativeRuntimeClassInitialization initializers, NativeSourceOwnerCatalog catalog, EmittedRuntime runtime)
    {
        if (IsComplete || !initializers.IsComplete || !catalog.IsComplete ||
            !ReferenceEquals(initializers.Initialize.Module, _owner.Module) || !ReferenceEquals(catalog.Module, _owner.Module) ||
            !ReferenceEquals(runtime.ObjectFields.Interface.Module, _owner.Module) || !ReferenceEquals(catalog.Contract, runtime.ObjectFields.Interface))
            throw new InvalidOperationException("Native static-member presence requires completed metadata from this exact output.");
        UsesLegacy = initializers.UsesLegacy;
        var rows = new List<Rows>();
        if (!UsesLegacy)
        {
            foreach (var owner in initializers.Owners.OfType<TypeBuilder>())
            {
                if (!owner.IsCreated()) throw new InvalidOperationException("Static-member metadata has not been finalized.");
                try
                {
                    var metadata = _types.GetDeclaredStaticMetadata(owner);
                    if (metadata.Properties.Any(property => property.GetGetMethod(nonPublic: true) is { IsPublic: false } ||
                        property.GetSetMethod(nonPublic: true) is { IsPublic: false }))
                    { UsesLegacy = true; break; }
                    var fields = metadata.Fields.GroupBy(field => field.Name, StringComparer.Ordinal).ToArray();
                    var properties = metadata.Properties.GroupBy(property => property.Name, StringComparer.Ordinal).ToArray();
                    // Builder and loaded-runtime binders can resolve ambiguous
                    // metadata differently. Keep the complete old operation.
                    if (fields.Any(group => group.Count() != 1) || properties.Any(group => group.Count() != 1))
                    { UsesLegacy = true; break; }
                    var methods = catalog.Implementers.Contains(owner) ? _types.GetMethods(owner, Flags) : [];
                    rows.Add(new(owner, fields.Select(group => group.Single()).ToArray(), properties.Select(group => group.Key).ToArray(), methods));
                }
                catch (NotSupportedException) { UsesLegacy = true; break; }
            }
        }
        var il = HasMember.GetILGenerator();
        if (UsesLegacy) EmitLegacy(il, runtime);
        else
        {
            var validName = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Brtrue, validName);
            il.Emit(OpCodes.Ldstr, "name");
            il.Emit(OpCodes.Newobj, typeof(ArgumentNullException).GetConstructor([typeof(string)])!); il.Emit(OpCodes.Throw);
            il.MarkLabel(validName);
            foreach (var row in rows)
            {
                var next = MatchOwner(il, row.Owner);
                var present = il.DefineLabel();
                foreach (var field in row.Fields)
                {
                    var nextField = il.DefineLabel(); var fieldMatch = il.DefineLabel();
                    EmitNameMatch(il, field.Name, fieldMatch); il.Emit(OpCodes.Br, nextField);
                    il.MarkLabel(fieldMatch);
                    // Companion descriptor/deletion operations still reflect this
                    // exact field. Retain its metadata through a literal handle.
                    il.Emit(OpCodes.Ldtoken, field);
                    il.Emit(OpCodes.Call, _types.GetMethod(typeof(FieldInfo), nameof(FieldInfo.GetFieldFromHandle), typeof(RuntimeFieldHandle)));
                    il.Emit(OpCodes.Brtrue, present); il.MarkLabel(nextField);
                }
                foreach (var name in row.Properties) EmitNameMatch(il, name, present);
                if (catalog.Implementers.Contains(row.Owner))
                {
                    foreach (var methods in row.Methods.GroupBy(method => method.Name, StringComparer.Ordinal))
                    {
                        EmitNameMatch(il, methods.Key, present);
                    }
                    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Call, runtime.SymbolAccessors.FindOwnMethod); il.Emit(OpCodes.Brtrue, present);
                }
                il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
                il.MarkLabel(present); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
                il.MarkLabel(next);
            }
            foreach (var type in initializers.Owners.Where(type => type is not TypeBuilder))
            {
                var next = MatchOwner(il, type);
                // The exact literal owner keeps the real framework query and its
                // exception behavior, with statically bounded metadata retention.
                EmitFieldAndPropertyQueries(il, () => { il.Emit(OpCodes.Ldtoken, type); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); }, out var present);
                il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
                il.MarkLabel(present); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
                il.MarkLabel(next);
            }
            GuestErrorEmitter.ThrowError(il, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor, UnsupportedOwnerMessage);
        }
        _owner.CreateType();
        IsComplete = true;
    }

    private Label MatchOwner(ILGenerator il, Type type)
    {
        var next = il.DefineLabel(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldtoken, type);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); il.Emit(OpCodes.Bne_Un, next); return next;
    }

    private void EmitNameMatch(ILGenerator il, string name, Label target)
    {
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldstr, name);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String)); il.Emit(OpCodes.Brtrue, target);
    }

    private void EmitFieldAndPropertyQueries(ILGenerator il, Action owner, out Label present)
    {
        present = il.DefineLabel();
        owner(); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, (int)Flags);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", _types.String, typeof(BindingFlags))); il.Emit(OpCodes.Brtrue, present);
        owner(); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, (int)Flags);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String, typeof(BindingFlags))); il.Emit(OpCodes.Brtrue, present);
    }

    private void EmitLegacy(ILGenerator il, EmittedRuntime runtime)
    {
        EmitFieldAndPropertyQueries(il, () => il.Emit(OpCodes.Ldarg_0), out var present);
        var absent = il.DefineLabel();
        il.Emit(OpCodes.Ldtoken, runtime.ObjectFields.Interface); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "IsAssignableFrom", _types.Type)); il.Emit(OpCodes.Brfalse, absent);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, (int)MemberTypes.Method); il.Emit(OpCodes.Ldc_I4, (int)Flags);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMember", _types.String, typeof(MemberTypes), typeof(BindingFlags)));
        il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Brtrue, present);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.SymbolAccessors.FindOwnMethod); il.Emit(OpCodes.Brtrue, present);
        il.MarkLabel(absent); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        il.MarkLabel(present); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
    }
}
