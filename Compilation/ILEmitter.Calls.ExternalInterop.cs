using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// External .NET type interop methods for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    /// <summary>
    /// Emits an instance method call on an external .NET type (via @DotNetType).
    /// </summary>
    private void EmitExternalInstanceMethodCall(Expr receiver, Type externalType, string methodName, List<Expr> arguments)
    {
        // Special-case event subscription: these names are reserved on @DotNetType
        // instances and route to DotNetEventBinder.Compiled(Add|Remove)EventListener.
        if (methodName == "addEventListener" || methodName == "removeEventListener")
        {
            EmitExternalEventSubscription(receiver, externalType, methodName, arguments, isStatic: false);
            return;
        }

        // Try to find the instance method - first with original name, then with PascalCase
        string pascalMethodName = NamingConventions.ToPascalCase(methodName);
        var methods = externalType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == methodName || m.Name == pascalMethodName)
            .ToArray();

        if (methods.Length == 0)
        {
            throw new CompileException($"Instance method '{methodName}' (or '{pascalMethodName}') not found on external type {externalType.FullName}");
        }

        // Use type-aware overload resolution, honoring @DotNetOverload if declared.
        var resolver = new ExternalMethodResolver(_ctx.TypeMap, _ctx.Types);
        string? hint = _ctx.TypeMapper.GetOverloadHint(externalType, methodName);
        var candidate = resolver.ResolveMethod(methods, arguments, hint);
        var method = (MethodInfo)candidate.Method;

        // Emit receiver and prepare for member access
        EmitExpression(receiver);
        EmitBoxIfNeeded(receiver);
        bool isValueType;
        if (externalType.IsValueType && !method.DeclaringType!.IsValueType)
        {
            // A method inherited from a reference base (Enum.ToString(), ValueType.Equals,
            // Object.GetHashCode, …) takes the BOXED receiver as `this` — Ldloca/Call with an
            // unboxed address would pass a managed pointer where an object reference is
            // expected (NRE inside the callee).
            IL.Emit(OpCodes.Castclass, method.DeclaringType);
            isValueType = false;
        }
        else
        {
            isValueType = PrepareReceiverForMemberAccess(externalType);
        }

        // Emit arguments with type conversion (handles params arrays)
        EmitExternalCallArguments(arguments, method, candidate);

        // Emit the call - use Call for value types (with address), Callvirt for reference types
        IL.Emit(isValueType ? OpCodes.Call : OpCodes.Callvirt, method);

        // Handle return value
        if (method.ReturnType == typeof(void))
        {
            IL.Emit(OpCodes.Ldnull); // void returns undefined
        }
        else
        {
            BoxResultIfValueType(method.ReturnType);
        }
        SetStackUnknown();
    }

    /// <summary>
    /// Emits construction of an external .NET type (via @DotNetType).
    /// </summary>
    private void EmitExternalTypeConstruction(Type externalType, List<Expr> arguments)
    {
        // Find a constructor matching the argument count
        var ctors = externalType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        if (ctors.Length == 0)
        {
            throw new CompileException($"No public constructors found on external type {externalType.FullName}");
        }

        // Use type-aware overload resolution, honoring @DotNetOverload("...") on the
        // TS constructor declaration if declared.
        var resolver = new ExternalMethodResolver(_ctx.TypeMap, _ctx.Types);
        string? hint = _ctx.TypeMapper.GetOverloadHint(externalType, "constructor");
        var candidate = resolver.ResolveConstructor(ctors, arguments, hint);
        var ctor = (ConstructorInfo)candidate.Method;

        // Emit arguments with type conversion (handles params arrays)
        EmitExternalCallArguments(arguments, ctor, candidate);

        // Emit newobj instruction
        IL.Emit(OpCodes.Newobj, ctor);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits a static method call on an external .NET type (via @DotNetType).
    /// </summary>
    private void EmitExternalStaticMethodCall(Type externalType, string methodName, List<Expr> arguments)
    {
        // Special-case static event subscription.
        if (methodName == "addEventListener" || methodName == "removeEventListener")
        {
            EmitExternalEventSubscription(receiver: null, externalType, methodName, arguments, isStatic: true);
            return;
        }

        // Try to find the static method - first with original name, then with PascalCase
        string pascalMethodName = NamingConventions.ToPascalCase(methodName);
        var methods = externalType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == methodName || m.Name == pascalMethodName)
            .ToArray();

        if (methods.Length == 0)
        {
            throw new CompileException($"Static method '{methodName}' (or '{pascalMethodName}') not found on external type {externalType.FullName}");
        }

        // Use type-aware overload resolution, honoring @DotNetOverload if declared.
        var resolver = new ExternalMethodResolver(_ctx.TypeMap, _ctx.Types);
        string? hint = _ctx.TypeMapper.GetOverloadHint(externalType, methodName);
        var candidate = resolver.ResolveMethod(methods, arguments, hint);
        var method = (MethodInfo)candidate.Method;

        // Emit arguments with type conversion (handles params arrays)
        EmitExternalCallArguments(arguments, method, candidate);

        // Emit the static call
        IL.Emit(OpCodes.Call, method);

        // Handle return value
        if (method.ReturnType == typeof(void))
        {
            IL.Emit(OpCodes.Ldnull); // void returns undefined
        }
        else if (method.ReturnType.IsValueType)
        {
            IL.Emit(OpCodes.Box, method.ReturnType);
        }
        SetStackUnknown();
    }

    /// <summary>
    /// Emits arguments for an external method call, handling params arrays if present.
    /// </summary>
    private void EmitExternalCallArguments(List<Expr> arguments, MethodBase method, MethodCandidate candidate)
    {
        var parameters = method.GetParameters();

        if (candidate.ParamsStartIndex < 0)
        {
            // No params array - emit arguments normally
            for (int i = 0; i < arguments.Count; i++)
            {
                EmitExpression(arguments[i]);
                EmitExternalTypeConversion(parameters[i].ParameterType);
            }
        }
        else
        {
            // Emit regular (non-params) arguments first
            for (int i = 0; i < candidate.ParamsStartIndex; i++)
            {
                EmitExpression(arguments[i]);
                EmitExternalTypeConversion(parameters[i].ParameterType);
            }

            // Create and fill the params array
            var paramsParam = parameters[candidate.ParamsStartIndex];
            var elementType = paramsParam.ParameterType.GetElementType()!;
            int paramsCount = arguments.Count - candidate.ParamsStartIndex;

            // Emit array creation: new T[paramsCount]
            IL.Emit(OpCodes.Ldc_I4, paramsCount);
            IL.Emit(OpCodes.Newarr, elementType);

            // Fill array elements
            bool isObjectArray = elementType == _ctx.Types.Object || elementType == typeof(object);
            for (int i = 0; i < paramsCount; i++)
            {
                IL.Emit(OpCodes.Dup);                    // Duplicate array reference
                IL.Emit(OpCodes.Ldc_I4, i);              // Push index
                EmitExpression(arguments[candidate.ParamsStartIndex + i]);

                // For object[], box value types but leave reference types as-is
                if (isObjectArray)
                {
                    // Box unboxed value types on the stack (numbers, booleans)
                    if (_stackType == StackType.Double)
                    {
                        IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    }
                    else if (_stackType == StackType.Boolean)
                    {
                        IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                    }
                    // Reference types (strings, objects) are already boxed, no action needed
                }
                else
                {
                    EmitExternalTypeConversion(elementType);
                    if (elementType.IsValueType)
                        IL.Emit(OpCodes.Box, elementType);
                }

                IL.Emit(OpCodes.Stelem_Ref);             // Store in array
            }
            SetStackUnknown();
        }
    }

    /// <summary>
    /// Emits type conversion for passing arguments to external .NET methods.
    /// </summary>
    private void EmitExternalTypeConversion(Type targetType)
    {
        if (targetType == _ctx.Types.Double || targetType == typeof(double))
        {
            // If we already have a native double on the stack, no conversion needed
            if (_stackType == StackType.Double)
                return;
            EmitUnboxToDouble();
        }
        else if (targetType == _ctx.Types.Boolean || targetType == typeof(bool))
        {
            // If we already have a native boolean on the stack, no conversion needed
            if (_stackType == StackType.Boolean)
                return;
            IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Boolean);
        }
        else if (targetType == _ctx.Types.Int32 || targetType == typeof(int))
        {
            // If we already have a native double, just convert to int
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_I4);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_I4);
        }
        else if (targetType == _ctx.Types.Int64 || targetType == typeof(long))
        {
            // If we already have a native double, just convert to long
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_I8);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_I8);
        }
        else if (targetType == _ctx.Types.Single || targetType == typeof(float))
        {
            // Float (single precision)
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_R4);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_R4);
        }
        else if (targetType == _ctx.Types.Int16 || targetType == typeof(short))
        {
            // Short (16-bit signed)
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_I4);
                IL.Emit(OpCodes.Conv_I2);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_I4);
            IL.Emit(OpCodes.Conv_I2);
        }
        else if (targetType == _ctx.Types.Byte || targetType == typeof(byte))
        {
            // Byte (8-bit unsigned)
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_I4);
                IL.Emit(OpCodes.Conv_U1);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_I4);
            IL.Emit(OpCodes.Conv_U1);
        }
        else if (targetType == _ctx.Types.SByte || targetType == typeof(sbyte))
        {
            // SByte (8-bit signed)
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_I4);
                IL.Emit(OpCodes.Conv_I1);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_I4);
            IL.Emit(OpCodes.Conv_I1);
        }
        else if (targetType == _ctx.Types.UInt16 || targetType == typeof(ushort))
        {
            // UInt16 (16-bit unsigned)
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_I4);
                IL.Emit(OpCodes.Conv_U2);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_I4);
            IL.Emit(OpCodes.Conv_U2);
        }
        else if (targetType == _ctx.Types.UInt32 || targetType == typeof(uint))
        {
            // UInt32 (32-bit unsigned)
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_U4);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_U4);
        }
        else if (targetType == _ctx.Types.UInt64 || targetType == typeof(ulong))
        {
            // UInt64 (64-bit unsigned)
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_U8);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_U8);
        }
        else if (targetType == _ctx.Types.Char || targetType == typeof(char))
        {
            // Char (16-bit Unicode character, treated as unsigned)
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Conv_I4);
                IL.Emit(OpCodes.Conv_U2);
                return;
            }
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Conv_I4);
            IL.Emit(OpCodes.Conv_U2);
        }
        else if (targetType == _ctx.Types.Decimal || targetType == typeof(decimal))
        {
            // Decimal requires calling the explicit conversion operator
            if (_stackType != StackType.Double)
                EmitUnboxToDouble();
            var opExplicit = _ctx.Types.Decimal.GetMethod("op_Explicit",
                BindingFlags.Public | BindingFlags.Static, [_ctx.Types.Double]);
            IL.Emit(OpCodes.Call, opExplicit!);
        }
        else if (targetType == _ctx.Types.String || targetType == typeof(string))
        {
            // If we already have a string on the stack, no conversion needed
            if (_stackType == StackType.String)
                return;
            IL.Emit(OpCodes.Castclass, _ctx.Types.String);
        }
        else if (typeof(Delegate).IsAssignableFrom(targetType))
        {
            // TS function ($TSFunction on stack) → .NET Delegate. Emits a per-delegate-type
            // adapter class inside the compiled DLL and binds its Invoke as the delegate
            // target — fully standalone, no runtime dependency on SharpTS.dll.
            EmitDelegateConversion(targetType);
        }
        else if (targetType.IsValueType)
        {
            IL.Emit(OpCodes.Unbox_Any, targetType);
        }
        else if (!_ctx.Types.IsObject(targetType))
        {
            IL.Emit(OpCodes.Castclass, targetType);
        }
        else
        {
            // For object type, box unboxed value types
            if (_stackType == StackType.Double)
            {
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                SetStackUnknown();
            }
            else if (_stackType == StackType.Boolean)
            {
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                SetStackUnknown();
            }
            // Reference types are already objects, no conversion needed
        }
    }

    /// <summary>
    /// Emits a <c>@DotNetType addEventListener(name, handler)</c> or
    /// <c>removeEventListener(name, handler)</c> call. Pushes <c>null</c> (the JS-level
    /// <c>undefined</c>) on return to match the void-return convention for other
    /// external calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fast path (literal event name): resolves the <see cref="EventInfo"/> at compile
    /// time, uses <see cref="DelegateAdapterEmitter"/> to construct a delegate matching
    /// the event's handler type, registers in the emitted <c>$Runtime</c> subscription
    /// table, and emits a direct <c>call</c>/<c>callvirt</c> to the event's
    /// <c>add_</c>/<c>remove_</c> accessor. Fully standalone — no runtime dependency on
    /// SharpTS.dll.
    /// </para>
    /// <para>
    /// Slow path (dynamic event name): falls through to the reflection-into-SharpTS
    /// helper. Requires SharpTS.dll to be loadable at runtime; used only when the first
    /// argument isn't a string literal.
    /// </para>
    /// </remarks>
    /// <param name="receiver">Receiver expression for instance events, or null for static.</param>
    /// <param name="externalType">The <c>@DotNetType</c> target (used for event lookup).</param>
    /// <param name="methodName">Either <c>addEventListener</c> or <c>removeEventListener</c>.</param>
    /// <param name="arguments">The TS arguments — expected: (name: string, handler: function).</param>
    /// <param name="isStatic">True when emitted from a static-method-call dispatch.</param>
    private void EmitExternalEventSubscription(
        Expr? receiver,
        Type externalType,
        string methodName,
        List<Expr> arguments,
        bool isStatic)
    {
        if (arguments.Count < 2)
        {
            throw new CompileException(
                $"'{methodName}' on '@DotNetType {externalType.FullName}' requires (eventName, handler) — got {arguments.Count} argument(s).");
        }

        // Fast path: string-literal event name → compile-time EventInfo, full standalone emit.
        if (arguments[0] is Expr.Literal { Value: string literalEventName })
        {
            EmitExternalEventSubscriptionStandalone(
                receiver, externalType, methodName, literalEventName, arguments[1], isStatic);
            return;
        }

        // Slow path: dynamic event name — fall back to reflection-into-SharpTS.
        EmitExternalEventSubscriptionReflected(receiver, externalType, methodName, arguments, isStatic);
    }

    /// <summary>
    /// Fast-path emission used when the event name is a compile-time string literal.
    /// The full add/remove flow is baked into the compiled DLL and depends only on the
    /// BCL + emitted <c>$Runtime</c> helpers.
    /// </summary>
    private void EmitExternalEventSubscriptionStandalone(
        Expr? receiver,
        Type externalType,
        string methodName,
        string eventName,
        Expr handlerExpr,
        bool isStatic)
    {
        bool isAdd = methodName == "addEventListener";

        var evt = ResolveExternalEvent(externalType, eventName)
            ?? throw new CompileException(
                $"Event '{eventName}' not found on '@DotNetType {externalType.FullName}'.");

        var handlerDelegateType = evt.EventHandlerType
            ?? throw new CompileException(
                $"Event '{eventName}' on '{externalType.FullName}' has no EventHandlerType.");

        var accessor = (isAdd ? evt.AddMethod : evt.RemoveMethod)
            ?? throw new CompileException(
                $"Event '{eventName}' on '{externalType.FullName}' has no {(isAdd ? "add" : "remove")} accessor.");

        // Evaluate handler (tsFunction) into a local.
        var tsFuncLocal = IL.DeclareLocal(_ctx.Types.Object);
        EmitExpression(handlerExpr);
        EmitBoxIfNeeded(handlerExpr);
        IL.Emit(OpCodes.Stloc, tsFuncLocal);

        // Evaluate receiver (null for static) into a local.
        var receiverLocal = IL.DeclareLocal(_ctx.Types.Object);
        if (isStatic || receiver == null)
        {
            IL.Emit(OpCodes.Ldnull);
        }
        else
        {
            EmitExpression(receiver);
            EmitBoxIfNeeded(receiver);
        }
        IL.Emit(OpCodes.Stloc, receiverLocal);

        if (isAdd)
        {
            // Build the delegate via a per-handler-type adapter.
            var adapter = _ctx.TypeMapper.DelegateAdapters.GetOrEmit(handlerDelegateType);
            var delegateLocal = IL.DeclareLocal(handlerDelegateType);

            IL.Emit(OpCodes.Ldloc, tsFuncLocal);
            IL.Emit(OpCodes.Castclass, _ctx.Runtime!.TSFunctionType);
            IL.Emit(OpCodes.Newobj, adapter.Ctor);
            IL.Emit(OpCodes.Ldftn, adapter.Invoke);
            var delegateCtor = handlerDelegateType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, [typeof(object), typeof(IntPtr)], null)
                ?? throw new CompileException(
                    $"Delegate type '{handlerDelegateType.FullName}' lacks the standard (object, IntPtr) constructor.");
            IL.Emit(OpCodes.Newobj, delegateCtor);
            IL.Emit(OpCodes.Stloc, delegateLocal);

            // Register in $Runtime table — no-op if already registered.
            EmitPushOwner(receiverLocal, externalType, isStatic);
            IL.Emit(OpCodes.Ldstr, eventName);
            IL.Emit(OpCodes.Ldloc, tsFuncLocal);
            IL.Emit(OpCodes.Ldloc, delegateLocal);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.AddEventSubscription);

            // If the subscription was a duplicate (add returned false), skip the actual AddEventHandler.
            var skipAddLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Brfalse, skipAddLabel);

            // Call the event's add accessor: evt.add_X(delegate).
            EmitEventAccessorCall(receiverLocal, accessor, isStatic, delegateLocal, handlerDelegateType);

            IL.MarkLabel(skipAddLabel);
        }
        else
        {
            // Remove path: look up the previously-registered Delegate and unsubscribe.
            var delegateLocal = IL.DeclareLocal(typeof(Delegate));
            EmitPushOwner(receiverLocal, externalType, isStatic);
            IL.Emit(OpCodes.Ldstr, eventName);
            IL.Emit(OpCodes.Ldloc, tsFuncLocal);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.RemoveEventSubscription);
            IL.Emit(OpCodes.Stloc, delegateLocal);

            var skipRemoveLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, delegateLocal);
            IL.Emit(OpCodes.Brfalse, skipRemoveLabel);

            // Delegate → EventHandlerType cast is needed because the accessor's param is the
            // specific delegate type, not the base Delegate.
            var typedDelegateLocal = IL.DeclareLocal(handlerDelegateType);
            IL.Emit(OpCodes.Ldloc, delegateLocal);
            IL.Emit(OpCodes.Castclass, handlerDelegateType);
            IL.Emit(OpCodes.Stloc, typedDelegateLocal);

            EmitEventAccessorCall(receiverLocal, accessor, isStatic, typedDelegateLocal, handlerDelegateType);

            IL.MarkLabel(skipRemoveLabel);
        }

        // Void return on the JS side → undefined (null in compiled convention).
        IL.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    /// <summary>
    /// Pushes the subscription-table "owner" key: the receiver for instance events, or
    /// <c>typeof(externalType)</c> for static events. Matches the interpreter's
    /// <c>DotNetEventBinder</c> keying.
    /// </summary>
    private void EmitPushOwner(LocalBuilder receiverLocal, Type externalType, bool isStatic)
    {
        if (isStatic)
        {
            IL.Emit(OpCodes.Ldtoken, externalType);
            IL.Emit(OpCodes.Call, _ctx.Types.TypeGetTypeFromHandle);
        }
        else
        {
            IL.Emit(OpCodes.Ldloc, receiverLocal);
        }
    }

    /// <summary>
    /// Emits a direct call to the event's <c>add_X</c> / <c>remove_X</c> accessor.
    /// Static events take no receiver; instance events take it as <c>this</c>.
    /// </summary>
    private void EmitEventAccessorCall(
        LocalBuilder receiverLocal,
        MethodInfo accessor,
        bool isStatic,
        LocalBuilder delegateLocal,
        Type handlerDelegateType)
    {
        if (!isStatic && !accessor.IsStatic)
        {
            IL.Emit(OpCodes.Ldloc, receiverLocal);
            // The accessor expects the specific declaring type as `this`; cast the object.
            IL.Emit(OpCodes.Castclass, accessor.DeclaringType ?? _ctx.Types.Object);
        }

        IL.Emit(OpCodes.Ldloc, delegateLocal);

        // For instance accessors on reference types, callvirt gives proper dispatch even
        // though event accessors aren't typically virtual. For static accessors, call.
        IL.Emit(accessor.IsStatic ? OpCodes.Call : OpCodes.Callvirt, accessor);
    }

    /// <summary>
    /// Resolves an event on <paramref name="externalType"/> by its TS-facing name. Tries
    /// the original name first, then PascalCase (e.g. <c>processExit</c> → <c>ProcessExit</c>).
    /// </summary>
    private static EventInfo? ResolveExternalEvent(Type externalType, string eventName)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        var evt = externalType.GetEvent(eventName, flags);
        if (evt != null) return evt;

        // PascalCase fallback.
        if (eventName.Length > 0 && char.IsLower(eventName[0]))
        {
            var pascal = char.ToUpperInvariant(eventName[0]) + eventName[1..];
            evt = externalType.GetEvent(pascal, flags);
            if (evt != null) return evt;
        }

        return null;
    }

    /// <summary>
    /// Legacy reflection-into-SharpTS path, kept for the case where the event name isn't
    /// a compile-time string literal. Requires SharpTS.dll to be loadable at runtime.
    /// </summary>
    private void EmitExternalEventSubscriptionReflected(
        Expr? receiver,
        Type externalType,
        string methodName,
        List<Expr> arguments,
        bool isStatic)
    {
        string helperMethod = methodName == "addEventListener"
            ? "CompiledAddEventListener"
            : "CompiledRemoveEventListener";

        // This slow path reflects into DotNetEventBinder in the SharpTS runtime — record the
        // soft dependency so the build co-locates SharpTS.dll. (The literal-event-name fast
        // path is pure IL and does not reach here.)
        _ctx.Runtime?.RequireSharpTSRuntime("@DotNetType dynamic event binding");

        // Locals: object receiver, object[] args
        var receiverLocal = IL.DeclareLocal(_ctx.Types.Object);
        if (isStatic || receiver == null)
        {
            IL.Emit(OpCodes.Ldnull);
        }
        else
        {
            EmitExpression(receiver);
            EmitBoxIfNeeded(receiver);
        }
        IL.Emit(OpCodes.Stloc, receiverLocal);

        // args = new object[4]
        IL.Emit(OpCodes.Ldc_I4_4);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
        var argsLocal = IL.DeclareLocal(_ctx.Types.ObjectArray);
        IL.Emit(OpCodes.Stloc, argsLocal);

        // args[0] = receiver
        IL.Emit(OpCodes.Ldloc, argsLocal);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Ldloc, receiverLocal);
        IL.Emit(OpCodes.Stelem_Ref);

        // args[1] = typeof(externalType)
        IL.Emit(OpCodes.Ldloc, argsLocal);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Ldtoken, externalType);
        IL.Emit(OpCodes.Call, _ctx.Types.TypeGetTypeFromHandle);
        IL.Emit(OpCodes.Stelem_Ref);

        // args[2] = eventName (first TS arg)
        IL.Emit(OpCodes.Ldloc, argsLocal);
        IL.Emit(OpCodes.Ldc_I4_2);
        EmitExpression(arguments[0]);
        EmitBoxIfNeeded(arguments[0]);
        IL.Emit(OpCodes.Stelem_Ref);

        // args[3] = tsFunction (second TS arg)
        IL.Emit(OpCodes.Ldloc, argsLocal);
        IL.Emit(OpCodes.Ldc_I4_3);
        EmitExpression(arguments[1]);
        EmitBoxIfNeeded(arguments[1]);
        IL.Emit(OpCodes.Stelem_Ref);

        // Type t = Type.GetType("SharpTS.Runtime.DotNet.DotNetEventBinder, SharpTS");
        IL.Emit(OpCodes.Ldstr, "SharpTS.Runtime.DotNet.DotNetEventBinder, SharpTS");
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Type, "GetType", _ctx.Types.String));

        // MethodInfo m = t.GetMethod(helperMethod);
        IL.Emit(OpCodes.Ldstr, helperMethod);
        IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(_ctx.Types.Type, "GetMethod", _ctx.Types.String));

        // m.Invoke(null, args)
        IL.Emit(OpCodes.Ldnull);
        IL.Emit(OpCodes.Ldloc, argsLocal);
        IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(
            _ctx.Types.MethodInfo, "Invoke", _ctx.Types.Object, _ctx.Types.ObjectArray));

        // Discard the helper's return (null) and push JS undefined (null) for the
        // external call convention.
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits IL that converts a <c>$TSFunction</c> reference on the stack into a
    /// <see cref="Delegate"/> of <paramref name="delegateType"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses a compile-time-emitted adapter class (one per unique delegate type)
    /// that holds the <c>$TSFunction</c> and exposes an <c>Invoke</c> method
    /// matching the delegate's signature. The call site then constructs a standard
    /// delegate via <c>new TDelegate(adapter, adapter.Invoke)</c> — the canonical
    /// method-group-to-delegate pattern in IL.
    /// </para>
    /// <para>
    /// Stack in:  [object] — the <c>$TSFunction</c> reference (typed as object).<br/>
    /// Stack out: [TDelegate] — a delegate of <paramref name="delegateType"/>.
    /// </para>
    /// <para>
    /// Keeping the adapter in the compiled DLL (rather than reflecting into
    /// <c>DotNetDelegateShim</c> on SharpTS) preserves the standalone property:
    /// the compiled output runs without SharpTS.dll present.
    /// </para>
    /// </remarks>
    private void EmitDelegateConversion(Type delegateType)
    {
        var adapter = _ctx.TypeMapper.DelegateAdapters.GetOrEmit(delegateType);

        // Cast the $TSFunction reference (currently typed as object on the stack) to
        // the emitted $TSFunction type so the adapter ctor signature matches.
        IL.Emit(OpCodes.Castclass, _ctx.Runtime!.TSFunctionType);

        // new Adapter(tsFunction) — consumes the $TSFunction, leaves the adapter on the stack.
        // That adapter also serves as the delegate's target instance for the ctor below.
        IL.Emit(OpCodes.Newobj, adapter.Ctor);

        // Load the adapter's Invoke method pointer. Stack now: [adapter, IntPtr] — the
        // exact shape the Delegate(object, IntPtr) ctor expects.
        IL.Emit(OpCodes.Ldftn, adapter.Invoke);

        // new TDelegate(object target, IntPtr method) — every Delegate has this ctor.
        var delegateCtor = delegateType.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(object), typeof(IntPtr)],
            null)
            ?? throw new InvalidOperationException(
                $"Delegate type '{delegateType.FullName}' lacks the standard (object, IntPtr) constructor.");
        IL.Emit(OpCodes.Newobj, delegateCtor);

        SetStackUnknown();
    }
}
