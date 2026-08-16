using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void DefineObjectPrototypePopulateShell(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.ObjectPrototypePopulateMethod = typeBuilder.DefineMethod(
            "_ObjectPrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    /// <summary>
    /// Populates <see cref="EmittedRuntime.ObjectPrototypeField"/> with
    /// <c>$TSFunction</c> wrappers for hasOwnProperty/isPrototypeOf/toString/
    /// valueOf/etc. Required for Test262 tests that probe
    /// <c>Object.prototype.isPrototypeOf(SomeBuiltin.prototype)</c>.
    /// </summary>
    private void EmitObjectPrototypePopulate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = runtime.ObjectPrototypePopulateMethod;
        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, runtime.ObjectPrototypeField);

        // ECMA-262 19.1.3 Object.prototype.constructor === Object. Compiled
        // bare `Object` resolves to typeof(object) (per ObjectStaticEmitter).
        // Plant in dict + non-enumerable PDS descriptor (built-in §17 attrs).
        var protoDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        EmitInstallConstructor(il, runtime, runtime.ObjectPrototypeField, protoDescLocal, setItem, () =>
        {
            il.Emit(OpCodes.Ldtoken, _types.Object);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });

        // Wire methods backed by $Runtime helpers. Each wrapper has the
        // helper as its MethodInfo and uses TSFunctionCtorWithCache for
        // proper .name + .length per ECMA-262. The helpers define their own
        // "__this" first-parameter name at their emit site (nameThisParam:
        // false skips the rename).
        void Wire(string jsName, MethodBuilder helper, int jsLength)
            => EmitWirePrototypeMethod(il, runtime, runtime.ObjectPrototypeField, protoDescLocal,
                setItem, jsName, helper, jsLength, nameThisParam: false);

        Wire("hasOwnProperty", runtime.HasOwnPropertyHelperMethod, 1);
        Wire("isPrototypeOf",  runtime.IsPrototypeOfHelperMethod,  1);
        // toString — ECMA-262 19.1.3.6 returns "[object X]" brand. Borrowed-
        // method patterns (`obj.getClass = Object.prototype.toString;
        // obj.getClass()`) need a real brand-tag function (the generic stub
        // returns Convert.ToString of receiver, which is wrong for arrays).
        Wire("toString",       runtime.ObjectProtoToStringHelper, 0);
        // ECMA-262 19.1.3.7 Object.prototype.valueOf returns ! ToObject(this).
        // For non-null/undefined values that means returning the receiver as
        // a JS object (we don't distinguish here — primitive receivers get the
        // primitive back, which the materializer's ToPrimitive treats as a
        // "valueOf returned non-primitive" signal so toString fires next).
        Wire("valueOf",        runtime.ObjectProtoValueOfHelper, 0);
        // toLocaleString = ToObject(this).toString — split helper enforces the
        // spec null/undef throw before delegating to ObjectProtoToString.
        Wire("toLocaleString", runtime.ObjectProtoToLocaleStringHelper, 0);
        Wire("propertyIsEnumerable", runtime.PropertyIsEnumerableHelperMethod, 1);
        // ECMA-262 §B.2.2 legacy accessor lookup methods.
        Wire("__lookupGetter__", runtime.LookupGetterHelperMethod, 1);
        Wire("__lookupSetter__", runtime.LookupSetterHelperMethod, 1);
        Wire("__defineGetter__", runtime.DefineGetterHelperMethod, 2);
        Wire("__defineSetter__", runtime.DefineSetterHelperMethod, 2);

        il.Emit(OpCodes.Ret);
    }
}
