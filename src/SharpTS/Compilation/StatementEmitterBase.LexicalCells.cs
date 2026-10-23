using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class StatementEmitterBase
{
    protected sealed record LoopBodyCellScopeEntry(string Name, Token Token, LocalBuilder? Cell, FieldBuilder? Field, FieldBuilder? ShadowField, Token? Declaration, Token? Shadow);
    private int _lexicalCellCount;
    protected virtual FieldBuilder? DefineLexicalCellField(string name, Token declaration) => null;

    protected void EnterLoopBodyCellScope(IEnumerable<Stmt> statements, List<LoopBodyCellScopeEntry> prior,
        bool ordinaryHandler = false, bool preserveActiveDeclarations = false)
    {
        foreach (var (declaration, token) in DirectLexicalDeclarations(statements))
        {
            if (preserveActiveDeclarations && Ctx.LexicalCellStorageNames.TryGetValue(token, out var storage)
                && (ReferenceEquals(Ctx.LoopBodyCellDeclarations.GetValueOrDefault(storage), token)
                    || ReferenceEquals(Ctx.LoopBodyCellShadows.GetValueOrDefault(storage), token)))
                continue;
            EnterLexicalCellBinding(declaration, token, prior, ordinaryHandler);
        }
    }

    private protected void EnterOrdinaryBlockCellScope(IEnumerable<Stmt> statements, List<LoopBodyCellScopeEntry> prior)
    {
        foreach (var (declaration, token) in DirectLexicalDeclarations(statements))
            EnterLexicalCellBinding(declaration, token, prior,
                Ctx.ClosureAnalyzer?.IsOrdinaryBlockShadowDeclaration(token) == true);
        foreach (var declaration in statements.OfType<Stmt.Class>())
        {
            if (!Ctx.LoopBodyCellDeclarations.TryGetValue(declaration.Name.Lexeme, out var outer)
                || Ctx.ClosureAnalyzer?.IsFunctionBodyVarDeclaration(outer) != true) continue;
            // A block class owns its binding even when the outer function var
            // uses a cell outside LocalsManager. Restore that cell on block exit.
            Ctx.RuntimeClassLexicalShadows.Add(declaration);
            EnterLexicalCellBinding(declaration, declaration.Name, prior);
        }
    }

    protected bool NeedsLexicalCellScope(IEnumerable<Stmt> statements, bool ordinaryHandler = false) =>
        DirectLexicalDeclarations(statements).Any(binding =>
            NeedsLexicalCellBinding(binding.Declaration, binding.Token, ordinaryHandler));

    private bool NeedsLexicalCellBinding(object declaration, Token token, bool ordinaryHandler)
    {
        string name = GetBindingStorageName(declaration, token.Lexeme);
        return Ctx.ClosureAnalyzer?.IsLoopBodyCellDeclaration(token) == true
            || Ctx.CellBindingLocals.ContainsKey(name)
            || Ctx.LoopBodyCellFields.ContainsKey(name)
            || Ctx.LoopBodyCellShadows.ContainsKey(name)
            || Ctx.LoopBodyCellCaptures?.ContainsKey(name) == true
            || ordinaryHandler && HasActiveCapturedBinding(name);
    }

    protected void EnterLexicalCellBinding(object declaration, Token token, List<LoopBodyCellScopeEntry> prior,
        bool ordinaryHandler = false, bool classStaticBlockVar = false)
    {
            string name = GetBindingStorageName(declaration, token.Lexeme);
            bool selected = Ctx.ClosureAnalyzer?.IsLoopBodyCellDeclaration(token) == true;
            if (!classStaticBlockVar && !NeedsLexicalCellBinding(declaration, token, ordinaryHandler))
                return;

            prior.Add(new(name, token, Ctx.CellBindingLocals.GetValueOrDefault(name), Ctx.LoopBodyCellFields.GetValueOrDefault(name),
                Ctx.LoopBodyCellShadowFields.GetValueOrDefault(name),
                Ctx.LoopBodyCellDeclarations.GetValueOrDefault(name),
                Ctx.LoopBodyCellShadows.GetValueOrDefault(name)));
            Ctx.CellBindingLocals.Remove(name);
            Ctx.LoopBodyCellFields.Remove(name);
            Ctx.LoopBodyCellShadowFields.Remove(name);
            Ctx.LoopBodyCellDeclarations.Remove(name);
            Ctx.LoopBodyCellShadows.Remove(name);

            Ctx.LexicalCellStorageNames[token] = name;
            if (selected)
            {
                var storage = LexicalCellStorage.ForDeclaration(Ctx, token);
                var field = DefineLexicalCellField($"<>lexicalCell{_lexicalCellCount++}", token);
                if (field is not null) IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldsfld, classStaticBlockVar
                    ? Ctx.Runtime!.Sentinels.UndefinedInstance
                    : Ctx.Runtime!.Sentinels.LexicalUninitializedInstance);
                IL.Emit(OpCodes.Newobj, storage.Constructor);
                if (field is not null)
                {
                    IL.Emit(OpCodes.Stfld, field);
                    Ctx.LoopBodyCellFields[name] = field;
                }
                else
                {
                    var cell = Ctx.Locals.DeclareLocal(name, storage.ReferenceType, token);
                    IL.Emit(OpCodes.Stloc, cell);
                    Ctx.CellBindingLocals[name] = cell;
                }
                Ctx.LoopBodyCellDeclarations[name] = token;
            }
            else
            {
                // A plain inner declaration masks the outer box from block entry,
                // including reads before its textual initialization.
                var field = DefineLexicalCellField($"<>lexicalShadow{_lexicalCellCount++}", token);
                if (field is not null) IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldsfld, classStaticBlockVar
                    ? Ctx.Runtime!.Sentinels.UndefinedInstance
                    : Ctx.Runtime!.Sentinels.LexicalUninitializedInstance);
                if (field is not null)
                {
                    IL.Emit(OpCodes.Newobj, LexicalCellStorage.ForDeclaration(Ctx, token).Constructor);
                    IL.Emit(OpCodes.Stfld, field);
                    Ctx.LoopBodyCellShadowFields[name] = field;
                }
                else
                {
                    var shadow = Ctx.Locals.DeclareLocal(name, Ctx.Types.Object,
                        declaration is Stmt.Class ? declaration : token);
                    IL.Emit(OpCodes.Stloc, shadow);
                }
                Ctx.LoopBodyCellShadows[name] = token;
            }
    }

    private bool HasActiveCapturedBinding(string name)
    {
        string functionName = Ctx.ResolveFunctionDCFieldName(name);
        return Ctx.CapturedFunctionLocals?.Contains(functionName) == true
                && Ctx.FunctionDisplayClassFields?.ContainsKey(functionName) == true
                && (Ctx.FunctionDisplayClassLocal is not null || Ctx.CurrentArrowFunctionDCField is not null)
            || Ctx.CapturedArrowLocals?.Contains(name) == true
                && Ctx.ArrowScopeDisplayClassFields?.ContainsKey(name) == true
                && (Ctx.ArrowScopeDisplayClassLocal is not null || Ctx.CurrentArrowScopeDCField is not null)
            || Ctx.ParentArrowCapturedLocals?.Contains(name) == true
                && Ctx.ParentArrowScopeDisplayClassFields?.ContainsKey(name) == true
                && Ctx.CurrentArrowScopeDCField is not null
            || Ctx.ExtraArrowScopeBindings?.ContainsKey(name) == true
            || Ctx.IsInstanceMethod && Ctx.CapturedFields?.ContainsKey(name) == true
            || Ctx.CapturedTopLevelVars?.Contains(name) == true
                && Ctx.EntryPointDisplayClassFields?.ContainsKey(name) == true
                && (Ctx.EntryPointDisplayClassLocal is not null || Ctx.CurrentArrowEntryPointDCField is not null
                    || Ctx.EntryPointDisplayClassStaticField is not null);
    }

    protected bool TryInitializeLexicalShadow(Token token, LocalBuilder value)
    {
        if (!Ctx.LexicalCellStorageNames.TryGetValue(token, out var name)
            || !Ctx.LoopBodyCellShadows.TryGetValue(name, out var shadow)
            || !ReferenceEquals(token, shadow)) return false;
        if (Ctx.LoopBodyCellShadowFields.TryGetValue(name, out var field))
        {
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, field);
            IL.Emit(OpCodes.Ldloc, value);
            IL.Emit(OpCodes.Stfld, LexicalCellStorage.ForDeclaration(Ctx, token).ValueField);
        }
        else
        {
            IL.Emit(OpCodes.Ldloc, value);
            IL.Emit(OpCodes.Stloc, Ctx.Locals.GetLocal(name)!);
        }
        RefreshLexicalCaptureSnapshots(name, value);
        return true;
    }

    protected void ExitLoopBodyCellScope(List<LoopBodyCellScopeEntry> prior)
    {
        for (int i = prior.Count - 1; i >= 0; i--)
        {
            var entry = prior[i];
            if (entry.Cell is { } cell) Ctx.CellBindingLocals[entry.Name] = cell;
            else Ctx.CellBindingLocals.Remove(entry.Name);
            if (entry.Field is { } field) Ctx.LoopBodyCellFields[entry.Name] = field;
            else Ctx.LoopBodyCellFields.Remove(entry.Name);
            if (entry.ShadowField is { } shadowField) Ctx.LoopBodyCellShadowFields[entry.Name] = shadowField;
            else Ctx.LoopBodyCellShadowFields.Remove(entry.Name);
            Ctx.LexicalCellStorageNames.Remove(entry.Token);
            if (entry.Declaration is { } declaration) Ctx.LoopBodyCellDeclarations[entry.Name] = declaration;
            else Ctx.LoopBodyCellDeclarations.Remove(entry.Name);
            if (entry.Shadow is { } shadow) Ctx.LoopBodyCellShadows[entry.Name] = shadow;
            else Ctx.LoopBodyCellShadows.Remove(entry.Name);
        }
    }

    private static IEnumerable<(Stmt Declaration, Token Token)> DirectLexicalDeclarations(IEnumerable<Stmt> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case Stmt.Var { IsVar: false } variable: yield return (statement, variable.Name); break;
                case Stmt.Const constant: yield return (statement, constant.Name); break;
                case Stmt.Sequence sequence:
                    foreach (var item in DirectLexicalDeclarations(sequence.Statements)) yield return item;
                    break;
            }
        }
    }

    protected bool TryInitializeLoopBodyBinding(Stmt.Var declaration)
    {
        var declarationToken = declaration.IsVar
            ? Ctx.ClosureAnalyzer?.GetFunctionBodyVarDeclaration(declaration.Name) ?? declaration.Name
            : declaration.Name;
        string name = Ctx.LexicalCellStorageNames.GetValueOrDefault(declarationToken) ?? declarationToken.Lexeme;
        bool cell = Ctx.LoopBodyCellDeclarations.TryGetValue(name, out var token)
            && ReferenceEquals(token, declarationToken);
        bool shadow = Ctx.LoopBodyCellShadows.TryGetValue(name, out token)
            && ReferenceEquals(token, declarationToken);
        if (!cell && !shadow) return false;

        // Body declaration instantiation already supplied this var's initial
        // value. An initializer-free declaration must preserve it.
        if (declaration.IsVar && declaration.Initializer is null
            && Ctx.ClosureAnalyzer?.IsFunctionBodyVarDeclaration(declaration.Name) == true)
            return true;

        if (declaration.Initializer is { } initializer)
        {
            EmitExpression(initializer);
            EnsureBoxed();
        }
        else IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.Sentinels.UndefinedInstance);
        var value = IL.DeclareLocal(Types.Object);
        IL.Emit(OpCodes.Stloc, value);
        if (cell)
        {
            IL.Emit(OpCodes.Ldloc, value);
            TryInitializeLexicalCell(declarationToken);
        }
        else
        {
            if (Ctx.LoopBodyCellShadowFields.TryGetValue(name, out var field))
            {
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, field);
                IL.Emit(OpCodes.Ldloc, value);
                IL.Emit(OpCodes.Stfld, LexicalCellStorage.ForDeclaration(Ctx, declarationToken).ValueField);
            }
            else
            {
                IL.Emit(OpCodes.Ldloc, value);
                IL.Emit(OpCodes.Stloc, Ctx.Locals.GetLocal(name)!);
            }
            RefreshLexicalCaptureSnapshots(name, value);
        }
        return true;
    }

    protected bool TryInitializeLexicalCell(Token token, bool fresh = false)
    {
        if (!Ctx.LexicalCellStorageNames.TryGetValue(token, out var name)
            || !Ctx.LoopBodyCellDeclarations.TryGetValue(name, out var declaration)
            || !ReferenceEquals(token, declaration)) return false;
        var value = IL.DeclareLocal(Types.Object);
        IL.Emit(OpCodes.Stloc, value);
        if (fresh)
        {
            bool hoisted = Ctx.LoopBodyCellFields.TryGetValue(name, out var field);
            if (hoisted) IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldloc, value);
            IL.Emit(OpCodes.Newobj, LexicalCellStorage.ForDeclaration(Ctx, token).Constructor);
            if (hoisted) IL.Emit(OpCodes.Stfld, field!);
            else IL.Emit(OpCodes.Stloc, Ctx.CellBindingLocals[name]);
        }
        else
        {
            if (Ctx.LoopBodyCellFields.TryGetValue(name, out var field))
            {
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, field);
            }
            else IL.Emit(OpCodes.Ldloc, Ctx.CellBindingLocals[name]);
            IL.Emit(OpCodes.Ldloc, value);
            IL.Emit(OpCodes.Stfld, LexicalCellStorage.ForDeclaration(Ctx, token).ValueField);
        }
        return true;
    }

    protected bool TryCopyLexicalCell(Token token)
    {
        if (!Ctx.LexicalCellStorageNames.TryGetValue(token, out var name)
            || !Ctx.LoopBodyCellDeclarations.ContainsKey(name)) return false;
        LexicalEnvironmentCellAccess.TryLoad(IL, Ctx, name);
        return TryInitializeLexicalCell(token, fresh: true);
    }

    protected void RefreshLexicalCaptureSnapshots(string name, LocalBuilder binding)
    {
        if (!Ctx.LexicalCaptureWriteBacks.TryGetValue(name, out var writeBacks)) return;
        foreach (var (displayClass, field) in writeBacks)
        {
            IL.Emit(OpCodes.Ldloc, displayClass);
            IL.Emit(OpCodes.Ldloc, binding);
            IL.Emit(OpCodes.Stfld, field);
        }
    }

}
