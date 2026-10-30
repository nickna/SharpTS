using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

internal sealed partial class NativeUnionArgumentConversion
{
    internal sealed record FrameworkParameterToken(MethodInfo Member, Type Owner, int MethodOffset,
        int OwnerOffset, int ResolveOffset, IReadOnlyList<ParameterInfo>? Parameters);

    internal ReflectionMethodEmission EmitListAddParameters(ILGenerator body, MethodInfo member, Type owner)
    {
        EnsureSourceMutable();
        int methodOffset = body.ILOffset; body.Emit(OpCodes.Ldtoken, member);
        int ownerOffset = body.ILOffset; body.Emit(OpCodes.Ldtoken, owner);
        int resolveOffset = body.ILOffset; body.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandleWithType);
        body.Emit(OpCodes.Castclass, _types.MethodInfo);
        IReadOnlyList<ParameterInfo>? parameters = null;
        // Capture only the genuine existing TypeProvider result, never inspect
        // an arbitrary caller-supplied MethodInfo as an ownership authority.
        if (ReferenceEquals(member, _types.GetMethod(_types.ListOfObject, "Add", _types.Object)) &&
            ReferenceEquals(owner, _types.ListOfObject))
        {
            try { parameters = Array.AsReadOnly(member.GetParameters()); }
            catch (NotSupportedException) { }
        }
        var token = new FrameworkParameterToken(member, owner, methodOffset, ownerOffset, resolveOffset, parameters);
        var emission = new ReflectionMethodEmission(body, methodOffset, member, member, null, Framework: token);
        _reflectionMethods.Add(emission); return emission;
    }

    private bool ValidListAddParameters(FrameworkParameterToken token, ILGenerator body)
    {
        var expectedOwner = _types.ListOfObject;
        var expectedMember = _types.GetMethod(expectedOwner, "Add", _types.Object);
        if (!ReferenceEquals(token.Member, expectedMember) || !ReferenceEquals(token.Owner, expectedOwner) ||
            token.Owner.GetType() != _types.Object.GetType() || token.Owner.Assembly != _types.CoreAssembly ||
            token.Member.DeclaringType != expectedOwner || token.Member.Module != expectedOwner.Module ||
            token.Owner.ContainsGenericParameters || token.Member.IsGenericMethod || token.Member.ContainsGenericParameters ||
            !token.Member.IsPublic || token.Member.IsStatic || token.Member.ReturnType != _types.Void ||
            token.MethodOffset < 0 || token.OwnerOffset <= token.MethodOffset || token.ResolveOffset <= token.OwnerOffset ||
            body.ILOffset <= token.ResolveOffset || token.Parameters is not [{ ParameterType: var parameter }] || parameter != _types.Object)
            return false;
        var actual = token.Member.GetParameters();
        if (actual.Length != token.Parameters.Count) return false;
        for (int i = 0; i < actual.Length; i++)
            if (actual[i].Member != token.Member || token.Parameters[i].Member != token.Member ||
                actual[i].Position != token.Parameters[i].Position || actual[i].ParameterType != token.Parameters[i].ParameterType ||
                actual[i].Attributes != token.Parameters[i].Attributes || actual[i].Name != token.Parameters[i].Name ||
                !TryCompletedParameter(actual[i].ParameterType, [], out var target) || target is not null)
                return false;
        return true;
    }
}
