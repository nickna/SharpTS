using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation.Symbols;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private Dictionary<object, StateMachineDebugSymbols>? _debugHoistedBindings;

    private StateMachineDebugSymbols GetDebugHoistedBindings(
        IReadOnlyList<Stmt.Parameter> parameters, IReadOnlyList<Stmt>? body,
        IReadOnlyDictionary<object, string>? renames, StateMachineDebugSymbols? ancestor = null)
    {
        _debugHoistedBindings ??= new(ReferenceEqualityComparer.Instance);
        object key = (object?)body ?? parameters;
        if (!_debugHoistedBindings.TryGetValue(key, out StateMachineDebugSymbols? symbols))
            _debugHoistedBindings.Add(key, symbols = StateMachineDebugSymbols.Create(parameters, body, renames, ancestor?.DescendantSlotBase ?? 0));
        return symbols;
    }

    private StateMachineDebugSymbols? GetDebugHoistedBindings(object? callable)
    {
        if (!EmitDebugSymbols) return null;
        return callable switch
        {
            Stmt.Function function when function.IsAsync || function.IsGenerator => GetDebugHoistedBindings(
                function.Parameters, function.Body,
                GeneratorBlockScopeRenamer.Compute(function, preserveDebugBindings: true).Renames),
            Expr.ArrowFunction arrow when arrow.IsAsync => GetDebugHoistedBindings(
                arrow.Parameters, arrow.BlockBody,
                GeneratorBlockScopeRenamer.Compute(arrow, preserveDebugBindings: true).Renames,
                GetDebugHoistedBindings(_arrowEnclosingCallable.GetValueOrDefault(arrow))),
            _ => null,
        };
    }

    private void ConfigureDebugStateMachineOwner(
        StateMachineBuilderBase builder,
        TypeBuilder containingType,
        string kickoffName)
    {
        if (EmitDebugSymbols)
            builder.SetDebugMetadataOwner(containingType, kickoffName);
    }

    private void ConfigureDebugHoistedBindings(
        StateMachineBuilderBase builder,
        IReadOnlyList<Stmt.Parameter> parameters,
        IReadOnlyList<Stmt>? body,
        IReadOnlyDictionary<object, string>? renames, StateMachineDebugSymbols? ancestor = null)
    {
        if (EmitDebugSymbols)
            builder.DebugSymbols = GetDebugHoistedBindings(parameters, body, renames, ancestor);
    }

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
