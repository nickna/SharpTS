using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation.Symbols;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private Dictionary<object, StateMachineDebugSymbols>? _debugHoistedBindings;
    private readonly Dictionary<Token, LexicalCellStorage> _debugLexicalCellStorages = new(ReferenceEqualityComparer.Instance);

    private StateMachineDebugSymbols GetDebugHoistedBindings(
        IReadOnlyList<Stmt.Parameter> parameters, IReadOnlyList<Stmt>? body,
        IReadOnlyDictionary<object, string>? renames, StateMachineDebugSymbols? ancestor = null)
    {
        _debugHoistedBindings ??= new(ReferenceEqualityComparer.Instance);
        object key = (object?)body ?? parameters;
        if (!_debugHoistedBindings.TryGetValue(key, out StateMachineDebugSymbols? symbols))
        {
            _debugHoistedBindings.Add(key, symbols = StateMachineDebugSymbols.Create(parameters, body, renames, ancestor?.DescendantSlotBase ?? 0));
            DefineDebugLexicalCellStorages(symbols);
        }
        return symbols;
    }

    private void DefineDebugLexicalCellStorages(StateMachineDebugSymbols symbols)
    {
        foreach (StateMachineDebugSymbols.Binding binding in symbols.Bindings)
        {
            if (!_closures.Analyzer.IsLoopBodyCellDeclaration(binding.SourceToken)
                || _closures.Analyzer.GetLoopBodyCellOwner(binding.SourceToken) is not
                    Stmt.Function { IsAsync: true, IsGenerator: false }
                || _debugLexicalCellStorages.ContainsKey(binding.SourceToken))
                continue;

            // The projected field IS the cell payload. Body and closure operations both use this
            // descriptor, so debugger reads cannot observe a stale state-machine mirror.
            var cell = EmitTypeDefinitions.DefineType(_moduleBuilder,
                $"<>c__DisplayClass_LexicalCell{_debugLexicalCellStorages.Count}",
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, _types.Object);
            MarkCompilerGenerated(cell);
            var payload = cell.DefineField(symbols.DefineDisplayClassFieldName(binding.StorageName),
                _types.Object, FieldAttributes.Public);
            var constructor = cell.DefineConstructor(MethodAttributes.Public,
                CallingConventions.Standard, [_types.Object]);
            var il = constructor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, payload);
            il.Emit(OpCodes.Ret);
            _debugLexicalCellStorages.Add(binding.SourceToken,
                new(cell, constructor, payload, symbols.DefineLexicalCellHolderName(binding.StorageName), symbols));
        }
    }

    private void FinalizeDebugLexicalCellTypes()
    {
        foreach (LexicalCellStorage storage in _debugLexicalCellStorages.Values)
            ((TypeBuilder)storage.ReferenceType).CreateType();
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
                GeneratorBlockScopeRenamer.Compute(arrow, preserveDebugBindings: true,
                    outerCaptureNames: arrow.IsGenerator ? null :
                        _closures.Analyzer.GetLoopBodyCellCaptures(arrow)?.Keys).Renames,
                GetDebugArrowAncestorBindings(arrow)),
            _ => null,
        };
    }

    private StateMachineDebugSymbols? GetDebugArrowAncestorBindings(
        Expr.ArrowFunction arrow, StateMachineDebugSymbols? ancestor = null)
    {
        var lexicalParent = GetDebugHoistedBindings(_arrowEnclosingCallable.GetValueOrDefault(arrow));
        if (lexicalParent is not null && (ancestor is null || lexicalParent.DescendantSlotBase > ancestor.DescendantSlotBase))
            ancestor = lexicalParent;
        // A synchronous relay need not have its own state-machine symbols, but the incoming cell
        // still belongs to an ancestor's slot space. Reserve that space before allocating own slots.
        if (_closures.Analyzer.GetLoopBodyCellCaptures(arrow) is { } captures)
            foreach (Token declaration in captures.Values)
            {
                // Cache creation can precede the source owner's definition pass. Resolve through
                // declaration identity before allocating child slots, rather than trusting names.
                if (!_debugLexicalCellStorages.ContainsKey(declaration)
                    && _closures.Analyzer.GetLoopBodyCellOwner(declaration) is
                        Stmt.Function { IsAsync: true, IsGenerator: false } ownerFunction)
                    GetDebugHoistedBindings(ownerFunction);
                if (_debugLexicalCellStorages.TryGetValue(declaration, out var storage)
                    && storage.DebugOwner is { } owner
                    && (ancestor is null || owner.DescendantSlotBase > ancestor.DescendantSlotBase))
                    ancestor = owner;
            }
        return ancestor;
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
        {
            builder.DebugSymbols = builder is AsyncArrowStateMachineBuilder arrow
                ? GetDebugHoistedBindings(parameters, body, renames, GetDebugArrowAncestorBindings(arrow.Arrow, ancestor))
                : GetDebugHoistedBindings(parameters, body, renames, ancestor);
            builder.DebugLexicalCellStorages = _debugLexicalCellStorages;
        }
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
