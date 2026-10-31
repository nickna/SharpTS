using SharpTS.Parsing;

namespace SharpTS.Compilation;

internal sealed partial class NativeClrAdmission
{
    // These are actual selected CLR effects, unlike the possible effects used by catch
    // admission. Unrelated unknown guest operations must not taint every async body.
    private void CheckExceptionalCallableIngress()
    {
        if (_effects.Count == 0) return;
        var known = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var effect in _effects)
            if (_owners.GetValueOrDefault(effect) is { } owner) known.Add(owner);
        bool changed;
        do
        {
            changed = false;
            foreach (var (node, owner) in _owners)
            {
                if (owner is null || known.Contains(owner) || node is not Expr.Call call) continue;
                var target = ResolveCallable(call.Callee, new(ReferenceEqualityComparer.Instance));
                if (target is not null && known.Contains(target)) changed |= known.Add(owner);
            }
        } while (changed);

        CheckDeferredModuleIngress(known);
        foreach (var owner in known)
        {
            if (owner is Stmt.Function function)
            {
                if (function.IsAsync || function.IsGenerator)
                    Fail("async/generator source body has unimplemented CLR exceptional ingress", function.Name);
                else if (IsPublishedMember(function))
                    Fail("class/namespace source member has unimplemented CLR exceptional ingress", function.Name);
                else
                {
                    var bindings = _functions.Where(pair => ReferenceEquals(pair.Value, owner)).Select(pair => pair.Key).ToArray();
                    if (bindings.Length == 0) Fail("effectful source callable has no checked binding", function.Name);
                    foreach (var binding in bindings)
                    {
                        if (_exports.Contains(binding) || _parents.GetValueOrDefault(function) is Stmt.Export)
                            Fail("exported source callable carries CLR exceptional effects", function.Name);
                        foreach (var read in _reads.GetValueOrDefault(binding) ?? [])
                            CheckExceptionalCallableUse(read, owner, new(ReferenceEqualityComparer.Instance));
                    }
                }
            }
            else if (owner is Expr.ArrowFunction arrow)
            {
                if (arrow.IsAsync || arrow.IsGenerator || arrow.IsMethod)
                    Fail("async/generator/method expression has unimplemented CLR exceptional ingress", arrow.Name);
                else CheckExceptionalCallableUse(arrow, owner, new(ReferenceEqualityComparer.Instance));
            }
            else Fail("class accessor/initializer has unimplemented CLR exceptional ingress", owner);
        }
    }

    private bool IsPublishedMember(Stmt.Function function)
    {
        object child = function;
        while (_parents.TryGetValue(child, out var parent) && parent is not null)
        {
            if (parent is Stmt.Class or Expr.ClassExpr or Stmt.Namespace) return true;
            if (parent is Stmt.Function or Expr.ArrowFunction) return false;
            child = parent;
        }
        return false;
    }

    private void CheckExceptionalCallableUse(Expr expression, object target, HashSet<Expr> seen)
    {
        if (!seen.Add(expression)) return;
        if (_sharedSourceUses.Contains(expression))
        {
            Fail("effectful source callable has ambiguous shared original uses", expression);
            return;
        }
        var parent = _parents.GetValueOrDefault(expression);
        // Intentionally separate from _typedEdges: a CLR delegate argument is not a
        // safe exceptional boundary, even if all of its ordinary arguments are scalar.
        if (parent is Expr.Call call && ReferenceEquals(call.Callee, expression) &&
            ReferenceEquals(ResolveCallable(call.Callee, new(ReferenceEqualityComparer.Instance)), target)) return;
        if (parent is Expr.Grouping or Expr.TypeAssertion or Expr.Satisfies or Expr.NonNullAssertion or Expr.Instantiation)
        {
            CheckExceptionalCallableUse((Expr)parent, target, seen);
            return;
        }
        if (parent is Stmt.Const declaration && ReferenceEquals(declaration.Initializer, expression) &&
            _types.GetValueBinding(declaration.Name) is { } binding && _constants.ContainsKey(binding) && !_exports.Contains(binding))
        {
            foreach (var read in _reads.GetValueOrDefault(binding) ?? []) CheckExceptionalCallableUse(read, target, seen);
            return;
        }
        Fail("source callable with CLR exceptional effects crosses an unsupported callback, capture, return/export, heap or dynamic invocation boundary", parent);
    }
}
