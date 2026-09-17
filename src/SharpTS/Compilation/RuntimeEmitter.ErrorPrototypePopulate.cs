using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct ErrorPrototypePopulateInputs(
        EmittedDescriptorStorageRuntime DescriptorStorage,
        MethodBuilder GetProperty,
        FieldBuilder ObjectPrototypeField,
        EmittedStringCoercionRuntime StringCoercion,
        EmittedSymbolRuntime Symbols,
        ConstructorBuilder TSFunctionCtorWithCache,
        Type UndefinedType
    );

    private readonly record struct ErrorToStringSpecHelperInputs(
        MethodBuilder GetProperty,
        EmittedStringCoercionRuntime StringCoercion,
        EmittedSymbolRuntime Symbols,
        Type UndefinedType
    );

    /// <summary>
    /// Populates <see cref="EmittedErrorRuntime.Prototype"/> with
    /// <c>$TSFunction</c> wrappers for the spec-compliant
    /// <c>ErrorToStringSpec</c> + <c>constructor</c> slot. Reached by
    /// <c>Error.prototype.toString.call(non-error)</c> via GetProperty's
    /// Type-receiver branch — required so the brand-checking helper runs
    /// instead of generic class reflection on $Error.
    /// </summary>
    private void DefineErrorPrototypePopulateShell(TypeBuilder typeBuilder, EmittedErrorRuntime errors)
    {
        errors.PrototypePopulate = typeBuilder.DefineMethod(
            "_ErrorPrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void EmitErrorPrototypePopulate(
        TypeBuilder typeBuilder,
        EmittedErrorRuntime errors,
        ErrorPrototypePopulateInputs inputs
    )
    {
        var descriptors = new PrototypeDescriptorInputs(
            inputs.DescriptorStorage.DescriptorConstructor, inputs.DescriptorStorage.DescriptorValue.GetSetMethod()!,
            inputs.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!, inputs.DescriptorStorage.DefineProperty);
        // Emit the spec-compliant toString helper before the populate body
        // that wires it up.
        var errorToStringSpec = EmitErrorToStringSpecHelper(
            typeBuilder,
            errors,
            new ErrorToStringSpecHelperInputs(
                inputs.GetProperty,
                inputs.StringCoercion,
                inputs.Symbols,
                inputs.UndefinedType
            )
        );

        var method = errors.PrototypePopulate;
        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, errors.Prototype);

        // ECMA-262 20.5.3 Error.prototype.constructor === Error. Compiled
        // bare `Error` resolves to typeof($Error).
        il.Emit(OpCodes.Ldsfld, errors.Prototype);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Ldtoken, errors.Type);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Callvirt, setItem);

        // ECMA-262 20.5.3 Error.prototype.name === "Error" and message === "".
        il.Emit(OpCodes.Ldsfld, errors.Prototype);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Ldstr, "Error");
        il.Emit(OpCodes.Callvirt, setItem);
        il.Emit(OpCodes.Ldsfld, errors.Prototype);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Callvirt, setItem);

        // Wire the toString $TSFunction wrapper. First parameter named "__this"
        // so $TSFunction.InvokeWithThis prepends the call-site receiver when
        // borrowed (`obj.toString = Error.prototype.toString; obj.toString()`).
        try { errorToStringSpec.DefineParameter(1, ParameterAttributes.None, "__this"); }
        catch { /* already named — ignore */ }
        var errToStringWrapperLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull);
        _types.EmitLoadMethodInfo(il, errorToStringSpec);
        il.Emit(OpCodes.Ldstr, "toString");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, inputs.TSFunctionCtorWithCache);
        il.Emit(OpCodes.Stloc, errToStringWrapperLocal);
        il.Emit(OpCodes.Ldsfld, errors.Prototype);
        il.Emit(OpCodes.Ldstr, "toString");
        il.Emit(OpCodes.Ldloc, errToStringWrapperLocal);
        il.Emit(OpCodes.Callvirt, setItem);

        // Install non-enumerable PDS descriptors for constructor/name/message/
        // toString per ECMA-262 §20.5.3 + §17 (built-in data properties are
        // W:T,E:F,C:T).
        var errDescLocal = il.DeclareLocal(inputs.DescriptorStorage.DescriptorType);
        void InstallNonEnumerableErr(string jsName, System.Action emitValue)
            => EmitInstallNonEnumerableDescriptor(il, descriptors, errors.Prototype, errDescLocal, jsName, emitValue);
        InstallNonEnumerableErr("constructor", () =>
        {
            il.Emit(OpCodes.Ldtoken, errors.Type);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });
        InstallNonEnumerableErr("name", () => il.Emit(OpCodes.Ldstr, "Error"));
        InstallNonEnumerableErr("message", () => il.Emit(OpCodes.Ldstr, ""));
        InstallNonEnumerableErr("toString", () => il.Emit(OpCodes.Ldloc, errToStringWrapperLocal));

        // Per ECMA-262 §20.5.3 Error.prototype's [[Prototype]] is %Object.prototype%.
        il.Emit(OpCodes.Ldsfld, errors.Prototype);
        il.Emit(OpCodes.Ldsfld, inputs.ObjectPrototypeField);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.SetPrototype);

        il.Emit(OpCodes.Ret);
        errors.MarkPrototypeBodyEmitted();
    }

    /// <summary>
    /// Emits ECMA-262 20.5.3.4 Error.prototype.toString:
    /// <list type="number">
    /// <item>If <c>this</c> is not an Object, throw TypeError.</item>
    /// <item>Read <c>name</c> (default "Error") and <c>message</c> (default "")
    /// via Get; coerce both to strings via ToString.</item>
    /// <item>Return name + ": " + message, or just one if either is empty.</item>
    /// </list>
    /// "Object" here means anything that isn't <c>null</c>, <c>$Undefined</c>,
    /// <c>bool</c>, <c>double</c>, <c>string</c> — matches the $Runtime.TypeOf
    /// classification.
    /// </summary>
    private MethodBuilder EmitErrorToStringSpecHelper(
        TypeBuilder typeBuilder,
        EmittedErrorRuntime errors,
        ErrorToStringSpecHelperInputs inputs
    )
    {
        var method = typeBuilder.DefineMethod(
            "ErrorToStringSpec",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);
        var il = method.GetILGenerator();

        var throwLabel = il.DefineLabel();
        var passLabel = il.DefineLabel();

        // Step 2: brand check. Anything primitive throws TypeError.
        // null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwLabel);
        // $Undefined
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, throwLabel);
        // bool
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, throwLabel);
        // double
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, throwLabel);
        // string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, throwLabel);
        // Symbol (TSSymbol). ECMA-262 §20.5.3.4 step 2 brand check rejects all
        // primitives including Symbol; the dispatch above only catches the
        // common five. invalid-receiver.js iterates Symbol() alongside the
        // others.
        if (inputs.Symbols.Type != null)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, inputs.Symbols.Type);
            il.Emit(OpCodes.Brtrue, throwLabel);
        }

        il.Emit(OpCodes.Br, passLabel);

        il.MarkLabel(throwLabel);
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, "Error.prototype.toString called on non-object");

        il.MarkLabel(passLabel);

        // Step 3-4: name = Get(O, "name"); if undefined, name = "Error".
        var nameLocal = il.DeclareLocal(_types.Object);
        var nameStrLocal = il.DeclareLocal(_types.String);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, inputs.GetProperty);
        il.Emit(OpCodes.Stloc, nameLocal);

        var nameDefinedLabel = il.DefineLabel();
        var nameDoneLabel = il.DefineLabel();
        // undefined?
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Brfalse, nameDefinedLabel); // null acts as undefined here
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brfalse, nameDefinedLabel);
        // name is undefined → "Error"
        il.Emit(OpCodes.Ldstr, "Error");
        il.Emit(OpCodes.Stloc, nameStrLocal);
        il.Emit(OpCodes.Br, nameDoneLabel);

        il.MarkLabel(nameDefinedLabel);
        // ToString(name) via $Runtime.ToJsString.
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, inputs.StringCoercion.ToJsString);
        il.Emit(OpCodes.Stloc, nameStrLocal);
        il.MarkLabel(nameDoneLabel);

        // Step 5-6: message = Get(O, "message"); if undefined, message = "".
        var msgLocal = il.DeclareLocal(_types.Object);
        var msgStrLocal = il.DeclareLocal(_types.String);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Call, inputs.GetProperty);
        il.Emit(OpCodes.Stloc, msgLocal);

        var msgDefinedLabel = il.DefineLabel();
        var msgDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, msgLocal);
        il.Emit(OpCodes.Brfalse, msgDefinedLabel);
        il.Emit(OpCodes.Ldloc, msgLocal);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brfalse, msgDefinedLabel);
        // message is undefined → ""
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, msgStrLocal);
        il.Emit(OpCodes.Br, msgDoneLabel);

        il.MarkLabel(msgDefinedLabel);
        il.Emit(OpCodes.Ldloc, msgLocal);
        il.Emit(OpCodes.Call, inputs.StringCoercion.ToJsString);
        il.Emit(OpCodes.Stloc, msgStrLocal);
        il.MarkLabel(msgDoneLabel);

        // Step 7-9: combine.
        // if (name == "") return msg
        var nameNotEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nameStrLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, nameNotEmptyLabel);
        il.Emit(OpCodes.Ldloc, msgStrLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nameNotEmptyLabel);

        // if (msg == "") return name
        var msgNotEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, msgStrLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, msgNotEmptyLabel);
        il.Emit(OpCodes.Ldloc, nameStrLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(msgNotEmptyLabel);

        // return name + ": " + msg
        il.Emit(OpCodes.Ldloc, nameStrLocal);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Ldloc, msgStrLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Pre-declares the populate-method shells for each NativeError subclass
    /// prototype. Same idempotent pattern as Error.prototype: each method
    /// fills the dict with constructor/name/message + non-enumerable PDS
    /// descriptors, and chains [[Prototype]] to %Error.prototype%.
    /// </summary>
    private void DefineNativeErrorPrototypePopulateShells(TypeBuilder typeBuilder, EmittedErrorRuntime errors)
    {
        MethodBuilder DefineShell(string name) => typeBuilder.DefineMethod(
            name, MethodAttributes.Public | MethodAttributes.Static,
            _types.Void, Type.EmptyTypes);
        errors.TypeErrorPrototypePopulate      = DefineShell("_TypeErrorPrototypePopulate");
        errors.RangeErrorPrototypePopulate     = DefineShell("_RangeErrorPrototypePopulate");
        errors.ReferenceErrorPrototypePopulate = DefineShell("_ReferenceErrorPrototypePopulate");
        errors.SyntaxErrorPrototypePopulate    = DefineShell("_SyntaxErrorPrototypePopulate");
        errors.URIErrorPrototypePopulate       = DefineShell("_URIErrorPrototypePopulate");
        errors.EvalErrorPrototypePopulate      = DefineShell("_EvalErrorPrototypePopulate");
        errors.AggregateErrorPrototypePopulate = DefineShell("_AggregateErrorPrototypePopulate");
    }

    private void EmitNativeErrorPrototypePopulates(EmittedErrorRuntime errors, EmittedDescriptorStorageRuntime descriptorStorage)
    {
        EmitOneNativeErrorPopulate(
            errors,
            descriptorStorage,
            errors.TypeErrorPrototypePopulate,
            errors.TypeErrorPrototype,
            errors.TypeErrorType,
            "TypeError"
        );
        EmitOneNativeErrorPopulate(
            errors,
            descriptorStorage,
            errors.RangeErrorPrototypePopulate,
            errors.RangeErrorPrototype,
            errors.RangeErrorType,
            "RangeError"
        );
        EmitOneNativeErrorPopulate(
            errors,
            descriptorStorage,
            errors.ReferenceErrorPrototypePopulate,
            errors.ReferenceErrorPrototype,
            errors.ReferenceErrorType,
            "ReferenceError"
        );
        EmitOneNativeErrorPopulate(
            errors,
            descriptorStorage,
            errors.SyntaxErrorPrototypePopulate,
            errors.SyntaxErrorPrototype,
            errors.SyntaxErrorType,
            "SyntaxError"
        );
        EmitOneNativeErrorPopulate(
            errors,
            descriptorStorage,
            errors.URIErrorPrototypePopulate,
            errors.URIErrorPrototype,
            errors.URIErrorType,
            "URIError"
        );
        EmitOneNativeErrorPopulate(
            errors,
            descriptorStorage,
            errors.EvalErrorPrototypePopulate,
            errors.EvalErrorPrototype,
            errors.EvalErrorType,
            "EvalError"
        );
        EmitOneNativeErrorPopulate(
            errors,
            descriptorStorage,
            errors.AggregateErrorPrototypePopulate,
            errors.AggregateErrorPrototype,
            errors.AggregateErrorType,
            "AggregateError"
        );
        errors.MarkNativePrototypeBodiesEmitted();
    }

    private void EmitOneNativeErrorPopulate(
        EmittedErrorRuntime errors,
        EmittedDescriptorStorageRuntime descriptorStorage,
        MethodBuilder method,
        FieldBuilder protoField,
        Type ctorType,
        string errorName
    )
    {
        var descriptors = new PrototypeDescriptorInputs(
            descriptorStorage.DescriptorConstructor, descriptorStorage.DescriptorValue.GetSetMethod()!,
            descriptorStorage.DescriptorEnumerable.GetSetMethod()!, descriptorStorage.DefineProperty);
        var il = ((MethodBuilder)method).GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, protoField);

        // constructor = typeof(<ctorType>)
        il.Emit(OpCodes.Ldsfld, protoField);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Ldtoken, ctorType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Callvirt, setItem);

        // name = "<errorName>" and message = "".
        il.Emit(OpCodes.Ldsfld, protoField);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Ldstr, errorName);
        il.Emit(OpCodes.Callvirt, setItem);
        il.Emit(OpCodes.Ldsfld, protoField);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Callvirt, setItem);

        // Install non-enumerable PDS descriptors for constructor/name/message
        // (ECMA-262 §17 — built-in data properties are W:T, E:F, C:T).
        var descLocal = il.DeclareLocal(descriptorStorage.DescriptorType);
        void InstallNonEnum(string jsName, System.Action emitValue)
            => EmitInstallNonEnumerableDescriptor(il, descriptors, protoField, descLocal, jsName, emitValue);
        InstallNonEnum("constructor", () =>
        {
            il.Emit(OpCodes.Ldtoken, ctorType);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });
        InstallNonEnum("name", () => il.Emit(OpCodes.Ldstr, errorName));
        InstallNonEnum("message", () => il.Emit(OpCodes.Ldstr, ""));

        // Per ECMA-262 §20.5.6.4 the subclass prototype's [[Prototype]] is
        // %Error.prototype% (not %Object.prototype%). Eagerly ensure Error
        // proto is populated so subsequent walks see its slots.
        il.Emit(OpCodes.Call, errors.PrototypePopulate);
        il.Emit(OpCodes.Ldsfld, protoField);
        il.Emit(OpCodes.Ldsfld, errors.Prototype);
        il.Emit(OpCodes.Call, descriptorStorage.SetPrototype);

        il.Emit(OpCodes.Ret);
    }
}
