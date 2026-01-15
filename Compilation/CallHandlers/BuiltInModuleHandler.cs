using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles built-in module method calls: path.join, fs.readFileSync, os.platform, etc.
/// Delegates to BuiltInModuleEmitterRegistry for module-specific emission.
/// </summary>
public class BuiltInModuleHandler : ICallHandler
{
    public int Priority => 40;

    public bool TryHandle(ILEmitter emitter, Expr.Call call)
    {
        // Must be a method call on a variable (e.g., path.join())
        if (call.Callee is not Expr.Get builtInGet ||
            builtInGet.Object is not Expr.Variable builtInVar)
        {
            return false;
        }

        var ctx = emitter.Context;
        if (ctx.BuiltInModuleNamespaces == null || ctx.BuiltInModuleEmitterRegistry == null)
            return false;

        // Check if this variable is a known built-in module
        if (!ctx.BuiltInModuleNamespaces.TryGetValue(builtInVar.Name.Lexeme, out var builtInModuleName))
            return false;

        // Get the emitter for this module
        var builtInEmitter = ctx.BuiltInModuleEmitterRegistry.GetEmitter(builtInModuleName);
        if (builtInEmitter == null)
            return false;

        if (!builtInEmitter.TryEmitMethodCall(emitter, builtInGet.Name.Lexeme, call.Arguments))
            return false;

        emitter.ResetStackType();
        return true;
    }
}
