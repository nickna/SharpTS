using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed record TypedArrayDeclaration(TypeInfo Definition, BindingSymbol? Constructor);
    private readonly Dictionary<string, TypedArrayDeclaration> _typedArrayDeclarations = new(StringComparer.Ordinal);
    private TypeInfo? _typedArrayArrayBufferDeclaration;
    private TypeInfo? _typedArraySharedBufferDeclaration;

    private void ResetTypedArrayDeclarations()
    {
        _typedArrayDeclarations.Clear();
        _typedArrayArrayBufferDeclaration = null;
        _typedArraySharedBufferDeclaration = null;
    }

    private void CaptureTypedArrayDeclarations(TypeEnvironment environment)
    {
        foreach (string name in BuiltInNames.TypedArrayNames)
            if (environment.GetTypeBinding(name) is TypeInfo.Interface or TypeInfo.GenericInterface)
                _typedArrayDeclarations[name] = new(environment.GetTypeBinding(name)!, environment.GetValueBinding(name));
        _typedArrayArrayBufferDeclaration = environment.GetTypeBinding("ArrayBuffer");
        _typedArraySharedBufferDeclaration = environment.GetTypeBinding("SharedArrayBuffer");
    }

    private bool TryGetDeclaredTypedArrayView(TypeInfo receiver, out TypeInfo.Interface view)
    {
        receiver = RefreshCompletedInterfaceBinding(receiver);
        TypeInfo definition = receiver is TypeInfo.InstantiatedGeneric instance
            ? RefreshCompletedInterfaceBinding(instance.GenericDefinition) : receiver;
        foreach (var declaration in _typedArrayDeclarations.Values)
        {
            if (!ReferenceEquals(definition, RefreshCompletedInterfaceBinding(declaration.Definition))) continue;
            TypeInfo? projected = receiver;
            if (projected is TypeInfo.GenericInterface generic)
                projected = new TypeInfo.InstantiatedGeneric(generic, generic.TypeParams.Cast<TypeInfo>().ToList());
            if (projected is TypeInfo.InstantiatedGeneric instantiated)
                projected = FlattenInstantiatedInterface(instantiated);
            if (projected is TypeInfo.Interface result)
            {
                view = result;
                return true;
            }
        }
        view = null!;
        return false;
    }

    private TypeInfo DeclaredTypedArrayReceiver(TypeInfo receiver) =>
        TryGetDeclaredTypedArrayView(receiver, out var view) ? view : receiver;

    private bool IsDeclaredTypedArrayReceiver(TypeInfo? receiver) => receiver switch
    {
        TypeInfo.TypeParameter { Constraint: { } constraint } => IsDeclaredTypedArrayReceiver(constraint),
        TypeInfo.Union union => union.FlattenedTypes.Any(IsDeclaredTypedArrayReceiver),
        null => false,
        _ => TryGetDeclaredTypedArrayView(receiver, out _),
    };

    private void ObserveDeclaredTypedArrayRead(Expr.GetIndex read, TypeInfo? receiver, TypeInfo result)
    {
        // An interface is a semantic contract, not proof of a packed runtime carrier.
        // Keep arbitrary/missing values boxed even when a scalar annotation says otherwise.
        if (_typeMap.Get(read.Index) is { } index && IsNumber(index)
            && IsDeclaredTypedArrayReceiver(receiver) && (IsNumber(result) || IsBigInt(result)))
            _typeMap.MarkDeclaredTypedArrayValueRead(read);
    }

    private bool IsShadowedDeclaredTypedArrayConstructor(string name) =>
        _typedArrayDeclarations.TryGetValue(name, out var declaration)
        && declaration.Constructor is not null
        && !ReferenceEquals(_environment.GetValueBinding(name), declaration.Constructor);

    private TypeInfo.TypedArray TypedArrayConstructorType(string name, TypeInfo? input)
    {
        var intrinsic = new TypeInfo.TypedArray(name[..^"Array".Length]);
        if (!_typedArrayDeclarations.TryGetValue(name, out var declaration)
            || declaration.Constructor is null
            || !ReferenceEquals(_environment.GetValueBinding(name), declaration.Constructor))
            return intrinsic;

        TypeInfo definition = RefreshCompletedInterfaceBinding(declaration.Definition);
        TypeInfo? buffer = ConstructorBufferType(input, definition);
        return buffer is null ? intrinsic : intrinsic with
        {
            DeclaredDefinition = definition,
            BufferType = buffer,
        };
    }

    private TypeInfo? ConstructorBufferType(TypeInfo? input, TypeInfo definition)
    {
        TypeInfo? fresh = _typedArrayArrayBufferDeclaration is { } arrayBuffer
            ? RefreshCompletedInterfaceBinding(arrayBuffer) : null;
        if (input is null) return fresh;
        if (input is TypeInfo.Union union)
        {
            var buffers = union.FlattenedTypes.Select(member => ConstructorBufferType(member, definition)).ToList();
            return buffers.Any(buffer => buffer is null) ? null
                : buffers.Cast<TypeInfo>().Distinct(TypeInfoEqualityComparer.Instance).Aggregate(CreateUnion);
        }
        if (input is TypeInfo.TypedArray || TryGetDeclaredTypedArrayView(input, out _)) return fresh;
        if (input is TypeInfo.TypeParameter { Constraint: { } constraint })
        {
            if (IsBufferInput(constraint)) return input;
            // An unrecognized constrained value is not proof of a fresh buffer.
            // Keep only known length/copy inputs precise; otherwise retain its
            // existing constraint, without asserting a fresh allocation.
            if (constraint is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }
                or TypeInfo.NumberLiteral or TypeInfo.Array or TypeInfo.Tuple or TypeInfo.TypedArray
                || TryGetDeclaredTypedArrayView(constraint, out _)
                || NumberIndexOf(constraint) is not null
                    && GetMemberType(constraint, "length") is { } length && IsNumber(length)) return fresh;
            if (constraint is not (TypeInfo.Any or TypeInfo.Unknown)) return input;
            return definition is TypeInfo.GenericInterface { TypeParams.Count: > 0 } open
                ? open.TypeParams[0] : null;
        }
        if (input is TypeInfo.Any or TypeInfo.Unknown or TypeInfo.TypeParameter)
            return definition is TypeInfo.GenericInterface { TypeParams.Count: > 0 } generic
                ? generic.TypeParams[0] : null;
        if (input is TypeInfo.SharedArrayBuffer)
            return _typedArraySharedBufferDeclaration is { } shared
                ? RefreshCompletedInterfaceBinding(shared) : null;
        if (input is TypeInfo.ArrayBuffer) return fresh;
        return IsBufferInput(input) ? input : fresh;
    }

    private bool IsBufferInput(TypeInfo input) => input switch
    {
        TypeInfo.Any or TypeInfo.Unknown => false,
        TypeInfo.Union union => union.FlattenedTypes.All(IsBufferInput),
        TypeInfo.TypeParameter { Constraint: { } constraint } => IsBufferInput(constraint),
        _ => _typedArrayArrayBufferDeclaration is { } buffer
            && IsCompatible(RefreshCompletedInterfaceBinding(buffer), input)
        || _typedArraySharedBufferDeclaration is { } shared
            && IsCompatible(RefreshCompletedInterfaceBinding(shared), input),
    };

    private bool IsTypedArrayDeclarationPair(TypeInfo expected, TypeInfo actual) =>
        (actual is TypeInfo.TypedArray { DeclaredDefinition: not null }
            || TryGetDeclaredTypedArrayArguments(actual, out _, out _))
        && expected is TypeInfo.Interface or TypeInfo.GenericInterface
            or TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface };

    private bool TryGetDeclaredTypedArrayArguments(TypeInfo type, out TypeInfo definition, out TypeInfo? buffer)
    {
        type = RefreshCompletedInterfaceBinding(type);
        definition = type is TypeInfo.InstantiatedGeneric instance
            ? RefreshCompletedInterfaceBinding(instance.GenericDefinition) : type;
        bool captured = false;
        foreach (var declaration in _typedArrayDeclarations.Values)
            if (ReferenceEquals(definition, RefreshCompletedInterfaceBinding(declaration.Definition)))
            {
                captured = true;
                break;
            }
        if (!captured)
        {
            buffer = null;
            return false;
        }
        buffer = type is TypeInfo.InstantiatedGeneric { TypeArguments.Count: 1 } explicitType
            ? explicitType.TypeArguments[0]
            : definition is TypeInfo.GenericInterface { TypeParams.Count: 1 } generic
                ? generic.TypeParams[0] : null;
        return true;
    }

    private bool TryRelateTypedArrayDeclaration(TypeInfo expected, TypeInfo actual, out bool result)
    {
        result = false;
        TypeInfo actualDefinition;
        TypeInfo? actualBuffer;
        if (actual is TypeInfo.TypedArray { DeclaredDefinition: { } intrinsicDefinition, BufferType: { } intrinsicBuffer })
        {
            actualDefinition = intrinsicDefinition;
            actualBuffer = intrinsicBuffer;
        }
        else if (!TryGetDeclaredTypedArrayArguments(actual, out actualDefinition, out actualBuffer)) return false;
        expected = RefreshCompletedInterfaceBinding(expected);
        TypeInfo expectedDefinition = expected is TypeInfo.InstantiatedGeneric instance
            ? RefreshCompletedInterfaceBinding(instance.GenericDefinition) : expected;
        if (!_typedArrayDeclarations.Values.Any(declaration =>
            ReferenceEquals(expectedDefinition, RefreshCompletedInterfaceBinding(declaration.Definition)))) return false;

        // Both surfaces are the same captured declaration. Re-comparing its own
        // generic method overloads would ask an unrelated structural inference
        // question; only the retained buffer argument can differ here.
        if (!ReferenceEquals(expectedDefinition, RefreshCompletedInterfaceBinding(actualDefinition))) return true;
        if (expectedDefinition is not TypeInfo.GenericInterface generic)
        {
            result = true;
            return true;
        }
        if (generic.TypeParams.Count != 1) return true;
        var parameter = generic.TypeParams[0];
        TypeInfo? expectedBuffer = expected is TypeInfo.InstantiatedGeneric { TypeArguments.Count: 1 } explicitType
            ? explicitType.TypeArguments[0] : parameter.Default ?? parameter.Constraint;
        // A bare captured interface with an open parameter discharges that
        // declaration's existing constraint; no default or Any is invented.
        result = expectedBuffer is not null && actualBuffer is not null && IsCompatible(expectedBuffer, actualBuffer)
            && (parameter.Constraint is null || IsCompatible(parameter.Constraint, actualBuffer));
        return true;
    }
}
