using SharpTS.Parsing;

namespace SharpTS.Compilation;

internal static class StaticEvalLowering
{
    internal static bool CanEmitStatement(Stmt statement) => statement is
        Stmt.Expression or Stmt.Var { IsVar: true } or Stmt.Function { Body: not null }
        or Stmt.Directive or Stmt.Block or Stmt.Class or Stmt.For;

    // A lexical arrow provides a fresh variable environment on each execution while
    // retaining the caller's this, arguments, and new.target. Ordinary closure analysis
    // then shares that activation with any functions which escape the eval.
    internal static List<Stmt> IsolateStrictDeclarations(List<Stmt> statements, Token site)
    {
        if (!statements.All(CanEmitStatement)) return statements;
        var bindings = Runtime.ParameterBinder.BodyVarDeclarations(statements).ToList();
        if (bindings.Count == 0) return statements;
        var result = new Token(TokenType.IDENTIFIER, "<eval-completion>", null, site.Line)
            { IsGeneratedTemporary = true };
        var undefined = new Expr.Literal(Runtime.Types.SharpTSUndefined.Instance);
        var prologue = statements.TakeWhile(s => s is Stmt.Directive).Cast<Stmt.Directive>().ToList();
        List<Stmt> body = [new Stmt.Directive("use strict", site),
            new Stmt.Var(result, null, prologue.Count == 0 ? undefined : new Expr.Literal(prologue[^1].Value))];
        var functions = bindings.Where(binding => binding.IsFunction).Select(binding => binding.Name.Lexeme)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (name, _) in bindings.DistinctBy(binding => binding.Name.Lexeme))
            if (!functions.Contains(name.Lexeme))
                body.Add(new Stmt.Var(name, null, undefined, IsVar: true));
        foreach (var statement in statements.Skip(prologue.Count)) body.Add(CaptureCompletion(statement));
        body.Add(new Stmt.Return(site, new Expr.Variable(result)));
        var activation = new Expr.ArrowFunction(null, null, null, [], null, body, null);
        return [new Stmt.Expression(new Expr.Call(activation, site, null, []))];

        Stmt CaptureCompletion(Stmt statement) => statement switch
        {
            Stmt.Var { IsVar: true } variable => variable.Initializer is { } initializer
                ? new Stmt.Expression(new Expr.Assign(variable.Name, initializer))
                : new Stmt.Sequence([]),
            Stmt.Expression { HasEmptyCompletion: false } expression =>
                new Stmt.Expression(new Expr.Assign(result, expression.Expr)),
            Stmt.Block block => block with { Statements = block.Statements.Select(CaptureCompletion).ToList() },
            Stmt.Sequence sequence => sequence with { Statements = sequence.Statements.Select(CaptureCompletion).ToList() },
            Stmt.For loop => new Stmt.Sequence([new Stmt.Expression(new Expr.Assign(result, undefined)),
                loop with { Body = CaptureCompletion(loop.Body) }]),
            Stmt.If conditional => new Stmt.Sequence([new Stmt.Expression(new Expr.Assign(result, undefined)),
                conditional with { ThenBranch = CaptureCompletion(conditional.ThenBranch),
                    ElseBranch = conditional.ElseBranch is null ? null : CaptureCompletion(conditional.ElseBranch) }]),
            _ => statement
        };
    }

    internal static List<Stmt> LowerFunctionDeclarations(List<Stmt> statements, bool direct)
    {
        bool Lower(Stmt statement) => statement is Stmt.Function { Body: not null } function
            && (direct || function is { IsAsync: true, IsGenerator: false });
        if (!statements.Any(Lower))
            return statements;

        List<Stmt> declarations = [];
        List<Stmt> body = [];
        foreach (var statement in statements)
        {
            if (statement is not Stmt.Function function || !Lower(function))
            {
                body.Add(statement);
                continue;
            }

            // The function expression closes over the eval variable environment,
            // including its own mutable binding and siblings initialized later.
            var value = new Expr.ArrowFunction(null, function.TypeParams, function.ThisType,
                function.Parameters, null, function.Body, function.ReturnType,
                HasOwnThis: true, IsAsync: function.IsAsync, IsGenerator: function.IsGenerator,
                ThisTypeNode: function.ThisTypeNode,
                ReturnTypeNode: function.ReturnTypeNode)
            { RuntimeName = function.RuntimeName ?? function.Name.Lexeme };
            declarations.Add(new Stmt.Var(function.Name, null, value, IsVar: true));
        }

        // Declaration instantiation precedes executable statements without
        // displacing the directive prologue or changing eval's completion value.
        int directives = body.TakeWhile(statement => statement is Stmt.Directive).Count();
        body.InsertRange(directives, declarations);
        return body;
    }
}
