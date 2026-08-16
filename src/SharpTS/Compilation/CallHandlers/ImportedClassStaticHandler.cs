using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles static method calls on imported classes (import X = require('./module') where module exports a class).
/// </summary>
public class ImportedClassStaticHandler : ICallHandler
{
    public int Priority => 72;

    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
    {
        if (call.Callee is not Expr.Get importedGet ||
            importedGet.Object is not Expr.Variable importedVar)
            return false;

        var ctx = emitter.Context;
        if (ctx.ImportedClassAliases?.TryGetValue(importedVar.Name.Lexeme, out var importedQualifiedClassName) != true ||
            importedQualifiedClassName == null ||
            !ctx.Classes.TryGetValue(importedQualifiedClassName, out var importedClassBuilder))
            return false;

        if (!ctx.ClassRegistry!.TryGetCallableStaticMethod(importedQualifiedClassName, importedGet.Name.Lexeme, importedClassBuilder, out var callableMethod))
            return false;

        var il = emitter.IL;
        var methodParams = callableMethod!.GetParameters();
        emitter.EmitStaticCallArguments(call.Arguments, methodParams);
        il.Emit(OpCodes.Call, callableMethod);
        emitter.SetStackUnknown();
        return true;
    }
}
