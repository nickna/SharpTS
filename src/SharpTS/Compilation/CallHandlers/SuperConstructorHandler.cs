using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles super() and super.constructor() calls in derived class constructors.
/// Resolves the parent constructor from class declarations or class expressions.
/// </summary>
public class SuperConstructorHandler : ICallHandler
{
    public int Priority => 10; // Highest priority — must run before other handlers

    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
    {
        // Must be super() or super.constructor()
        if (call.Callee is not Expr.Super superExpr)
            return false;
        if (superExpr.Method != null && superExpr.Method.Lexeme != "constructor")
            return false;

        var ctx = emitter.Context;

        // Try class declaration constructors first
        var parentCtor = ctx.CurrentSuperclassName != null
            ? ctx.ClassRegistry?.GetConstructor(ctx.CurrentSuperclassName)
            : null;
        if (parentCtor != null)
        {
            EmitSuperCtorCall(emitter, parentCtor, call.Arguments);
            return true;
        }

        // Try built-in Error type constructors
        if (ctx.CurrentSuperclassName != null && BuiltInNames.IsErrorTypeName(ctx.CurrentSuperclassName))
        {
            EmitSuperErrorCtorCall(emitter, call.Arguments, ctx.CurrentSuperclassName);
            return true;
        }

        // Built-in Array (#233): chain to $Array's ctor-args constructor,
        // which applies ECMA-262 Array(...) semantics (single numeric arg
        // sets the length; otherwise args become elements). Only reached when
        // no user class claimed the name via ClassRegistry above.
        if (ctx.CurrentSuperclassName == "Array" && ctx.Runtime?.TSArrayCtorFromCtorArgs != null)
        {
            EmitSuperArrayCtorCall(emitter, call.Arguments);
            return true;
        }

        // Built-in Promise (#242): run the executor through
        // PromiseFromExecutor and chain the resulting Task<object?> to
        // $Promise's constructor.
        if (ctx.CurrentSuperclassName == "Promise" && ctx.Runtime?.TSPromiseCtor != null)
        {
            EmitSuperPromiseCtorCall(emitter, call.Arguments);
            return true;
        }

        // Try class expression constructors
        if (ctx.CurrentClassExpr != null &&
            ctx.ClassExprSuperclass?.TryGetValue(ctx.CurrentClassExpr, out var superclassName) == true &&
            superclassName != null)
        {
            ConstructorBuilder? parentExprCtor = null;

            // Check class expression constructors using VarToClassExpr mapping
            if (ctx.VarToClassExpr != null &&
                ctx.VarToClassExpr.TryGetValue(superclassName, out var parentClassExpr) &&
                ctx.ClassExprConstructors != null &&
                ctx.ClassExprConstructors.TryGetValue(parentClassExpr, out var exprCtor))
            {
                parentExprCtor = exprCtor;
            }

            // If not found in class expressions, try class declarations
            parentExprCtor ??= ctx.ClassRegistry?.GetConstructorByQualifiedName(superclassName);

            if (parentExprCtor != null)
            {
                EmitSuperCtorCall(emitter, parentExprCtor, call.Arguments);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Emits a super() call that chains to a built-in Error constructor.
    /// The emitted $Error/$TypeError etc. constructors take (string? message).
    /// </summary>
    private static void EmitSuperErrorCtorCall(IEmitterContext emitter, List<Expr> arguments, string errorTypeName)
    {
        var il = emitter.IL;
        var ctx = emitter.Context;

        // Resolve the parent constructor from the emitted error types
        var baseType = ctx.CurrentClassBuilder?.BaseType;
        if (baseType == null)
        {
            il.Emit(OpCodes.Ldnull);
            emitter.SetStackUnknown();
            return;
        }

        // Find the single-param (string? message) constructor on the error base type
        var ctorParams = new[] { typeof(string) };
        var baseCtor = ctx.Types.TryGetConstructor(baseType, ctorParams);

        if (baseCtor == null)
        {
            // Fallback: try the (string name, string? message) constructor on $Error
            ctorParams = [typeof(string), typeof(string)];
            baseCtor = ctx.Types.TryGetConstructor(baseType, ctorParams);
        }

        if (baseCtor == null)
        {
            il.Emit(OpCodes.Ldnull);
            emitter.SetStackUnknown();
            return;
        }

        il.Emit(OpCodes.Ldarg_0); // this

        if (baseCtor.GetParameters().Length == 1)
        {
            // (string? message) constructor
            if (arguments.Count > 0)
            {
                emitter.EmitExpression(arguments[0]);
                emitter.EmitConversionForParameter(arguments[0], typeof(string));
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }
        }
        else
        {
            // (string name, string? message) constructor
            il.Emit(OpCodes.Ldstr, errorTypeName);
            if (arguments.Count > 0)
            {
                emitter.EmitExpression(arguments[0]);
                emitter.EmitConversionForParameter(arguments[0], typeof(string));
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }
        }

        il.Emit(OpCodes.Call, baseCtor);

        il.Emit(OpCodes.Ldnull); // super() returns undefined
        emitter.SetStackUnknown();
    }

    /// <summary>
    /// Emits a super(...) call that chains to $Array's ctor-args constructor:
    /// builds an object[] from the evaluated arguments and calls
    /// $Array(object?[] ctorArgs).
    /// </summary>
    private static void EmitSuperArrayCtorCall(IEmitterContext emitter, List<Expr> arguments)
    {
        var il = emitter.IL;
        var ctx = emitter.Context;

        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(arguments[i]);
            emitter.EmitBoxIfNeeded(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.TSArrayCtorFromCtorArgs);

        il.Emit(OpCodes.Ldnull); // super() returns undefined
        emitter.SetStackUnknown();
    }

    /// <summary>
    /// Emits a super(executor) call that chains to $Promise's constructor:
    /// PromiseFromExecutor(executor) produces the Task&lt;object?&gt; the base
    /// wraps. PromiseFromExecutor also adopts a raw Task passed in place of
    /// an executor — that's how derived-promise construction (inherited
    /// statics, subclass-typed then results) reuses this same constructor.
    /// </summary>
    private static void EmitSuperPromiseCtorCall(IEmitterContext emitter, List<Expr> arguments)
    {
        var il = emitter.IL;
        var ctx = emitter.Context;

        il.Emit(OpCodes.Ldarg_0); // this
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.PromiseFromExecutor);
        il.Emit(OpCodes.Call, ctx.Runtime!.TSPromiseCtor);

        il.Emit(OpCodes.Ldnull); // super() returns undefined
        emitter.SetStackUnknown();
    }

    private static void EmitSuperCtorCall(IEmitterContext emitter, ConstructorBuilder parentCtor, List<Expr> arguments)
    {
        var il = emitter.IL;
        var ctx = emitter.Context;

        il.Emit(OpCodes.Ldarg_0);

        var ctorParams = parentCtor.GetParameters();
        for (int i = 0; i < arguments.Count; i++)
        {
            emitter.EmitExpression(arguments[i]);
            if (i < ctorParams.Length)
                emitter.EmitConversionForParameter(arguments[i], ctorParams[i].ParameterType);
            else
                emitter.EmitBoxIfNeeded(arguments[i]);
        }

        for (int i = arguments.Count; i < ctorParams.Length; i++)
            emitter.EmitOmittedArgument(ctorParams[i].ParameterType);

        System.Reflection.ConstructorInfo ctorToCall = parentCtor;
        Type? baseType = ctx.CurrentClassBuilder?.BaseType;
        if (baseType != null && baseType.IsGenericType && baseType.IsConstructedGenericType)
            ctorToCall = EmitterTypeHelpers.ResolveConstructor(baseType, parentCtor);

        il.Emit(OpCodes.Call, ctorToCall);
        il.Emit(OpCodes.Ldnull);
        emitter.SetStackUnknown();
    }
}
