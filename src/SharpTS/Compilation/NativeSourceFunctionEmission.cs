using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Supplies exact source invocation tokens at the callable's creation site.</summary>
internal static class NativeSourceFunctionEmission
{
    // The target object and original MethodInfo are already on the stack.
    internal static void EmitConstructor(ILGenerator il, TypeProvider types, EmittedRuntime runtime,
        MethodInfo method, Type? actualOwner = null, FunctionGenericParameterRegistry? generics = null)
    {
        var source = runtime.NativeCallables?.SourceFunctions;
        if (source is not null && TryEmitInvocationTarget(il, types, method, actualOwner, generics))
            il.Emit(OpCodes.Newobj, source.Constructor);
        else
            il.Emit(OpCodes.Newobj, runtime.FunctionConstruction.Constructor);
    }

    // The original MethodInfo, JS name and length are already on the stack.
    internal static void EmitCachedFactory(ILGenerator il, TypeProvider types, EmittedRuntime runtime,
        MethodInfo method, Type? actualOwner = null, FunctionGenericParameterRegistry? generics = null)
    {
        var source = runtime.NativeCallables?.SourceFunctions;
        if (source is not null && TryEmitInvocationTarget(il, types, method, actualOwner, generics))
            il.Emit(OpCodes.Call, source.GetOrCreate);
        else
            il.Emit(OpCodes.Call, runtime.FunctionConstruction.GetOrCreate);
    }

    // The target, original MethodInfo, JS name and length are already on the stack.
    internal static void EmitCachedConstructor(ILGenerator il, TypeProvider types, EmittedRuntime runtime,
        MethodInfo method, Type? actualOwner = null, FunctionGenericParameterRegistry? generics = null)
    {
        var source = runtime.NativeCallables?.SourceFunctions;
        if (source is not null && TryEmitInvocationTarget(il, types, method, actualOwner, generics))
            il.Emit(OpCodes.Newobj, source.CachedConstructor);
        else
            il.Emit(OpCodes.Newobj, runtime.FunctionConstruction.CachedConstructor);
    }

    private static bool TryEmitInvocationTarget(ILGenerator il, TypeProvider types,
        MethodInfo original, Type? actualOwner, FunctionGenericParameterRegistry? generics)
    {
        MethodInfo target = original;
        if (original.IsGenericMethodDefinition &&
            (original is not MethodBuilder builder || generics?.TryGetErasedValueInvocationTarget(builder, out target!) != true))
            return false;
        Type? owner = actualOwner ?? target.DeclaringType;
        // A generic definition does not identify the current CLR instance.
        // Callers inside a generic owner must supply its instantiated self form.
        if (owner is null || owner.IsGenericTypeDefinition) return false;
        if (owner != target.DeclaringType)
        {
            if (target is not MethodBuilder method || !owner.IsGenericType ||
                owner.GetGenericTypeDefinition() != method.DeclaringType)
                return false;
            target = TypeBuilder.GetMethod(owner, method);
        }
        il.Emit(OpCodes.Ldtoken, target);
        il.Emit(OpCodes.Ldtoken, owner);
        il.Emit(OpCodes.Call, types.MethodBaseGetMethodFromHandleWithType);
        il.Emit(OpCodes.Castclass, types.MethodInfo);
        return true;
    }
}
