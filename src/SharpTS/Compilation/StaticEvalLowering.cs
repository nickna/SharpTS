using SharpTS.Parsing;

namespace SharpTS.Compilation;

internal static class StaticEvalLowering
{
    internal static List<Stmt> LowerAsyncDeclarations(List<Stmt> statements)
    {
        if (!statements.Any(statement => statement is Stmt.Function { IsAsync: true, IsGenerator: false }))
            return statements;

        List<Stmt> declarations = [];
        List<Stmt> body = [];
        foreach (var statement in statements)
        {
            if (statement is not Stmt.Function { IsAsync: true, IsGenerator: false, Body: not null } function)
            {
                body.Add(statement);
                continue;
            }

            // Use the normal async function-expression state machine. The outer
            // binding remains mutable, just as for an ordinary declaration.
            var value = new Expr.ArrowFunction(null, function.TypeParams, function.ThisType,
                function.Parameters, null, function.Body, function.ReturnType,
                HasOwnThis: true, IsAsync: true, ThisTypeNode: function.ThisTypeNode,
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
