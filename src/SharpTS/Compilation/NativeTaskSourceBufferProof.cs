using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using PEPacker;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Compilation;

/// <summary>
/// The actual runtime-owned TCS local, not a classification of arbitrary Task results.
/// Its closed BCL construction/getter operations discharge only the shared-buffer
/// query's negative obligation. They never add a query owner or change other roles.
/// </summary>
internal sealed class NativeTaskSourceBufferProof
{
    private readonly ModuleBuilder _module;
    private readonly TypeProvider _types;
    private readonly MethodBuilder _method;
    private readonly ILGenerator _body;
    private readonly LocalBuilder _local;
    private readonly MethodInfo _getter;
    private readonly List<(int New, int Store, ConstructorInfo Constructor, int? Options)> _allocations = [];
    private readonly HashSet<int> _loads = [];
    private readonly HashSet<int> _results = [];
    private bool _complete;
    private bool _used;

    internal NativeTaskSourceBufferProof(ModuleBuilder module, TypeProvider types, MethodBuilder method)
    {
        _module = module; _types = types; _method = method; _body = method.GetILGenerator();
        _getter = types.GetProperty(types.TaskCompletionSourceOfObject, "Task").GetGetMethod()!;
        if (!ReferenceEquals(method.Module, module) || method.DeclaringType is not TypeBuilder { } owner || owner.IsCreated() ||
            _getter.DeclaringType != types.TaskCompletionSourceOfObject ||
            _getter.IsStatic || _getter.IsVirtual || _getter.ReturnType != types.TaskOfObject || _getter.GetParameters().Length != 0)
            throw new InvalidOperationException("Task-source proof requires the actual live owned body, local and nonvirtual BCL getter.");
        // The proof owns this exact DeclareLocal operation; a same-index local
        // from a different body cannot be supplied as a witness.
        _local = _body.DeclareLocal(types.TaskCompletionSourceOfObject);
    }

    internal LocalBuilder Local => _local;

    internal void RecordAllocation(ConstructorInfo constructor, int allocationOffset, int storeOffset, int? options)
    {
        Mutable();
        var expected = options is null ? _types.GetDefaultConstructor(_types.TaskCompletionSourceOfObject)
            : _types.GetConstructor(_types.TaskCompletionSourceOfObject, typeof(TaskCreationOptions));
        if (constructor != expected || options is not null && options != (int)TaskCreationOptions.RunContinuationsAsynchronously ||
            allocationOffset < 0 || storeOffset <= allocationOffset || _body.ILOffset <= storeOffset ||
            _allocations.Any(a => a.Store == storeOffset))
            throw new InvalidOperationException("Task-source allocation requires its actual literal BCL constructor and completed local store.");
        _allocations.Add((allocationOffset, storeOffset, constructor, options));
    }

    internal void RecordLoad(int offset)
    {
        Mutable();
        if (offset < 0 || _body.ILOffset <= offset || !_loads.Add(offset))
            throw new InvalidOperationException("Task-source loads require distinct completed operations in the actual body.");
    }

    internal void RecordResult(int offset)
    {
        Mutable();
        if (offset < 0 || _body.ILOffset <= offset || !_results.Add(offset))
            throw new InvalidOperationException("Task-source results require distinct completed getter operations in the actual body.");
    }

    internal void Complete(EmittedRuntime runtime)
    {
        Mutable();
        if (runtime.Promise is not { } promise || !ReferenceEquals(_method, promise.ResolveValueWithRootMethod) ||
            _method.DeclaringType is not TypeBuilder { } owner || !owner.IsCreated() || _allocations.Count == 0 ||
            _loads.Count == 0 || _results.Count == 0 || _body.ILOffset == 0)
            throw new InvalidOperationException("Task-source proof requires the completed actual Promise resolution body and operations.");
        _complete = true;
    }

    internal bool Claims(NativeUnprovedCarrierResultProducer result)
    {
        if (!_complete) throw new InvalidOperationException("Task-source proof is incomplete.");
        bool matches = ReferenceEquals(result.Body, _body) && result.Producer == _getter &&
            result.CompletedBodyLength == _body.ILOffset && _results.Contains(result.CallOffset);
        _used |= matches;
        return matches;
    }

    internal void ValidateImage(System.Reflection.PortableExecutable.PEReader pe, MetadataReader metadata)
    {
        if (!_complete) Refuse("The task-source operation records are incomplete");
        if (!_used) return;
        if (metadata.GetGuid(metadata.GetModuleDefinition().Mvid) != _module.ModuleVersionId)
            Refuse("The task-source records belong to another output");
        var methodHandle = MetadataTokens.MethodDefinitionHandle(_method.MetadataToken);
        var method = metadata.GetMethodDefinition(methodHandle);
        var owner = metadata.GetTypeDefinition(method.GetDeclaringType());
        if (method.Attributes != _method.Attributes || metadata.GetString(method.Name) != _method.Name ||
            metadata.GetString(owner.Name) != _method.DeclaringType!.Name ||
            metadata.GetString(owner.Namespace) != (_method.DeclaringType.Namespace ?? "") ||
            MetadataTokens.GetToken(method.GetDeclaringType()) != _method.DeclaringType.MetadataToken)
            Refuse("The task-source method changed owner or identity");
        var methodReader = metadata.GetBlobReader(method.Signature);
        var methodSignature = new SignatureDecoder<string, object?>(new SignatureNames(CheckedReference), metadata, null).DecodeMethodSignature(ref methodReader);
        if (methodSignature.Header.RawValue != 0 || methodSignature.GenericParameterCount != 0 ||
            methodSignature.RequiredParameterCount != 2 || !methodSignature.ParameterTypes.SequenceEqual(["Object", "Object"]) ||
            methodSignature.ReturnType != "System.Threading.Tasks.Task`1<Object>" || methodReader.RemainingBytes != 0)
            Refuse("The actual Promise resolution method signature changed");
        var body = pe.GetMethodBody(method.RelativeVirtualAddress);
        byte[] bytes = body.GetILBytes()!;
        if (bytes.Length != _body.ILOffset || body.LocalSignature.IsNil)
            Refuse("The completed task-source body or local signature changed");
        var signatures = new SignatureNames();
        var localReader = metadata.GetBlobReader(metadata.GetStandaloneSignature(body.LocalSignature).Signature);
        var locals = new SignatureDecoder<string, object?>(signatures, metadata, null).DecodeLocalSignature(ref localReader);
        if (localReader.RemainingBytes != 0) Refuse("The task-source local signature has trailing data");
        var tcsHandle = MetadataTokens.EntityHandle(_module.GetTypeMetadataToken(_types.TaskCompletionSourceOfObject));
        if (tcsHandle.Kind != HandleKind.TypeSpecification || _local.LocalIndex >= locals.Length ||
            locals[_local.LocalIndex] != metadata.GetTypeSpecification((TypeSpecificationHandle)tcsHandle).DecodeSignature(signatures, null))
            Refuse("The task-source local lost its exact closed BCL type");
        ValidateTaskSourceType(metadata, (TypeSpecificationHandle)tcsHandle);
        var instructions = Read(bytes);
        var indices = instructions.Select((row, index) => (row.Offset, index)).ToDictionary(row => row.Offset, row => row.index);
        var predecessors = instructions.Select(_ => new HashSet<int>()).ToArray();
        var successors = instructions.Select(_ => new HashSet<int>()).ToArray();
        for (int i = 0; i < instructions.Count; i++)
        {
            var row = instructions[i];
            if (IsBranch(row.Code))
            {
                int target = row.Offset + row.Size + row.Operand;
                if (!indices.TryGetValue(target, out int destination)) Refuse("A branch has no instruction boundary");
                Edge(i, destination);
            }
            if (row.Code is not (ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Leave or ILOpCode.Leave_s or ILOpCode.Ret or ILOpCode.Throw) && i + 1 < instructions.Count)
                Edge(i, i + 1);
        }
        var stores = new HashSet<int>(); var loads = new HashSet<int>();
        int getterToken = _module.GetMethodMetadataToken(_getter);
        ValidateMember(metadata, getterToken, tcsHandle, getter: true, options: false);
        foreach (var row in instructions)
        {
            var access = LocalAccess(row);
            if (access.Index != _local.LocalIndex) continue;
            if (access.Kind == 1) loads.Add(row.Offset);
            else if (access.Kind == 2) stores.Add(row.Offset);
            else Refuse("The task-source local address escapes");
        }
        if (!loads.SetEquals(_loads) || !stores.SetEquals(_allocations.Select(a => a.Store)))
            Refuse("The final task-source local has unrecorded reads or writes");
        foreach (var allocation in _allocations)
        {
            var created = At(allocation.New); var stored = At(allocation.Store);
            int storeIndex = indices[allocation.Store];
            int constructorToken = _module.GetMethodMetadataToken(allocation.Constructor);
            ValidateMember(metadata, constructorToken, tcsHandle, getter: false, options: allocation.Options is not null);
            if (created.Code != ILOpCode.Newobj || created.Operand != constructorToken ||
                created.Offset + created.Size != stored.Offset || predecessors[storeIndex].Count != 1 ||
                !predecessors[storeIndex].Contains(indices[allocation.New]))
                Refuse("A task-source store is not the recorded direct BCL allocation");
            if (allocation.Options is int options)
            {
                int index = indices[allocation.New];
                if (index == 0 || Constant(instructions[index - 1]) != options || predecessors[index].Count != 1 || !predecessors[index].Contains(index - 1))
                    Refuse("The task-source constructor options are not the recorded literal");
            }
        }
        foreach (int offset in _results)
        {
            var call = At(offset); int index = indices[offset];
            if (call.Code != ILOpCode.Callvirt || call.Operand != getterToken || index == 0 ||
                !_loads.Contains(instructions[index - 1].Offset) || predecessors[index].Count != 1 || !predecessors[index].Contains(index - 1))
                Refuse("A task result does not use its actual local and nonvirtual BCL getter");
        }
        // False means the local is not definitely initialized on this path.
        var states = new bool?[instructions.Count]; var pending = new Queue<int>();
        Visit(0, false);
        foreach (var region in body.ExceptionRegions)
        {
            if (region.Kind != ExceptionRegionKind.Catch)
                Refuse("The task-source exception region is outside its proved schema");
            if (!indices.TryGetValue(region.HandlerOffset, out int handler))
                Refuse("The task-source exception region is outside its proved schema");
            Visit(handler, false);
        }
        while (pending.TryDequeue(out int index))
        {
            bool initialized = states[index]!.Value;
            var access = LocalAccess(instructions[index]);
            if (access.Index == _local.LocalIndex)
            {
                if (access.Kind == 1 && !initialized) Refuse("A task-source read can precede its actual allocation");
                if (access.Kind == 2) initialized = true;
            }
            foreach (int next in successors[index]) Visit(next, initialized);
        }
        return;

        Instruction At(int offset) => indices.TryGetValue(offset, out int index) ? instructions[index] : throw Failure("An operation offset is not an instruction");
        void Edge(int from, int to) { successors[from].Add(to); predecessors[to].Add(from); }
        void Visit(int index, bool initialized)
        {
            bool merged = (states[index] ?? true) && initialized;
            if (states[index] is null || states[index] != merged) { states[index] = merged; pending.Enqueue(index); }
        }
    }

    private readonly record struct Instruction(int Offset, ILOpCode Code, int Operand, int Size);
    private static List<Instruction> Read(byte[] bytes)
    {
        var result = new List<Instruction>();
        for (int offset = 0; offset < bytes.Length;)
        {
            int start = offset; int value = bytes[offset++];
            if (value == 0xfe) { if (offset == bytes.Length) Refuse("A task-source opcode is truncated"); value = 0xfe00 | bytes[offset++]; }
            var code = (ILOpCode)value;
            int size = code switch
            {
                ILOpCode.Nop or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3 or
                ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or
                ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3 or
                ILOpCode.Ldnull or ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2 or
                ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8 or
                ILOpCode.Pop or ILOpCode.Dup or ILOpCode.Ret or ILOpCode.Throw => 0,
                ILOpCode.Ldarg_s or ILOpCode.Ldloc_s or ILOpCode.Stloc_s or ILOpCode.Ldloca_s or ILOpCode.Ldc_i4_s or
                ILOpCode.Br_s or ILOpCode.Brtrue_s or ILOpCode.Brfalse_s or ILOpCode.Leave_s => 1,
                ILOpCode.Ldarg or ILOpCode.Ldloc or ILOpCode.Stloc or ILOpCode.Ldloca => 2,
                ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Ldstr or ILOpCode.Isinst or ILOpCode.Ldftn or
                ILOpCode.Ldc_i4 or ILOpCode.Br or ILOpCode.Brtrue or ILOpCode.Brfalse or ILOpCode.Leave => 4,
                _ => throw Failure("The task-source body contains an unproved opcode")
            };
            if (offset + size > bytes.Length) Refuse("A task-source operand is truncated");
            int operand = size switch { 0 => 0, 1 => bytes[offset], 2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2)), _ => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4)) };
            if (size == 1 && (IsBranch(code) || code == ILOpCode.Ldc_i4_s)) operand = unchecked((sbyte)operand);
            offset += size; result.Add(new(start, code, operand, offset - start));
        }
        if (result.Count == 0) Refuse("The task-source body is empty");
        return result;
    }

    private static bool IsBranch(ILOpCode code) => code is ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Brtrue or ILOpCode.Brtrue_s or ILOpCode.Brfalse or ILOpCode.Brfalse_s or ILOpCode.Leave or ILOpCode.Leave_s;
    private static (int Kind, int Index) LocalAccess(Instruction row) => row.Code switch
    {
        ILOpCode.Ldloc_0 => (1, 0), ILOpCode.Ldloc_1 => (1, 1), ILOpCode.Ldloc_2 => (1, 2), ILOpCode.Ldloc_3 => (1, 3),
        ILOpCode.Stloc_0 => (2, 0), ILOpCode.Stloc_1 => (2, 1), ILOpCode.Stloc_2 => (2, 2), ILOpCode.Stloc_3 => (2, 3),
        ILOpCode.Ldloc or ILOpCode.Ldloc_s => (1, row.Operand), ILOpCode.Stloc or ILOpCode.Stloc_s => (2, row.Operand),
        ILOpCode.Ldloca or ILOpCode.Ldloca_s => (3, row.Operand), _ => (0, -1)
    };
    private static int? Constant(Instruction row) => row.Code switch
    {
        ILOpCode.Ldc_i4_m1 => -1, ILOpCode.Ldc_i4_0 => 0, ILOpCode.Ldc_i4_1 => 1, ILOpCode.Ldc_i4_2 => 2,
        ILOpCode.Ldc_i4_3 => 3, ILOpCode.Ldc_i4_4 => 4, ILOpCode.Ldc_i4_5 => 5, ILOpCode.Ldc_i4_6 => 6,
        ILOpCode.Ldc_i4_7 => 7, ILOpCode.Ldc_i4_8 => 8, ILOpCode.Ldc_i4 or ILOpCode.Ldc_i4_s => row.Operand, _ => null
    };

    private static void ValidateTaskSourceType(MetadataReader metadata, TypeSpecificationHandle handle)
    {
        var reader = metadata.GetBlobReader(metadata.GetTypeSpecification(handle).Signature);
        string actual = new SignatureDecoder<string, object?>(new SignatureNames(CheckedReference), metadata, null).DecodeType(ref reader);
        if (actual != "System.Threading.Tasks.TaskCompletionSource`1<Object>" || reader.RemainingBytes != 0)
            Refuse("The final task-source parent is not the actual closed BCL task source");
    }

    private static void ValidateMember(MetadataReader metadata, int token, EntityHandle parent, bool getter, bool options)
    {
        var handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind != HandleKind.MemberReference) Refuse("A task-source BCL member lost its external MemberRef identity");
        var member = metadata.GetMemberReference((MemberReferenceHandle)handle);
        if (member.Parent != parent || metadata.GetString(member.Name) != (getter ? "get_Task" : ".ctor"))
            Refuse("A task-source BCL member changed owner or name");
        var reader = metadata.GetBlobReader(member.Signature);
        var signature = new SignatureDecoder<string, object?>(new SignatureNames(CheckedReference), metadata, ImmutableArray.Create("Object")).DecodeMethodSignature(ref reader);
        string[] parameters = options ? ["System.Threading.Tasks.TaskCreationOptions"] : [];
        if (signature.Header.RawValue != 0x20 || signature.GenericParameterCount != 0 ||
            signature.RequiredParameterCount != parameters.Length || !signature.ParameterTypes.SequenceEqual(parameters) ||
            signature.ReturnType != (getter ? "System.Threading.Tasks.Task`1<Object>" : "Void") || reader.RemainingBytes != 0)
            Refuse("A task-source BCL member changed its actual instance signature");
    }

    private static string CheckedReference(MetadataReader metadata, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var row = metadata.GetTypeReference(handle);
        string name = metadata.GetString(row.Namespace) + "." + metadata.GetString(row.Name);
        Type expected = name switch
        {
            "System.Threading.Tasks.TaskCompletionSource`1" => typeof(TaskCompletionSource<>),
            "System.Threading.Tasks.Task`1" => typeof(Task<>),
            "System.Threading.Tasks.TaskCreationOptions" => typeof(TaskCreationOptions),
            _ => throw Failure("A task-source BCL signature names an unproved type")
        };
        if (row.ResolutionScope.Kind != HandleKind.AssemblyReference ||
            rawTypeKind != (expected.IsValueType ? (byte)SignatureTypeKind.ValueType : (byte)SignatureTypeKind.Class))
            Refuse("A task-source BCL type lost its actual assembly scope or kind");
        var actual = NativeAssemblyIdentity.Reference(metadata, (AssemblyReferenceHandle)row.ResolutionScope);
        var runtime = expected.Assembly.GetName();
        var runtimeIdentity = new NativeAssemblyIdentity(runtime.Name!, runtime.Version!, runtime.CultureName ?? "",
            Convert.ToHexStringLower(runtime.GetPublicKeyToken() ?? []), 0);
        bool mapped = EmbeddedReferenceAssemblyIndex.Default.TryResolveType(name, out var identity) &&
            actual == new NativeAssemblyIdentity(identity.Name, identity.Version, identity.CultureName,
                Convert.ToHexStringLower(identity.PublicKeyToken.AsSpan()), identity.Flags & ~AssemblyFlags.PublicKey);
        if (actual != runtimeIdentity && !mapped)
            Refuse("A task-source BCL type changed its exact runtime or supported facade identity");
        return name;
    }

    private sealed class SignatureNames(Func<MetadataReader, TypeReferenceHandle, byte, string>? checkedReference = null) : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string elementType, ArrayShape shape) => "array:" + elementType;
        public string GetByReferenceType(string elementType) => "ref:" + elementType;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "function";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
        public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? context, int index) => context is ImmutableArray<string> arguments && index < arguments.Length ? arguments[index] : "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool required) => "modified:" + modifier + ":" + unmodifiedType;
        public string GetPinnedType(string elementType) => "pinned:" + elementType;
        public string GetPointerType(string elementType) => "pointer:" + elementType;
        public string GetPrimitiveType(PrimitiveTypeCode code) => code.ToString();
        public string GetSZArrayType(string elementType) => "[]:" + elementType;
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => "definition:" + MetadataTokens.GetToken(handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => checkedReference?.Invoke(reader, handle, rawTypeKind) ?? "reference:" + MetadataTokens.GetToken(handle);
        public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
    }

    private void Mutable() { if (_complete) throw new InvalidOperationException("Task-source proof emission has completed."); }
    private static CompileException Failure(string reason) => new(DiagnosticCode.NativeGuestClrEscape,
        "Experimental native guest shared-buffer result certificate refused: " + reason + ". Use managed output for this shape.");
    private static void Refuse(string reason) => throw Failure(reason);
}
