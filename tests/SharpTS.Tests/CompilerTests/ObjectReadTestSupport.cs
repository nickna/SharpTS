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
            return (parameter.Name switch { "TSFunctionCtor" => runtime.FunctionConstruction.Constructor, "TSFunctionCtorWithCache" => runtime.FunctionConstruction.CachedConstructor, "TSFunctionGetOrCreate" => runtime.FunctionConstruction.GetOrCreate, _ => typeof(EmittedRuntime).GetProperty(parameter.Name!)!.GetValue(runtime) });
        }).ToArray());
    }
}
