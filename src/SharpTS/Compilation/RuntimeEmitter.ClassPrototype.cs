using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the cache for user-class prototype objects. Prototype allocation is
    /// performed by class initialization, using bookkeeping constructors for
    /// concrete owners and ordinary objects for abstract owners. This helper
    /// registers stable identities and wires prototype inheritance.
    /// </summary>
    private void EmitClassPrototypeSupport(
        TypeBuilder typeBuilder,
        EmittedClassPrototypeRuntime classPrototypes,
        EmittedObjectPrototypeRuntime objectPrototypes,
        EmittedDescriptorStorageRuntime descriptorStorage,
        EmittedErrorRuntime errors,
        Type userClassInterface,
        FieldBuilder cacheField,
        FieldBuilder parentsField,
        Type arrayType,
        FieldBuilder arrayPrototype,
        EmittedPromiseRuntime? promise,
        NativeSourceOwnerCatalog? sourceOwners = null
    )
    {
        var cacheType = cacheField.FieldType;
        var tryGetValue = _types.GetMethod(
            cacheType, "TryGetValue", [_types.Type, _types.Object.MakeByRefType()])!;
        var setItem = _types.GetMethod(
            cacheType, "set_Item", [_types.Type, _types.Object])!;

        void EmitNominalParent(ILGenerator il, LocalBuilder parentType, LocalBuilder parent)
        {
            var nativeParent = il.DefineLabel();
            var objectParent = il.DefineLabel();
            var ready = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, parentType);
            il.Emit(OpCodes.Brfalse, objectParent);
            // Explicit registrations are authoritative even for native carrier
            // types that do not implement the generated user-class interface.
            il.Emit(OpCodes.Ldsfld, cacheField);
            il.Emit(OpCodes.Ldloc, parentType);
            il.Emit(OpCodes.Ldloca, parent);
            il.Emit(OpCodes.Callvirt, tryGetValue);
            il.Emit(OpCodes.Brtrue, ready);
            il.Emit(OpCodes.Ldtoken, userClassInterface);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Ldloc, parentType);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "IsAssignableFrom", _types.Type));
            il.Emit(OpCodes.Brfalse, nativeParent);
            il.Emit(OpCodes.Ldloc, parentType);
            il.Emit(OpCodes.Call, classPrototypes.Get);
            il.Emit(OpCodes.Stloc, parent);
            il.Emit(OpCodes.Br, ready);
            il.MarkLabel(nativeParent);
            il.Emit(OpCodes.Ldloc, parentType);
            il.Emit(OpCodes.Ldtoken, arrayType);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            var nativeErrorParent = il.DefineLabel();
            il.Emit(OpCodes.Bne_Un, nativeErrorParent);
            il.Emit(OpCodes.Ldsfld, arrayPrototype);
            il.Emit(OpCodes.Stloc, parent);
            il.Emit(OpCodes.Br, ready);
            il.MarkLabel(nativeErrorParent);
            if (promise != null)
            {
                var otherNativeParent = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, parentType);
                il.Emit(OpCodes.Ldtoken, promise.Type);
                il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
                il.Emit(OpCodes.Bne_Un, otherNativeParent);
                il.Emit(OpCodes.Ldsfld, promise.PrototypeField);
                il.Emit(OpCodes.Stloc, parent);
                il.Emit(OpCodes.Br, ready);
                il.MarkLabel(otherNativeParent);
            }
            foreach (var (errorType, prototype) in new[]
            {
                (errors.Type, errors.Prototype),
                (errors.TypeErrorType, errors.TypeErrorPrototype),
                (errors.RangeErrorType, errors.RangeErrorPrototype),
                (errors.ReferenceErrorType, errors.ReferenceErrorPrototype),
                (errors.SyntaxErrorType, errors.SyntaxErrorPrototype),
                (errors.URIErrorType, errors.URIErrorPrototype),
                (errors.EvalErrorType, errors.EvalErrorPrototype),
                (errors.AggregateErrorType, errors.AggregateErrorPrototype)
            })
            {
                var nextError = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, parentType);
                il.Emit(OpCodes.Ldtoken, errorType);
                il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
                il.Emit(OpCodes.Bne_Un, nextError);
                il.Emit(OpCodes.Ldsfld, prototype);
                il.Emit(OpCodes.Stloc, parent);
                il.Emit(OpCodes.Br, ready);
                il.MarkLabel(nextError);
            }
            il.MarkLabel(objectParent);
            il.Emit(OpCodes.Ldsfld, objectPrototypes.Prototype);
            il.Emit(OpCodes.Stloc, parent);
            il.MarkLabel(ready);
        }

        var getMethod = typeBuilder.DefineMethod(
            "GetClassPrototype",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Type]);
        classPrototypes.Get = getMethod;

        var registerMethod = typeBuilder.DefineMethod(
            "RegisterClassPrototype",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Type, _types.Object, _types.Type]);
        classPrototypes.Register = registerMethod;

        var hasModifiedChain = typeBuilder.DefineMethod(
            "HasModifiedClassPrototypeChain",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Type]);
        classPrototypes.HasModifiedChain = hasModifiedChain;
        var isPrototype = typeBuilder.DefineMethod(
            "IsClassPrototype", MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean, [_types.Object]);
        classPrototypes.IsPrototype = isPrototype;
        var normalizePrototype = typeBuilder.DefineMethod(
            "NormalizeClassPrototype", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object]);
        classPrototypes.NormalizePrototype = normalizePrototype;
        var updateParent = typeBuilder.DefineMethod(
            "UpdateClassPrototypeParent", MethodAttributes.Public | MethodAttributes.Static,
            _types.Void, [_types.Object, _types.Object]);
        classPrototypes.UpdateParent = updateParent;

        // GetClassPrototype(Type): registered user classes return their stable
        // object. A cache miss forces the compiler-generated type initializer,
        // which registers the prototype without reflection or running a user
        // constructor. Other $IHasFields implementers (notably $Object) use the
        // normal Object.prototype fallback.
        {
            var il = getMethod.GetILGenerator();
            var prototypeLocal = il.DeclareLocal(_types.Object);
            var useObjectPrototype = il.DefineLabel();

            // A bare generic constructor is represented by its open Type token.
            // Its JavaScript prototype belongs to the existing erased instantiation,
            // while closed CLR instance types retain their own registered identities.
            var constructorOwner = il.DeclareLocal(_types.Type);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Stloc, constructorOwner);
            var prototypeOwner = EmitStaticMemberLookupOwner(il, constructorOwner, userClassInterface, sourceOwners);
            il.Emit(OpCodes.Ldloc, prototypeOwner);
            il.Emit(OpCodes.Starg_S, (byte)0);

            il.Emit(OpCodes.Ldsfld, cacheField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, prototypeLocal);
            il.Emit(OpCodes.Callvirt, tryGetValue);
            var initializeType = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, initializeType);
            il.Emit(OpCodes.Ldloc, prototypeLocal);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(initializeType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt,
                typeof(Type).GetProperty(nameof(Type.TypeHandle))!.GetGetMethod()!);
            il.Emit(OpCodes.Call, _types.RuntimeHelpersRunClassConstructor);

            // The generated initializer registers user-class prototypes. If the
            // type was a runtime implementation rather than a user class, the
            // second lookup still misses and falls back to Object.prototype.
            il.Emit(OpCodes.Ldsfld, cacheField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, prototypeLocal);
            il.Emit(OpCodes.Callvirt, tryGetValue);
            il.Emit(OpCodes.Brfalse, useObjectPrototype);
            il.Emit(OpCodes.Ldloc, prototypeLocal);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(useObjectPrototype);
            il.Emit(OpCodes.Ldsfld, objectPrototypes.Prototype);
            il.Emit(OpCodes.Ret);
        }

        // RegisterClassPrototype(Type, object, Type?): publish first, then resolve
        // the base. Publishing first keeps recursive prototype resolution stable.
        {
            var il = registerMethod.GetILGenerator();
            var baseTypeLocal = il.DeclareLocal(_types.Type);
            var basePrototypeLocal = il.DeclareLocal(_types.Object);

            il.Emit(OpCodes.Ldsfld, cacheField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, setItem);

            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stloc, baseTypeLocal);
            EmitNominalParent(il, baseTypeLocal, basePrototypeLocal);

            var parentSelected = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "IsGenericType")!.GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, parentSelected);
            il.Emit(OpCodes.Ldsfld, parentsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "GetGenericTypeDefinition"));
            var sharedParent = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldloca, sharedParent);
            il.Emit(OpCodes.Callvirt, tryGetValue);
            il.Emit(OpCodes.Brfalse, parentSelected);
            il.Emit(OpCodes.Ldloc, sharedParent);
            il.Emit(OpCodes.Stloc, basePrototypeLocal);
            il.MarkLabel(parentSelected);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, basePrototypeLocal);
            il.Emit(OpCodes.Call, descriptorStorage.SetPrototype);
            il.Emit(OpCodes.Ret);
        }

        // With no guest classes there are no registered class prototype
        // carriers to normalize, mutate or inspect. Keep the metadata handles
        // available to shared helpers without retaining their class-only work.
        // Hosted runtimes can receive guest class values from their host.
        if (!_emitHosted && !_features.UsesUserClasses)
        {
            var isPrototypeIL = isPrototype.GetILGenerator();
            isPrototypeIL.Emit(OpCodes.Ldc_I4_0);
            isPrototypeIL.Emit(OpCodes.Ret);
            var normalizeIL = normalizePrototype.GetILGenerator();
            normalizeIL.Emit(OpCodes.Ldarg_0);
            normalizeIL.Emit(OpCodes.Ret);
            updateParent.GetILGenerator().Emit(OpCodes.Ret);
            var modifiedIL = hasModifiedChain.GetILGenerator();
            modifiedIL.Emit(OpCodes.Ldc_I4_0);
            modifiedIL.Emit(OpCodes.Ret);
            return;
        }

        // Most carriers have their owner's CLR type. Abstract owners use an
        // ordinary object instead; recover their owner only by registered value
        // identity, without running class initialization or guest code here.
        var findOwner = typeBuilder.DefineMethod(
            "FindRegisteredClassPrototypeOwner", MethodAttributes.Private | MethodAttributes.Static,
            _types.Type, [_types.Object]);
        {
            var il = findOwner.GetILGenerator();
            var type = il.DeclareLocal(_types.Type);
            var cached = il.DeclareLocal(_types.Object);
            var search = il.DefineLabel();
            var missing = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brfalse, missing);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
            il.Emit(OpCodes.Stloc, type);
            il.Emit(OpCodes.Ldsfld, cacheField); il.Emit(OpCodes.Ldloc, type); il.Emit(OpCodes.Ldloca, cached);
            il.Emit(OpCodes.Callvirt, tryGetValue); il.Emit(OpCodes.Brfalse, search);
            il.Emit(OpCodes.Ldloc, cached); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Bne_Un, search);
            il.Emit(OpCodes.Ldloc, type); il.Emit(OpCodes.Ret);

            il.MarkLabel(search);
            var enumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.Enumerator), _types.Type, _types.Object);
            var entryType = _types.MakeGenericType(_types.KeyValuePairOpen, _types.Type, _types.Object);
            var enumerator = il.DeclareLocal(enumeratorType);
            var entry = il.DeclareLocal(entryType);
            var check = il.DefineLabel();
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, cacheField);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(cacheType, "GetEnumerator"));
            il.Emit(OpCodes.Stloc, enumerator); il.Emit(OpCodes.Br, check);
            il.MarkLabel(next);
            il.Emit(OpCodes.Ldloca, enumerator);
            il.Emit(OpCodes.Call, _types.GetProperty(enumeratorType, "Current")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, entry);
            il.Emit(OpCodes.Ldloca, entry);
            il.Emit(OpCodes.Call, _types.GetProperty(entryType, "Value")!.GetGetMethod()!);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Bne_Un, check);
            il.Emit(OpCodes.Ldloca, entry);
            il.Emit(OpCodes.Call, _types.GetProperty(entryType, "Key")!.GetGetMethod()!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(check);
            il.Emit(OpCodes.Ldloca, enumerator);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(enumeratorType, "MoveNext"));
            il.Emit(OpCodes.Brtrue, next);
            il.MarkLabel(missing); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
        }

        // The ordinary instance path needs only one identity lookup. Abstract
        // prototypes use an ordinary object carrier and need the owner search.
        {
            var il = isPrototype.GetILGenerator();
            var cached = il.DeclareLocal(_types.Object);
            var search = il.DefineLabel();
            var missing = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brfalse, missing);
            il.Emit(OpCodes.Ldsfld, cacheField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
            il.Emit(OpCodes.Ldloca, cached);
            il.Emit(OpCodes.Callvirt, tryGetValue); il.Emit(OpCodes.Brfalse, search);
            il.Emit(OpCodes.Ldloc, cached); il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ret);
            il.MarkLabel(search);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, findOwner);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Cgt_Un); il.Emit(OpCodes.Ret);
            il.MarkLabel(missing); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        }

        // Generic prototype carriers retain their CLR storage identities, but
        // cycle checks must recognize the one source-level prototype home.
        {
            var il = normalizePrototype.GetILGenerator();
            var type = il.DeclareLocal(_types.Type);
            var unchanged = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, findOwner);
            il.Emit(OpCodes.Stloc, type);
            il.Emit(OpCodes.Ldloc, type); il.Emit(OpCodes.Brfalse, unchanged);
            il.Emit(OpCodes.Ldloc, type);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "IsGenericType")!.GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, unchanged);
            il.Emit(OpCodes.Ldloc, type);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "GetGenericTypeDefinition"));
            il.Emit(OpCodes.Call, getMethod); il.Emit(OpCodes.Ret);
            il.MarkLabel(unchanged); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
        }

        // Parent mutation is source-owned across existing and future closed
        // storage carriers. No guest code runs while updating the cache values.
        {
            var il = updateParent.GetILGenerator();
            var type = il.DeclareLocal(_types.Type);
            var definition = il.DeclareLocal(_types.Type);
            var parentType = il.DeclareLocal(_types.Type);
            var parent = il.DeclareLocal(_types.Object);
            var restoreNominal = il.DeclareLocal(_types.Boolean);
            var done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, findOwner);
            il.Emit(OpCodes.Stloc, type);
            il.Emit(OpCodes.Ldloc, type); il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldloc, type);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "IsGenericType")!.GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldloc, type);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "GetGenericTypeDefinition"));
            il.Emit(OpCodes.Stloc, definition);
            // Resolve the canonical family before enumeration: doing so may
            // initialize its erased carrier and register its nominal bases.
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, normalizePrototype); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldloc, type);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "BaseType")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, parentType);
            EmitNominalParent(il, parentType, parent);
            il.Emit(OpCodes.Ldloc, parent); il.Emit(OpCodes.Call, normalizePrototype);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, normalizePrototype);
            il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Stloc, restoreNominal);
            var storeOverride = il.DefineLabel();
            var enumerate = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, restoreNominal); il.Emit(OpCodes.Brfalse, storeOverride);
            il.Emit(OpCodes.Ldsfld, parentsField); il.Emit(OpCodes.Ldloc, definition);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(cacheType, "Remove", [_types.Type])!);
            il.Emit(OpCodes.Pop); il.Emit(OpCodes.Br, enumerate);
            il.MarkLabel(storeOverride);
            il.Emit(OpCodes.Ldsfld, parentsField); il.Emit(OpCodes.Ldloc, definition); il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, setItem);

            il.MarkLabel(enumerate);
            var enumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.Enumerator), _types.Type, _types.Object);
            var entryType = _types.MakeGenericType(_types.KeyValuePairOpen, _types.Type, _types.Object);
            var enumerator = il.DeclareLocal(enumeratorType);
            var entry = il.DeclareLocal(entryType);
            var check = il.DefineLabel(); var next = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, cacheField);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(cacheType, "GetEnumerator"));
            il.Emit(OpCodes.Stloc, enumerator); il.Emit(OpCodes.Br, check);
            il.MarkLabel(next);
            il.Emit(OpCodes.Ldloca, enumerator);
            il.Emit(OpCodes.Call, _types.GetProperty(enumeratorType, "Current")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, entry);
            il.Emit(OpCodes.Ldloca, entry);
            il.Emit(OpCodes.Call, _types.GetProperty(entryType, "Key")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, type);
            il.Emit(OpCodes.Ldloc, type);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "IsGenericType")!.GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, check);
            il.Emit(OpCodes.Ldloc, type);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "GetGenericTypeDefinition"));
            il.Emit(OpCodes.Ldloc, definition); il.Emit(OpCodes.Bne_Un, check);
            var explicitParent = il.DefineLabel();
            var parentReady = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, restoreNominal); il.Emit(OpCodes.Brfalse, explicitParent);
            // All registered carriers resolved their nominal bases at creation,
            // so these lookups cannot add cache entries during enumeration.
            il.Emit(OpCodes.Ldloc, type);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "BaseType")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, parentType);
            EmitNominalParent(il, parentType, parent);
            il.Emit(OpCodes.Br, parentReady);
            il.MarkLabel(explicitParent);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stloc, parent);
            il.MarkLabel(parentReady);
            il.Emit(OpCodes.Ldloca, entry);
            il.Emit(OpCodes.Call, _types.GetProperty(entryType, "Value")!.GetGetMethod()!);
            il.Emit(OpCodes.Ldloc, parent); il.Emit(OpCodes.Call, descriptorStorage.SetPrototype);
            il.MarkLabel(check);
            il.Emit(OpCodes.Ldloca, enumerator);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(enumeratorType, "MoveNext"));
            il.Emit(OpCodes.Brtrue, next);
            il.MarkLabel(done); il.Emit(OpCodes.Ret);
        }

        // Compare registered nominal links, rather than the instance's CLR
        // ancestry, so a mutation to any lexical home is visible to descendants.
        // Reading only compiler-created prototype objects avoids guest traps.
        {
            var il = hasModifiedChain.GetILGenerator();
            var prototype = il.DeclareLocal(_types.Object);
            var cached = il.DeclareLocal(_types.Object);
            var owner = il.DeclareLocal(_types.Type);
            var parentType = il.DeclareLocal(_types.Type);
            var expectedParent = il.DeclareLocal(_types.Object);
            var loop = il.DefineLabel();
            var unchanged = il.DefineLabel();
            var modified = il.DefineLabel();
            il.MarkLabel(loop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brfalse, unchanged);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Stloc, owner);
            var lookupOwner = EmitStaticMemberLookupOwner(il, owner, userClassInterface, sourceOwners);
            il.Emit(OpCodes.Ldloc, lookupOwner); il.Emit(OpCodes.Starg_S, (byte)0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getMethod);
            il.Emit(OpCodes.Stloc, prototype);
            // Detached abstract carriers and nominal concrete carriers both
            // belong to the exact registered owner. Implementation fallbacks
            // have no cache entry and therefore no class link to compare.
            il.Emit(OpCodes.Ldsfld, cacheField); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloca, cached);
            il.Emit(OpCodes.Callvirt, tryGetValue); il.Emit(OpCodes.Brfalse, unchanged);
            il.Emit(OpCodes.Ldloc, cached); il.Emit(OpCodes.Ldloc, prototype);
            il.Emit(OpCodes.Bne_Un, unchanged);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "BaseType")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, parentType);
            EmitNominalParent(il, parentType, expectedParent);
            il.Emit(OpCodes.Ldloc, prototype);
            il.Emit(OpCodes.Call, descriptorStorage.GetPrototype);
            il.Emit(OpCodes.Ldloc, expectedParent);
            il.Emit(OpCodes.Bne_Un, modified);
            il.Emit(OpCodes.Ldloc, parentType);
            il.Emit(OpCodes.Starg_S, (byte)0);
            il.Emit(OpCodes.Br, loop);
            il.MarkLabel(unchanged);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(modified);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }
    }
}
