using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits <c>$Runtime.NewOnFunction(object fn, object[] args) → object</c>, the
    /// JS <c>new</c> protocol for runtime-valued function callees (<c>$TSFunction</c>,
    /// <c>$BoundTSFunction</c>). Builds a fresh <c>$Object</c> to serve as the
    /// constructor's <c>this</c>, invokes the function via <c>InvokeWithThis</c>
    /// (and also via a thread-local <c>this</c> slot so bodies without <c>__this</c>
    /// params resolve `this` to the new object), and returns the explicit object
    /// return value if the body yielded one — otherwise the constructed <c>this</c>.
    /// </summary>
    /// <remarks>
    /// Non-callable values return null, mirroring the pre-existing <c>Ldnull</c>
    /// behavior rather than throwing — legacy code paths for <c>new &lt;not-a-function&gt;</c>
    /// shouldn't regress. Must be emitted after <c>$Object</c>, <c>$TSFunction</c>,
    /// and <c>$BoundTSFunction</c> are defined.
    /// </remarks>
    internal void EmitNewOnFunction(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // The thread-static `_currentFunctionThis` field is now defined on $TSFunction
        // (during EmitTSFunctionClass) so that $TSFunction.InvokeWithThis can also
        // set/restore it for `Fn.call(target, ...)` paths. NewOnFunction just consumes
        // runtime.CurrentFunctionThisField below.
        var currentThisField = runtime.CurrentFunctionThisField;

        var method = runtime.NewOnFunction;

        var il = method.GetILGenerator();

        var newObjLocal = il.DeclareLocal(runtime.TSObjectType);
        var resultLocal = il.DeclareLocal(_types.Object);
        var prevThisLocal = il.DeclareLocal(_types.Object);
        var notCallableLocal = il.DeclareLocal(_types.Boolean);

        var proxyLabel = il.DefineLabel();
        var notProxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(
            il, () => il.Emit(OpCodes.Ldarg_0), proxyLabel, notProxyLabel);
        il.MarkLabel(proxyLabel);
        EmitProxyMethodCallUnwrapped(
            il, runtime, () => il.Emit(OpCodes.Ldarg_0),
            "TrapConstructCompiled", () =>
            {
                il.Emit(OpCodes.Ldc_I4_4);
                il.Emit(OpCodes.Newarr, _types.Object);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stelem_Ref);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Stelem_Ref);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_2);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldftn, runtime.NewOnFunction);
                il.Emit(OpCodes.Newobj, _types.GetConstructor(
                    typeof(Func<object, object?[], object?>),
                    _types.Object, _types.IntPtr)!);
                il.Emit(OpCodes.Stelem_Ref);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_3);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldftn, runtime.GetProperty);
                il.Emit(OpCodes.Newobj, _types.GetConstructor(
                    typeof(Func<object, string, object?>),
                    _types.Object, _types.IntPtr)!);
                il.Emit(OpCodes.Stelem_Ref);
            });
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notProxyLabel);

        // ECMA-262 §7.3.14 Construct: throw TypeError if callee is a $TSFunction
        // wrapping a built-in helper (declaring type == $Runtime). Catches
        // Test262 patterns like `new Array.prototype.sort()` where the user
        // expects TypeError on a non-constructor. Routed through IsConstructor
        // so the policy stays in one place. We only throw for the explicit
        // "$TSFunction wrapping $Runtime" subset — class refs (Type) and user
        // function decls fall through to the existing construct path.
        var isConstructorOkLabel = il.DefineLabel();
        var skipConstructorCheckLabel = il.DefineLabel();
        // Only run the check for $TSFunction inputs — Type and other callees
        // were already constructable in the legacy code path.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, skipConstructorCheckLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.IsConstructorMethod);
        il.Emit(OpCodes.Brtrue, isConstructorOkLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "not a constructor");
        il.MarkLabel(skipConstructorCheckLabel);
        il.MarkLabel(isConstructorOkLabel);

        // `new <plain object>` — plain objects have no [[Construct]] (#224):
        // throw TypeError instead of the legacy silent null. Namespace
        // singletons (AbortSignal, Intl, Math) land here when constructed
        // directly, matching WebIDL "Illegal constructor" behavior.
        var notPlainObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notPlainObjectLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "not a constructor");
        il.MarkLabel(notPlainObjectLabel);

        // newObj = new $Object(new Dictionary<string, object>())
        var dictCtor = _types.GetDefaultConstructor(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, dictCtor);
        il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);
        il.Emit(OpCodes.Stloc, newObjLocal);

        // Per JS spec, `new F()` links the freshly-created object to F.prototype:
        //   newObj.[[Prototype]] = F.prototype
        // Store via PDSSetPrototype so a subsequent `instance instanceof F`
        // walks the prototype chain via PDSGetPrototype and finds F.prototype.
        // Without this, yallist/semver's `if (!(this instanceof F)) return new F(args)`
        // recurses infinitely. Stage 0b makes F.prototype real (lazy-creates an
        // empty $Object on first read); this stage links newObj to it. Only
        // applies to $TSFunction callees ($BoundTSFunction / non-callable callees
        // skip the prototype link).
        var skipProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, skipProtoLabel);

        // Read F.prototype via $Runtime.GetFunctionMethod(fn, "prototype") so
        // the lazy-init + cache from Stage 0b is reused (no fresh $Object per
        // construct, identity stable across invocations).
        var fnProtoLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "prototype");
        il.Emit(OpCodes.Call, runtime.GetFunctionMethod);
        il.Emit(OpCodes.Stloc, fnProtoLocal);

        // PDSSetPrototype(newObj, fnProto) — only when fnProto is non-null.
        il.Emit(OpCodes.Ldloc, fnProtoLocal);
        il.Emit(OpCodes.Brfalse, skipProtoLabel);
        il.Emit(OpCodes.Ldloc, newObjLocal);
        il.Emit(OpCodes.Ldloc, fnProtoLocal);
        il.Emit(OpCodes.Call, runtime.PDSSetPrototype);

        il.MarkLabel(skipProtoLabel);

        // Save current thread-local this so nested `new F()` inside `new G()` doesn't
        // leak G's newObj to F. Restored in the finally below.
        il.Emit(OpCodes.Ldsfld, currentThisField);
        il.Emit(OpCodes.Stloc, prevThisLocal);

        // Set _currentFunctionThis to newObj so the body's `LoadThis` picks it up.
        il.Emit(OpCodes.Ldloc, newObjLocal);
        il.Emit(OpCodes.Stsfld, currentThisField);

        il.BeginExceptionBlock();

        var tryBound = il.DefineLabel();
        var notCallable = il.DefineLabel();
        var afterTry = il.DefineLabel();

        // if (fn is $TSFunction) result = fn.InvokeWithThis(newObj, args);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, tryBound);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, newObjLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, afterTry);

        // else if (fn is $BoundTSFunction) result = fn.InvokeWithThis(newObj, args);
        il.MarkLabel(tryBound);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brfalse, notCallable);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldloc, newObjLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvokeWithThis);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, afterTry);

        // Non-callable: flag so the post-finally code returns null (legacy behavior).
        il.MarkLabel(notCallable);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, notCallableLocal);
        il.Emit(OpCodes.Leave, afterTry);

        il.BeginFinallyBlock();
        // Restore thread-local this regardless of success/throw.
        il.Emit(OpCodes.Ldloc, prevThisLocal);
        il.Emit(OpCodes.Stsfld, currentThisField);
        il.Emit(OpCodes.Endfinally);
        il.EndExceptionBlock();

        il.MarkLabel(afterTry);

        // Non-callable callee → throw TypeError per ECMA-262 (spec: `new <not a constructor>`
        // throws TypeError). Previously returned null silently, which masked test262
        // checks like `try { new String.prototype.match } catch (e) { ... }` —
        // those tests EXPECT the throw and have no other passing path. The legacy
        // null-return only "worked" when downstream `throw new Test262Error(...)`
        // was *also* broken (Test262Error was a function declaration whose `new`
        // returned null, so `throw null` propagated benignly). With Stage 0b/0c
        // Test262Error throws properly, exposing the broken silent-null path.
        var notCallableSkip = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, notCallableLocal);
        il.Emit(OpCodes.Brfalse, notCallableSkip);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "not a constructor");
        il.MarkLabel(notCallableSkip);

        // Per JS: return result if it's an object, else newObj.
        var returnNewObj = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Brfalse, returnNewObj);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Beq, returnNewObj);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, returnNewObj);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, returnNewObj);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, returnNewObj);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnNewObj);
        il.Emit(OpCodes.Ldloc, newObjLocal);
        il.Emit(OpCodes.Ret);
    }
}
