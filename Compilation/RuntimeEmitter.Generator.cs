using System.Collections;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits generator interface and support methods into the generated assembly.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $IGenerator interface that extends IEnumerator&lt;object&gt; with Return/Throw methods.
    /// </summary>
    private void EmitGeneratorInterface(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define interface: public interface $IGenerator : IEnumerator<object>, IEnumerable<object>
        var interfaceBuilder = moduleBuilder.DefineType(
            "$IGenerator",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            null,
            [_types.IEnumeratorOfObject, _types.IEnumerableOfObject]
        );
        runtime.GeneratorInterfaceType = interfaceBuilder;

        // Define next() method: object next()
        // This wraps MoveNext + Current into a single call returning iterator result
        // Using lowercase to match JavaScript API
        var nextMethod = interfaceBuilder.DefineMethod(
            "next",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.GeneratorNextMethod = nextMethod;

        // Define return(object value) method: object return(object value)
        // Note: "return" is a C# keyword but valid as a method name via reflection
        var returnMethod = interfaceBuilder.DefineMethod(
            "return",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Object,
            [_types.Object]
        );
        runtime.GeneratorReturnMethod = returnMethod;

        // Define throw(object error) method: object throw(object error)
        // Note: "throw" is a C# keyword but valid as a method name via reflection
        var throwMethod = interfaceBuilder.DefineMethod(
            "throw",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Object,
            [_types.Object]
        );
        runtime.GeneratorThrowMethod = throwMethod;

        interfaceBuilder.CreateType();
    }
}

