using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits a weak-target validation helper — <c>void methodName(object value)</c> — that throws
    /// when the value is a JS primitive: string, number (boxed double/int/long/float/decimal), or
    /// boolean. WeakMap keys, WeakSet values, and WeakRef targets share this single primitive-probe
    /// sequence so future Symbol/BigInt/null conformance fixes cannot drift between the three weak
    /// constructs; only the method name and the construct-specific error wording differ.
    /// </summary>
    /// <param name="errorPrefix">
    /// Construct-specific message up to (but not including) the <c>, not 'type'.</c> suffix, e.g.
    /// "Runtime Error: Invalid value used as weak map key. WeakMap keys must be objects".
    /// </param>
    private MethodBuilder EmitWeakTargetValidator(TypeBuilder typeBuilder, string methodName, string errorPrefix)
    {
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        var stringLabel = il.DefineLabel();
        var numberLabel = il.DefineLabel();
        var booleanLabel = il.DefineLabel();
        var validLabel = il.DefineLabel();

        // Check string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Check the boxed numeric primitives
        foreach (var numericType in new[] { _types.Double, _types.Int32, _types.Int64, _types.Single, _types.Decimal })
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, numericType);
            il.Emit(OpCodes.Brtrue, numberLabel);
        }

        // Check bool (boxed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, booleanLabel);

        // Value is valid (not a primitive)
        il.Emit(OpCodes.Br, validLabel);

        il.MarkLabel(stringLabel);
        EmitThrow("string");
        il.MarkLabel(numberLabel);
        EmitThrow("number");
        il.MarkLabel(booleanLabel);
        EmitThrow("boolean");

        // Valid - just return
        il.MarkLabel(validLabel);
        il.Emit(OpCodes.Ret);

        return method;

        void EmitThrow(string typeofName)
        {
            il.Emit(OpCodes.Ldstr, $"{errorPrefix}, not '{typeofName}'.");
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
            il.Emit(OpCodes.Throw);
        }
    }
}
