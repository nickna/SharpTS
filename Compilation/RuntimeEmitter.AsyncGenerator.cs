using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits async generator interface and support methods into the generated assembly.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $IAsyncGenerator interface that extends IAsyncEnumerator&lt;object&gt; with async Return/Throw methods.
    /// </summary>
    private void EmitAsyncGeneratorInterface(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define interface: public interface $IAsyncGenerator : IAsyncEnumerator<object>, IAsyncEnumerable<object>
        var interfaceBuilder = moduleBuilder.DefineType(
            "$IAsyncGenerator",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            null,
            [_types.IAsyncEnumeratorOfObject, _types.IAsyncEnumerableOfObject]
        );
        runtime.AsyncGeneratorInterfaceType = interfaceBuilder;

        // Define next() method: Task<object> next()
        // This wraps MoveNextAsync + Current into a single async call returning iterator result
        // Using lowercase to match JavaScript API
        var nextMethod = interfaceBuilder.DefineMethod(
            "next",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.TaskOfObject,
            Type.EmptyTypes
        );
        runtime.AsyncGeneratorNextMethod = nextMethod;

        // Define return(object value) method: Task<object> return(object value)
        // Note: "return" is a C# keyword but valid as a method name via reflection
        var returnMethod = interfaceBuilder.DefineMethod(
            "return",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.TaskOfObject,
            [_types.Object]
        );
        runtime.AsyncGeneratorReturnMethod = returnMethod;

        // Define throw(object error) method: Task<object> throw(object error)
        // Note: "throw" is a C# keyword but valid as a method name via reflection
        var throwMethod = interfaceBuilder.DefineMethod(
            "throw",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.TaskOfObject,
            [_types.Object]
        );
        runtime.AsyncGeneratorThrowMethod = throwMethod;

        interfaceBuilder.CreateType();
    }
}
