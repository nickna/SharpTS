using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Arrow function and closure emission for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    protected override void EmitArrowFunction(Expr.ArrowFunction af)
    {
        // Get the method for this arrow function
        if (!_ctx.ArrowMethods.TryGetValue(af, out var method))
        {
            // Fallback if not found (shouldn't happen with proper collection)
            IL.Emit(OpCodes.Ldnull);
            return;
        }

        // Check if this is a capturing arrow (has display class)
        if (_ctx.DisplayClasses.TryGetValue(af, out var displayClass))
        {
            // Capturing arrow: create display class instance and populate fields
            EmitCapturingArrowFunction(af, method, displayClass);
        }
        else
        {
            // Non-capturing arrow: create TSFunction wrapping static method
            EmitNonCapturingArrowFunction(af, method);
        }
    }

    private void EmitNonCapturingArrowFunction(Expr.ArrowFunction af, MethodBuilder method)
    {
        // Create TSFunction for static method:
        // new TSFunction(null, method)

        // Push null (no target)
        IL.Emit(OpCodes.Ldnull);

        // Get MethodInfo from the method builder using reflection
        // We need to load the method as a MethodInfo at runtime
        // Use Type.GetMethod or RuntimeMethodHandle

        // Load the method as a runtime handle and convert to MethodInfo
        IL.Emit(OpCodes.Ldtoken, method);

        // For static methods on a non-generic type:
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.MethodBase, "GetMethodFromHandle", _ctx.Types.RuntimeMethodHandle));
        IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);

        // Call $TSFunction constructor
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
    }

    private void EmitCapturingArrowFunction(Expr.ArrowFunction af, MethodBuilder method, TypeBuilder displayClass)
    {
        // Get the pre-tracked constructor (we can't call GetConstructors() on TypeBuilder before CreateType)
        if (!_ctx.DisplayClassConstructors.TryGetValue(af, out var displayCtor))
        {
            // Fallback
            IL.Emit(OpCodes.Ldnull);
            return;
        }

        IL.Emit(OpCodes.Newobj, displayCtor);

        // Get captured variables for this arrow using the stored field mapping
        if (!_ctx.DisplayClassFields.TryGetValue(af, out var fieldMap))
        {
            // No fields to populate, just create TSFunction
            IL.Emit(OpCodes.Ldtoken, method);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.MethodBase, "GetMethodFromHandle", _ctx.Types.RuntimeMethodHandle));
            IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);
            IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            return;
        }

        // Populate captured fields
        foreach (var (capturedVar, field) in fieldMap)
        {
            IL.Emit(OpCodes.Dup); // Keep display class on stack

            // Load the captured variable's current value
            if (capturedVar == "this")
            {
                // 'this' is captured - load the enclosing instance
                if (_ctx.IsInstanceMethod)
                {
                    IL.Emit(OpCodes.Ldarg_0);  // Load 'this' from enclosing method
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
            }
            else if (_ctx.TryGetParameter(capturedVar, out var argIndex))
            {
                IL.Emit(OpCodes.Ldarg, argIndex);
                // If parameter is typed (value type), box it for object field storage
                if (_ctx.TryGetParameterType(capturedVar, out var paramType) && paramType != null && paramType.IsValueType)
                {
                    IL.Emit(OpCodes.Box, paramType);
                }
            }
            else if (_ctx.CapturedFields != null && _ctx.CapturedFields.TryGetValue(capturedVar, out var capturedField))
            {
                // Variable is captured from outer closure
                IL.Emit(OpCodes.Ldarg_0); // this (display class)
                IL.Emit(OpCodes.Ldfld, capturedField);
            }
            else
            {
                var local = _ctx.Locals.GetLocal(capturedVar);
                if (local != null)
                {
                    IL.Emit(OpCodes.Ldloc, local);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull); // Variable not found
                }
            }

            IL.Emit(OpCodes.Stfld, field);
        }

        // Create TSFunction: new TSFunction(displayInstance, method)
        // Stack has: displayInstance

        // Load method info
        IL.Emit(OpCodes.Ldtoken, method);
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.MethodBase, "GetMethodFromHandle", _ctx.Types.RuntimeMethodHandle));
        IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);

        // Call $TSFunction constructor
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
    }
}
