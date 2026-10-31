using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    // Native module exports have dedicated emitted constructor adapters; some
    // imported value representations are intentionally not constructable. Match
    // the checked import symbol, rather than a spelling also used by a local.
    protected bool TryEmitNativeModuleConstructor(Expr.New construction)
    {
        string? module = null, member = null;
        switch (ExpressionUnwrapper.Unwrap(construction.Callee))
        {
            case Expr.Variable variable when Ctx.TypeMap?.GetValueBinding(variable.Name) is { } namedBinding &&
                Ctx.NativeModuleConstructorBindings?.TryGetValue(namedBinding, out var import) == true:
                (module, member) = import;
                break;
            case Expr.Get { Optional: false, Object: Expr.Variable owner } access when
                Ctx.TypeMap?.GetValueBinding(owner.Name) is { } namespaceBinding &&
                Ctx.NativeModuleNamespaceBindings?.TryGetValue(namespaceBinding, out var importedModule) == true:
                (module, member) = (importedModule, access.Name.Lexeme);
                break;
        }
        if (module is null || member is null) return false;
        bool vmConstructor = (module is "vm" or "primitive:vm") &&
            member is "Script" or "SourceTextModule" or "SyntheticModule";
        bool eventsFacade = module == "stdlib:events" && member == "EventEmitter";
        if (eventsFacade && Ctx.TypeMap?.Get(construction) is not SharpTS.TypeSystem.TypeInfo.EventEmitter)
            return false;
        if (!vmConstructor && !eventsFacade && Ctx.BuiltInModuleEmitterRegistry?.GetEmitter(module)?.GetExportedMembers()
                .Contains(member, StringComparer.Ordinal) != true)
            return false;
        return TryEmitBuiltInConstructor(member, construction.Arguments);
    }
}
