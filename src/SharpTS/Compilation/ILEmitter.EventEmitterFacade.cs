using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    // The embedded events module owns the constructor statics used by its
    // helpers. A named import also has a built-in constructor binding for
    // instance construction, so that binding must not redirect these statics
    // to the separate internal $EventEmitter type.
    private bool TryGetEventEmitterFacade(Expr receiver, string property,
        out string className, out TypeBuilder builder)
    {
        className = string.Empty;
        builder = null!;
        if (property != "defaultMaxListeners" || receiver is not Expr.Variable variable)
            return false;
        if (_ctx.ImportedClassAliases?.TryGetValue(variable.Name.Lexeme, out var importedClassName) == true)
        {
            className = importedClassName;
            return _ctx.Classes.TryGetValue(className, out builder!);
        }
        bool imported = _ctx.CurrentModulePath is { } module
            && _ctx.ModuleImportFields?.TryGetValue(module, out var imports) == true
            && imports.ContainsKey(variable.Name.Lexeme);
        bool builtIn = _ctx.BuiltInModuleMethodBindings?.TryGetValue(variable.Name.Lexeme, out var binding) == true
            && binding.ModuleName == "events" && binding.MethodName == "EventEmitter";
        if (!imported && !builtIn) return false;
        className = _ctx.ResolveClassName(_ctx.TypeMap?.Get(variable), variable.Name.Lexeme);
        return _ctx.Classes.TryGetValue(className, out builder!);
    }

    private bool TryEmitEventEmitterFacadeGet(Expr.Get get)
    {
        if (!TryGetEventEmitterFacade(get.Object, get.Name.Lexeme, out var className, out var builder)
            || !_ctx.ClassRegistry!.TryGetCallableStaticField(className, get.Name.Lexeme, builder, out var field))
            return false;
        // Module helper writes use the constructor value's descriptor store.
        // Read that same live value instead of its CLR template field.
        EmitExpression(get.Object);
        EmitBoxIfNeeded(get.Object);
        IL.Emit(OpCodes.Ldstr, get.Name.Lexeme);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.ObjectRead.Property);
        SetStackUnknown();
        return true;
    }
}
