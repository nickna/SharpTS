using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class AsyncFunctionMoveNextEmitter
{
    private sealed record AsyncUsingFields(FieldBuilder Resource, FieldBuilder Method, FieldBuilder? AwaitResult);
    private readonly Dictionary<List<Stmt>, List<Stmt>> _asyncUsingScopes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt.Using, AsyncUsingFields> _asyncUsingRegistrations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt, AsyncUsingFields> _asyncUsingCleanups = new(ReferenceEqualityComparer.Instance);
    private int _asyncUsingCount;

    // Keep every checked source node. Only compiler-owned registration/cleanup
    // receipts are synthetic; existing resumable finally routing owns their exits.
    private List<Stmt> LowerAsyncUsingScopes(List<Stmt> statements)
    {
        if (_asyncUsingScopes.TryGetValue(statements, out var existing)) return existing;
        if (!statements.Any(statement => statement is Stmt.Using))
            return _asyncUsingScopes[statements] = statements;
        List<Stmt> result = [];
        for (int index = 0; index < statements.Count; index++)
        {
            if (statements[index] is not Stmt.Using declaration || _asyncUsingRegistrations.ContainsKey(declaration))
            {
                result.Add(statements[index]);
                continue;
            }
            int id = _asyncUsingCount++;
            var fields = new AsyncUsingFields(
                DefineStateMachineField($"<>usingResource{id}", Types.Object),
                DefineStateMachineField($"<>usingMethod{id}", Types.Object),
                declaration.IsAsync ? DefineStateMachineField($"<>usingAwaitResult{id}", Types.Boolean) : null);
            var registration = declaration with { Bindings = [declaration.Bindings[0]] };
            Expr cleanupExpression = new Expr.Literal(null);
            if (declaration.IsAsync) cleanupExpression = new Expr.Await(declaration.Keyword, cleanupExpression);
            var cleanup = new Stmt.Expression(cleanupExpression);
            _asyncUsingRegistrations.Add(registration, fields);
            _asyncUsingCleanups.Add(cleanup, fields);
            result.Add(registration);
            var remaining = statements.Skip(index + 1).ToList();
            if (declaration.Bindings.Count > 1)
                remaining.Insert(0, declaration with { Bindings = declaration.Bindings.Skip(1).ToList() });
            result.Add(new Stmt.TryCatch(LowerAsyncUsingScopes(remaining), null, null, [cleanup]));
            break;
        }
        _asyncUsingScopes[statements] = result;
        _asyncUsingScopes[result] = result;
        return result;
    }

    protected override void EmitScopedStatements(List<Stmt> statements) => EmitAsyncUsingStatements(statements);

    protected void EmitAsyncUsingStatements(List<Stmt> statements)
    {
        foreach (var declaration in statements.OfType<Stmt.Using>())
            foreach (var binding in declaration.Bindings)
                if (binding.Name is { } token)
                {
                    IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.Sentinels.LexicalUninitializedInstance);
                    EmitStoreVariable(GetBindingStorageName(binding, token.Lexeme));
                }
        foreach (var statement in LowerAsyncUsingScopes(statements)) EmitStatement(statement);
    }

    protected override void EmitStatementCore(Stmt statement)
    {
        if (!_asyncUsingCleanups.TryGetValue(statement, out var fields))
        {
            base.EmitStatementCore(statement);
            return;
        }
        if (fields.AwaitResult is { } awaitResult)
        {
            UsingResourceEmitter.DisposeAsync(IL, Types, Ctx.Runtime!,
                () => LoadAsyncUsingField(fields.Resource), () => LoadAsyncUsingField(fields.Method),
                () => LoadAsyncUsingField(awaitResult));
            EmitAwaitFromValueOnStack(NextAwaitState());
            IL.Emit(OpCodes.Pop);
        }
        else
            UsingResourceEmitter.Dispose(IL, Types, Ctx.Runtime!,
                () => LoadAsyncUsingField(fields.Resource), () => LoadAsyncUsingField(fields.Method));
    }

    protected override void EmitUsingDeclaration(Stmt.Using declaration)
    {
        if (!_asyncUsingRegistrations.TryGetValue(declaration, out var fields))
        {
            base.EmitUsingDeclaration(declaration);
            return;
        }
        var binding = declaration.Bindings[0];
        EmitExpression(binding.Initializer);
        EnsureBoxed();
        StoreAsyncUsingField(fields.Resource);
        IL.Emit(OpCodes.Ldnull);
        StoreAsyncUsingField(fields.Method);
        if (fields.AwaitResult is { } awaitResult)
            UsingResourceEmitter.AcquireAsync(IL, Types, Ctx.Runtime!,
                () => LoadAsyncUsingField(fields.Resource), () => LoadAsyncUsingField(fields.Method),
                () => StoreAsyncUsingField(fields.Method), value =>
                {
                    IL.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                    StoreAsyncUsingField(awaitResult);
                });
        else
            UsingResourceEmitter.Acquire(IL, Types, Ctx.Runtime!,
                () => LoadAsyncUsingField(fields.Resource), () => LoadAsyncUsingField(fields.Method),
                () => StoreAsyncUsingField(fields.Method));
        if (binding.Name is { } token)
        {
            LoadAsyncUsingField(fields.Resource);
            EmitStoreVariable(GetBindingStorageName(binding, token.Lexeme));
        }
    }

    private void LoadAsyncUsingField(FieldBuilder field)
    {
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldfld, field);
    }

    private void StoreAsyncUsingField(FieldBuilder field)
    {
        var value = IL.DeclareLocal(field.FieldType);
        IL.Emit(OpCodes.Stloc, value);
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldloc, value);
        IL.Emit(OpCodes.Stfld, field);
    }
}
