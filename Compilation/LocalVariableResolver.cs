using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Variable resolver for standard IL emission contexts (non-state-machine).
/// Handles locals, parameters, and captured variables from display classes.
/// </summary>
/// <remarks>
/// Resolution order:
/// 1. Parameters (via CompilationContext.TryGetParameter)
/// 2. Local variables (via CompilationContext.Locals)
/// 3. Captured fields (via CompilationContext.CapturedFields)
///
/// Does NOT handle: functions, namespaces, classes, Math (caller handles these as fallback).
/// </remarks>
public class LocalVariableResolver : IVariableResolver
{
    private readonly ILGenerator _il;
    private readonly CompilationContext _ctx;
    private readonly TypeProvider _types;

    /// <summary>
    /// Creates a new resolver for standard IL emission variable access.
    /// </summary>
    /// <param name="il">The IL generator for emitting instructions</param>
    /// <param name="ctx">The compilation context with locals, parameters, and captured fields</param>
    /// <param name="types">The type provider for type checking</param>
    public LocalVariableResolver(ILGenerator il, CompilationContext ctx, TypeProvider types)
    {
        _il = il;
        _ctx = ctx;
        _types = types;
    }

    /// <inheritdoc />
    public StackType? TryLoadVariable(string name)
    {
        // 1. Parameters
        if (_ctx.TryGetParameter(name, out var argIndex))
        {
            _il.Emit(OpCodes.Ldarg, argIndex);
            return StackType.Unknown; // Parameters are always object
        }

        // 2. Locals (with type awareness)
        var local = _ctx.Locals.GetLocal(name);
        if (local != null)
        {
            var localType = _ctx.Locals.GetLocalType(name);
            _il.Emit(OpCodes.Ldloc, local);
            return MapTypeToStackType(localType);
        }

        // 3. Captured fields (closure)
        if (_ctx.CapturedFields?.TryGetValue(name, out var field) == true)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, field);
            return MapTypeToStackType(field.FieldType);
        }

        return null; // Caller handles fallback (Math, classes, functions, namespaces)
    }

    /// <inheritdoc />
    public bool TryStoreVariable(string name)
    {
        // 1. Locals
        if (_ctx.Locals.TryGetLocal(name, out var local))
        {
            _il.Emit(OpCodes.Stloc, local);
            return true;
        }

        // 2. Parameters
        if (_ctx.TryGetParameter(name, out var argIndex))
        {
            _il.Emit(OpCodes.Starg, argIndex);
            return true;
        }

        // 3. Captured fields (auto-detect value/reference type)
        if (_ctx.CapturedFields?.TryGetValue(name, out var field) == true)
        {
            // Use temp local pattern for storing to fields
            // This works for both value and reference type display classes
            var temp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, field);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void LoadThis()
    {
        // 1. Captured this (closure)
        if (_ctx.CapturedFields?.TryGetValue("this", out var thisField) == true)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, thisField);
            return;
        }

        // 2. __this parameter (object method shorthand)
        if (_ctx.TryGetParameter("__this", out var thisArgIndex))
        {
            _il.Emit(OpCodes.Ldarg, thisArgIndex);
            return;
        }

        // 3. Instance method
        if (_ctx.IsInstanceMethod)
        {
            _il.Emit(OpCodes.Ldarg_0);
            return;
        }

        // 4. Static context
        _il.Emit(OpCodes.Ldnull);
    }

    private StackType MapTypeToStackType(Type? type)
    {
        if (type == null) return StackType.Unknown;
        if (_types.IsDouble(type)) return StackType.Double;
        if (_types.IsBoolean(type)) return StackType.Boolean;
        if (_types.IsString(type)) return StackType.String;
        return StackType.Unknown;
    }
}
