using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits dns.Resolver factory into the $Runtime class.
/// Uses late-binding to RuntimeTypes.DnsCreateResolver() — the Resolver is stateful
/// and too complex for pure IL emission.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits DnsResolverFactory: creates a dns.Resolver instance via late-binding
    /// to RuntimeTypes.DnsCreateResolver().
    /// </summary>
    private void EmitDnsResolverFactoryMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DnsResolverFactory",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes);

        runtime.DnsResolverFactory = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);

        // var type = Type.GetType("SharpTS.Compilation.RuntimeTypes, SharpTS");
        il.Emit(OpCodes.Ldstr, "SharpTS.Compilation.RuntimeTypes, SharpTS");
        il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetType", [typeof(string)])!);
        var typeLocal = il.DeclareLocal(typeof(Type));
        il.Emit(OpCodes.Stloc, typeLocal);

        // if (type == null) return new Dictionary<string,object?>()
        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Brtrue, foundLabel);
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object?>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(foundLabel);

        // var methodInfo = type.GetMethod("DnsCreateResolver", BindingFlags.Public | BindingFlags.Static);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "DnsCreateResolver");
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Public | BindingFlags.Static));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            typeof(Type),
            "GetMethod",
            [typeof(string), typeof(BindingFlags)]));
        var methodInfoLocal = il.DeclareLocal(typeof(MethodInfo));
        il.Emit(OpCodes.Stloc, methodInfoLocal);

        // if (methodInfo == null) return new Dictionary<string,object?>()
        var invokeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, methodInfoLocal);
        il.Emit(OpCodes.Brtrue, invokeLabel);
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object?>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invokeLabel);

        // return methodInfo.Invoke(null, null);
        il.Emit(OpCodes.Ldloc, methodInfoLocal);
        il.Emit(OpCodes.Ldnull); // instance (static)
        il.Emit(OpCodes.Ldnull); // args (none)
        il.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
        il.Emit(OpCodes.Ret);
    }
}
