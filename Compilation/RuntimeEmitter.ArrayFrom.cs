using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits ArrayFrom: creates an array from an iterable with optional map function.
    /// Signature: <c>$Array ArrayFrom(object iterable, object mapFn, $TSSymbol iteratorSymbol, Type runtimeType)</c>.
    /// Stage E.2 M2: returns <c>$Array</c> (was <c>List&lt;object?&gt;</c>).
    /// </summary>
    private void EmitArrayFrom(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Signature: (iterable, mapFn, iteratorSymbol, runtimeType, thisArg).
        // thisArg added in #101 session — appended at end for caller-side
        // backward compat: existing callers updated to push $Undefined.
        var method = typeBuilder.DefineMethod(
            "ArrayFrom",
            MethodAttributes.Public | MethodAttributes.Static,
            runtime.TSArrayType,
            [_types.Object, _types.Object, runtime.TSSymbolType, _types.Type, _types.Object]
        );
        runtime.ArrayFrom = method;

        var il = method.GetILGenerator();

        // Locals
        var resultLocal = il.DeclareLocal(_types.ListOfObject);     // The result list from IterateToList or the mapped result
        var indexLocal = il.DeclareLocal(_types.Int32);             // Loop counter
        var mappedResultLocal = il.DeclareLocal(_types.ListOfObject); // Mapped result when mapFn is provided

        // Labels
        var noMapFnLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();

        // ECMA-262 23.1.2.1 Array.from step 1: ToObject(items). Throws TypeError
        // for null / undefined receivers. Pre-fix tolerated these silently
        // (empty result), but Test262 explicitly tests `Array.from(null)` /
        // `Array.from(undefined)` for the throw.
        var nonNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, nonNullLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Array.from requires an array-like object - not null or undefined");
        il.MarkLabel(nonNullLabel);
        var nonUndefLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, nonUndefLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Array.from requires an array-like object - not null or undefined");
        il.MarkLabel(nonUndefLabel);

        // ECMA-262 23.1.2.1 step 2-3: if mapfn is undefined, mapping is false.
        // Otherwise mapfn must be callable; non-callable (null/Symbol/Number/
        // string/Object) → throw TypeError.
        // Callers emit $Undefined.Instance for absent args, so this distinguishes
        // "not provided" from "provided as null".
        var mapFnOkLabel = il.DefineLabel();
        // $Undefined → skip check
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, mapFnOkLabel);
        // Provided value: must be callable. $TSFunction passes; everything else throws.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, mapFnOkLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Array.from: mapfn argument must be callable");
        il.MarkLabel(mapFnOkLabel);

        // ECMA-262 Array.from: if @@iterator is missing, treat receiver as
        // array-like (read length, iterate by index). The IterateToList
        // fallback enumerates Dictionary<string,object> as KeyValuePair —
        // wrong semantics for `Array.from({0:'a',1:'b',length:2})`. Detect
        // Dictionary receivers without a Symbol.iterator method and route
        // through ArrayLikeMaterialize, which handles length+indexed reads
        // and preserves holes.
        var notArrayLikeDictLabel = il.DefineLabel();
        var afterListInit = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notArrayLikeDictLabel);
        // GetIteratorFunction(iterable, iteratorSymbol) — null means array-like
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.GetIteratorFunction);
        il.Emit(OpCodes.Brtrue, notArrayLikeDictLabel);
        // dict has no @@iterator → ArrayLikeMaterialize
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ArrayLikeMaterialize);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, afterListInit);
        il.MarkLabel(notArrayLikeDictLabel);

        // Call IterateToList(iterable, iteratorSymbol, runtimeType) to get the initial list
        il.Emit(OpCodes.Ldarg_0);  // iterable
        il.Emit(OpCodes.Ldarg_2);  // iteratorSymbol
        il.Emit(OpCodes.Ldarg_3);  // runtimeType
        il.Emit(OpCodes.Call, runtime.IterateToList);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.MarkLabel(afterListInit);

        // if (mapFn is $Undefined) return result. Callers now pass $Undefined
        // for absent mapfn, so the prior null-check is replaced by Isinst.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, noMapFnLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noMapFnLabel);

        // Create mapped result list
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, mappedResultLocal);

        // int i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // Loop: for (int i = 0; i < result.Count; i++)
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEndLabel);

        // Create args array: [result[i], (double)i]
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);

        // args[0] = result[i]
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = (double)i
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);

        // Store args array
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Stloc, argsLocal);

        // Call mapFn with thisArg via InvokeMethodValue(thisArg, fn, args).
        // Pre-fix InvokeValue passed no this binding, so Array.from(items, fn,
        // thisArg) ignored the thisArg per ECMA-262 23.1.2.1 step 5.b.
        il.Emit(OpCodes.Ldarg, (short)4);  // thisArg
        il.Emit(OpCodes.Ldarg_1);  // mapFn
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);

        // Add result to mappedResult
        var callResultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, callResultLocal);
        il.Emit(OpCodes.Ldloc, mappedResultLocal);
        il.Emit(OpCodes.Ldloc, callResultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);
        // Return mapped result
        il.Emit(OpCodes.Ldloc, mappedResultLocal);
        il.Emit(OpCodes.Br, returnLabel);

        // No map function - return the original result from IterateToList
        il.MarkLabel(noMapFnLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);

        il.MarkLabel(returnLabel);
        // Wrap the List<object?> in $Array on the way out.
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ArrayOf: creates an array from arguments.
    /// Signature: <c>$Array ArrayOf(object[] args)</c>.
    /// Stage E.2 M2: returns <c>$Array</c> (was <c>List&lt;object?&gt;</c>).
    /// </summary>
    private void EmitArrayOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayOf",
            MethodAttributes.Public | MethodAttributes.Static,
            runtime.TSArrayType,
            [_types.ObjectArray]
        );
        runtime.ArrayOf = method;

        var il = method.GetILGenerator();

        // return new $Array(new List<object?>(args));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, typeof(IEnumerable<object>)));
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Stage 4y: adapter wrapping ArrayFrom for value-form access.
    /// `let f = Array.from; f(iterable, mapFn)` — TSFunction.Invoke maps
    /// args[0]/[1] to (iterable, mapFn) and we supply the spec-fixed
    /// SymbolIterator + runtime-Type internally. Signature: object(object[]).
    /// </summary>
    private void EmitArrayFromAdapter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayFromAdapter",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.ArrayFromAdapter = method;

        var il = method.GetILGenerator();
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        var iterableLocal = il.DeclareLocal(_types.Object);
        var mapFnLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, argsLocal);

        // iterable = args.Length > 0 ? args[0] : null
        var hasIterable = il.DefineLabel();
        var iterableSet = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, hasIterable);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, iterableLocal);
        il.Emit(OpCodes.Br, iterableSet);
        il.MarkLabel(hasIterable);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, iterableLocal);
        il.MarkLabel(iterableSet);

        // mapFn = args.Length > 1 ? args[1] : $Undefined.Instance
        // (absent → $Undefined so ArrayFrom can distinguish from explicit null
        // per ECMA-262 23.1.2.1 step 2: undefined skips mapping; null throws.)
        var hasMapFn = il.DefineLabel();
        var mapFnSet = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasMapFn);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, mapFnLocal);
        il.Emit(OpCodes.Br, mapFnSet);
        il.MarkLabel(hasMapFn);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, mapFnLocal);
        il.MarkLabel(mapFnSet);

        // Read thisArg from args[2] if provided, else $Undefined.
        var thisArgLocal = il.DeclareLocal(_types.Object);
        var hasThisArg = il.DefineLabel();
        var thisArgSet = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bgt, hasThisArg);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, thisArgLocal);
        il.Emit(OpCodes.Br, thisArgSet);
        il.MarkLabel(hasThisArg);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, thisArgLocal);
        il.MarkLabel(thisArgSet);

        // return ArrayFrom(iterable, mapFn, SymbolIterator, typeof($Runtime), thisArg)
        il.Emit(OpCodes.Ldloc, iterableLocal);
        il.Emit(OpCodes.Ldloc, mapFnLocal);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolIterator);
        il.Emit(OpCodes.Ldtoken, runtime.RuntimeType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ldloc, thisArgLocal);
        il.Emit(OpCodes.Call, runtime.ArrayFrom);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the ECMAScript <c>Array</c> constructor (issue #61):
    /// <list type="bullet">
    /// <item><c>Array()</c> / <c>new Array()</c> → empty <c>$Array</c>.</item>
    /// <item><c>Array(n)</c> where <c>n</c> is a valid uint32 number → empty
    ///   <c>$Array</c> whose length is set via <c>SetLength(n)</c>. Beyond the
    ///   sparse threshold the backing transitions to sparse storage, so
    ///   <c>new Array(10_000_000)</c> allocates O(1) instead of paging the
    ///   heap full of holes.</item>
    /// <item><c>Array(x)</c> where <c>x</c> is not a number → single-element
    ///   <c>$Array([x])</c>.</item>
    /// <item><c>Array(a, b, c, …)</c> → <c>$Array</c> wrapping a list of all args.</item>
    /// </list>
    /// Out-of-range numeric inputs (negative, non-integer, &gt; uint32 max)
    /// throw <c>RangeError</c> — <c>SetLength</c> enforces this, matching the
    /// interpreter and ECMA-262 §23.1.1.1.
    /// Signature: <c>$Array ArrayConstructor(object[] args)</c>.
    /// Stage E.2 M2: the Stage-D 1M guard is gone; sparse storage absorbs huge N.
    /// </summary>
    private void EmitArrayConstructor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayConstructor",
            MethodAttributes.Public | MethodAttributes.Static,
            runtime.TSArrayType,
            [_types.ObjectArray]
        );
        runtime.ArrayConstructor = method;

        var il = method.GetILGenerator();
        var lenLocal = il.DeclareLocal(_types.Int32);
        var argLocal = il.DeclareLocal(_types.Object);
        var dLocal = il.DeclareLocal(_types.Double);
        var nLocal = il.DeclareLocal(_types.Int64);
        var arrLocal = il.DeclareLocal(runtime.TSArrayType);

        var emptyCaseLabel = il.DefineLabel();
        var singleArgLabel = il.DefineLabel();
        var multiArgLabel = il.DefineLabel();
        var notNumberLabel = il.DefineLabel();

        // if (args == null) → empty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, emptyCaseLabel);

        // lenLocal = args.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lenLocal);

        // if (len == 0) → empty
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Brfalse, emptyCaseLabel);

        // if (len == 1) → single-arg branch
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, singleArgLabel);

        // else → multi-arg branch
        il.Emit(OpCodes.Br, multiArgLabel);

        // --- empty: return new $Array(new List<object?>()) ---
        il.MarkLabel(emptyCaseLabel);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);

        // --- single-arg: check numeric-vs-other ---
        il.MarkLabel(singleArgLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, argLocal);

        // if (!(arg is double)) → single-element $Array([arg])
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notNumberLabel);

        // Numeric: validate range per ECMA-262 ToUint32.
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, dLocal);

        var rangeErrorLabel = il.DefineLabel();
        var inRangeLabel = il.DefineLabel();
        // if (d < 0) → throw
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Blt_Un, rangeErrorLabel);
        // if (d > uint.MaxValue) → throw
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldc_R8, (double)uint.MaxValue);
        il.Emit(OpCodes.Bgt_Un, rangeErrorLabel);
        // if (d != floor(d)) → throw (non-integer)
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Floor", _types.Double));
        il.Emit(OpCodes.Bne_Un, rangeErrorLabel);
        il.Emit(OpCodes.Br, inRangeLabel);

        il.MarkLabel(rangeErrorLabel);
        // Inline-throw pattern: $RangeError(msg) wrapped in CLR Exception with
        // Data["__tsValue"]=err so WrapException returns it on catch. Used here
        // because EmitArrayConstructor runs before EmitCreateException in the
        // emit pipeline (must precede InvokeValue), so runtime.CreateException
        // isn't yet defined.
        var errLocal = il.DeclareLocal(_types.Object);
        var exLocal = il.DeclareLocal(_types.Exception);
        il.Emit(OpCodes.Ldstr, "Invalid array length");
        il.Emit(OpCodes.Newobj, runtime.TSRangeErrorCtor);
        il.Emit(OpCodes.Stloc, errLocal);
        il.Emit(OpCodes.Ldstr, "Invalid array length");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Stloc, exLocal);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__tsValue");
        il.Emit(OpCodes.Ldloc, errLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "set_Item"));
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(inRangeLabel);

        // long n = (long)d;
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Stloc, nLocal);

        // arr = new $Array(new List<object?>());
        // arr.SetLength(n);
        // return arr;
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Stloc, arrLocal);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSArraySetLength);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ret);

        // --- single-arg, non-numeric: return $Array([arg]) ---
        il.MarkLabel(notNumberLabel);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);

        // --- multi-arg: $Array wrapping new List<object>(args) ---
        il.MarkLabel(multiArgLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, typeof(IEnumerable<object>)));
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }
}
