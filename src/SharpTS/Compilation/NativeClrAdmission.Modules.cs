using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

internal sealed partial class NativeClrAdmission
{
    private sealed record DeferredModule(ParsedModule Identity, IReadOnlyList<Stmt> OriginalRoots);
    private readonly List<DeferredModule> _deferredModules = [];

    internal static NativeClrAdmission CaptureModules(IReadOnlyList<ParsedModule> modules, TypeMap types, TypeProvider framework)
    {
        var admission = new NativeClrAdmission(modules.SelectMany(module => module.Statements), types, framework);
        var entry = modules.LastOrDefault();
        foreach (var module in modules)
            if (module.IsDynamicImportOnly || module.IsCommonJs && !ReferenceEquals(module, entry))
                admission._deferredModules.Add(new(module, module.Statements.ToArray()));
        return admission;
    }

    private void CheckDeferredModuleIngress(HashSet<object> knownEffects)
    {
        foreach (var module in _deferredModules)
        {
            foreach (var (node, owner) in _owners)
            {
                if (owner is not null || node is not Expr expression) continue;
                bool selected = _effects.Contains(expression);
                bool transitive = expression is Expr.Call call &&
                    ResolveCallable(call.Callee, new(ReferenceEqualityComparer.Instance)) is { } target && knownEffects.Contains(target);
                if ((!selected && !transitive) || !HasRoot(node, module.OriginalRoots)) continue;
                Fail($"deferred module '{module.Identity.Path}' has unimplemented CLR exceptional initialization ingress", node);
                break;
            }
        }
    }

    private bool HasRoot(object node, IReadOnlyList<Stmt> roots)
    {
        object current = node;
        while (_parents.TryGetValue(current, out var parent) && parent is not null) current = parent;
        return roots.Any(root => ReferenceEquals(root, current));
    }
}
