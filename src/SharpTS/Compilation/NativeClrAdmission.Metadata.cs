using System.Reflection;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

internal sealed partial class NativeClrAdmission
{
    private void CheckReflectedMetadataCapability(MemberInfo member, Expr source)
    {
        if (member is not MethodInfo { DeclaringType: { } owner } method) return;
        bool executesOrMutatesMetadata = method.Name switch
        {
            nameof(FieldInfo.SetValue) => typeof(FieldInfo).IsAssignableFrom(owner) || typeof(PropertyInfo).IsAssignableFrom(owner),
            nameof(MethodBase.Invoke) => typeof(MethodBase).IsAssignableFrom(owner),
            nameof(Type.InvokeMember) => typeof(Type).IsAssignableFrom(owner),
            nameof(MethodInfo.CreateDelegate) => typeof(MethodInfo).IsAssignableFrom(owner) || typeof(Delegate).IsAssignableFrom(owner),
            _ => false
        };
        // The selected receiver's CLR origin does not prove the owner addressed by its
        // metadata. No exact target-owner proof is implemented for this bounded family.
        if (executesOrMutatesMetadata)
            Fail($"reflected metadata execution/mutation {owner.FullName}.{method.Name} has no proved target ownership", source);
    }
}
