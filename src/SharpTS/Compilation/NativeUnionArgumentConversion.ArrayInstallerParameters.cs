using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

internal sealed partial class NativeUnionArgumentConversion
{
    internal sealed record ArrayInstallerContribution(ReflectionMethodEmission? Token, MethodBuilder Caller, ILGenerator Body, int CallOffset);
    internal sealed record CompletedArrayInstaller(MethodBuilder Method, ILGenerator Body, int ParameterOffset,
        int FactoryOffset, int CompletedBodyLength, IReadOnlyList<ArrayInstallerContribution> Contributors);
    private (MethodBuilder Method, ILGenerator Body, int ParameterOffset, int FactoryOffset, int Length)? _arrayInstaller;
    private readonly List<ArrayInstallerContribution> _arrayContributors = [];
    private CompletedArrayInstaller? _completedArrayInstaller;
    internal CompletedArrayInstaller? ArrayInstallerProof => IsComplete ? _completedArrayInstaller :
        throw new InvalidOperationException("Array installer parameter producers have not completed.");

    internal int EmitArrayInstallerFactory(ILGenerator body, MethodBuilder installer, int parameterOffset)
    {
        EnsureSourceMutable();
        if (!ReferenceEquals(installer.Module, _module) || !ReferenceEquals(installer.GetILGenerator(), body) ||
            parameterOffset < 0 || body.ILOffset <= parameterOffset)
            throw new InvalidOperationException("Array installer parameters require their actual arg2 load and factory call.");
        var producer = new ReflectionMethodEmission(body, parameterOffset, installer, null, null, ArrayInstaller: true);
        _reflectionMethods.Add(producer);
        EmitReflectionFactory(body, producer);
        return _reflectionConsumers[^1].Offset;
    }

    internal void RecordArrayInstaller(MethodBuilder method, ILGenerator body, int parameterOffset, int factoryOffset)
    {
        EnsureSourceMutable();
        if (_arrayInstaller is not null || !ReferenceEquals(method.Module, _module) ||
            !ReferenceEquals(method.GetILGenerator(), body) || parameterOffset < 0 || factoryOffset <= parameterOffset || body.ILOffset <= factoryOffset)
            throw new InvalidOperationException("Array installer parameters require their actual private body and factory operation.");
        _arrayInstaller = (method, body, parameterOffset, factoryOffset, body.ILOffset);
    }

    internal void RecordArrayInstallerContributor(MethodBuilder installer, ReflectionMethodEmission? token, MethodBuilder caller, ILGenerator body, int callOffset)
    {
        EnsureSourceMutable();
        if (_arrayInstaller is not { } actual || !ReferenceEquals(installer, actual.Method) || !ReferenceEquals(caller.Module, _module) ||
            !ReferenceEquals(caller.GetILGenerator(), body) || callOffset < 0 || body.ILOffset <= callOffset)
            throw new InvalidOperationException("Array installer contributors must reach its actual private call.");
        _arrayContributors.Add(new(token, caller, body, callOffset));
    }

    internal bool ValidateArrayInstallerProof(CompletedArrayInstaller proof)
    {
        if (_arrayInstaller is not { } actual || !ReferenceEquals(proof.Method, actual.Method) ||
            !ReferenceEquals(proof.Body, actual.Body) || proof.ParameterOffset != actual.ParameterOffset ||
            proof.FactoryOffset != actual.FactoryOffset || proof.CompletedBodyLength != actual.Length || proof.Body.ILOffset != actual.Length ||
            !ReferenceEquals(proof.Method.Module, _module) || proof.Method.DeclaringType is not TypeBuilder owner || !owner.IsCreated() ||
            !proof.Method.IsPrivate || !proof.Method.IsStatic || proof.Method.IsGenericMethod || proof.Method.ReturnType != _types.Void ||
            !proof.Method.GetParameters().Select(p => p.ParameterType).SequenceEqual([_types.DictionaryStringObject, _types.String, _types.MethodInfo, _types.Int32]) ||
            proof.Contributors.Count == 0 || proof.Contributors.Count != _arrayContributors.Count ||
            !_reflectionConsumers.Any(row => row.Producer.ArrayInstaller && ReferenceEquals(row.Producer.Producer, proof.Method) &&
                ReferenceEquals(row.Body, proof.Body) && row.Producer.Offset == proof.ParameterOffset && row.Offset == proof.FactoryOffset &&
                ReferenceEquals(row.Consumer, ReflectionGetOrCreate)))
            return false;
        for (int i = 0; i < proof.Contributors.Count; i++)
        {
            var contribution = proof.Contributors[i];
            if (!ReferenceEquals(contribution, _arrayContributors[i]) || contribution.Token is not { Literal: MethodBuilder member } token ||
                !_reflectionMethods.Any(actualToken => ReferenceEquals(actualToken, token)) ||
                !ReferenceEquals(token.Body, contribution.Body) || !ReferenceEquals(contribution.Caller.Module, _module) ||
                !ReferenceEquals(contribution.Caller.GetILGenerator(), contribution.Body) ||
                contribution.Caller.DeclaringType is not TypeBuilder callerOwner || !callerOwner.IsCreated() ||
                contribution.Caller.IsGenericMethod || callerOwner.IsGenericType || token.Offset < 0 || contribution.CallOffset <= token.Offset ||
                contribution.Body.ILOffset <= contribution.CallOffset || !ReferenceEquals(member.Module, _module) || member.IsGenericMethod ||
                member.DeclaringType is not TypeBuilder memberOwner || !memberOwner.IsCreated() || memberOwner.IsGenericType || member.GetILGenerator().ILOffset == 0)
                return false;
            try
            {
                foreach (var parameter in member.GetParameters())
                    if (!TryCompletedParameter(parameter.ParameterType, [], out var target) || target is not null) return false;
            }
            catch (NotSupportedException) { return false; }
        }
        return true;
    }

    private void CompleteArrayInstallerParameters(HashSet<MethodInfo> members)
    {
        if (!_reflectionConsumers.Any(row => row.Producer.ArrayInstaller)) return;
        if (_arrayInstaller is not { } actual)
        {
            _reflectionBlockers.Add("Actual array installer parameter writer has not completed"); return;
        }
        _completedArrayInstaller = new(actual.Method, actual.Body, actual.ParameterOffset, actual.FactoryOffset,
            actual.Length, _arrayContributors.AsReadOnly());
        if (!ValidateArrayInstallerProof(_completedArrayInstaller))
        {
            _reflectionBlockers.Add("Unproved actual array installer parameter contributor or writer"); return;
        }
        foreach (var contribution in _arrayContributors) members.Add(contribution.Token!.Literal!);
    }
}
