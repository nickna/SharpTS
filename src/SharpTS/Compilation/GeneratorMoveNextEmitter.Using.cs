using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    private sealed record UsingResourceFields(FieldBuilder Resource, FieldBuilder Method);
    private readonly Dictionary<Stmt.Using, UsingResourceFields> _usingRegistrations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt, UsingResourceFields> _usingCleanups = new(ReferenceEqualityComparer.Instance);
    private int _usingCounter;

    // Keep original source nodes (yield/loop/closure analysis keys) intact. Each
    // acquired resource encloses the remaining statements in an implied finally,
    // so existing suspension and non-local-exit routing also handles cleanup.
    private List<Stmt> LowerUsingScopes(List<Stmt> statements)
    {
        var result = new List<Stmt>();
        for (int i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not Stmt.Using declaration || _usingRegistrations.ContainsKey(declaration))
            {
                result.Add(statements[i]);
                continue;
            }

            var binding = declaration.Bindings[0];
            int id = _usingCounter++;
            string? name = binding.Name?.Lexeme;
            if (name is not null && _analysis.BlockScopeRenames.TryGetValue(binding, out var renamed))
                name = renamed;
            var resource = name is not null ? _builder.GetVariableField(name)! :
                DefineStateMachineField($"<>usingResource{id}", _types.Object);
            var fields = new UsingResourceFields(resource,
                DefineStateMachineField($"<>usingMethod{id}", _types.Object));
            var registration = declaration with { Bindings = [binding] };
            var cleanup = new Stmt.Expression(new Expr.Literal(null));
            _usingRegistrations.Add(registration, fields);
            _usingCleanups.Add(cleanup, fields);
            result.Add(registration);
            var remaining = statements.Skip(i + 1).ToList();
            if (declaration.Bindings.Count > 1)
                remaining.Insert(0, declaration with { Bindings = declaration.Bindings.Skip(1).ToList() });
            result.Add(new Stmt.TryCatch(LowerUsingScopes(remaining), null, null, [cleanup]));
            return result;
        }
        return result;
    }

    protected override void EmitBlock(Stmt.Block block) =>
        base.EmitBlock(new Stmt.Block(LowerUsingScopes(block.Statements)));

    protected override void EmitStatementCore(Stmt statement)
    {
        if (_usingCleanups.TryGetValue(statement, out var resource))
        {
            UsingResourceEmitter.Dispose(_il, _types, _ctx!.Runtime!,
                () => LoadUsingField(resource.Resource), () => LoadUsingField(resource.Method));
            return;
        }
        base.EmitStatementCore(statement);
    }

    protected override void EmitUsingDeclaration(Stmt.Using declaration)
    {
        var fields = _usingRegistrations[declaration];
        EmitExpression(declaration.Bindings[0].Initializer);
        EnsureBoxed();
        StoreUsingField(fields.Resource);
        _il.Emit(OpCodes.Ldnull);
        StoreUsingField(fields.Method);
        UsingResourceEmitter.Acquire(_il, _types, _ctx!.Runtime!,
            () => LoadUsingField(fields.Resource), () => LoadUsingField(fields.Method),
            () => StoreUsingField(fields.Method));
    }

    private void LoadUsingField(FieldBuilder field)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, field);
    }

    private void StoreUsingField(FieldBuilder field)
    {
        var value = _il.DeclareLocal(_types.Object);
        _il.Emit(OpCodes.Stloc, value);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, value);
        _il.Emit(OpCodes.Stfld, field);
    }
}
