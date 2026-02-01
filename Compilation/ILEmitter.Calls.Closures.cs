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
        // Check if this is an async arrow function with a state machine
        if (af.IsAsync && _ctx.AsyncArrowBuilders?.TryGetValue(af, out var arrowBuilder) == true)
        {
            // Async arrow with its own state machine - emit a callable for the stub method
            EmitAsyncArrowFunction(af, arrowBuilder);
            SetStackUnknown();
            return;
        }

        // Get the method for this arrow function
        if (!_ctx.ArrowMethods.TryGetValue(af, out var method))
        {
            // Fallback if not found (shouldn't happen with proper collection)
            IL.Emit(OpCodes.Ldnull);
            SetStackUnknown();
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

        // $TSFunction is a reference type, mark stack as unknown (not a value type)
        SetStackUnknown();
    }

    private void EmitAsyncArrowFunction(Expr.ArrowFunction af, AsyncArrowStateMachineBuilder arrowBuilder)
    {
        // For standalone async arrows, create a TSFunction that wraps the stub method
        // The stub method takes just the parameters (no outer SM for standalone arrows)
        var stubMethod = arrowBuilder.StubMethod;

        // Non-capturing async arrow: new TSFunction(null, stubMethod)
        IL.Emit(OpCodes.Ldnull);

        // Load method info for the stub method
        IL.Emit(OpCodes.Ldtoken, stubMethod);
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.MethodBase, "GetMethodFromHandle", _ctx.Types.RuntimeMethodHandle));
        IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);

        // Create TSFunction
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
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

        // Populate $entryPointDC field if this arrow captures top-level variables
        if (_ctx.ArrowEntryPointDCFields?.TryGetValue(af, out var entryPointDCField) == true)
        {
            if (_ctx.EntryPointDisplayClassLocal != null)
            {
                // In entry point method - use local variable
                IL.Emit(OpCodes.Dup); // Keep display class on stack
                IL.Emit(OpCodes.Ldloc, _ctx.EntryPointDisplayClassLocal);
                IL.Emit(OpCodes.Stfld, entryPointDCField);
            }
            else if (_ctx.CurrentArrowEntryPointDCField != null)
            {
                // In arrow body - get from parent arrow's $entryPointDC field
                IL.Emit(OpCodes.Dup); // Keep display class on stack
                IL.Emit(OpCodes.Ldarg_0); // Load parent display class
                IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowEntryPointDCField);
                IL.Emit(OpCodes.Stfld, entryPointDCField);
            }
            else if (_ctx.EntryPointDisplayClassStaticField != null)
            {
                // Fallback to static field
                IL.Emit(OpCodes.Dup); // Keep display class on stack
                IL.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
                IL.Emit(OpCodes.Stfld, entryPointDCField);
            }
        }

        // Get captured variables for this arrow using the stored field mapping
        if (!_ctx.DisplayClassFields.TryGetValue(af, out var fieldMap))
        {
            // No fields to populate, just create TSFunction
            // Use two-argument GetMethodFromHandle for display class methods
            IL.Emit(OpCodes.Ldtoken, method);
            IL.Emit(OpCodes.Ldtoken, displayClass);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.MethodBase, "GetMethodFromHandle", _ctx.Types.RuntimeMethodHandle, _ctx.Types.RuntimeTypeHandle));
            IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);
            IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            return;
        }

        // Determine if this is a named function expression with self-reference
        string? selfRefName = af.Name?.Lexeme;
        FieldBuilder? selfRefField = null;
        if (selfRefName != null && fieldMap.TryGetValue(selfRefName, out var srf))
        {
            selfRefField = srf;
        }

        // If we have a self-reference, save the display class instance for later use
        LocalBuilder? displayClassLocal = null;
        if (selfRefField != null)
        {
            displayClassLocal = IL.DeclareLocal(displayClass);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stloc, displayClassLocal);
        }

        // Populate captured fields (except self-reference which needs to be set after TSFunction creation)
        foreach (var (capturedVar, field) in fieldMap)
        {
            // Skip self-reference field - will be populated after TSFunction is created
            if (selfRefField != null && capturedVar == selfRefName) continue;

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
            else if (_ctx.CapturedTopLevelVars?.Contains(capturedVar) == true &&
                     _ctx.EntryPointDisplayClassFields?.TryGetValue(capturedVar, out var entryPointField) == true)
            {
                // Variable is a captured top-level var in entry-point display class
                if (_ctx.EntryPointDisplayClassLocal != null)
                {
                    IL.Emit(OpCodes.Ldloc, _ctx.EntryPointDisplayClassLocal);
                }
                else if (_ctx.EntryPointDisplayClassStaticField != null)
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Ldfld, entryPointField);
            }
            else if (_ctx.TopLevelStaticVars != null && _ctx.TopLevelStaticVars.TryGetValue(capturedVar, out var topLevelField))
            {
                // Variable is a top-level static var
                IL.Emit(OpCodes.Ldsfld, topLevelField);
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

        // Load method info - use two-argument GetMethodFromHandle for display class methods
        // This is required because the method's parameter types need the declaring type context to resolve
        IL.Emit(OpCodes.Ldtoken, method);
        IL.Emit(OpCodes.Ldtoken, displayClass);
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.MethodBase, "GetMethodFromHandle", _ctx.Types.RuntimeMethodHandle, _ctx.Types.RuntimeTypeHandle));
        IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);

        // Call $TSFunction constructor
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);

        // For named function expressions, populate the self-reference field with the TSFunction
        // Stack now has: TSFunction
        if (selfRefField != null && displayClassLocal != null)
        {
            // Save TSFunction to local
            var tsFuncLocal = IL.DeclareLocal(_ctx.Runtime!.TSFunctionType);
            IL.Emit(OpCodes.Stloc, tsFuncLocal);

            // Load display class, load TSFunction, store in self-reference field
            IL.Emit(OpCodes.Ldloc, displayClassLocal);
            IL.Emit(OpCodes.Ldloc, tsFuncLocal);
            IL.Emit(OpCodes.Stfld, selfRefField);

            // Leave TSFunction on stack for the return value
            IL.Emit(OpCodes.Ldloc, tsFuncLocal);
        }
    }
}
