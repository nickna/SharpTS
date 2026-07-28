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
        kickoff.SetCustomAttribute(new CustomAttributeBuilder(
            attributeType.GetConstructor([typeof(Type)])!,
            [stateMachine]));

        foreach (MethodBuilder? method in infrastructure)
        {
            if (method is null)
                continue;
            MarkCompilerGenerated(method);
            method.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(System.Diagnostics.DebuggerNonUserCodeAttribute)
                    .GetConstructor(Type.EmptyTypes)!,
                []));
        }

        if (EmitDebugSymbols)
            _debugInfo.RecordStateMachine(kickoff, moveNext);
    }

    private static void MarkCompilerGenerated(TypeBuilder type) =>
        type.SetCustomAttribute(CompilerGeneratedAttribute());

    private static void MarkCompilerGenerated(MethodBuilder method) =>
        method.SetCustomAttribute(CompilerGeneratedAttribute());

    private static CustomAttributeBuilder CompilerGeneratedAttribute() =>
        new(
            typeof(CompilerGeneratedAttribute).GetConstructor(Type.EmptyTypes)!,
            []);
}
