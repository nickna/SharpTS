using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitGlobalTimerFunctionValues(MethodBuilder initializer, EmittedRuntime runtime)
    {
        var il = initializer.GetILGenerator();
        var storage = runtime.DescriptorStorage;
        var descriptor = il.DeclareLocal(storage.DescriptorType);
        foreach (var (name, length) in new[]
        {
            ("setTimeout", 2), ("clearTimeout", 1), ("setInterval", 2),
            ("clearInterval", 1), ("setImmediate", 1), ("clearImmediate", 1)
        })
        {
            // Actual module declarations own these values; the source feature
            // flag is only a conservative reachability gate.
            var wrapper = runtime.BuiltInModules.Require("timers", name);
            il.Emit(OpCodes.Newobj, storage.DescriptorConstructor);
            il.Emit(OpCodes.Stloc, descriptor);
            il.Emit(OpCodes.Ldloc, descriptor);
            var parameters = EmitReflectedMethodLiteralViaHandle(il, wrapper);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldc_I4, length);
            EmitReflectedFunctionFactory(il, runtime.FunctionConstruction.GetOrCreate, parameters);
            il.Emit(OpCodes.Callvirt, storage.DescriptorValue.GetSetMethod()!);
            foreach (var attribute in new[] { storage.DescriptorWritable,
                storage.DescriptorEnumerable, storage.DescriptorConfigurable })
            {
                il.Emit(OpCodes.Ldloc, descriptor);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Callvirt, attribute.GetSetMethod()!);
            }
            il.Emit(OpCodes.Ldsfld, runtime.GlobalObject.SingletonField);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Call, storage.DefineProperty);
            il.Emit(OpCodes.Pop);
        }
        il.Emit(OpCodes.Ret);
    }
}