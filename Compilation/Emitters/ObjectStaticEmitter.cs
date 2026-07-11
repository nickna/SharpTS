using System.Reflection;
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
                    il.Emit(OpCodes.Ldnull);
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

        // Object.prototype — singleton dict populated lazily with hasOwnProperty,
        // isPrototypeOf, toString, valueOf wrappers. Required for Test262
        // patterns like `Object.prototype.isPrototypeOf(Number.prototype)`.
        if (propertyName == "prototype")
        {
            var protoIL = ctx.IL;
            protoIL.Emit(OpCodes.Call, runtime.ObjectPrototypePopulateMethod);
            protoIL.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
            return true;
        }

        // Constructor metadata properties (ECMA-262 §20.1.2): Object.length is 1, name is "Object".
        if (propertyName == "length")
        {
            ctx.IL.Emit(OpCodes.Ldc_R8, 1.0);
            ctx.IL.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }
        if (propertyName == "name")
        {
            ctx.IL.Emit(OpCodes.Ldstr, "Object");
            return true;
        }

        // Stage 4y: expose Object.* static methods as values so
        // `let f = Object.keys; f(obj)` works AND so test262's isConstructor
        // harness sees `typeof f === "function"`.
        // Only methods with a uniform `(object) -> object` or
        // `(object[], object) -> object` shape compatible with the
        // $TSFunction reflection-dispatch path are wrapped here. Methods
        // with mismatched return types (e.g. bool) need their own adapters
        // — deferred to a follow-up stage.
        MethodInfo? method = propertyName switch
        {
            "keys"                 => runtime.GetKeys,
            "values"               => runtime.GetValues,
            "entries"              => runtime.GetEntries,
            "fromEntries"          => runtime.ObjectFromEntries,
            "freeze"               => runtime.ObjectFreeze,
            "seal"                 => runtime.ObjectSeal,
            "preventExtensions"    => runtime.ObjectPreventExtensions,
            "getOwnPropertyNames"  => runtime.GetOwnPropertyNames,
            "getOwnPropertySymbols" => runtime.GetOwnPropertySymbols,
            "getPrototypeOf"       => runtime.ObjectGetPrototypeOf,
            "setPrototypeOf"       => runtime.ObjectSetPrototypeOf,
            "defineProperty"       => runtime.ObjectDefineProperty,
            "defineProperties"     => runtime.ObjectDefineProperties,
            "getOwnPropertyDescriptor"  => runtime.ObjectGetOwnPropertyDescriptor,
            "getOwnPropertyDescriptors" => runtime.ObjectGetOwnPropertyDescriptors,
            // Value-form wrapper (NOT raw ObjectCreate): under-application via
            // $TSFunction pads props with null, which the raw method must
            // treat as the explicit-null TypeError. Must match the runtime
            // LookupBuiltInStaticMember entry so wrapper identity holds across
            // both access forms.
            "create"               => runtime.ObjectCreateValueForm,
            "assign"               => runtime.ObjectAssign,
            "is"                   => runtime.ObjectIs,
            "hasOwn"               => runtime.ObjectHasOwn,
            "groupBy"              => runtime.ObjectGroupBy,
            "isExtensible"         => runtime.ObjectIsExtensible,
            "isFrozen"             => runtime.ObjectIsFrozen,
            "isSealed"             => runtime.ObjectIsSealed,
            _ => null
        };
        if (method == null) return false;

        // ECMA-262 §17: built-in function `name` matches the spec property name,
        // and `length` is the spec-defined arity (not derived from CLR signature
        // — e.g. `assign(target, ...sources)` has spec length 2 but our .NET
        // implementation is `(object, List<object?>)` where the rest param is
        // skipped by lazy compute, returning 1). Pass explicit length per spec.
        int specLength = propertyName switch
        {
            "setPrototypeOf" => 2,
            "defineProperty" => 3,
            "defineProperties" => 2,
            "getOwnPropertyDescriptor" => 2,
            "create" => 2,
            "assign" => 2,
            "is" => 2,
            "hasOwn" => 2,
            "groupBy" => 2,
            _ => 1,
        };

        // Use the GetOrCreate factory (not the ctor directly) so repeated
        // access to the same Object.X returns the SAME $TSFunction instance.
        // Without identity, `Object.is === Object.is` would be false and any
        // `delete fn.length` mark wouldn't survive — propertyHelper's
        // isConfigurable check fails because the second instance is fresh.
        var il = ctx.IL;
        il.Emit(OpCodes.Ldtoken, method);
        il.Emit(OpCodes.Ldtoken, method.DeclaringType!);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.MethodBase, "GetMethodFromHandle",
            ctx.Types.RuntimeMethodHandle, ctx.Types.RuntimeTypeHandle));
        il.Emit(OpCodes.Castclass, ctx.Types.MethodInfo);
        il.Emit(OpCodes.Ldstr, propertyName);
        il.Emit(OpCodes.Ldc_I4, specLength);
        il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
        return true;
    }

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
