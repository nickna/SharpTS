using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits <c>$Runtime.HasOwnPropertyHelper(object obj, object name) -&gt; bool</c>.
    /// Used to back <c>obj.hasOwnProperty(name)</c> for $TSFunction wrappers
    /// (Test262 frequently probes <c>String.prototype.X.hasOwnProperty("length")</c>),
    /// $Object instances, Dictionary literals, and arrays. Wired up in
    /// GetFunctionMethod / GetProperty / GetFieldsProperty for "hasOwnProperty".
    /// </summary>
    /// <remarks>
    /// Per ECMA-262, hasOwnProperty checks "own" properties — those directly
    /// on the receiver, not inherited. For each backing type:
    /// - $TSFunction: name is "name"/"length" (always cached) or PDS has a
    ///   user-defined descriptor for it.
    /// - $Object: name is in _fields or _getters.
    /// - $IHasFields (user-defined class instances): per-class HasProperty
    ///   knows typed backing fields + the dynamic _fields dict.
    /// - Dictionary: ContainsKey(name).
    /// - List/$Array: name is "length" or a numeric index in range.
    /// - String: numeric index in [0,len) or "length".
    /// - Otherwise: false.
    /// Symbols / non-string names are coerced via ToString.
    /// </remarks>
    private void EmitHasOwnPropertyHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "HasOwnPropertyHelper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]);
        runtime.HasOwnPropertyHelperMethod = method;

        // Name parameter 0 as "__this" so the wrapping $TSFunction sets
        // _expectsThis. That routes `.call(receiver, name)` through
        // InvokeWithThis's expectsThis branch (which prepends the explicit
        // thisArg as args[0]) instead of relying on a target-bound shortcut
        // that double-prepends and trims the wrong argument.
        method.DefineParameter(1, ParameterAttributes.None, "__this");
        method.DefineParameter(2, ParameterAttributes.None, "name");

        var il = method.GetILGenerator();
        var nameLocal = il.DeclareLocal(_types.String);

        var falseLabel = il.DefineLabel();
        var trueLabel = il.DefineLabel();

        // Null receiver → false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // ECMA-262 §20.1.3.2 step 1: Let P be ? ToPropertyKey(V). For Symbol,
        // ToPropertyKey returns the symbol itself — NOT a string. Symbol keys
        // are stored in the per-object symbol dict (GetSymbolDict), so resolve
        // them on that side path before falling through to the string-key
        // helpers below.
        var notSymbolKeyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brfalse, notSymbolKeyLabel);
        // GetSymbolDict(obj).ContainsKey(symbol)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "ContainsKey", _types.Object));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notSymbolKeyLabel);

        // Coerce name to string
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, nameLocal);

        // The standard globals are own properties of the global object even
        // though compiled mode resolves them through emitted helpers rather
        // than storing every intrinsic in the user-property dictionary.
        var notGlobalObject = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.GlobalThisSingletonField);
        il.Emit(OpCodes.Bne_Un, notGlobalObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brtrue, falseLabel);
        foreach (var globalName in new[]
        {
            "globalThis", "undefined", "NaN", "Infinity", "parseInt",
            "parseFloat", "isNaN", "isFinite", "eval", "Array", "Date",
            "RegExp", "Map", "Set", "WeakMap", "WeakSet", "Promise",
            "Function", "Object", "Number", "String", "Boolean", "Symbol",
            "Error", "TypeError", "RangeError", "ReferenceError", "SyntaxError",
            "URIError", "EvalError", "AggregateError", "Math", "JSON"
        })
        {
            il.Emit(OpCodes.Ldloc, nameLocal);
            il.Emit(OpCodes.Ldstr, globalName);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality",
                _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, trueLabel);
        }
        il.Emit(OpCodes.Br, falseLabel);
        il.MarkLabel(notGlobalObject);

        // Function-like runtime wrappers branch. Promise resolving functions
        // are anonymous ECMAScript built-ins and expose the same configurable
        // own name/length slots as $TSFunction wrappers.
        var notTSFunction = il.DefineLabel();
        var functionOwnPropertyCheck = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, functionOwnPropertyCheck);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.PromiseResolveCallbackType);
        il.Emit(OpCodes.Brtrue, functionOwnPropertyCheck);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.PromiseRejectCallbackType);
        il.Emit(OpCodes.Brfalse, notTSFunction);
        il.MarkLabel(functionOwnPropertyCheck);
        // If `name` or `length` was deleted on this instance, the property is
        // no longer own — report false. Per ECMA-262 §17, both are configurable.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brtrue, falseLabel);
        // True for "name" or "length"
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, trueLabel);
        // Otherwise check PDS for own descriptor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Br, falseLabel);
        il.MarkLabel(notTSFunction);

        // $Object branch
        var notTSObject = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObject);
        // First: HasProperty (checks _fields + _getters + _setters).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectHasProperty);
        il.Emit(OpCodes.Brtrue, trueLabel);
        // Fallback: PDS check (defineProperty-installed accessors on $TSObject
        // sit in PDS, not in TSObject._getters/_setters). Without this, hasOwn
        // returns false even though Object.keys finds the key (via PDS extras).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Br, falseLabel);
        il.MarkLabel(notTSObject);

        // RegExp instances have an intrinsic own lastIndex data property even
        // though its live value is stored in typed fields rather than a user
        // dictionary. Other own properties may still be installed through PDS.
        if (_features.UsesRegExp)
        {
            var notRegExp = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brfalse, notRegExp);
            il.Emit(OpCodes.Ldloc, nameLocal);
            il.Emit(OpCodes.Ldstr, "lastIndex");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, trueLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, nameLocal);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Brtrue, trueLabel);
            il.Emit(OpCodes.Br, falseLabel);
            il.MarkLabel(notRegExp);
        }

        // $IHasFields branch — user-defined class instances. Each compiled
        // class implements $IHasFields with a per-class HasProperty that
        // checks typed backing field names + the dynamic _fields dict
        // (see ILCompiler.Classes.HasFields.EmitHasPropertyBodyCore).
        // PDS fallback covers `Object.defineProperty(instance, name, ...)`.
        //
        // Must run after $TSObject (which also implements $IHasFields but
        // has its own richer dispatch via TSObjectHasProperty) and before
        // the Dictionary branch (a class instance isn't a Dictionary, but
        // ordering here documents the precedence).
        var notIHasFieldsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, notIHasFieldsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsHasProperty);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Br, falseLabel);
        il.MarkLabel(notIHasFieldsLabel);

        // Dictionary<string,object> — own property is either a direct key in
        // the backing dict (\"foo\" = 1 syntax) OR a PDS-stored descriptor
        // (Object.defineProperty path, which doesn't write to _fields). Both
        // count as \"own\" per ECMA-262 §7.3.13. Pre-fix the PDS branch was
        // missing, so non-enumerable defineProperty results returned false
        // here even though they were truly own — that's the residual #103
        // tail (~3500 Fail entries that read like `assert(obj.hasOwnProperty(...))`
        // after a defineProperty with no enumerable: true).
        var notDict = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notDict);

        // Local NameEq helper (mirrors the Type branch's helper).
        void DictNameEq(string n)
        {
            il.Emit(OpCodes.Ldloc, nameLocal);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, trueLabel);
        }

        // Math singleton — synth-check the known Math names. The dict is
        // empty but Math.X resolves through static dispatch; per spec
        // hasOwn(Math, "abs") === true.
        var notMathForHasOwnLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.MathSingletonField);
        il.Emit(OpCodes.Bne_Un, notMathForHasOwnLabel);
        // First skip if marked deleted in tracker.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brtrue, falseLabel);
        foreach (var m in new[] { "abs", "acos", "acosh", "asin", "asinh", "atan", "atan2",
            "atanh", "cbrt", "ceil", "clz32", "cos", "cosh", "exp", "expm1", "floor",
            "fround", "hypot", "imul", "log", "log10", "log1p", "log2", "max", "min",
            "pow", "random", "round", "sign", "sin", "sinh", "sqrt", "tan", "tanh", "trunc",
            "f16round", "sumPrecise",
            "E", "LN10", "LN2", "LOG10E", "LOG2E", "PI", "SQRT1_2", "SQRT2" })
            DictNameEq(m);
        il.MarkLabel(notMathForHasOwnLabel);

        // JSON singleton — same pattern.
        var notJsonForHasOwnLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.JsonSingletonField);
        il.Emit(OpCodes.Bne_Un, notJsonForHasOwnLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brtrue, falseLabel);
        DictNameEq("parse"); DictNameEq("stringify"); DictNameEq("isRawJSON"); DictNameEq("rawJSON");
        il.MarkLabel(notJsonForHasOwnLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brtrue, trueLabel);
        // Fall through to PDS check on dict-keyed property descriptors.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Br, falseLabel);
        il.MarkLabel(notDict);

        // String — numeric index in [0,len) or "length"
        var notString = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notString);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, trueLabel);
        // Numeric index — try int.TryParse
        var strIdxLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldloca, strIdxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, falseLabel);
        // 0 <= idx < str.Length
        il.Emit(OpCodes.Ldloc, strIdxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, falseLabel);
        il.Emit(OpCodes.Ldloc, strIdxLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, falseLabel);
        il.Emit(OpCodes.Br, trueLabel);
        il.MarkLabel(notString);

        // List<object> / $Array — "length" or numeric index in range. Also
        // honor PDS-installed named properties: ECMA-262 §23.1.5 allows
        // arbitrary named properties on Array exotic objects (the SetProperty
        // path stores `arr.foo = X` in PDS). Without the PDS fallback,
        // verifyProperty on a frozen array's own-defined string-keyed prop
        // would fail because hasOwn would lie.
        var notList = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notList);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, trueLabel);
        var listIdxLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldloca, listIdxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        var listNotIndexLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, listNotIndexLabel);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, listNotIndexLabel);
        // $TSArray inherits List<object>, but its logical length may contain
        // holes. Consult HasIndex before the dense List range check; if the
        // slot is an accessor-only index, the PDS fallback below still reports
        // it as own.
        var listReceiverIsPlain = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, listReceiverIsPlain);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayHasIndex);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Br, listNotIndexLabel);
        il.MarkLabel(listReceiverIsPlain);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, listNotIndexLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, listNotIndexLabel);
        il.Emit(OpCodes.Br, trueLabel);
        il.MarkLabel(listNotIndexLabel);
        // PDS fallback for named properties stored via the $TSArray set path.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Br, falseLabel);
        il.MarkLabel(notList);

        // System.Type — check known own properties for built-in JS constructors
        // (Boolean/Number/String have "prototype"; all have "name" and "length").
        // Also check known JS-spec static names per Type, then static reflection.
        var notTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brfalse, notTypeLabel);
        // Skip if the (Type, name) pair was deleted via the per-Type tracker
        // (e.g. `delete Object.assign` marks Object Type + "assign" as deleted).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brtrue, falseLabel);
        // "prototype" / "name" / "length" → true for any Type
        void NameEq(string n)
        {
            il.Emit(OpCodes.Ldloc, nameLocal);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, trueLabel);
        }
        NameEq("prototype");
        NameEq("name");
        NameEq("length");

        // Number static names — JS-spec own properties of the Number constructor.
        // System.Double's own static members don't match, so we check by Type ==
        // typeof(double).
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.Double);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Bne_Un, notDoubleLabel);
        NameEq("MAX_VALUE"); NameEq("MIN_VALUE");
        NameEq("NaN"); NameEq("POSITIVE_INFINITY"); NameEq("NEGATIVE_INFINITY");
        NameEq("MAX_SAFE_INTEGER"); NameEq("MIN_SAFE_INTEGER"); NameEq("EPSILON");
        NameEq("parseInt"); NameEq("parseFloat");
        NameEq("isNaN"); NameEq("isFinite"); NameEq("isInteger"); NameEq("isSafeInteger");
        il.MarkLabel(notDoubleLabel);

        // String static names.
        var notStringTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.String);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Bne_Un, notStringTypeLabel);
        NameEq("fromCharCode"); NameEq("fromCodePoint"); NameEq("raw");
        il.MarkLabel(notStringTypeLabel);

        // System.Object → JS Object constructor's static names. Reflection on
        // System.Object can't find them (they're SharpTS-intercepted). List the
        // ECMA-262 §20.1 static methods explicitly so hasOwn(Object, "keys")
        // / Object.hasOwn round-trip.
        var notObjectTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.Object);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Bne_Un, notObjectTypeLabel);
        NameEq("assign"); NameEq("create"); NameEq("defineProperties");
        NameEq("defineProperty"); NameEq("entries"); NameEq("freeze");
        NameEq("fromEntries"); NameEq("getOwnPropertyDescriptor");
        NameEq("getOwnPropertyDescriptors"); NameEq("getOwnPropertyNames");
        NameEq("getOwnPropertySymbols"); NameEq("getPrototypeOf");
        NameEq("groupBy"); NameEq("hasOwn"); NameEq("is"); NameEq("isExtensible");
        NameEq("isFrozen"); NameEq("isSealed"); NameEq("keys"); NameEq("preventExtensions");
        NameEq("seal"); NameEq("setPrototypeOf"); NameEq("values");
        il.MarkLabel(notObjectTypeLabel);

        // Array static names (typeof Array → IList<object>).
        var notArrayConsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.IListOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Bne_Un, notArrayConsLabel);
        NameEq("from"); NameEq("fromAsync"); NameEq("isArray"); NameEq("of");
        il.MarkLabel(notArrayConsLabel);

        // Promise and Error statics are runtime-emitted helpers rather than
        // reflection-visible members on Task<object> / $Error.
        var notPromiseConsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Bne_Un, notPromiseConsLabel);
        NameEq("resolve"); NameEq("reject"); NameEq("all"); NameEq("race");
        NameEq("allKeyed"); NameEq("allSettled"); NameEq("allSettledKeyed");
        NameEq("any"); NameEq("withResolvers");
        il.MarkLabel(notPromiseConsLabel);

        var notErrorConsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, runtime.TSErrorType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Bne_Un, notErrorConsLabel);
        NameEq("isError");
        il.MarkLabel(notErrorConsLabel);

        if (_features.UsesRegExp)
        {
            var notRegExpConsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, runtime.TSRegExpType);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
            il.Emit(OpCodes.Bne_Un, notRegExpConsLabel);
            NameEq("escape");
            il.MarkLabel(notRegExpConsLabel);
        }

        // Shared JS built-in static dispatch is also the authoritative own-
        // property inventory for runtime-emitted members such as Date.now,
        // Date.UTC, and Date.parse. Reflection cannot discover their adapted
        // JS names/signatures.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.Type);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, runtime.LookupBuiltInStaticMember);
        il.Emit(OpCodes.Brtrue, trueLabel);

        // Reflection: type.GetField(name, Public|Static) ?? type.GetMethod(name, Public|Static)
        const System.Reflection.BindingFlags staticPub = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
        var typeLocal2 = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.Type);
        il.Emit(OpCodes.Stloc, typeLocal2);
        // GetField
        il.Emit(OpCodes.Ldloc, typeLocal2);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldc_I4, (int)staticPub);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", _types.String, typeof(System.Reflection.BindingFlags)));
        il.Emit(OpCodes.Brtrue, trueLabel);
        // GetProperty(name) — covers static .NET property accessors.
        il.Emit(OpCodes.Ldloc, typeLocal2);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldc_I4, (int)staticPub);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String, typeof(System.Reflection.BindingFlags)));
        il.Emit(OpCodes.Brtrue, trueLabel);
        // PDS check — user-set static properties (e.g. `Object.assign = X`,
        // verifyProperty's isConfigurable round-trip via delete+hasOwn).
        // Without this, propertyHelper sees configurable:true but isConfigurable
        // returns false because the bracket set didn't surface.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Br, falseLabel);
        il.MarkLabel(notTypeLabel);

        // Default: PDS check (might find user-set descriptor)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, trueLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>$Runtime.PropertyIsEnumerableHelper(object obj, object name) -&gt; bool</c>.
    /// ECMA-262 §19.1.3.4: returns the property's [[Enumerable]] descriptor bit
    /// when the property is own, false otherwise. Spec defaults for built-in
    /// data properties are enumerable=false (§17), so PDS-installed descriptors
    /// for built-in accessors override the dict's default-true.
    /// </summary>
    /// <remarks>
    /// Pre-fix this slot pointed at <c>StringPrototypeGenericStub</c> which
    /// returned <c>Convert.ToString(receiver)</c> — i.e. a Dictionary's
    /// type-name string instead of a bool. verifyProperty's enumerable
    /// assertion (in propertyHelper.js) flunked every RegExp.prototype
    /// accessor's prop-desc.js as a result.
    /// </remarks>
    private void EmitPropertyIsEnumerableHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "PropertyIsEnumerableHelper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]);
        runtime.PropertyIsEnumerableHelperMethod = method;
        method.DefineParameter(1, ParameterAttributes.None, "__this");
        method.DefineParameter(2, ParameterAttributes.None, "name");

        var il = method.GetILGenerator();
        var nameLocal = il.DeclareLocal(_types.String);
        var descLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var falseLabel = il.DefineLabel();

        // ECMA-262 §20.1.3.4 step 1: Let O be ? ToObject(this value). ToObject
        // throws TypeError on null/undefined. Test262 S15.2.4.7_A12/A13 verify.
        // (The prior deferred-throw note cited a -2 in regen-13 due to
        // hasOwnProperty.call(undef-resolveFn, "prototype") on incompletely-
        // populated Promise resolve-element bindings — but those paths now
        // populate real $TSFunction wrappers, so the cascade no longer fires.
        // Watch the regen diff.)
        var pieNullThrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, pieNullThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, pieNullThrowLabel);
        var pieAfterNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, pieAfterNullLabel);
        il.MarkLabel(pieNullThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
        il.MarkLabel(pieAfterNullLabel);

        // Symbol-keyed lookup. PDSGetPropertyDescriptor's name parameter is
        // string-typed, so symbol keys can't live there — defineProperty with
        // a symbol key writes to the per-receiver symbol dict (GetSymbolDict).
        // Plain values use enumerable=true (ordinary assignment), while
        // defineProperty entries carry a CompiledPropertyDescriptor whose
        // Enumerable bit must be observed directly.
        var pieNotSymbolLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brfalse, pieNotSymbolLabel);
        var pieSymbolValueLocal = il.DeclareLocal(_types.Object);
        var pieSymbolDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var pieSymbolPresentLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, pieSymbolValueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
        il.Emit(OpCodes.Brtrue, pieSymbolPresentLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(pieSymbolPresentLabel);
        il.Emit(OpCodes.Ldloc, pieSymbolValueLocal);
        il.Emit(OpCodes.Isinst, runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Stloc, pieSymbolDescriptorLocal);
        var piePlainSymbolLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, pieSymbolDescriptorLocal);
        il.Emit(OpCodes.Brfalse, piePlainSymbolLabel);
        il.Emit(OpCodes.Ldloc, pieSymbolDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(piePlainSymbolLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(pieNotSymbolLabel);

        // Coerce name to string. (Object.ToString — same convention as
        // HasOwnPropertyHelper.)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, nameLocal);

        // RegExp lastIndex is an intrinsic own non-enumerable property. It is
        // not placed in PDS until user code redefines its attributes.
        if (_features.UsesRegExp)
        {
            var pieNotRegExpLastIndex = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brfalse, pieNotRegExpLastIndex);
            il.Emit(OpCodes.Ldloc, nameLocal);
            il.Emit(OpCodes.Ldstr, "lastIndex");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, falseLabel);
            il.MarkLabel(pieNotRegExpLastIndex);
        }

        // ECMA-262 §17: built-in function .name / .length are
        // { writable:false, enumerable:false, configurable:true } — return
        // false explicitly when the receiver is a $TSFunction and the name
        // is one of these synthetic slots. HasOwnPropertyHelper would
        // otherwise report true and the PropertyIsEnumerable fallback below
        // would mistakenly inherit that as enumerable=true.
        var notFunctionBuiltinLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notFunctionBuiltinLabel);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.MarkLabel(notFunctionBuiltinLabel);

        // PDS descriptor lookup. When defineProperty installed a descriptor
        // for this name we already have the spec-correct Enumerable bit.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descLocal);
        var noPdsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Brfalse, noPdsLabel);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(noPdsLabel);

        // System.Type receivers represent built-in constructors. Their
        // descriptor-less synthetic own properties (prototype, name, length,
        // constants, and static methods) are all non-enumerable per ECMA-262
        // §17. User-defined properties installed through defineProperty are
        // represented in PDS and were handled above. Missing properties also
        // correctly report false, so no synthetic-name inventory is needed.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, falseLabel);

        // No PDS descriptor — fall back to HasOwnPropertyHelper. ECMA-262
        // says only "own" properties qualify; plain dict entries with no
        // explicit descriptor are spec-default enumerable=true, which is
        // exactly what HasOwn returns when found.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.HasOwnPropertyHelperMethod);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }
}
