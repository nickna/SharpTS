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
            if (parameter.Name == "TSNamespaceType") return runtime.Namespaces.Type;
            if (parameter.Name == "TSNamespaceGet") return runtime.Namespaces.Get;
            if (parameter.Name == "CommonJs") return runtime.Modules.CommonJs;
            if (parameter.Name == "GlobalThisSingletonField") return runtime.GlobalObject.SingletonField;
            if (parameter.Name == "GlobalThisGetProperty") return runtime.GlobalObject.GetProperty;
            if (parameter.Name == "LookupBuiltInStaticMember") return runtime.BuiltInStatics.Lookup;
            if (parameter.Name == "TSFunctionLengthGetter") return runtime.FunctionValues.LengthGetter;
            if (parameter.Name == "TSFunctionNameGetter") return runtime.FunctionValues.NameGetter;
            if (parameter.Name == "TSFunctionCachedCtor") return runtime.FunctionConstruction.CachedConstructor;
            if (parameter.Name == "GeneratorInterface") return runtime.Generators.Type;
            if (parameter.Name == "AsyncGeneratorInterface") return runtime.AsyncGenerators?.Type;
            if (parameter.Name == "ReadGeneratorProperty") return runtime.GeneratorPrototypes.ReadIteratorProperty;
            if (parameter.Name == "ReflectGet") return runtime.Reflect.Get;
            if (parameter.Name == "MethodCallableCtor") return runtime.ReflectedMethods.CallableConstructor;
            if (parameter.Name == "ReflectedMethodCacheField") return runtime.ReflectedMethods.Cache;
            if (parameter.Name == "ToPascalCase") return runtime.ReflectedMethods.ToPascalCase;
            if (parameter.Name == "NativeProxyRejection") return runtime.ReflectedMethods.NativeProxyRejection;
            return parameter.Name switch
            {
                "TSFunctionCtor" => runtime.FunctionConstruction.Constructor,
                "TSFunctionCtorWithCache" => runtime.FunctionConstruction.CachedConstructor,
                "TSFunctionGetOrCreate" => runtime.FunctionConstruction.GetOrCreate,
                "ArgumentsLengthField" => runtime.Arguments.LengthField,
                "ArgumentsType" => runtime.Arguments.Type,
                "BoundAnyFunctionType" => runtime.FunctionBindings.AnyType,
                "BoundTSFunctionType" => runtime.FunctionBindings.BoundType,
                "FunctionApplyWrapperType" => runtime.FunctionBindings.ApplyType,
                "FunctionBindWrapperType" => runtime.FunctionBindings.BindType,
                "FunctionCallWrapperType" => runtime.FunctionBindings.CallType,
                "TSFunctionBindThis" => runtime.FunctionValues.BindThis,
                "TSFunctionExpectsThisField" => runtime.FunctionValues.ExpectsThisField,
                "TSFunctionInvokeWithThis" => runtime.FunctionValues.InvokeWithThis,
                "TSFunctionType" => runtime.FunctionValues.Type,
                "FunctionPrototypeField" => runtime.FunctionPrototypes.Prototype,
                "FunctionPrototypePopulateMethod" => runtime.FunctionPrototypes.Populate,
                "GetFunctionMethod" => runtime.FunctionIntrospection.GetProperty,
                "ToPropertyKey" => runtime.Reflect.ToPropertyKey,
                "UserClasses" => false,
                "InvokeMethodUnwrapped" => runtime.ReflectedMethods.InvokeUnwrapped,
                "SafeGetMethod" => runtime.ReflectedMethods.FindMethod,
                "InvokeValue" => runtime.Invocation.Value,
                "InvokeMethodValue" => runtime.Invocation.Method,
                "InvokeMethodValue0" => runtime.Invocation.Method0,
                "IHasFieldsInterface" => runtime.ObjectFields.Interface,
                "GuestClassFactoryAttribute" => runtime.DynamicConstruction.FactoryAttributeType,
                "IHasFieldsGetProperty" => runtime.ObjectFields.GetProperty,
                "IHasFieldsHasProperty" => runtime.ObjectFields.HasProperty,
                "UndefinedType" => runtime.Sentinels.UndefinedType,
                "UndefinedInstance" => runtime.Sentinels.UndefinedInstance,
                _ => (typeof(EmittedRuntime).GetProperty(parameter.Name!)
                    ?? throw new InvalidOperationException(
                        $"Object-read fixture has no input mapping for {type.Name}.{parameter.Name}."))
                    .GetValue(runtime)
            };
        }).ToArray());
    }
}
