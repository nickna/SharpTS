using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles built-in module method calls: path.join, fs.readFileSync, os.platform, etc.
/// Also handles nested calls like util.types.isArray().
/// Delegates to BuiltInModuleEmitterRegistry for module-specific emission, falling back
/// to $Runtime wrapper methods registered via RegisterBuiltInModuleMethod.
/// </summary>
public class BuiltInModuleHandler : ICallHandler
{
    public int Priority => 40;

    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
    {
        var ctx = emitter.Context;
        if (ctx.BuiltInModuleNamespaces == null || ctx.BuiltInModuleEmitterRegistry == null)
            return false;

        // Try direct module method call: module.method()
        if (call.Callee is Expr.Get builtInGet &&
            builtInGet.Object is Expr.Variable builtInVar &&
            ctx.BuiltInModuleNamespaces.TryGetValue(builtInVar.Name.Lexeme, out var builtInModuleName))
        {
            var builtInEmitter = ctx.BuiltInModuleEmitterRegistry.GetEmitter(builtInModuleName);
            if (builtInEmitter != null &&
                builtInEmitter.TryEmitMethodCall(emitter, builtInGet.Name.Lexeme, call.Arguments))
            {
                emitter.SetStackUnknown();
                return true;
            }

            // Fall back to the $Runtime wrapper registered for this method
            // (RegisterBuiltInModuleMethod — dns.resolve4, fs callbacks, ...).
            // Without this, the call only worked where the namespace object
            // happened to be reachable as an entry-point local: inside any
            // function/arrow body the variable resolved to null and the call
            // silently no-opped (#239).
            if (TryEmitRegisteredModuleMethodCall(emitter, builtInModuleName, builtInGet.Name.Lexeme, call.Arguments))
                return true;
        }

        // Try nested module method call: module.namespace.method() (e.g., util.types.isArray)
        if (call.Callee is Expr.Get nestedGet &&
            nestedGet.Object is Expr.Get parentGet &&
            parentGet.Object is Expr.Variable moduleVar &&
            ctx.BuiltInModuleNamespaces.TryGetValue(moduleVar.Name.Lexeme, out var nestedModuleName))
        {
            var nestedEmitter = ctx.BuiltInModuleEmitterRegistry.GetEmitter(nestedModuleName);
            if (nestedEmitter != null)
            {
                // Try with combined name: "types.isArray"
                var combinedName = $"{parentGet.Name.Lexeme}.{nestedGet.Name.Lexeme}";
                if (nestedEmitter.TryEmitMethodCall(emitter, combinedName, call.Arguments))
                {
                    emitter.SetStackUnknown();
                    return true;
                }
            }
        }

        // Try named import static method call: Readable.from(), EventEmitter.once(), etc.
        if (call.Callee is Expr.Get namedGet &&
            namedGet.Object is Expr.Variable namedVar &&
            ctx.BuiltInModuleMethodBindings?.TryGetValue(namedVar.Name.Lexeme, out var namedBinding) == true)
        {
            var namedEmitter = ctx.BuiltInModuleEmitterRegistry.GetEmitter(namedBinding.ModuleName);
            if (namedEmitter != null)
            {
                var combinedKey = $"{namedBinding.MethodName}.{namedGet.Name.Lexeme}";
                if (namedEmitter.TryEmitMethodCall(emitter, combinedKey, call.Arguments))
                {
                    emitter.SetStackUnknown();
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Emits a direct static call to a $Runtime module wrapper registered via
    /// RegisterBuiltInModuleMethod. Only fires for the uniform object-in/object-out
    /// wrapper shape; anything else keeps the previous fallthrough behavior.
    /// </summary>
    private static bool TryEmitRegisteredModuleMethodCall(
        IEmitterContext emitter, string moduleName, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var helper = ctx.Runtime?.GetBuiltInModuleMethod(moduleName, methodName);
        if (helper == null)
            return false;

        var parameters = helper.GetParameters();
        if (parameters.Any(p => p.ParameterType != typeof(object)) ||
            (helper.ReturnType != typeof(object) && helper.ReturnType != typeof(void)))
            return false;

        var il = ctx.IL;

        // Evaluate every argument into a temp local first: extra arguments must
        // still be evaluated for side effects, and state-machine emitters require
        // an empty IL stack across an await inside any argument.
        var temps = new List<LocalBuilder>();
        foreach (var arg in arguments)
        {
            emitter.EmitExpression(arg);
            emitter.EmitBoxIfNeeded(arg);
            var temp = il.DeclareLocal(typeof(object));
            il.Emit(OpCodes.Stloc, temp);
            temps.Add(temp);
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            if (i < temps.Count)
                il.Emit(OpCodes.Ldloc, temps[i]);
            else
                il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, helper);
        if (helper.ReturnType == typeof(void))
            il.Emit(OpCodes.Ldnull);
        emitter.SetStackUnknown();
        return true;
    }
}
