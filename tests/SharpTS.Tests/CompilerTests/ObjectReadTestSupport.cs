using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

internal static class ObjectReadTestSupport
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static EmittedObjectReadRuntime EmitProperty(RuntimeEmitter emitter, TypeBuilder probe, EmittedRuntime runtime,
        IReadOnlyDictionary<string, object?>? overrides = null)
    {
        // Re-emission gets a fresh owner; the completed runtime's declarations remain immutable.
        var owner = new EmittedObjectReadRuntime
        {
            FieldsProperty = runtime.ObjectRead.FieldsProperty, ListProperty = runtime.ObjectRead.ListProperty,
            Index = runtime.ObjectRead.Index, Length = runtime.ObjectRead.Length, Element = runtime.ObjectRead.Element
        };
        typeof(RuntimeEmitter).GetMethod("DeclareObjectReadProperty", Members)!.Invoke(emitter, [probe, owner]);
        var method = typeof(RuntimeEmitter).GetMethod("EmitGetProperty", Members)!;
        method.Invoke(emitter, [owner, MakeInputs(method.GetParameters()[1].ParameterType, runtime, overrides)]);
        owner.CompleteEmission();
        return owner;
    }

    internal static object MakeInputs(Type type, EmittedRuntime runtime, IReadOnlyDictionary<string, object?>? overrides = null)
    {
        var constructor = Assert.Single(type.GetConstructors());
        return constructor.Invoke(constructor.GetParameters().Select(parameter =>
        {
            if (overrides is not null && overrides.TryGetValue(parameter.Name!, out var value)) return value;
            if (parameter.Name == "CommonJs") return runtime.Modules.CommonJs;
            return (parameter.Name switch { "TSFunctionCtor" => runtime.FunctionConstruction.Constructor, "TSFunctionCtorWithCache" => runtime.FunctionConstruction.CachedConstructor, "TSFunctionGetOrCreate" => runtime.FunctionConstruction.GetOrCreate, _ => (parameter.Name switch { "ArgumentsLengthField" => runtime.Arguments.LengthField, "ArgumentsType" => runtime.Arguments.Type, "BoundAnyFunctionType" => runtime.FunctionBindings.AnyType, "BoundTSFunctionType" => runtime.FunctionBindings.BoundType, "FunctionApplyWrapperType" => runtime.FunctionBindings.ApplyType, "FunctionBindWrapperType" => runtime.FunctionBindings.BindType, "FunctionCallWrapperType" => runtime.FunctionBindings.CallType, "TSFunctionBindThis" => runtime.FunctionValues.BindThis, "TSFunctionExpectsThisField" => runtime.FunctionValues.ExpectsThisField, "TSFunctionInvokeWithThis" => runtime.FunctionValues.InvokeWithThis, "TSFunctionType" => runtime.FunctionValues.Type, _ => (parameter.Name switch { "FunctionPrototypeField" => runtime.FunctionPrototypes.Prototype, "FunctionPrototypePopulateMethod" => runtime.FunctionPrototypes.Populate, _ => (parameter.Name switch { "GetFunctionMethod" => runtime.FunctionIntrospection.GetProperty, _ => (parameter.Name switch { "InvokeMethodUnwrapped" => runtime.ReflectedMethods.InvokeUnwrapped, "SafeGetMethod" => runtime.ReflectedMethods.FindMethod, _ => (parameter.Name switch { "InvokeValue" => runtime.Invocation.Value, "InvokeMethodValue" => runtime.Invocation.Method, "InvokeMethodValue0" => runtime.Invocation.Method0, _ => (parameter.Name! switch { "UndefinedType" => runtime.Sentinels.UndefinedType, "UndefinedInstance" => runtime.Sentinels.UndefinedInstance, _ => typeof(EmittedRuntime).GetProperty(parameter.Name!)!.GetValue(runtime) }) }) }) }) }) }) });
        }).ToArray());
    }
}
