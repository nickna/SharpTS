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
            // Check if we have type information for this parameter
            if (_ctx.TryGetParameterType(name, out var paramType) && paramType != null)
            {
                var stackType = MapTypeToStackType(paramType);
                // Box union types immediately so that StackType.Unknown correctly means "boxed object"
                if (stackType == StackType.Unknown && UnionTypeHelper.IsUnionType(paramType))
                {
                    _il.Emit(OpCodes.Box, paramType);
                }
                return stackType;
            }
            return StackType.Unknown; // Fallback for untyped parameters
        }

        // 2. Function display class fields (captured function-local vars)
        // Check this BEFORE regular locals to ensure we use the shared storage
        if (_ctx.CapturedFunctionLocals?.Contains(name) == true &&
            _ctx.FunctionDisplayClassFields?.TryGetValue(name, out var funcDCField) == true)
        {
            if (_ctx.FunctionDisplayClassLocal != null)
            {
                // Direct access from function body - use the local
                _il.Emit(OpCodes.Ldloc, _ctx.FunctionDisplayClassLocal);
                _il.Emit(OpCodes.Ldfld, funcDCField);
            }
            else if (_ctx.CurrentArrowFunctionDCField != null)
            {
                // Access from arrow body - go through $functionDC field
                _il.Emit(OpCodes.Ldarg_0); // Load display class instance
                _il.Emit(OpCodes.Ldfld, _ctx.CurrentArrowFunctionDCField); // Load function display class
                _il.Emit(OpCodes.Ldfld, funcDCField); // Load the variable field
            }
            else
            {
                // Fallback - shouldn't happen
                return null;
            }
            return StackType.Unknown;
        }

        // 3. Locals (with type awareness)
        var local = _ctx.Locals.GetLocal(name);
        if (local != null)
        {
            var localType = _ctx.Locals.GetLocalType(name);
            _il.Emit(OpCodes.Ldloc, local);
            return MapTypeToStackType(localType);
        }

        // 4. Captured fields (closure)
        if (_ctx.CapturedFields?.TryGetValue(name, out var field) == true)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, field);
            return MapTypeToStackType(field.FieldType);
        }

        // 5. Entry-point display class fields (captured top-level vars)
        if (_ctx.CapturedTopLevelVars?.Contains(name) == true &&
            _ctx.EntryPointDisplayClassFields?.TryGetValue(name, out var entryPointField) == true)
        {
            if (_ctx.EntryPointDisplayClassLocal != null)
            {
                // Direct access from entry point - use the local
                _il.Emit(OpCodes.Ldloc, _ctx.EntryPointDisplayClassLocal);
                _il.Emit(OpCodes.Ldfld, entryPointField);
            }
            else if (_ctx.CurrentArrowEntryPointDCField != null)
            {
                // Access from arrow body - go through $entryPointDC field
                _il.Emit(OpCodes.Ldarg_0); // Load display class instance
                _il.Emit(OpCodes.Ldfld, _ctx.CurrentArrowEntryPointDCField); // Load entry-point display class
                _il.Emit(OpCodes.Ldfld, entryPointField); // Load the variable field
            }
            else if (_ctx.EntryPointDisplayClassStaticField != null)
            {
                // Access from module init method - use static field
                _il.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
                _il.Emit(OpCodes.Ldfld, entryPointField);
            }
            else
            {
                // Fallback - shouldn't happen
                return null;
            }
            return StackType.Unknown;
        }

        // 6. Top-level static vars (non-captured)
        if (_ctx.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            _il.Emit(OpCodes.Ldsfld, topLevelField);
            return StackType.Unknown;
        }

        return null; // Caller handles fallback (Math, classes, functions, namespaces)
    }

    /// <inheritdoc />
    public bool TryStoreVariable(string name)
    {
        // 1. Function display class fields (captured function-local vars)
        // Check this BEFORE regular locals to ensure we use the shared storage
        if (_ctx.CapturedFunctionLocals?.Contains(name) == true &&
            _ctx.FunctionDisplayClassFields?.TryGetValue(name, out var funcDCField) == true)
        {
            // Use temp local pattern for storing to fields
            var temp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, temp);

            if (_ctx.FunctionDisplayClassLocal != null)
            {
                // Direct access from function body - use the local
                _il.Emit(OpCodes.Ldloc, _ctx.FunctionDisplayClassLocal);
            }
            else if (_ctx.CurrentArrowFunctionDCField != null)
            {
                // Access from arrow body - go through $functionDC field
                _il.Emit(OpCodes.Ldarg_0); // Load display class instance
                _il.Emit(OpCodes.Ldfld, _ctx.CurrentArrowFunctionDCField); // Load function display class
            }
            else
            {
                // Fallback - shouldn't happen
                return false;
            }

            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, funcDCField);
            return true;
        }

        // 2. Locals
        if (_ctx.Locals.TryGetLocal(name, out var local))
        {
            _il.Emit(OpCodes.Stloc, local);
            return true;
        }

        // 3. Parameters
        if (_ctx.TryGetParameter(name, out var argIndex))
        {
            _il.Emit(OpCodes.Starg, argIndex);
            return true;
        }

        // 4. Captured fields (auto-detect value/reference type)
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

        // 5. Entry-point display class fields (captured top-level vars)
        if (_ctx.CapturedTopLevelVars?.Contains(name) == true &&
            _ctx.EntryPointDisplayClassFields?.TryGetValue(name, out var entryPointField) == true)
        {
            // Use temp local pattern for storing to fields
            var temp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, temp);

            if (_ctx.EntryPointDisplayClassLocal != null)
            {
                // Direct access from entry point - use the local
                _il.Emit(OpCodes.Ldloc, _ctx.EntryPointDisplayClassLocal);
            }
            else if (_ctx.CurrentArrowEntryPointDCField != null)
            {
                // Access from arrow body - go through $entryPointDC field
                _il.Emit(OpCodes.Ldarg_0); // Load display class instance
                _il.Emit(OpCodes.Ldfld, _ctx.CurrentArrowEntryPointDCField); // Load entry-point display class
            }
            else if (_ctx.EntryPointDisplayClassStaticField != null)
            {
                // Access from module init method - use static field
                _il.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
            }
            else
            {
                // Fallback - shouldn't happen
                return false;
            }

            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, entryPointField);
            return true;
        }

        // 6. Top-level static vars (non-captured)
        if (_ctx.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            _il.Emit(OpCodes.Stsfld, topLevelField);
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

        // 4. Static constructor context - 'this' is the class type
        if (_ctx.IsStaticConstructorContext && _ctx.CurrentClassBuilder != null)
        {
            // Load the Type object for the current class
            _il.Emit(OpCodes.Ldtoken, _ctx.CurrentClassBuilder);
            _il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            return;
        }

        // 5. Static context (not in static constructor)
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
