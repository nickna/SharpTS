using System.Collections.Immutable;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'vm' module.
/// All methods delegate to VmModuleInterpreter via reflection since vm
/// fundamentally requires the interpreter at runtime (compiles arbitrary strings).
/// </summary>
public sealed class VmModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "vm";

    private static readonly IReadOnlyList<string> _exportedMembers = (ImmutableArray<string>)
    [
        "runInNewContext", "runInThisContext", "runInContext", "createContext", "isContext",
        "compileFunction", "measureMemory", "constants", "Script"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "runInNewContext" => EmitRunInNewContext(emitter, arguments),
            "runInThisContext" => EmitRunInThisContext(emitter, arguments),
            "runInContext" => EmitRunInContext(emitter, arguments),
            "createContext" => EmitCreateContext(emitter, arguments),
            "isContext" => EmitIsContext(emitter, arguments),
            "compileFunction" => EmitCompileFunction(emitter, arguments),
            "measureMemory" => EmitMeasureMemory(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (propertyName == "Script")
        {
            il.Emit(OpCodes.Call, ctx.Runtime!.RequireVm().GetScriptConstructor);
            emitter.SetStackUnknown();
            return true;
        }

        if (propertyName == "constants")
        {
            il.Emit(OpCodes.Call, ctx.Runtime!.RequireVm().GetConstants);
            emitter.SetStackUnknown();
            return true;
        }

        // Methods emitted as null for namespace dict — actual calls go through TryEmitMethodCall
        if (propertyName is "runInNewContext" or "runInThisContext" or "runInContext"
            or "createContext" or "isContext" or "compileFunction" or "measureMemory")
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        return false;
    }

    public bool IsExportedProperty(string memberName) => memberName is "Script" or "constants";

    private static bool EmitRunInNewContext(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // code (string)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // contextObject (optional)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // options (optional)
        if (arguments.Count > 2)
        {
            emitter.EmitExpression(arguments[2]);
            emitter.EmitBoxIfNeeded(arguments[2]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireVm().RunInNewContext);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitRunInContext(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // code (string)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // contextifiedObject
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // options (optional)
        if (arguments.Count > 2)
        {
            emitter.EmitExpression(arguments[2]);
            emitter.EmitBoxIfNeeded(arguments[2]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireVm().RunInContext);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitRunInThisContext(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // code (string)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // options (optional)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireVm().RunInThisContext);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitCreateContext(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // contextObject (optional)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // options (optional)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireVm().CreateContext);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitMeasureMemory(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // options (optional)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireVm().MeasureMemory);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitIsContext(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireVm().IsContext);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitCompileFunction(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // code (string)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // params (string[] - optional)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // options (optional)
        if (arguments.Count > 2)
        {
            emitter.EmitExpression(arguments[2]);
            emitter.EmitBoxIfNeeded(arguments[2]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireVm().CompileFunction);
        emitter.SetStackUnknown();
        return true;
    }
}
