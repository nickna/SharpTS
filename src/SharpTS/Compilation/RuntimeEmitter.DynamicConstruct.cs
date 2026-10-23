using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct ConstructDynamicValueInputs(
        EmittedClassDefinitionRuntime ClassDefinitions,
        EmittedDynamicConstructionRuntime DynamicConstruction,
        EmittedArrayStorageRuntime ArrayStorage,
        EmittedDescriptorStorageRuntime DescriptorStorage,
        EmittedObjectReadRuntime ObjectRead,
        EmittedOperatorRuntime Operators,
        EmittedBoxedPrimitiveRuntime BoxedPrimitives,
        EmittedBooleanRuntime Booleans,
        EmittedErrorRuntime Errors,
        EmittedFunctionConstructionRuntime FunctionConstruction,
        EmittedFunctionValueRuntime FunctionValues,
        EmittedNumericCoercionRuntime NumericCoercion,
        EmittedPromiseRuntime? Promise,
        EmittedRegExpRuntime RegExps,
        EmittedStringCoercionRuntime StringCoercion,
        MethodInfo ArrayConstructor,
        FieldInfo UndefinedInstance,
        Type UndefinedType
    );

    private void EmitGuestConstructorFactoryAttribute(ModuleBuilder module, EmittedDynamicConstructionRuntime construction)
    {
        var type = module.DefineType("$GuestConstructorFactory", TypeAttributes.Public | TypeAttributes.Sealed, typeof(Attribute));
        var name = type.DefineField("Name", _types.String, FieldAttributes.Public | FieldAttributes.InitOnly);
        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [_types.String]);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Attribute).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, name);
        il.Emit(OpCodes.Ret);
        construction.FactoryAttributeType = type;
        construction.FactoryAttributeConstructor = constructor;
        construction.FactoryAttributeName = name;
        type.CreateType();
    }

    /// <summary>
    /// Emits ConstructDynamicValue: (object ctor, object[] args) → object.
    ///
    /// The runtime construct path for `new x(...)` when the callee is a VALUE
    /// rather than a compile-time-resolvable class name — e.g. an aliased
    /// built-in (`const C = ReadableStream; new C(...)`), a member of a
    /// namespace singleton (`const I = Intl; new I.NumberFormat(...)`), or any
    /// expression callee (`new (getCtor())()`). Mirrors ReflectConstruct's
    /// target dispatch (#224):
    ///   - marked guest System.Type → source-aware constructor factory
    ///   - native/CLR System.Type → Activator.CreateInstance(type, args)
    ///   - plain object / null / undefined → TypeError "not a constructor"
    ///   - everything else ($TSFunction etc.) → NewOnFunction, the shared JS
    ///     `new` protocol (fresh `this`, return-object-wins, IsConstructor
    ///     policy) — same routing ILEmitter's fallback construction uses.
    /// Must be emitted AFTER EmitNewOnFunction so runtime.DynamicConstruction.Function is set.
    /// </summary>
    private void EmitConstructDynamicValue(
        TypeBuilder typeBuilder,
        EmittedDynamicConstructionRuntime dynamicConstruction,
        ConstructDynamicValueInputs inputs
    )
    {
        var method = dynamicConstruction.ConstructWithNewTarget;

        var il = method.GetILGenerator();
        var throwLabel = il.DefineLabel();

        var notClassDefinition = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, inputs.ClassDefinitions.Type);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notClassDefinition);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, inputs.ClassDefinitions.Construct);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notClassDefinition);
        il.Emit(OpCodes.Pop);

        // null / $Undefined → TypeError
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, throwLabel);

        // Array values use an IList<object> Type token, whose CLR construction
        // is unrelated to JavaScript's numeric-length and element-list forms.
        var notArrayTypeLabel = il.DefineLabel();
        var constructArray = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.IListOfObject);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Beq, constructArray);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, inputs.ArrayStorage.Type);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Bne_Un, notArrayTypeLabel);
        il.MarkLabel(constructArray);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, inputs.ArrayConstructor);
        var array = il.DeclareLocal(_types.Object);
        var prototype = il.DeclareLocal(_types.Object);
        var prototypeKind = il.DeclareLocal(_types.String);
        var useArrayPrototype = il.DefineLabel();
        var installPrototype = il.DefineLabel();
        il.Emit(OpCodes.Stloc, array);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldstr, "prototype");
        il.Emit(OpCodes.Call, inputs.ObjectRead.Property);
        il.Emit(OpCodes.Stloc, prototype);
        il.Emit(OpCodes.Ldloc, prototype);
        il.Emit(OpCodes.Brfalse, useArrayPrototype);
        il.Emit(OpCodes.Ldloc, prototype);
        il.Emit(OpCodes.Call, inputs.Operators.TypeOf);
        il.Emit(OpCodes.Stloc, prototypeKind);
        foreach (var kind in new[] { "object", "function" })
        {
            il.Emit(OpCodes.Ldloc, prototypeKind);
            il.Emit(OpCodes.Ldstr, kind);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brtrue, installPrototype);
        }
        il.Emit(OpCodes.Br, useArrayPrototype);
        il.MarkLabel(installPrototype);
        il.Emit(OpCodes.Ldloc, array);
        il.Emit(OpCodes.Ldloc, prototype);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.SetPrototype);
        il.MarkLabel(useArrayPrototype);
        il.Emit(OpCodes.Ldloc, array);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notArrayTypeLabel);

        // Promise constructor values carry Task<object>, whose CLR callback
        // constructors do not implement the guest executor protocol.
        if (inputs.Promise is { } promise)
        {
            var notPromiseType = il.DefineLabel();
            var executorPresent = il.DefineLabel();
            var executorReady = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Bne_Un, notPromiseType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Brtrue, executorPresent);
            il.Emit(OpCodes.Ldsfld, inputs.UndefinedInstance);
            il.Emit(OpCodes.Br, executorReady);
            il.MarkLabel(executorPresent);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.MarkLabel(executorReady);
            il.Emit(OpCodes.Call, promise.FromExecutor);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notPromiseType);
        }

        // An aliased RegExp constructor is represented by the emitted $RegExp
        // Type. Activator cannot supply its JavaScript optional arguments (and
        // has no parameterless overload), so route it through RegExpFromArgs.
        if (inputs.RegExps.Implementation is not null)
        {
            var notRegExpTypeLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, inputs.RegExps.RequireImplementation().Type);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Bne_Un, notRegExpTypeLabel);
            var regexpArgsPresent = il.DefineLabel();
            var regexpFlagsPresent = il.DefineLabel();
            var regexpPatternLocal = il.DeclareLocal(_types.Object);
            var regexpFlagsLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldsfld, inputs.UndefinedInstance);
            il.Emit(OpCodes.Stloc, regexpPatternLocal);
            il.Emit(OpCodes.Ldsfld, inputs.UndefinedInstance);
            il.Emit(OpCodes.Stloc, regexpFlagsLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Brtrue, regexpArgsPresent);
            il.Emit(OpCodes.Br, regexpFlagsPresent);
            il.MarkLabel(regexpArgsPresent);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Stloc, regexpPatternLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ble, regexpFlagsPresent);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Stloc, regexpFlagsLocal);
            il.MarkLabel(regexpFlagsPresent);
            il.Emit(OpCodes.Ldloc, regexpPatternLocal);
            il.Emit(OpCodes.Ldloc, regexpFlagsLocal);
            il.Emit(OpCodes.Call, inputs.RegExps.RequireImplementation().FromArguments);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notRegExpTypeLabel);
        }

        // An aliased String constructor is represented by System.String, but
        // its CLR constructors are implementation details (notably
        // String(char[])). JavaScript `new String(value)` instead creates a
        // boxed String exotic object after applying ToString to its first
        // argument. Route the Type token through the same boxed-primitive
        // helper used by the statically recognized constructor path.
        var notStringTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.String);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Bne_Un, notStringTypeLabel);
        il.Emit(OpCodes.Ldstr, "String");
        var stringArgPresentLabel = il.DefineLabel();
        var stringValueReadyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, stringArgPresentLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, stringValueReadyLabel);
        il.MarkLabel(stringArgPresentLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, inputs.StringCoercion.ToJsString);
        il.MarkLabel(stringValueReadyLabel);
        il.Emit(OpCodes.Call, inputs.BoxedPrimitives.New);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notStringTypeLabel);

        // Number values use the CLR Double Type token, but construction creates
        // a guest Number wrapper. Omission produces zero; present undefined
        // goes through explicit Number conversion and produces NaN. This
        // conversion also accepts BigInt, unlike abstract ToNumber.
        var notNumberTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.Double);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Bne_Un, notNumberTypeLabel);
        il.Emit(OpCodes.Ldstr, "Number");
        var numberArgPresentLabel = il.DefineLabel();
        var numberValueReadyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, numberArgPresentLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Br, numberValueReadyLabel);
        il.MarkLabel(numberArgPresentLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, inputs.NumericCoercion.ConvertToNumber);
        il.MarkLabel(numberValueReadyLabel);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, inputs.BoxedPrimitives.New);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNumberTypeLabel);

        // Boolean construction uses truthiness without invoking primitive
        // conversion hooks, and omission initializes the wrapper to false.
        var notBooleanTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.Boolean);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Bne_Un, notBooleanTypeLabel);
        il.Emit(OpCodes.Ldstr, "Boolean");
        var booleanArgPresentLabel = il.DefineLabel();
        var booleanValueReadyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, booleanArgPresentLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, booleanValueReadyLabel);
        il.MarkLabel(booleanArgPresentLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, inputs.Booleans.IsTruthy);
        il.MarkLabel(booleanValueReadyLabel);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Call, inputs.BoxedPrimitives.New);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBooleanTypeLabel);

        var notFunctionType = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, inputs.FunctionValues.Type);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Bne_Un, notFunctionType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, inputs.FunctionConstruction.Construct);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notFunctionType);

        // System.Type → marked guest factory or native/CLR overload binding.
        var notTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brfalse, notTypeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.Type);
        var constructorType = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Stloc, constructorType);

        // Only emitted guest classes carry this factory. Its source-aware adapter
        // supplies defaults/rest slots without changing CLR/native overload binding.
        var useClrConstructor = il.DefineLabel();
        var attributes = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldloc, constructorType);
        il.Emit(OpCodes.Ldtoken, inputs.DynamicConstruction.FactoryAttributeType);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(typeof(System.Reflection.MemberInfo), "GetCustomAttributes", _types.Type, _types.Boolean));
        il.Emit(OpCodes.Stloc, attributes);
        il.Emit(OpCodes.Ldloc, attributes);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Brfalse, useClrConstructor);
        il.Emit(OpCodes.Ldtoken, typeof(Func<object[], object, object>));
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ldloc, constructorType);
        il.Emit(OpCodes.Ldloc, attributes);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, inputs.DynamicConstruction.FactoryAttributeType);
        il.Emit(OpCodes.Ldfld, inputs.DynamicConstruction.FactoryAttributeName);
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags)));
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(Delegate), "CreateDelegate", _types.Type, typeof(MethodInfo)));
        il.Emit(OpCodes.Castclass, typeof(Func<object[], object, object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(typeof(Func<object[], object, object>), "Invoke", _types.ObjectArray, _types.Object));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(useClrConstructor);
        il.Emit(OpCodes.Ldloc, constructorType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(Activator), "CreateInstance", _types.Type, _types.ObjectArray));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTypeLabel);

        // Plain objects have no [[Construct]] → TypeError. (Namespace
        // singletons like AbortSignal/Math land here: `new AbortSignal()`
        // must throw per WebIDL "Illegal constructor".)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, throwLabel);

        // Callable values ($TSFunction and wrappers) → NewOnFunction(ctor, args)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, dynamicConstruction.FunctionWithNewTarget);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        GuestErrorEmitter.ThrowError(il, inputs.Errors.CreateException, inputs.Errors.TypeErrorConstructor, "Value is not a constructor");
    }
}
