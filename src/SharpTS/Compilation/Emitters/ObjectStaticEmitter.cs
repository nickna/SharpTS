using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Object static method calls.
/// Handles Object.keys(), Object.values(), Object.entries().
/// </summary>
public sealed class ObjectStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for an Object static method call.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Closed non-escaping record (#1506): Object.keys(o) needs a fresh mutable result, but it
        // does not need to box/materialize o or enter GetKeys' descriptor/reflection dispatch. Shape
        // field order is the proven enumerable string-key order for this restricted record domain.
        if (methodName == "keys" && arguments is [Expr.Variable receiver]
            && ctx.TryGetPromotedObjectLocal(receiver.Name.Lexeme) is { } promoted)
        {
            il.Emit(OpCodes.Ldsfld, promoted.Shape.KeyMetadataField);
            il.Emit(OpCodes.Newobj, ctx.Types.GetConstructor(
                ctx.Types.ListOfObject, ctx.Types.IEnumerableOfObject));
            return true;
        }

        // Object methods take exactly one argument. The first arg is pushed
        // by the shared prologue below. Object.is is the exception — missing
        // arg1 should default to undefined (not null) so Object.is() returns
        // true (SameValue(undefined, undefined)) per ECMA-262 §20.1.2.13.
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else if (methodName == "is")
        {
            il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        switch (methodName)
        {
            case "keys":
                il.Emit(OpCodes.Call, ctx.Runtime!.GetKeys);
                return true;
            case "values":
                il.Emit(OpCodes.Call, ctx.Runtime!.GetValues);
                return true;
            case "entries":
                il.Emit(OpCodes.Call, ctx.Runtime!.GetEntries);
                return true;
            case "fromEntries":
                // Load Symbol.iterator and runtime type for IterateToList
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.SymbolIterator);
                il.Emit(OpCodes.Ldtoken, ctx.Runtime!.RuntimeType);
                il.Emit(OpCodes.Call, ctx.Types.TypeGetTypeFromHandle);
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectFromEntries);
                return true;
            case "hasOwn":
                // hasOwn takes 2 arguments: obj and key
                // First argument is already on the stack, emit second argument
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectHasOwn);
                // Box the bool result for consistency with other methods
                il.Emit(OpCodes.Box, typeof(bool));
                return true;
            case "is":
                // is takes 2 arguments: value1 and value2
                // First argument is already on the stack, emit second argument.
                // ECMA-262: missing arg2 → undefined (NOT null). Object.is(null)
                // is SameValue(null, undefined) which is false (different types).
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectIs);
                // Box the bool result for consistency with other methods
                il.Emit(OpCodes.Box, typeof(bool));
                return true;
            case "assign":
                // Object.assign(target, ...sources)
                // First argument (target) is already on the stack
                // Create a List<object> for all source arguments
                var listType = typeof(List<object?>);
                var listCtor = listType.GetConstructor(Type.EmptyTypes)!;
                var listAdd = listType.GetMethod("Add")!;

                // Create the sources list
                il.Emit(OpCodes.Newobj, listCtor);

                // Add each source argument to the list
                for (int i = 1; i < arguments.Count; i++)
                {
                    il.Emit(OpCodes.Dup);  // Duplicate list reference
                    emitter.EmitExpression(arguments[i]);
                    emitter.EmitBoxIfNeeded(arguments[i]);
                    il.Emit(OpCodes.Callvirt, listAdd);
                }

                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectAssign);
                return true;
            case "freeze":
                // Object.freeze(obj) - freezes the object and returns it
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectFreeze);
                return true;
            case "seal":
                // Object.seal(obj) - seals the object and returns it
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectSeal);
                return true;
            case "isFrozen":
                // Object.isFrozen(obj) - returns true if the object is frozen
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectIsFrozen);
                il.Emit(OpCodes.Box, typeof(bool));
                return true;
            case "isSealed":
                // Object.isSealed(obj) - returns true if the object is sealed
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectIsSealed);
                il.Emit(OpCodes.Box, typeof(bool));
                return true;
            case "defineProperty":
                // Object.defineProperty(obj, prop, descriptor) - defines a property
                // First argument (obj) is already on the stack
                // Emit second argument (property name)
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                // Emit third argument (descriptor)
                if (arguments.Count > 2)
                {
                    emitter.EmitExpression(arguments[2]);
                    emitter.EmitBoxIfNeeded(arguments[2]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectDefineProperty);
                return true;
            case "getOwnPropertyDescriptor":
                // Object.getOwnPropertyDescriptor(obj, prop) - gets a property descriptor.
                // ECMA-262 §20.1.2.6 step 1: Let obj be ? ToObject(O). ToObject
                // throws TypeError on null/undefined.
                EmitToObjectGuard(il, ctx.Runtime!, "Object.getOwnPropertyDescriptor");
                // First argument (obj) is already on the stack
                // Emit second argument (property name)
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectGetOwnPropertyDescriptor);
                return true;
            case "getOwnPropertyNames":
                // Object.getOwnPropertyNames(obj) - gets all own property names
                il.Emit(OpCodes.Call, ctx.Runtime!.GetOwnPropertyNames);
                return true;
            case "create":
                // Object.create(proto, propertiesObject?) - creates a new object with prototype
                // First argument (proto) is already on the stack
                // Emit second argument (propertiesObject) - optional. ECMA-262
                // §20.1.2.2 step 3 distinguishes Properties === undefined (skip)
                // from Properties === null (TypeError via ObjectDefineProperties).
                // Push $Undefined.Instance for the missing-arg case so the
                // runtime can apply the correct branch.
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectCreate);
                return true;
            case "preventExtensions":
                // Object.preventExtensions(obj) - prevents adding new properties
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectPreventExtensions);
                return true;
            case "isExtensible":
                // Object.isExtensible(obj) - returns whether object can have new properties
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectIsExtensible);
                il.Emit(OpCodes.Box, typeof(bool));
                return true;
            case "getOwnPropertySymbols":
                // Object.getOwnPropertySymbols(obj) - returns array of symbol-keyed properties
                il.Emit(OpCodes.Call, ctx.Runtime!.GetOwnPropertySymbols);
                return true;
            case "getPrototypeOf":
                // Object.getPrototypeOf(obj) - returns the prototype
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectGetPrototypeOf);
                return true;
            case "setPrototypeOf":
                // Object.setPrototypeOf(obj, proto) - sets the prototype
                // First argument (obj) is already on the stack
                // Emit second argument (proto)
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    // A missing prototype argument is undefined, not null;
                    // Object.setPrototypeOf({}, undefined) must reject it.
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectSetPrototypeOf);
                return true;
            case "groupBy":
                // Object.groupBy(iterable, callback) - groups elements by callback return
                // First argument (iterable) is already on the stack
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectGroupBy);
                return true;
            case "defineProperties":
                // Object.defineProperties(obj, props) - defines multiple properties
                // First argument (obj) is already on the stack
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectDefineProperties);
                return true;
            case "getOwnPropertyDescriptors":
                // Object.getOwnPropertyDescriptors(obj) - gets all property descriptors
                // First argument (obj) is already on the stack
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectGetOwnPropertyDescriptors);
                return true;
            default:
                // Pop the argument we pushed and return false
                il.Emit(OpCodes.Pop);
                return false;
        }
    }

    /// <summary>
    /// Object has no static properties.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var runtime = ctx.Runtime!;
        var il = ctx.IL;

        // Route static property reads through the value-form runtime lookup.
        // It checks PDS shadows before intrinsic members, so assignments such
        // as `Object.keys = replacement` become observable while retaining the
        // identity-stable wrappers used for untouched built-ins.
        il.Emit(OpCodes.Ldtoken, ctx.Types.Object);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Ldstr, propertyName);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        return true;
    }

    // Keep typeof's built-in-method shortcut from classifying Object's
    // non-callable constructor metadata as functions.
    public bool HasStaticProperty(string memberName) => memberName is
        "prototype" or "length" or "name";

    /// <summary>
    /// Emits a TypeError throw if the value on top of stack is null or $Undefined.Instance.
    /// Mirrors ECMA-262 ToObject step 1. Stack is preserved (value remains on top).
    /// </summary>
    private static void EmitToObjectGuard(ILGenerator il, EmittedRuntime runtime, string callName)
    {
        var throwLabel = il.DefineLabel();
        var okLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, throwLabel);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, throwLabel);
        il.Emit(OpCodes.Br, okLabel);
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, callName + " called on null or undefined");
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);
        il.MarkLabel(okLabel);
    }
}
