using System.Reflection;
using SharpTS.Compilation.Symbols;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// The reference object and authoritative payload of one lexical binding. Ordinary builds use
/// StrongBox&lt;object&gt;; a projected debug binding uses a source-named display-class cell instead.
/// Capture transport always forwards the reference object, never a copy of its payload.
/// </summary>
internal sealed record LexicalCellStorage(
    Type ReferenceType,
    ConstructorInfo Constructor,
    FieldInfo ValueField,
    string? DebugHolderName = null,
    StateMachineDebugSymbols? DebugOwner = null)
{
    internal static LexicalCellStorage ForDeclaration(CompilationContext context, Token? declaration)
    {
        if (declaration is not null && context.DebugLexicalCellStorages is { } storages
            && storages.TryGetValue(declaration, out var storage))
            return storage;
        return new(context.Types.StrongBoxOfObject, context.Types.StrongBoxOfObjectCtor,
            context.Types.StrongBoxOfObjectValueField);
    }

    internal static LexicalCellStorage ForBinding(CompilationContext context, string name) =>
        ForDeclaration(context, context.LoopBodyCellDeclarations.GetValueOrDefault(name)
            ?? context.LoopBodyCellShadows.GetValueOrDefault(name)
            ?? context.LoopBodyCellCaptures?.GetValueOrDefault(name));

    internal static LexicalCellStorage ForCapture(CompilationContext context, object closure, string name)
    {
        if (context.ClosureAnalyzer is { } analyzer
            && analyzer.TryGetLoopBodyCellCapture(closure, name, out var declaration))
            return ForDeclaration(context, declaration);
        return ForDeclaration(context, null);
    }
}
