using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Signature-only views joining one metadata context's core atoms to the emitter's runtime core.</summary>
internal sealed class ExternalMemberEmission
{
    private readonly MetadataLoadContext _context;
    private readonly ExternalMetadataTypes _identities;
    private readonly Dictionary<Type, Type> _types = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MemberInfo, MemberInfo> _members = new(ReferenceEqualityComparer.Instance);

    internal ExternalMemberEmission(MetadataLoadContext context, ExternalMetadataTypes identities)
    {
        _context = context;
        _identities = identities;
    }

    internal Type CoreAtom(Type value) => _identities.RuntimeAtom(value);
    internal MethodInfo Method(MethodInfo value) => (MethodInfo)Member(value);
    internal ConstructorInfo Constructor(ConstructorInfo value) => (ConstructorInfo)Member(value);
    internal FieldInfo Field(FieldInfo value) => (FieldInfo)Member(value);
    private MemberInfo Member(MemberInfo value)
    {
        if (!_context.GetAssemblies().Any(assembly => ReferenceEquals(assembly, value.Module.Assembly))) return value;
        if (_members.TryGetValue(value, out var cached)) return cached;
        MemberInfo view = value switch
        {
            MethodInfo method => new MethodView(this, method),
            ConstructorInfo constructor => new ConstructorView(this, constructor),
            FieldInfo field => new FieldView(this, field),
            _ => throw new NotSupportedException("Only emission method, constructor and field operands have signature views.")
        };
        _members.Add(value, view);
        return view;
    }

    internal Type Type(Type value)
    {
        if (_identities.TryRuntimeAtom(value, out var atom)) return atom;
        if (!value.IsFunctionPointer && !_identities.Owns(value.Assembly)) return value;
        if (value.IsGenericParameter && value.GetRequiredCustomModifiers().Length == 0 && value.GetOptionalCustomModifiers().Length == 0) return value;
        if (_types.TryGetValue(value, out var cached)) return cached;
        var view = new TypeView(this, value);
        _types.Add(value, view);
        return view;
    }
    private Type[] Types(Type[] values) => Array.ConvertAll(values, Type);
    private static MemberInfo Original(MemberInfo value) => value switch
    {
        MethodView view => view.Original,
        ConstructorView view => view.Original,
        FieldView view => view.Original,
        _ => value
    };
    private static NotSupportedException SignatureOnly() => new("External emission metadata is signature-only and cannot execute or instantiate attributes.");

    private sealed class TypeView(ExternalMemberEmission owner, Type source) : TypeDelegator(source)
    {
        public override Type UnderlyingSystemType => owner._identities.TryRuntimeAtom(typeImpl.UnderlyingSystemType, out var atom) ? atom :
            (typeImpl.HasElementType || typeImpl.IsConstructedGenericType || typeImpl.IsFunctionPointer ? this : typeImpl.UnderlyingSystemType);
        public override bool IsGenericType => typeImpl.IsGenericType;
        public override bool IsGenericTypeDefinition => typeImpl.IsGenericTypeDefinition;
        public override bool IsGenericParameter => typeImpl.IsGenericParameter;
        public override bool ContainsGenericParameters => typeImpl.ContainsGenericParameters;
        public override int GenericParameterPosition => typeImpl.UnderlyingSystemType.GenericParameterPosition;
        public override GenericParameterAttributes GenericParameterAttributes => typeImpl.UnderlyingSystemType.GenericParameterAttributes;
        public override MethodBase? DeclaringMethod => typeImpl.UnderlyingSystemType.DeclaringMethod;
        public override Type? DeclaringType => typeImpl.DeclaringType is { } type ? owner.Type(type) : null;
        public override Type? GetElementType() => typeImpl.GetElementType() is { } element ? owner.Type(element) : null;
        public override Type GetGenericTypeDefinition() => owner.Type(typeImpl.UnderlyingSystemType.GetGenericTypeDefinition());
        public override Type[] GetGenericArguments() => owner.Types(typeImpl.GetGenericArguments());
        public override Type[] GetRequiredCustomModifiers() => owner.Types(typeImpl.GetRequiredCustomModifiers());
        public override Type[] GetOptionalCustomModifiers() => owner.Types(typeImpl.GetOptionalCustomModifiers());
        public override Type GetFunctionPointerReturnType() => owner.Type(typeImpl.GetFunctionPointerReturnType());
        public override Type[] GetFunctionPointerParameterTypes() => owner.Types(typeImpl.GetFunctionPointerParameterTypes());
        public override Type[] GetFunctionPointerCallingConventions() => owner.Types(typeImpl.GetFunctionPointerCallingConventions());
        public override MemberInfo GetMemberWithSameMetadataDefinitionAs(MemberInfo member) =>
            owner.Member(typeImpl.GetMemberWithSameMetadataDefinitionAs(Original(member)));
        [RequiresDynamicCode("This operation constructs metadata-only signature shapes.")]
        [RequiresUnreferencedCode("Metadata-only signature shapes are not runtime reflection roots.")]
        public override Type MakeGenericType(params Type[] typeArguments) =>
            new ConstructedOwnerView(owner, typeImpl, typeArguments);
        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override bool Equals(Type? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }

    // PAB constructs a generic definition's self-instantiation when writing a field
    // reference. This represents that existing signature shape without runtime closure.
    private sealed class ConstructedOwnerView(ExternalMemberEmission owner, Type source, Type[] arguments) : TypeDelegator(source)
    {
        private readonly Type[] _arguments = (Type[])arguments.Clone();
        public override Type UnderlyingSystemType => this;
        public override bool IsTypeDefinition => false;
        public override bool IsGenericType => true;
        public override bool IsGenericTypeDefinition => false;
        public override bool IsConstructedGenericType => true;
        public override bool ContainsGenericParameters => _arguments.Any(type => type.ContainsGenericParameters);
        public override Type GetGenericTypeDefinition() => owner.Type(typeImpl);
        public override Type[] GetGenericArguments() => (Type[])_arguments.Clone();
        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override bool Equals(Type? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }

    private sealed class ParameterView(ExternalMemberEmission owner, ParameterInfo inner) : ParameterInfo
    {
        public override Type ParameterType => owner.Type(inner.ParameterType);
        public override Type GetModifiedParameterType() => owner.Type(inner.GetModifiedParameterType());
        public override Type[] GetRequiredCustomModifiers() => owner.Types(inner.GetRequiredCustomModifiers());
        public override Type[] GetOptionalCustomModifiers() => owner.Types(inner.GetOptionalCustomModifiers());
        public override ParameterAttributes Attributes => inner.Attributes;
        public override int Position => inner.Position;
        public override MemberInfo Member => owner.Member(inner.Member);
        public override string? Name => inner.Name;
        public override object? DefaultValue => inner.DefaultValue;
        public override object? RawDefaultValue => inner.RawDefaultValue;
        public override bool HasDefaultValue => inner.HasDefaultValue;
        public override IList<CustomAttributeData> GetCustomAttributesData() => inner.GetCustomAttributesData();
        public override object[] GetCustomAttributes(bool inherit) => throw SignatureOnly();
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => throw SignatureOnly();
        public override bool IsDefined(Type attributeType, bool inherit) => throw SignatureOnly();
    }

    private sealed class MethodView(ExternalMemberEmission owner, MethodInfo inner) : MethodInfo
    {
        internal MethodInfo Original => inner;
        public override string Name => inner.Name;
        public override Type? DeclaringType => inner.DeclaringType is { } type ? owner.Type(type) : null;
        public override Type? ReflectedType => inner.ReflectedType is { } type ? owner.Type(type) : null;
        public override Module Module => inner.Module;
        public override int MetadataToken => inner.MetadataToken;
        public override MethodAttributes Attributes => inner.Attributes;
        public override RuntimeMethodHandle MethodHandle => throw SignatureOnly();
        public override CallingConventions CallingConvention => inner.CallingConvention;
        public override Type ReturnType => owner.Type(inner.ReturnType);
        public override ParameterInfo ReturnParameter => new ParameterView(owner, inner.ReturnParameter);
        public override ICustomAttributeProvider ReturnTypeCustomAttributes => ReturnParameter;
        public override bool IsGenericMethod => inner.IsGenericMethod;
        public override bool IsGenericMethodDefinition => inner.IsGenericMethodDefinition;
        public override bool IsConstructedGenericMethod => inner.IsConstructedGenericMethod;
        public override bool ContainsGenericParameters => inner.ContainsGenericParameters;
        public override MethodInfo GetGenericMethodDefinition() => owner.Method(inner.GetGenericMethodDefinition());
        public override Type[] GetGenericArguments() => owner.Types(inner.GetGenericArguments());
        public override MethodInfo GetBaseDefinition() => owner.Method(inner.GetBaseDefinition());
        public override MethodImplAttributes GetMethodImplementationFlags() => inner.GetMethodImplementationFlags();
        public override ParameterInfo[] GetParameters() => Array.ConvertAll(inner.GetParameters(), parameter => (ParameterInfo)new ParameterView(owner, parameter));
        public override IList<CustomAttributeData> GetCustomAttributesData() => inner.GetCustomAttributesData();
        public override object[] GetCustomAttributes(bool inherit) => throw SignatureOnly();
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => throw SignatureOnly();
        public override bool IsDefined(Type attributeType, bool inherit) => throw SignatureOnly();
        public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters, CultureInfo? culture) => throw SignatureOnly();
        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }

    private sealed class ConstructorView(ExternalMemberEmission owner, ConstructorInfo inner) : ConstructorInfo
    {
        internal ConstructorInfo Original => inner;
        public override string Name => inner.Name;
        public override Type? DeclaringType => inner.DeclaringType is { } type ? owner.Type(type) : null;
        public override Type? ReflectedType => inner.ReflectedType is { } type ? owner.Type(type) : null;
        public override Module Module => inner.Module;
        public override int MetadataToken => inner.MetadataToken;
        public override MethodAttributes Attributes => inner.Attributes;
        public override RuntimeMethodHandle MethodHandle => throw SignatureOnly();
        public override CallingConventions CallingConvention => inner.CallingConvention;
        public override bool ContainsGenericParameters => inner.ContainsGenericParameters;
        public override MethodImplAttributes GetMethodImplementationFlags() => inner.GetMethodImplementationFlags();
        public override ParameterInfo[] GetParameters() => Array.ConvertAll(inner.GetParameters(), parameter => (ParameterInfo)new ParameterView(owner, parameter));
        public override IList<CustomAttributeData> GetCustomAttributesData() => inner.GetCustomAttributesData();
        public override object[] GetCustomAttributes(bool inherit) => throw SignatureOnly();
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => throw SignatureOnly();
        public override bool IsDefined(Type attributeType, bool inherit) => throw SignatureOnly();
        public override object Invoke(BindingFlags invokeAttr, Binder? binder, object?[]? parameters, CultureInfo? culture) => throw SignatureOnly();
        public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters, CultureInfo? culture) => throw SignatureOnly();
        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }

    private sealed class FieldView(ExternalMemberEmission owner, FieldInfo inner) : FieldInfo
    {
        internal FieldInfo Original => inner;
        public override string Name => inner.Name;
        public override Type? DeclaringType => inner.DeclaringType is { } type ? owner.Type(type) : null;
        public override Type? ReflectedType => inner.ReflectedType is { } type ? owner.Type(type) : null;
        public override Module Module => inner.Module;
        public override int MetadataToken => inner.MetadataToken;
        public override FieldAttributes Attributes => inner.Attributes;
        public override RuntimeFieldHandle FieldHandle => throw SignatureOnly();
        public override Type FieldType => owner.Type(inner.FieldType);
        public override Type GetModifiedFieldType() => owner.Type(inner.GetModifiedFieldType());
        public override Type[] GetRequiredCustomModifiers() => owner.Types(inner.GetRequiredCustomModifiers());
        public override Type[] GetOptionalCustomModifiers() => owner.Types(inner.GetOptionalCustomModifiers());
        public override IList<CustomAttributeData> GetCustomAttributesData() => inner.GetCustomAttributesData();
        public override object? GetRawConstantValue() => inner.GetRawConstantValue();
        public override object[] GetCustomAttributes(bool inherit) => throw SignatureOnly();
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => throw SignatureOnly();
        public override bool IsDefined(Type attributeType, bool inherit) => throw SignatureOnly();
        public override object? GetValue(object? obj) => throw SignatureOnly();
        public override void SetValue(object? obj, object? value, BindingFlags invokeAttr, Binder? binder, CultureInfo? culture) => throw SignatureOnly();
        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
}
