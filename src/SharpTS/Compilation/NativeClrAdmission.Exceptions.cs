using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

internal sealed partial class NativeClrAdmission
{
    // Scalar return proof says nothing about an exceptional payload. Keep a separate,
    // conservative effect graph; unclassified invocation/coercion can reenter source.
    private void CheckExceptionIngress()
    {
        if (_effects.Count == 0) return;
        var effectful = new HashSet<object>(ReferenceEqualityComparer.Instance);
        bool changed;
        do
        {
            changed = false;
            foreach (var (node, owner) in _owners)
                if (owner is not null && !effectful.Contains(owner) && MayThrowClr(node, effectful))
                    changed |= effectful.Add(owner);
        } while (changed);

        foreach (var attempt in _tries)
        {
            if (attempt.CatchParam is not { } token) continue;
            var binding = _types.GetValueBinding(token);
            if (binding is not null && !_reads.ContainsKey(binding)) continue;
            foreach (var node in _parents.Keys)
            {
                if (!ReferenceEquals(_owners.GetValueOrDefault(node), _owners.GetValueOrDefault(attempt)) ||
                    !IsInTry(node, attempt) || !MayThrowClr(node, effectful)) continue;
                Fail($"catch binding {token.Lexeme} can receive unsupported CLR exception metadata or unresolved call effects", token);
                break;
            }
        }
    }

    private bool IsInTry(object node, Stmt.TryCatch attempt)
    {
        object child = node;
        while (_parents.TryGetValue(child, out var parent) && parent is not null)
        {
            if (ReferenceEquals(parent, attempt))
                return attempt.TryBlock.Any(statement => ReferenceEquals(statement, child));
            child = parent;
        }
        return false;
    }

    private bool MayThrowClr(object node, HashSet<object> effectful)
    {
        if (node is Expr selected && _effects.Contains(selected)) return true;
        if (node is Expr.Call call)
        {
            var target = ResolveCallable(call.Callee, new(ReferenceEqualityComparer.Instance));
            if (target is null || effectful.Contains(target)) return true;
            // A selected callback can execute even if the callee's ordinary return is scalar.
            return call.Arguments.Any(argument => ResolveCallable(argument, new(ReferenceEqualityComparer.Instance)) is { } callback &&
                effectful.Contains(callback));
        }
        if (node is Expr.Get get && _parents.GetValueOrDefault(get) is Expr.Call selectedCall &&
            _effects.Contains(selectedCall)) return false; // Direct CLR selection bypasses dynamic lookup.
        return node switch
        {
            Expr.Literal or Expr.Variable or Expr.This or Expr.NewTarget or Expr.ArrowFunction or
            Expr.Grouping or Expr.TypeAssertion or Expr.Satisfies or Expr.NonNullAssertion or Expr.Instantiation or
            Expr.Assign or Expr.Comma or Expr.Logical or Expr.NullishCoalescing or Expr.Ternary or Expr.ArrayLiteral or
            Expr.RegexLiteral => false,
            Expr.Binary binary when binary.Operator.Type is TokenType.EQUAL_EQUAL_EQUAL or TokenType.BANG_EQUAL_EQUAL => false,
            Expr.Unary unary when unary.Operator.Type is TokenType.BANG or TokenType.TYPEOF or TokenType.VOID => false,
            Expr.ObjectLiteral literal => literal.Properties.Any(property => property.IsSpread || property.Key is Expr.ComputedKey),
            Expr => true,
            Stmt.ForOf or Stmt.ForIn or Stmt.Using or Stmt.Class => true,
            _ => false
        };
    }

    private object? ResolveCallable(Expr expression, HashSet<BindingSymbol> seen)
    {
        expression = (Expr)Source(expression);
        if (expression is Expr.ArrowFunction arrow) return arrow;
        if (expression is Expr.Grouping grouping) return ResolveCallable(grouping.Expression, seen);
        if (expression is Expr.TypeAssertion assertion) return ResolveCallable(assertion.Expression, seen);
        if (expression is not Expr.Variable variable || _types.GetValueBinding(variable.Name) is not { } binding || !seen.Add(binding)) return null;
        if (_functions.TryGetValue(binding, out var function)) return function;
        return _constants.TryGetValue(binding, out var declaration) ? ResolveCallable(declaration.Initializer, seen) : null;
    }
}
