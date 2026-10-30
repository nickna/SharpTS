using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private NativeUnionArgumentConversion.ReflectionMethodEmission? EmitReflectedMethodLiteral(ILGenerator body, MethodInfo method)
    {
        if (_nativeUnionParameters is { } conversion) return conversion.EmitReflectionLiteral(body, method);
        _types.EmitLoadMethodInfo(body, method);
        return null;
    }

    private NativeUnionArgumentConversion.ReflectionMethodEmission? EmitReflectedMethodLiteralViaHandle(ILGenerator body, MethodInfo method)
    {
        if (_nativeUnionParameters is { } conversion) return conversion.EmitReflectionLiteral(body, method, singleHandle: true);
        _types.EmitLoadMethodInfoViaHandle(body, method);
        return null;
    }

    private void EmitReflectedMethodQuery(ILGenerator body, MethodBuilder selector, LocalBuilder local)
    {
        if (_nativeUnionParameters is { } conversion) conversion.EmitReflectionQuery(body, selector, local);
        else { body.Emit(OpCodes.Call, selector); body.Emit(OpCodes.Stloc, local); }
    }

    private void EmitSymbolParameterQuery(ILGenerator body, MethodBuilder selector, LocalBuilder local)
    {
        if (_nativeUnionParameters is { } conversion) conversion.EmitSymbolParameterQuery(body, selector, local);
        else { body.Emit(OpCodes.Call, selector); body.Emit(OpCodes.Stloc, local); }
    }

    private void EmitReflectedFunctionConstructor(ILGenerator body, ConstructorInfo original,
        NativeUnionArgumentConversion.ReflectionMethodEmission? producer, bool cached)
    {
        if (_nativeUnionParameters is { } conversion && producer is not null && conversion.MatchesOriginalConstructor(original, cached)) conversion.EmitReflectionConstructor(body, producer, cached, _nativePrototypeFields);
        else NativePrototypeFieldsLookup.EmitCarrierCreate(_nativePrototypeFields, body, original);
    }

    private void EmitReflectedFunctionFactory(ILGenerator body, MethodInfo original,
        NativeUnionArgumentConversion.ReflectionMethodEmission? producer)
    {
        if (_nativeUnionParameters is { } conversion && producer is not null && conversion.MatchesOriginalFactory(original)) conversion.EmitReflectionFactory(body, producer);
        else body.Emit(OpCodes.Call, original);
    }
}
