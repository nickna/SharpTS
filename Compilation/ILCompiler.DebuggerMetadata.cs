using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private enum EmittedStateMachineKind
    {
        Async,
        Iterator,
        AsyncIterator,
    }

    private void RegisterStateMachine(
        MethodBuilder kickoff,
        TypeBuilder stateMachine,
        MethodBuilder moveNext,
        EmittedStateMachineKind kind,
        params MethodBuilder?[] infrastructure)
    {
        MarkCompilerGenerated(stateMachine);

        Type attributeType = kind switch
        {
            EmittedStateMachineKind.Async =>
                typeof(AsyncStateMachineAttribute),
            EmittedStateMachineKind.Iterator =>
                typeof(IteratorStateMachineAttribute),
            EmittedStateMachineKind.AsyncIterator =>
                typeof(AsyncIteratorStateMachineAttribute),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var stateMachineAttrCtor = attributeType.GetConstructor([typeof(Type)])!;
        kickoff.SetCustomAttribute(
            stateMachineAttrCtor, CustomAttributeEncoder.Encode(stateMachineAttrCtor, stateMachine));

        foreach (MethodBuilder? method in infrastructure)
        {
            if (method is null)
                continue;
            MarkCompilerGenerated(method);
            method.SetCustomAttribute(
                typeof(System.Diagnostics.DebuggerNonUserCodeAttribute)
                    .GetConstructor(Type.EmptyTypes)!,
                CustomAttributeEncoder.EmptyBlob);
        }

        if (EmitDebugSymbols)
            _debugInfo.RecordStateMachine(kickoff, moveNext);
    }

    private static readonly ConstructorInfo _compilerGeneratedCtor =
        typeof(CompilerGeneratedAttribute).GetConstructor(Type.EmptyTypes)!;

    private static void MarkCompilerGenerated(TypeBuilder type) =>
        type.SetCustomAttribute(_compilerGeneratedCtor, CustomAttributeEncoder.EmptyBlob);

    private static void MarkCompilerGenerated(MethodBuilder method) =>
        method.SetCustomAttribute(_compilerGeneratedCtor, CustomAttributeEncoder.EmptyBlob);
}
