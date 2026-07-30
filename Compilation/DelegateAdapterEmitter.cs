using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits per-delegate-type adapter classes that bridge a TS closure
/// (<c>$TSFunction</c>) to a .NET delegate required by a <c>@DotNetType</c> API.
/// </summary>
/// <remarks>
/// <para>
/// Each unique delegate type used as a parameter in the compiled program gets one
/// adapter class emitted into the module. The adapter holds a <c>$TSFunction</c>
/// reference and exposes a method matching the delegate's <c>Invoke</c> signature;
/// its body boxes each incoming arg into an <c>object[]</c>, calls
/// <c>$TSFunction.Invoke(object[])</c>, and marshals the returned <c>object</c>
/// back to the delegate's declared return type.
/// </para>
/// <para>
/// Keeping the adapter in the compiled DLL (rather than reflecting into
/// <c>DotNetDelegateShim</c> on SharpTS) preserves the standalone-DLL property:
/// users can ship their compiled output without SharpTS.dll.
/// </para>
/// </remarks>
public class DelegateAdapterEmitter
{
    private readonly ModuleBuilder _moduleBuilder;
    private readonly EmittedRuntime _runtime;
    private readonly TypeProvider _types;
    private readonly Dictionary<Type, AdapterHandle> _cache = [];
    private int _counter;

    /// <summary>
    /// Handle returned by <see cref="GetOrEmit"/> — holds the pieces a call-site
    /// emitter needs to construct an instance and bind its <c>Invoke</c> method
    /// to a delegate.
    /// </summary>
    public readonly record struct AdapterHandle(ConstructorInfo Ctor, MethodInfo Invoke);

    public DelegateAdapterEmitter(ModuleBuilder moduleBuilder, EmittedRuntime runtime, TypeProvider types)
    {
        _moduleBuilder = moduleBuilder;
        _runtime = runtime;
        _types = types;
    }

    /// <summary>
    /// Returns a (ctor, Invoke) pair for the adapter class matching
    /// <paramref name="delegateType"/>. Emits the adapter on first request; subsequent
    /// requests for the same delegate type reuse the cached class.
    /// </summary>
    public AdapterHandle GetOrEmit(Type delegateType)
    {
        if (_cache.TryGetValue(delegateType, out var existing)) return existing;
        var handle = Emit(delegateType);
        _cache[delegateType] = handle;
        return handle;
    }

    private AdapterHandle Emit(Type delegateType)
    {
        var invokeMethod = delegateType.GetMethod("Invoke")
            ?? throw new InvalidOperationException(
                $"Delegate type '{delegateType.FullName}' has no Invoke method.");

        var parameters = invokeMethod.GetParameters();
        var returnType = invokeMethod.ReturnType;

        // Unique type name keyed by a monotonic counter so two distinct delegate
        // types sharing a short name (e.g. two generic Func instantiations) don't
        // collide in the module's type namespace.
        var typeName = $"$DelegateAdapter_{SanitizeName(delegateType.Name)}_{_counter++}";

        var typeBuilder = _moduleBuilder.DefineType(
            typeName,
            TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object);

        // private readonly $TSFunction _fn;
        var fnField = typeBuilder.DefineField(
            "_fn",
            _runtime.TSFunctionType,
            FieldAttributes.Private | FieldAttributes.InitOnly);

        var ctor = EmitConstructor(typeBuilder, fnField);
        var invoke = EmitInvoke(typeBuilder, fnField, parameters, returnType);

        typeBuilder.CreateType();
        return new AdapterHandle(ctor, invoke);
    }

    private ConstructorBuilder EmitConstructor(TypeBuilder typeBuilder, FieldBuilder fnField)
    {
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.HasThis,
            [_runtime.TSFunctionType]);

        var il = ctorBuilder.GetILGenerator();

        // base().ctor()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);

        // this._fn = fn
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, fnField);

        il.Emit(OpCodes.Ret);
        return ctorBuilder;
    }

    private MethodBuilder EmitInvoke(
        TypeBuilder typeBuilder,
        FieldBuilder fnField,
        ParameterInfo[] parameters,
        Type returnType)
    {
        var paramTypes = parameters.Select(p => p.ParameterType).ToArray();
        var invokeBuilder = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            returnType,
            paramTypes);

        var il = invokeBuilder.GetILGenerator();

        // Push _fn on the stack: will be the `this` for the Invoke callvirt.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fnField);

        // new object[N]
        EmitLdcI4(il, parameters.Length);
        il.Emit(OpCodes.Newarr, _types.Object);

        // Fill the array with each arg, boxed for the TS side.
        for (int i = 0; i < parameters.Length; i++)
        {
            il.Emit(OpCodes.Dup);
            EmitLdcI4(il, i);
            EmitLdarg(il, i + 1); // +1 for `this`
            EmitBoxForTS(il, parameters[i].ParameterType);
            il.Emit(OpCodes.Stelem_Ref);
        }

        // _fn.Invoke(args) — stack: [_fn, args] → [object]
        il.Emit(OpCodes.Callvirt, _runtime.TSFunctionInvoke);

        EmitUnboxForReturn(il, returnType);
        il.Emit(OpCodes.Ret);

        return invokeBuilder;
    }

    /// <summary>
    /// Boxes (or coerces + boxes) a value on the evaluation stack so the TS side sees
    /// the type it expects: .NET numeric primitives become boxed <c>double</c> to match
    /// TS <c>number</c>, <c>bool</c> stays boxed bool, reference types pass through.
    /// Mirrors the interpreter's <c>DotNetMarshaller.WrapReturn</c> for the common primitives.
    /// </summary>
    /// <remarks>
    /// <c>internal static</c> so the boxed-callback adapter emitter for array HOFs
    /// (<see cref="ArrowBoxedAdapterEmitter"/>, #861) shares the exact same
    /// box/unbox conventions — keeping <c>number</c>↔boxed-<c>double</c> etc.
    /// consistent across every emitted marshalling site.
    /// </remarks>
    internal static void EmitBoxForTS(ILGenerator il, Type paramType)
    {
        if (!paramType.IsValueType)
        {
            // Reference type (string, object, class instance) — already boxed by the
            // time it's on the evaluation stack.
            return;
        }

        if (paramType == typeof(double))
        {
            il.Emit(OpCodes.Box, typeof(double));
        }
        else if (paramType == typeof(float))
        {
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, typeof(double));
        }
        else if (paramType == typeof(int) || paramType == typeof(short)
              || paramType == typeof(sbyte) || paramType == typeof(long))
        {
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, typeof(double));
        }
        else if (paramType == typeof(uint) || paramType == typeof(ushort)
              || paramType == typeof(byte) || paramType == typeof(ulong))
        {
            il.Emit(OpCodes.Conv_R_Un);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, typeof(double));
        }
        else if (paramType == typeof(bool))
        {
            il.Emit(OpCodes.Box, typeof(bool));
        }
        else
        {
            // Other value types (structs, char, decimal, DateTime, enums, etc.) —
            // box as-is. TS side will see a boxed .NET value rather than a coerced
            // primitive; good enough for common scenarios, less pretty than the
            // interpreter's DotNetMarshaller but fully functional.
            il.Emit(OpCodes.Box, paramType);
        }
    }

    /// <summary>
    /// Converts an <c>object</c> on the stack (the TS-side return value) back to the
    /// delegate's declared return type. void: pop. object: no-op. value types: unbox via
    /// boxed-double (matching <see cref="EmitBoxForTS"/>). Reference types: castclass.
    /// </summary>
    /// <remarks>
    /// <c>internal static</c>: also used by <see cref="ArrowBoxedAdapterEmitter"/>
    /// (#861) to coerce a boxed array element into an arrow's typed parameter slot
    /// (the inverse role), so the unbox/cast matches the reflective
    /// <c>MethodInvoker</c> path exactly.
    /// </remarks>
    internal static void EmitUnboxForReturn(ILGenerator il, Type returnType)
    {
        if (returnType == typeof(void))
        {
            il.Emit(OpCodes.Pop);
            return;
        }
        if (returnType == typeof(object))
        {
            return;
        }
        if (returnType == typeof(double))
        {
            il.Emit(OpCodes.Unbox_Any, typeof(double));
            return;
        }
        if (returnType == typeof(bool))
        {
            il.Emit(OpCodes.Unbox_Any, typeof(bool));
            return;
        }
        if (returnType == typeof(int))
        {
            il.Emit(OpCodes.Unbox_Any, typeof(double));
            il.Emit(OpCodes.Conv_I4);
            return;
        }
        if (returnType == typeof(long))
        {
            il.Emit(OpCodes.Unbox_Any, typeof(double));
            il.Emit(OpCodes.Conv_I8);
            return;
        }
        if (returnType == typeof(short))
        {
            il.Emit(OpCodes.Unbox_Any, typeof(double));
            il.Emit(OpCodes.Conv_I2);
            return;
        }
        if (returnType == typeof(byte))
        {
            il.Emit(OpCodes.Unbox_Any, typeof(double));
            il.Emit(OpCodes.Conv_U1);
            return;
        }
        if (returnType == typeof(float))
        {
            il.Emit(OpCodes.Unbox_Any, typeof(double));
            il.Emit(OpCodes.Conv_R4);
            return;
        }
        if (returnType.IsValueType)
        {
            il.Emit(OpCodes.Unbox_Any, returnType);
            return;
        }
        il.Emit(OpCodes.Castclass, returnType);
    }

    private static void EmitLdcI4(ILGenerator il, int value)
    {
        switch (value)
        {
            case 0: il.Emit(OpCodes.Ldc_I4_0); break;
            case 1: il.Emit(OpCodes.Ldc_I4_1); break;
            case 2: il.Emit(OpCodes.Ldc_I4_2); break;
            case 3: il.Emit(OpCodes.Ldc_I4_3); break;
            case 4: il.Emit(OpCodes.Ldc_I4_4); break;
            case 5: il.Emit(OpCodes.Ldc_I4_5); break;
            case 6: il.Emit(OpCodes.Ldc_I4_6); break;
            case 7: il.Emit(OpCodes.Ldc_I4_7); break;
            case 8: il.Emit(OpCodes.Ldc_I4_8); break;
            default:
                if (value >= -128 && value <= 127) il.Emit(OpCodes.Ldc_I4_S, (sbyte)value);
                else il.Emit(OpCodes.Ldc_I4, value);
                break;
        }
    }

    private static void EmitLdarg(ILGenerator il, int index)
    {
        switch (index)
        {
            case 0: il.Emit(OpCodes.Ldarg_0); break;
            case 1: il.Emit(OpCodes.Ldarg_1); break;
            case 2: il.Emit(OpCodes.Ldarg_2); break;
            case 3: il.Emit(OpCodes.Ldarg_3); break;
            default:
                if (index <= 255) il.Emit(OpCodes.Ldarg_S, (byte)index);
                else il.Emit(OpCodes.Ldarg, index);
                break;
        }
    }

    /// <summary>
    /// Strips characters that aren't valid in an emitted type name.
    /// </summary>
    private static string SanitizeName(string name)
    {
        var span = name.AsSpan();
        Span<char> buffer = stackalloc char[span.Length];
        for (int i = 0; i < span.Length; i++)
        {
            buffer[i] = char.IsLetterOrDigit(span[i]) || span[i] == '_' ? span[i] : '_';
        }
        return new string(buffer);
    }
}
