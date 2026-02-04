using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    protected override void EmitArrowFunction(Expr.ArrowFunction af)
    {
        // Check for async arrow functions first
        if (af.IsAsync)
        {
            EmitAsyncArrowFunction(af);
            return;
        }

        // Get the method for this arrow function (pre-compiled)
        if (_ctx!.ArrowMethods == null || !_ctx.ArrowMethods.TryGetValue(af, out var method))
        {
            // Fallback if not found
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        // Check if this is a capturing arrow (has display class)
        if (_ctx.DisplayClasses != null && _ctx.DisplayClasses.TryGetValue(af, out var displayClass))
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

    private void EmitAsyncArrowFunction(Expr.ArrowFunction af)
    {
        // Get the async arrow state machine builder
        if (_ctx?.AsyncArrowBuilders == null ||
            !_ctx.AsyncArrowBuilders.TryGetValue(af, out var arrowBuilder))
        {
            throw new CompileException(
                "Async arrow function not registered with state machine builder.");
        }

        // Create a TSFunction that wraps the stub method
        // The stub takes (outer state machine boxed, params...) and returns Task<object>
        // We pass the SelfBoxed reference (not a new box) to share state with the arrow

        // Load the SelfBoxed field - this is the same boxed instance the runtime is using
        if (_builder.SelfBoxedField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.SelfBoxedField);
        }
        else
        {
            // Fallback: box current state machine (won't share mutations correctly)
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldobj, _builder.StateMachineType);
            _il.Emit(OpCodes.Box, _builder.StateMachineType);
        }

        // Load the stub method
        _il.Emit(OpCodes.Ldtoken, arrowBuilder.StubMethod);
        _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        _il.Emit(OpCodes.Castclass, typeof(MethodInfo));

        // Create TSFunction(target: boxed outer SM, method: stub)
        _il.Emit(OpCodes.Newobj, _ctx!.Runtime!.TSFunctionCtor);

        SetStackUnknown();
    }

    private void EmitCapturingArrowFunction(Expr.ArrowFunction af, MethodBuilder method, TypeBuilder displayClass)
    {
        // Get the pre-tracked constructor
        if (_ctx!.DisplayClassConstructors == null || !_ctx.DisplayClassConstructors.TryGetValue(af, out var displayCtor))
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        // Create display class instance
        _il.Emit(OpCodes.Newobj, displayCtor);

        // Populate $entryPointDC field if this arrow captures top-level variables
        if (_ctx.ArrowEntryPointDCFields?.TryGetValue(af, out var entryPointDCField) == true &&
            _ctx.EntryPointDisplayClassStaticField != null)
        {
            _il.Emit(OpCodes.Dup); // Keep display class on stack
            _il.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
            _il.Emit(OpCodes.Stfld, entryPointDCField);
        }

        // Get captured variables field mapping
        if (_ctx.DisplayClassFields == null || !_ctx.DisplayClassFields.TryGetValue(af, out var fieldMap))
        {
            // No fields to populate, just create TSFunction
            _il.Emit(OpCodes.Ldtoken, method);
            _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            _il.Emit(OpCodes.Castclass, typeof(MethodInfo));
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            SetStackUnknown();
            return;
        }

        // Populate captured fields from async state machine context
        foreach (var (capturedVar, field) in fieldMap)
        {
            _il.Emit(OpCodes.Dup); // Keep display class instance on stack

            // Try to load the captured variable from various sources:
            // 1. Hoisted local/parameter in state machine
            var hoistedField = _builder.GetVariableField(capturedVar);
            if (hoistedField != null)
            {
                _il.Emit(OpCodes.Ldarg_0);  // State machine ref
                _il.Emit(OpCodes.Ldfld, hoistedField);
            }
            // 2. Hoisted 'this' reference
            else if (capturedVar == "this" && _builder.ThisField != null)
            {
                _il.Emit(OpCodes.Ldarg_0);  // State machine ref
                _il.Emit(OpCodes.Ldfld, _builder.ThisField);
            }
            // 3. Regular local variable (not hoisted)
            else if (_ctx.Locals.TryGetLocal(capturedVar, out var local))
            {
                _il.Emit(OpCodes.Ldloc, local);
            }
            // 4. Fallback: null
            else
            {
                _il.Emit(OpCodes.Ldnull);
            }

            _il.Emit(OpCodes.Stfld, field);
        }

        // Create TSFunction: new TSFunction(displayInstance, method)
        // Stack has: displayInstance
        _il.Emit(OpCodes.Ldtoken, method);
        _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        _il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
        SetStackUnknown();
    }

    private void EmitNonCapturingArrowFunction(Expr.ArrowFunction af, MethodBuilder method)
    {
        // Create TSFunction for static method:
        // new TSFunction(null, method)

        // Push null (no target)
        _il.Emit(OpCodes.Ldnull);

        // Load the method as a runtime handle and convert to MethodInfo
        _il.Emit(OpCodes.Ldtoken, method);

        // For static methods on a non-generic type:
        _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod(
            "GetMethodFromHandle",
            [typeof(RuntimeMethodHandle)])!);
        _il.Emit(OpCodes.Castclass, typeof(MethodInfo));

        // Call TSFunction constructor
        _il.Emit(OpCodes.Newobj, _ctx!.Runtime!.TSFunctionCtor);
        SetStackUnknown();
    }

    /// <summary>
    /// Try to emit a direct method call for known class instance types.
    /// Returns true if direct dispatch was emitted, false to fall back to runtime dispatch.
    /// </summary>
    private bool TryEmitDirectMethodCall(Expr receiver, string methodName, List<Expr> arguments)
    {
        // Try to get type information for the receiver
        var receiverType = _ctx?.TypeMap?.Get(receiver);

        // Only handle Instance types (e.g., let f: Foo = new Foo())
        if (receiverType is not TypeSystem.TypeInfo.Instance instance)
            return false;

        // Extract the class name from the instance's class type
        string? simpleClassName = instance.ClassType switch
        {
            TypeSystem.TypeInfo.Class c => c.Name,
            _ => null
        };
        if (simpleClassName == null)
            return false;

        // Resolve to qualified name for multi-module compilation
        string className = _ctx!.ResolveClassName(simpleClassName);

        // Look up the method in the class hierarchy
        var methodBuilder = _ctx.ResolveInstanceMethod(className, methodName);
        if (methodBuilder == null)
            return false;

        // Get the class type builder to cast the receiver
        if (!_ctx.Classes.TryGetValue(className, out var classType))
            return false;

        // Get method parameters for typed emission
        var methodParams = methodBuilder.GetParameters();
        int expectedParamCount = methodParams.Length;

        // IMPORTANT: In async context, await can happen in arguments
        // Emit all arguments first and store to temps before emitting receiver
        List<LocalBuilder> argTemps = [];
        foreach (var arg in arguments)
        {
            EmitExpression(arg);
            EnsureBoxed();
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            argTemps.Add(temp);
        }

        // Now emit receiver and cast
        EmitExpression(receiver);
        EnsureBoxed();
        _il.Emit(OpCodes.Castclass, classType);

        // Load all arguments back onto stack with proper type conversions
        for (int i = 0; i < argTemps.Count; i++)
        {
            _il.Emit(OpCodes.Ldloc, argTemps[i]);
            if (i < methodParams.Length)
            {
                var targetType = methodParams[i].ParameterType;
                if (targetType.IsValueType && targetType != typeof(object))
                {
                    _il.Emit(OpCodes.Unbox_Any, targetType);
                }
            }
        }

        // Pad missing optional arguments with appropriate default values
        for (int i = arguments.Count; i < expectedParamCount; i++)
        {
            EmitDefaultForType(methodParams[i].ParameterType);
        }

        // Emit the virtual call
        _il.Emit(OpCodes.Callvirt, methodBuilder);
        SetStackUnknown();
        return true;
    }

    /// <summary>
    /// Emits a default value for the given type.
    /// </summary>
    private void EmitDefaultForType(Type type)
    {
        if (type == typeof(double))
        {
            _il.Emit(OpCodes.Ldc_R8, 0.0);
        }
        else if (type == typeof(int))
        {
            _il.Emit(OpCodes.Ldc_I4_0);
        }
        else if (type == typeof(bool))
        {
            _il.Emit(OpCodes.Ldc_I4_0);
        }
        else if (type == typeof(float))
        {
            _il.Emit(OpCodes.Ldc_R4, 0.0f);
        }
        else if (type == typeof(long))
        {
            _il.Emit(OpCodes.Ldc_I8, 0L);
        }
        else if (type.IsValueType)
        {
            var local = _il.DeclareLocal(type);
            _il.Emit(OpCodes.Ldloca, local);
            _il.Emit(OpCodes.Initobj, type);
            _il.Emit(OpCodes.Ldloc, local);
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
        }
    }
}
