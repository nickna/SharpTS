using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private readonly Dictionary<MethodBuilder, (TypeBuilder Type, ConstructorBuilder Constructor, MethodBuilder Method, FieldBuilder Value)>
        _declarationMethodValues = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MethodBuilder, (TypeBuilder Owner, Stmt.Function Source)>
        _declarationMethodValueOwners = new(ReferenceEqualityComparer.Instance);

    private (TypeBuilder Type, ConstructorBuilder Constructor, MethodBuilder Method, FieldBuilder Value)
        DefineDeclarationMethodValue(TypeBuilder owner, Stmt.Function source, MethodBuilder original)
    {
        if (_declarationMethodValues.TryGetValue(original, out var existing)) return existing;
        var type = EmitTypeDefinitions.DefineType(_moduleBuilder,
            $"$MethodMember_{owner.FullName}_{original.Name}", TypeAttributes.Public | TypeAttributes.Sealed, _types.Object);
        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, _types.ObjectDefaultCtor); il.Emit(OpCodes.Ret);
        // The helper's CLR target is bookkeeping; the guest receiver is explicit.
        // Erased slots keep one method value across CLR generic instantiations.
        var method = type.DefineMethod("Invoke", MethodAttributes.Public, original.ReturnType,
            [_types.Object, .. source.Parameters.Select(parameter => parameter.IsRest ? _types.ListOfObject : _types.Object)]);
        method.DefineParameter(1, ParameterAttributes.None, "__this");
        MarkExpectsThis(method); MarkPadsUndefined(method);
        MarkFunctionLength(method, source.Parameters); MarkFunctionName(method, original.Name);
        if (!source.IsGenerator) MarkNonConstructible(method);
        _declarationMethodValueOwners.Add(method, (owner, source));
        var collector = new GuestReceiverArrowCollector(_guestReceiverArrows);
        foreach (var statement in source.Body ?? []) collector.Visit(statement);
        EmitGuestMethodValueBody(method, source, () =>
        {
            var context = CreateModuleMemberContext(method.GetILGenerator(), method);
            ApplyDeclarationMethodValueContext(context, method);
            context.EmittingTypeBuilder = type;
            return context;
        }, owner.FullName!);
        var value = type.DefineField("Value", _runtime.FunctionValues.Type,
            FieldAttributes.Public | FieldAttributes.Static);
        type.CreateType();
        var result = (type, constructor, method, value);
        _declarationMethodValues.Add(original, result);
        return result;
    }

    private void ApplyDeclarationMethodValueContext(CompilationContext context, MethodBuilder method)
    {
        if (!_declarationMethodValueOwners.TryGetValue(method, out var owner)) return;
        context.CurrentClassBuilder = owner.Owner;
        context.CurrentClassName = owner.Owner.FullName;
        context.IsStrictMode = true;
        context.IsInstanceMethod = !owner.Source.IsStatic;
        context.IsStaticSuperContext = owner.Source.IsStatic;
        context.HasGuestReceiver = true;
        context.UseNominalSuperCallBridge = true;
        context.GuestThisVariableName = "__this";
        var genericParameters = _classes.GenericParameters.Require(owner.Owner);
        var canonicalArguments = owner.Owner.IsGenericTypeDefinition
            ? CanonicalGenericClassOwner(owner.Owner).GetGenericArguments() : Type.EmptyTypes;
        for (int i = 0; i < genericParameters.Count; i++)
            context.GenericTypeParameters[genericParameters[i].Name] = canonicalArguments[i];
        foreach (var parameter in owner.Source.TypeParams ?? [])
            context.GenericTypeParameters[parameter.Name.Lexeme] = _types.Object;
    }

    private bool TryEmitDeclaredInstanceMethodValue(ILGenerator il, TypeBuilder owner, MethodBuilder original)
    {
        var source = GetNominalClassDeclaration(owner)?.Methods.FirstOrDefault(method =>
            method.Body != null && !method.IsStatic && !method.IsPrivate
            && method.ComputedKey == null && method.Name.Lexeme == original.Name);
        if (source == null) return false;

        // Method values carry a guest receiver, not the CLR instance that was
        // used to find them. The same value can be borrowed by another object
        // or a different closed generic instantiation without changing its body.
        var adapter = DefineDeclarationMethodValue(owner, source, original);
        var haveValue = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, adapter.Value);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, haveValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Newobj, adapter.Constructor);
        EmitMethodInfoLiteral(il, adapter.Method, adapter.Type);
        il.Emit(OpCodes.Newobj, _runtime.FunctionConstruction.Constructor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stsfld, adapter.Value);
        il.MarkLabel(haveValue);
        return true;
    }

    private bool CanOwnDeclaredSymbolMethodValue(TypeBuilder owner)
        => _classes.Declarations.Any(source => !_runtimeClassDeclarations.ContainsKey(source)
            && _classes.DeclarationNames.GetValueOrDefault(source) == owner.FullName);

    private void EmitDeclaredComputedMethodDescriptor(ILGenerator il, TypeBuilder owner,
        Stmt.Function source, MethodBuilder original, LocalBuilder key, Type? descriptorOwner = null,
        bool sourceOrdered = false)
    {
        var registrationDone = il.DefineLabel();
        if (source.IsStatic && owner.IsGenericTypeDefinition && descriptorOwner == null)
        {
            // A source generic class has one constructor object. Initializing
            // another CLR instantiation must not restore a deleted/replaced slot.
            il.Emit(OpCodes.Ldtoken, EmitGenerics.MakeGenericType(owner, owner.GetGenericArguments()));
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Ldtoken, CanonicalGenericClassOwner(owner));
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Bne_Un, registrationDone);
        }

        // Static plain members are installed before computed names. Preserve
        // their later-source collision wins; ordered instance installation lets
        // the later definition perform its own replacement instead.
        foreach (var later in GetNominalClassDeclaration(owner)?.Methods ?? [])
        {
            if (sourceOrdered || later.Body == null || later.IsPrivate || later.ComputedKey != null
                || later.IsStatic != source.IsStatic || later.Name.SourceOrderOffset <= source.Name.SourceOrderOffset
                || (!later.IsStatic && later.Name.Lexeme == "constructor")) continue;
            il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Ldstr, later.Name.Lexeme);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brtrue, registrationDone);
        }
        foreach (var later in GetNominalClassDeclaration(owner)?.Accessors ?? [])
        {
            if (sourceOrdered || later.IsAbstract || later.IsStatic != source.IsStatic
                || later.Name.SourceOrderOffset <= source.Name.SourceOrderOffset) continue;
            // Literal static computed accessors are also installed eagerly by
            // prototype registration, rather than replayed after static blocks.
            var laterName = later.ComputedKey == null ? later.Name.Lexeme
                : later.IsStatic ? GetConstantComputedAccessorName(later.ComputedKey) : null;
            if (laterName == null) continue;
            il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Ldstr, laterName);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brtrue, registrationDone);
        }

        var adapter = DefineDeclarationMethodValue(owner, source, original);
        var haveValue = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, adapter.Value);
        il.Emit(OpCodes.Brtrue, haveValue);
        var target = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Newobj, adapter.Constructor);
        il.Emit(OpCodes.Stloc, target);
        var metadata = il.DeclareLocal(_runtime.FunctionValues.Type);
        il.Emit(OpCodes.Ldloc, target);
        EmitMethodInfoLiteral(il, adapter.Method, adapter.Type);
        il.Emit(OpCodes.Newobj, _runtime.FunctionConstruction.Constructor);
        il.Emit(OpCodes.Stloc, metadata);
        il.Emit(OpCodes.Ldloc, target);
        EmitMethodInfoLiteral(il, adapter.Method, adapter.Type);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Ldloc, metadata);
        il.Emit(OpCodes.Callvirt, _runtime.FunctionValues.NameGetter);
        il.Emit(OpCodes.Call, _runtime.Symbols.MethodName);
        il.Emit(OpCodes.Ldloc, metadata);
        il.Emit(OpCodes.Callvirt, _runtime.FunctionValues.LengthGetter);
        il.Emit(OpCodes.Newobj, _runtime.FunctionConstruction.CachedConstructor);
        il.Emit(OpCodes.Stsfld, adapter.Value);
        il.MarkLabel(haveValue);

        var storage = _runtime.DescriptorStorage;
        var descriptor = il.DeclareLocal(storage.DescriptorType);
        il.Emit(OpCodes.Newobj, storage.DescriptorConstructor); il.Emit(OpCodes.Stloc, descriptor);
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Ldsfld, adapter.Value);
        il.Emit(OpCodes.Callvirt, storage.DescriptorValue.GetSetMethod()!);
        foreach (var property in new[] { storage.DescriptorWritable, storage.DescriptorConfigurable })
        {
            il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Callvirt, property.GetSetMethod()!);
        }
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, storage.DescriptorEnumerable.GetSetMethod()!);
        Type self = source.IsStatic ? owner : descriptorOwner ?? (owner.IsGenericTypeDefinition
            ? EmitGenerics.MakeGenericType(owner, owner.GetGenericArguments()) : owner);
        il.Emit(OpCodes.Ldtoken, self); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        if (!source.IsStatic) il.Emit(OpCodes.Call, _runtime.ClassPrototypes.Get);
        var receiver = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, receiver);
        var symbol = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Isinst, _types.String); il.Emit(OpCodes.Brfalse, symbol);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Call, storage.DefineProperty); il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, registrationDone);
        il.MarkLabel(symbol);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Call, _runtime.Symbols.GetStorage);
        il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item", _types.Object, _types.Object));
        il.MarkLabel(registrationDone);
    }
}
