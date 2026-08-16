using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $ArrayHole singleton class — sentinel for ECMA-262 array holes
    /// (index in [0, length) that was never written).
    /// Mirror of <c>SharpTS.Runtime.Types.ArrayHole</c> for standalone assemblies.
    /// </summary>
    private void EmitArrayHoleClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // public sealed class $ArrayHole
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$ArrayHole",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // public static readonly $ArrayHole Instance = new $ArrayHole();
        var instanceField = typeBuilder.DefineField(
            "Instance",
            typeBuilder,
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly
        );

        // Private constructor (singleton)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Private,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ret);

        // Static constructor: Instance = new $ArrayHole();
        var cctor = typeBuilder.DefineTypeInitializer();
        var cctorIL = cctor.GetILGenerator();
        cctorIL.Emit(OpCodes.Newobj, ctor);
        cctorIL.Emit(OpCodes.Stsfld, instanceField);
        cctorIL.Emit(OpCodes.Ret);

        // public override string ToString() => "undefined"
        // Holes render as undefined at the language boundary; this lets any
        // accidental stringification (debug prints) not leak the sentinel name.
        var toStringMethod = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var toStringIL = toStringMethod.GetILGenerator();
        toStringIL.Emit(OpCodes.Ldstr, "undefined");
        toStringIL.Emit(OpCodes.Ret);

        var createdType = typeBuilder.CreateType()!;
        runtime.ArrayHoleType = createdType;
        runtime.ArrayHoleInstance = createdType.GetField("Instance")!;
    }
}
