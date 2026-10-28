using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Compilation;

/// <summary>One output's receiver setters and final-image proof; never a process-wide type catalog.</summary>
internal sealed class NativeReceiverBinding
{
    internal const string ForeignMessage = "Binding this on an unregistered CLR receiver is not supported in native guest output. Use --target dll or --target exe.";
    private readonly ModuleBuilder _module;
    private readonly TypeProvider _types;
    private readonly TypeBuilder _owner;
    private readonly TypeBuilder _contract;
    private readonly MethodBuilder _set;
    private readonly List<(TypeBuilder Owner, FieldBuilder Field, MethodBuilder Setter)> _fields = [];
    private readonly List<(int Offset, Type Type)> _baseWitnesses = [];
    private readonly Type[] _roots;

    internal NativeReceiverBinding(ModuleBuilder module, TypeProvider types)
    {
        _module = module;
        _types = types;
        RuntimeType = types.Resolve("System.RuntimeType");
        if (RuntimeType != types.Object.GetType() || RuntimeType.Assembly != types.CoreAssembly)
            throw new InvalidOperationException("Native receiver binding requires the exact CoreLib runtime Type implementation.");
        _contract = module.DefineType("$INativeReceiverBinding", TypeAttributes.NotPublic | TypeAttributes.Interface | TypeAttributes.Abstract);
        _set = _contract.DefineMethod("SetReceiver", MethodAttributes.Public | MethodAttributes.Abstract |
            MethodAttributes.Virtual | MethodAttributes.NewSlot, types.Void, [types.Object]);
        _contract.CreateType();
        _owner = module.DefineType("$NativeReceiverBinding", TypeAttributes.NotPublic | TypeAttributes.Abstract |
            TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        Bind = _owner.DefineMethod("Bind", MethodAttributes.Assembly | MethodAttributes.Static,
            types.Void, [types.Object, types.Object]);
        // Most specific first: these are queried, not assumed to lack fields.
        _roots = [typeof(System.Net.Sockets.SocketException), typeof(Exception), typeof(Enum), typeof(ValueType), typeof(Attribute),
            typeof(List<object>), typeof(Dictionary<string, object>), typeof(Dictionary<object, object>),
            typeof(HashSet<object>), typeof(WeakReference<object>), typeof(SynchronizationContext), types.Object];
    }

    internal MethodBuilder Bind { get; }
    internal Type RuntimeType { get; }
    internal bool IsComplete { get; private set; }
    internal int FieldCount => _fields.Count;

    internal void Register(FieldBuilder field)
    {
        if (IsComplete || field.DeclaringType is not TypeBuilder owner || owner.IsCreated() ||
            owner.Module.ModuleVersionId != _module.ModuleVersionId || field.Name != "this" ||
            field.FieldType != _types.Object || !field.IsPublic || field.IsStatic || field.IsInitOnly ||
            _fields.Any(entry => entry.Owner == owner))
            throw new InvalidOperationException("Native receiver setters require one mutable public object this field on a live owned type.");
        var setter = owner.DefineMethod("$SetNativeReceiver", MethodAttributes.Private | MethodAttributes.Virtual |
            MethodAttributes.Final | MethodAttributes.NewSlot, _types.Void, [_types.Object]);
        var il = setter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, EmitterTypeHelpers.SelfFieldReference(field)); il.Emit(OpCodes.Ret);
        EmitTypeDefinitions.AddInterfaceImplementation(owner, _contract);
        owner.DefineMethodOverride(setter, _set);
        _fields.Add((owner, field, setter));
    }

    internal void CompleteEmission(EmittedErrorRuntime errors)
    {
        if (IsComplete || _fields.Any(entry => !entry.Owner.IsCreated()))
            throw new InvalidOperationException("Native receiver producers must be finalized once before completion.");
        var il = Bind.GetILGenerator();
        var targetType = il.DeclareLocal(_types.Type);
        var field = il.DeclareLocal(_types.FieldInfo);
        var nonNull = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brtrue, nonNull); il.Emit(OpCodes.Ret); il.MarkLabel(nonNull);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, targetType);
        var foreign = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, targetType); il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Type, "Assembly"));
        il.Emit(OpCodes.Ldtoken, _owner); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Type, "Assembly"));
        il.Emit(OpCodes.Bne_Un, foreign);
        var noSetter = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Isinst, _contract); il.Emit(OpCodes.Brfalse, noSetter);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, _contract); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _set); il.Emit(OpCodes.Ret); il.MarkLabel(noSetter);
        foreach (var root in _roots)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Isinst, root); il.Emit(OpCodes.Brfalse, next);
            _baseWitnesses.Add((il.ILOffset, root));
            EmitLiteralQuery(il, root, field);
            il.MarkLabel(next);
        }
        // A final-image proof requires every possible owned base chain above.
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, ForeignMessage);
        il.MarkLabel(foreign);
        foreach (var type in KnownForeignTypes())
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, targetType); il.Emit(OpCodes.Ldtoken, type);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); il.Emit(OpCodes.Bne_Un, next);
            EmitLiteralQuery(il, type, field);
            il.MarkLabel(next);
        }
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, ForeignMessage);
        _owner.CreateType();
        IsComplete = true;
    }

    private Type[] KnownForeignTypes() => [_types.Object, _types.ObjectArray, typeof(List<object>),
        typeof(Dictionary<string, object>), typeof(Dictionary<object, object>), typeof(HashSet<object>),
        typeof(Func<object[], object>), _types.String, _types.Boolean, _types.Double, typeof(System.Numerics.BigInteger),
        RuntimeType, typeof(Exception), typeof(System.Net.Sockets.SocketException),
        typeof(System.Runtime.CompilerServices.ConditionalWeakTable<object, object>), typeof(WeakReference<object>)];

    private void EmitLiteralQuery(ILGenerator il, Type type, LocalBuilder field)
    {
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldtoken, type); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ldstr, "this"); il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Public | BindingFlags.Instance));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", _types.String, _types.BindingFlags));
        il.Emit(OpCodes.Stloc, field); il.Emit(OpCodes.Ldloc, field); il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, field); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.FieldInfo, "SetValue", _types.Object, _types.Object));
        il.MarkLabel(done); il.Emit(OpCodes.Ret);
    }

    internal void ValidateImage(byte[] image)
    {
        if (!IsComplete) throw new InvalidOperationException("Native receiver producers are not complete.");
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var metadata = pe.GetMetadataReader();
        if (metadata.GetGuid(metadata.GetModuleDefinition().Mvid) != _module.ModuleVersionId)
            Refuse("The final image does not belong to this receiver session");
        var contract = MetadataTokens.TypeDefinitionHandle(_contract.MetadataToken);
        if (FullName(contract) != _contract.FullName || metadata.GetTypeDefinition(contract).Attributes != _contract.Attributes ||
            !metadata.GetTypeDefinition(contract).GetMethods().Contains(MetadataTokens.MethodDefinitionHandle(_set.MetadataToken)))
            Refuse("The final receiver contract changed identity");
        var expected = _fields.ToDictionary(entry => entry.Field.MetadataToken);
        var actualOwners = new HashSet<int>();
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            bool implements = type.GetInterfaceImplementations().Any(implementation =>
                metadata.GetInterfaceImplementation(implementation).Interface == contract);
            bool registered = _fields.Any(entry => entry.Owner.MetadataToken == MetadataTokens.GetToken(handle));
            if (implements != registered) Refuse("The final receiver interface inventory changed");
            foreach (var fieldHandle in type.GetFields())
            {
                var field = metadata.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public ||
                    (field.Attributes & FieldAttributes.Static) != 0 || metadata.GetString(field.Name) != "this") continue;
                int token = MetadataTokens.GetToken(fieldHandle);
                if (!expected.Remove(token, out var entry) || entry.Owner.MetadataToken != MetadataTokens.GetToken(handle) ||
                    FullName(handle) != entry.Owner.FullName || field.Attributes != entry.Field.Attributes ||
                    !metadata.GetBlobBytes(field.Signature).AsSpan().SequenceEqual(new byte[] { 0x06, 0x1c }))
                    Refuse("An unproved public instance this field exists in the final image");
                var setter = metadata.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(entry.Setter.MetadataToken));
                var setterHandle = MetadataTokens.MethodDefinitionHandle(entry.Setter.MetadataToken);
                if (metadata.GetString(setter.Name) != entry.Setter.Name || setter.Attributes != entry.Setter.Attributes ||
                    !type.GetMethods().Contains(setterHandle) ||
                    !metadata.GetBlobBytes(setter.Signature).AsSpan().SequenceEqual(new byte[] { 0x20, 0x01, 0x01, 0x1c }))
                    Refuse("A registered receiver setter changed owner or identity");
                var overrides = type.GetMethodImplementations().Select(implementation => metadata.GetMethodImplementation(implementation))
                    .Where(implementation => implementation.MethodDeclaration == MetadataTokens.MethodDefinitionHandle(_set.MetadataToken)).ToArray();
                if (overrides.Length != 1 || overrides[0].MethodBody != setterHandle)
                    Refuse("A registered receiver setter lost its exact interface override");
                var setterBody = pe.GetMethodBody(setter.RelativeVirtualAddress).GetILBytes()!;
                if (setterBody.Length != 8 || setterBody[0] != 0x02 || setterBody[1] != 0x03 || setterBody[2] != 0x7d || setterBody[7] != 0x2a)
                    Refuse("A registered receiver setter is not a direct field store");
                var storedField = MetadataTokens.EntityHandle(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(setterBody.AsSpan(3, 4)));
                if (storedField != fieldHandle)
                {
                    if (storedField.Kind != HandleKind.MemberReference) Refuse("A receiver setter stores an unproved field");
                    var member = metadata.GetMemberReference((MemberReferenceHandle)storedField);
                    if (metadata.GetString(member.Name) != "this" || member.Parent.Kind != HandleKind.TypeSpecification ||
                        !metadata.GetBlobBytes(member.Signature).AsSpan().SequenceEqual(new byte[] { 0x06, 0x1c }))
                        Refuse("A generic receiver setter stores an unproved field");
                    var signature = metadata.GetBlobReader(metadata.GetTypeSpecification((TypeSpecificationHandle)member.Parent).Signature);
                    if (signature.ReadByte() != 0x15 || signature.ReadByte() != 0x12 || signature.ReadTypeHandle() != handle ||
                        signature.ReadCompressedInteger() != type.GetGenericParameters().Count)
                        Refuse("A generic receiver setter does not use its own closed context");
                    for (int index = 0; index < type.GetGenericParameters().Count; index++)
                        if (signature.ReadByte() != 0x13 || signature.ReadCompressedInteger() != index)
                            Refuse("A generic receiver setter has unproved type arguments");
                    if (signature.RemainingBytes != 0) Refuse("A generic receiver setter has trailing type arguments");
                }
                actualOwners.Add(MetadataTokens.GetToken(handle));
            }
        }
        if (expected.Count != 0 || actualOwners.Count != _fields.Count) Refuse("A registered receiver field disappeared from the final image");
        var body = pe.GetMethodBody(metadata.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(Bind.MetadataToken)).RelativeVirtualAddress).GetILBytes()!;
        var allowedRoots = new HashSet<EntityHandle>();
        foreach (var witness in _baseWitnesses)
        {
            if (body[witness.Offset] != unchecked((byte)OpCodes.Ldtoken.Value)) Refuse("A receiver base literal changed");
            allowedRoots.Add(MetadataTokens.EntityHandle(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(witness.Offset + 1, 4))));
        }
        foreach (var handle in metadata.TypeDefinitions)
            CheckBase(handle, new HashSet<TypeDefinitionHandle>());

        string FullName(TypeDefinitionHandle handle)
        {
            var type = metadata.GetTypeDefinition(handle);
            if (!type.GetDeclaringType().IsNil) return FullName(type.GetDeclaringType()) + "+" + metadata.GetString(type.Name);
            string ns = metadata.GetString(type.Namespace);
            return (ns.Length == 0 ? "" : ns + ".") + metadata.GetString(type.Name);
        }

        void CheckBase(TypeDefinitionHandle handle, HashSet<TypeDefinitionHandle> path)
        {
            if (!path.Add(handle)) Refuse("A receiver base chain is cyclic");
            var parent = metadata.GetTypeDefinition(handle).BaseType;
            if (parent.IsNil) return; // interface or <Module>
            if (allowedRoots.Contains(parent)) return;
            if (parent.Kind == HandleKind.TypeSpecification)
            {
                var reader = metadata.GetBlobReader(metadata.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
                if (reader.ReadByte() != 0x15) Refuse("An unsupported receiver base TypeSpec exists");
                byte kind = reader.ReadByte();
                if (kind is not (0x11 or 0x12)) Refuse("An unsupported receiver generic base exists");
                parent = reader.ReadTypeHandle();
            }
            if (parent.Kind != HandleKind.TypeDefinition) Refuse("A foreign or open-dependent receiver base is not proved by a literal root: " +
                metadata.GetString(metadata.GetTypeDefinition(handle).Name) + " -> 0x" + MetadataTokens.GetToken(parent).ToString("X8") +
                (parent.Kind == HandleKind.TypeReference ? " " + metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)parent).Name) : ""));
            CheckBase((TypeDefinitionHandle)parent, path);
        }
    }

    private static void Refuse(string reason) => throw new CompileException(DiagnosticCode.NativeGuestReceiverCertificate,
        "Experimental native guest receiver certificate refused: " + reason + ". Use managed output for this shape.");
}
