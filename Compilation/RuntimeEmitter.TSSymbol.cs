using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitTSSymbolClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $TSSymbol
        var typeBuilder = moduleBuilder.DefineType(
            "$TSSymbol",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSSymbolType = typeBuilder;

        // Static field for next ID
        var nextIdField = typeBuilder.DefineField("_nextId", _types.Int32, FieldAttributes.Private | FieldAttributes.Static);

        // Instance fields
        var idField = typeBuilder.DefineField("_id", _types.Int32, FieldAttributes.Private);
        var descriptionField = typeBuilder.DefineField("_description", _types.String, FieldAttributes.Private);

        // Global registry fields for Symbol.for() / Symbol.keyFor()
        var registryLockField = typeBuilder.DefineField(
            "_registryLock",
            _types.Object,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        var globalRegistryType = _types.MakeGenericType(typeof(Dictionary<,>), _types.String, typeBuilder);
        var globalRegistryField = typeBuilder.DefineField(
            "_globalRegistry",
            globalRegistryType,
            FieldAttributes.Private | FieldAttributes.Static
        );
        var reverseRegistryType = _types.MakeGenericType(typeof(Dictionary<,>), typeBuilder, _types.String);
        var reverseRegistryField = typeBuilder.DefineField(
            "_reverseRegistry",
            reverseRegistryType,
            FieldAttributes.Private | FieldAttributes.Static
        );

        // Constructor: public $TSSymbol(string? description)
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        runtime.TSSymbolCtor = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        // Call base constructor
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        // _id = Interlocked.Increment(ref _nextId)
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldsflda, nextIdField);
        ctorIL.Emit(OpCodes.Call, _types.Interlocked.GetMethod("Increment", [_types.Int32.MakeByRefType()])!);
        ctorIL.Emit(OpCodes.Stfld, idField);
        // _description = description
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, descriptionField);
        ctorIL.Emit(OpCodes.Ret);

        // Well-known symbol static fields
        var iteratorField = typeBuilder.DefineField("iterator", typeBuilder, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.SymbolIterator = iteratorField;
        var asyncIteratorField = typeBuilder.DefineField("asyncIterator", typeBuilder, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.SymbolAsyncIterator = asyncIteratorField;
        var toStringTagField = typeBuilder.DefineField("toStringTag", typeBuilder, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.SymbolToStringTag = toStringTagField;
        var hasInstanceField = typeBuilder.DefineField("hasInstance", typeBuilder, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.SymbolHasInstance = hasInstanceField;
        var isConcatSpreadableField = typeBuilder.DefineField("isConcatSpreadable", typeBuilder, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.SymbolIsConcatSpreadable = isConcatSpreadableField;
        var toPrimitiveField = typeBuilder.DefineField("toPrimitive", typeBuilder, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.SymbolToPrimitive = toPrimitiveField;
        var speciesField = typeBuilder.DefineField("species", typeBuilder, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.SymbolSpecies = speciesField;
        var unscopablesField = typeBuilder.DefineField("unscopables", typeBuilder, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.SymbolUnscopables = unscopablesField;
        var disposeField = typeBuilder.DefineField("dispose", typeBuilder, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.SymbolDispose = disposeField;
        var asyncDisposeField = typeBuilder.DefineField("asyncDispose", typeBuilder, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.SymbolAsyncDispose = asyncDisposeField;
        var matchField = typeBuilder.DefineField("match", typeBuilder, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.SymbolMatch = matchField;
        var matchAllField = typeBuilder.DefineField("matchAll", typeBuilder, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.SymbolMatchAll = matchAllField;
        var replaceField = typeBuilder.DefineField("replace", typeBuilder, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.SymbolReplace = replaceField;
        var searchField = typeBuilder.DefineField("search", typeBuilder, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.SymbolSearch = searchField;
        var splitField = typeBuilder.DefineField("split", typeBuilder, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.SymbolSplit = splitField;

        // ============================================================
        // Symbol.For(string key) - static method
        // ============================================================
        var forMethod = typeBuilder.DefineMethod(
            "For",
            MethodAttributes.Public | MethodAttributes.Static,
            typeBuilder,
            [_types.String]
        );
        runtime.SymbolFor = forMethod;
        var forIL = forMethod.GetILGenerator();

        // Emit: lock (_registryLock) { ... }
        var forExisting = forIL.DeclareLocal(typeBuilder);  // local 0: existing symbol
        var forLockTaken = forIL.DeclareLocal(_types.Boolean);  // local 1: lockTaken
        var forLockObj = forIL.DeclareLocal(_types.Object);  // local 2: lockObj

        var forFoundLabel = forIL.DefineLabel();
        var forEndLabel = forIL.DefineLabel();
        var forFinallyStart = forIL.DefineLabel();

        // lockObj = _registryLock
        forIL.Emit(OpCodes.Ldsfld, registryLockField);
        forIL.Emit(OpCodes.Stloc_2);
        // lockTaken = false
        forIL.Emit(OpCodes.Ldc_I4_0);
        forIL.Emit(OpCodes.Stloc_1);

        // try {
        forIL.BeginExceptionBlock();

        // Monitor.Enter(lockObj, ref lockTaken)
        forIL.Emit(OpCodes.Ldloc_2);
        forIL.Emit(OpCodes.Ldloca_S, forLockTaken);
        forIL.Emit(OpCodes.Call, _types.Monitor.GetMethod("Enter", [_types.Object, _types.Boolean.MakeByRefType()])!);

        // if (_globalRegistry.TryGetValue(key, out existing)) return existing;
        forIL.Emit(OpCodes.Ldsfld, globalRegistryField);
        forIL.Emit(OpCodes.Ldarg_0);  // key
        forIL.Emit(OpCodes.Ldloca_S, forExisting);
        var tryGetValueMethod = EmitterTypeHelpers.ResolveMethod(globalRegistryType, typeof(Dictionary<,>).GetMethod("TryGetValue")!);
        forIL.Emit(OpCodes.Callvirt, tryGetValueMethod);
        forIL.Emit(OpCodes.Brtrue, forFoundLabel);

        // var symbol = new $TSSymbol(key);
        forIL.Emit(OpCodes.Ldarg_0);  // key
        forIL.Emit(OpCodes.Newobj, ctorBuilder);
        forIL.Emit(OpCodes.Stloc_0);  // existing = new symbol

        // _globalRegistry[key] = symbol;
        forIL.Emit(OpCodes.Ldsfld, globalRegistryField);
        forIL.Emit(OpCodes.Ldarg_0);  // key
        forIL.Emit(OpCodes.Ldloc_0);  // symbol
        var setItemMethod = EmitterTypeHelpers.ResolveMethod(globalRegistryType, typeof(Dictionary<,>).GetMethod("set_Item")!);
        forIL.Emit(OpCodes.Callvirt, setItemMethod);

        // _reverseRegistry[symbol] = key;
        forIL.Emit(OpCodes.Ldsfld, reverseRegistryField);
        forIL.Emit(OpCodes.Ldloc_0);  // symbol
        forIL.Emit(OpCodes.Ldarg_0);  // key
        var reverseSetItemMethod = EmitterTypeHelpers.ResolveMethod(reverseRegistryType, typeof(Dictionary<,>).GetMethod("set_Item")!);
        forIL.Emit(OpCodes.Callvirt, reverseSetItemMethod);

        // Fall through to return existing (which now holds new symbol)
        forIL.MarkLabel(forFoundLabel);
        forIL.Emit(OpCodes.Ldloc_0);  // return existing
        forIL.Emit(OpCodes.Stloc_0);  // Store result temporarily
        forIL.Emit(OpCodes.Leave, forEndLabel);

        // } finally { if (lockTaken) Monitor.Exit(lockObj); }
        forIL.BeginFinallyBlock();
        var skipExit = forIL.DefineLabel();
        forIL.Emit(OpCodes.Ldloc_1);  // lockTaken
        forIL.Emit(OpCodes.Brfalse, skipExit);
        forIL.Emit(OpCodes.Ldloc_2);  // lockObj
        forIL.Emit(OpCodes.Call, _types.Monitor.GetMethod("Exit", [_types.Object])!);
        forIL.MarkLabel(skipExit);
        forIL.EndExceptionBlock();

        forIL.MarkLabel(forEndLabel);
        forIL.Emit(OpCodes.Ldloc_0);  // return result
        forIL.Emit(OpCodes.Ret);

        // ============================================================
        // Symbol.KeyFor($TSSymbol symbol) - static method, returns string or null
        // ============================================================
        var keyForMethod = typeBuilder.DefineMethod(
            "KeyFor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [typeBuilder]
        );
        runtime.SymbolKeyFor = keyForMethod;
        var keyForIL = keyForMethod.GetILGenerator();

        var keyForResult = keyForIL.DeclareLocal(_types.String);  // local 0: result
        var keyForLockTaken = keyForIL.DeclareLocal(_types.Boolean);  // local 1: lockTaken
        var keyForLockObj = keyForIL.DeclareLocal(_types.Object);  // local 2: lockObj

        var keyForEndLabel = keyForIL.DefineLabel();

        // lockObj = _registryLock
        keyForIL.Emit(OpCodes.Ldsfld, registryLockField);
        keyForIL.Emit(OpCodes.Stloc_2);
        // lockTaken = false
        keyForIL.Emit(OpCodes.Ldc_I4_0);
        keyForIL.Emit(OpCodes.Stloc_1);
        // result = null
        keyForIL.Emit(OpCodes.Ldnull);
        keyForIL.Emit(OpCodes.Stloc_0);

        // try {
        keyForIL.BeginExceptionBlock();

        // Monitor.Enter(lockObj, ref lockTaken)
        keyForIL.Emit(OpCodes.Ldloc_2);
        keyForIL.Emit(OpCodes.Ldloca_S, keyForLockTaken);
        keyForIL.Emit(OpCodes.Call, _types.Monitor.GetMethod("Enter", [_types.Object, _types.Boolean.MakeByRefType()])!);

        // _reverseRegistry.TryGetValue(symbol, out result)
        keyForIL.Emit(OpCodes.Ldsfld, reverseRegistryField);
        keyForIL.Emit(OpCodes.Ldarg_0);  // symbol
        keyForIL.Emit(OpCodes.Ldloca_S, keyForResult);
        var reverseTryGetValueMethod = EmitterTypeHelpers.ResolveMethod(reverseRegistryType, typeof(Dictionary<,>).GetMethod("TryGetValue")!);
        keyForIL.Emit(OpCodes.Callvirt, reverseTryGetValueMethod);
        keyForIL.Emit(OpCodes.Pop);  // Discard bool result, we just want the out value (null if not found)

        keyForIL.Emit(OpCodes.Leave, keyForEndLabel);

        // } finally { if (lockTaken) Monitor.Exit(lockObj); }
        keyForIL.BeginFinallyBlock();
        var keyForSkipExit = keyForIL.DefineLabel();
        keyForIL.Emit(OpCodes.Ldloc_1);  // lockTaken
        keyForIL.Emit(OpCodes.Brfalse, keyForSkipExit);
        keyForIL.Emit(OpCodes.Ldloc_2);  // lockObj
        keyForIL.Emit(OpCodes.Call, _types.Monitor.GetMethod("Exit", [_types.Object])!);
        keyForIL.MarkLabel(keyForSkipExit);
        keyForIL.EndExceptionBlock();

        keyForIL.MarkLabel(keyForEndLabel);
        keyForIL.Emit(OpCodes.Ldloc_0);  // return result
        keyForIL.Emit(OpCodes.Ret);

        // Static constructor to initialize well-known symbols and registry
        var cctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var cctorIL = cctorBuilder.GetILGenerator();

        // Initialize registry lock: _registryLock = new object()
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.Object));
        cctorIL.Emit(OpCodes.Stsfld, registryLockField);

        // Initialize global registry: _globalRegistry = new Dictionary<string, $TSSymbol>()
        var globalRegistryCtor = EmitterTypeHelpers.ResolveConstructor(globalRegistryType, typeof(Dictionary<,>).GetConstructor(Type.EmptyTypes)!);
        cctorIL.Emit(OpCodes.Newobj, globalRegistryCtor);
        cctorIL.Emit(OpCodes.Stsfld, globalRegistryField);

        // Initialize reverse registry: _reverseRegistry = new Dictionary<$TSSymbol, string>()
        var reverseRegistryCtor = EmitterTypeHelpers.ResolveConstructor(reverseRegistryType, typeof(Dictionary<,>).GetConstructor(Type.EmptyTypes)!);
        cctorIL.Emit(OpCodes.Newobj, reverseRegistryCtor);
        cctorIL.Emit(OpCodes.Stsfld, reverseRegistryField);

        // iterator = new $TSSymbol("Symbol.iterator")
        cctorIL.Emit(OpCodes.Ldstr, "Symbol.iterator");
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, iteratorField);

        // asyncIterator = new $TSSymbol("Symbol.asyncIterator")
        cctorIL.Emit(OpCodes.Ldstr, "Symbol.asyncIterator");
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, asyncIteratorField);

        // toStringTag = new $TSSymbol("Symbol.toStringTag")
        cctorIL.Emit(OpCodes.Ldstr, "Symbol.toStringTag");
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, toStringTagField);

        // hasInstance = new $TSSymbol("Symbol.hasInstance")
        cctorIL.Emit(OpCodes.Ldstr, "Symbol.hasInstance");
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, hasInstanceField);

        // isConcatSpreadable = new $TSSymbol("Symbol.isConcatSpreadable")
        cctorIL.Emit(OpCodes.Ldstr, "Symbol.isConcatSpreadable");
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, isConcatSpreadableField);

        // toPrimitive = new $TSSymbol("Symbol.toPrimitive")
        cctorIL.Emit(OpCodes.Ldstr, "Symbol.toPrimitive");
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, toPrimitiveField);

        // species = new $TSSymbol("Symbol.species")
        cctorIL.Emit(OpCodes.Ldstr, "Symbol.species");
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, speciesField);

        // unscopables = new $TSSymbol("Symbol.unscopables")
        cctorIL.Emit(OpCodes.Ldstr, "Symbol.unscopables");
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, unscopablesField);

        // dispose = new $TSSymbol("Symbol.dispose")
        cctorIL.Emit(OpCodes.Ldstr, "Symbol.dispose");
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, disposeField);

        // asyncDispose = new $TSSymbol("Symbol.asyncDispose")
        cctorIL.Emit(OpCodes.Ldstr, "Symbol.asyncDispose");
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, asyncDisposeField);

        cctorIL.Emit(OpCodes.Ldstr, "Symbol.match");
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, matchField);

        cctorIL.Emit(OpCodes.Ldstr, "Symbol.matchAll");
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, matchAllField);

        cctorIL.Emit(OpCodes.Ldstr, "Symbol.replace");
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, replaceField);

        cctorIL.Emit(OpCodes.Ldstr, "Symbol.search");
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, searchField);

        cctorIL.Emit(OpCodes.Ldstr, "Symbol.split");
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, splitField);

        cctorIL.Emit(OpCodes.Ret);

        // Equals method: public override bool Equals(object? obj)
        var equalsBuilder = typeBuilder.DefineMethod(
            "Equals",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Boolean,
            [_types.Object]
        );
        var equalsIL = equalsBuilder.GetILGenerator();
        var notSymbol = equalsIL.DefineLabel();
        var returnFalse = equalsIL.DefineLabel();
        // if (obj is not $TSSymbol other) return false
        equalsIL.Emit(OpCodes.Ldarg_1);
        equalsIL.Emit(OpCodes.Isinst, typeBuilder);
        equalsIL.Emit(OpCodes.Brfalse, returnFalse);
        // return this._id == other._id
        equalsIL.Emit(OpCodes.Ldarg_0);
        equalsIL.Emit(OpCodes.Ldfld, idField);
        equalsIL.Emit(OpCodes.Ldarg_1);
        equalsIL.Emit(OpCodes.Castclass, typeBuilder);
        equalsIL.Emit(OpCodes.Ldfld, idField);
        equalsIL.Emit(OpCodes.Ceq);
        equalsIL.Emit(OpCodes.Ret);
        equalsIL.MarkLabel(returnFalse);
        equalsIL.Emit(OpCodes.Ldc_I4_0);
        equalsIL.Emit(OpCodes.Ret);

        // GetHashCode method: public override int GetHashCode()
        var hashCodeBuilder = typeBuilder.DefineMethod(
            "GetHashCode",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Int32,
            Type.EmptyTypes
        );
        var hashCodeIL = hashCodeBuilder.GetILGenerator();
        hashCodeIL.Emit(OpCodes.Ldarg_0);
        hashCodeIL.Emit(OpCodes.Ldfld, idField);
        hashCodeIL.Emit(OpCodes.Ret);

        // ToString method: public override string ToString()
        var toStringBuilder = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var toStringIL = toStringBuilder.GetILGenerator();
        var hasDescription = toStringIL.DefineLabel();
        var doneToString = toStringIL.DefineLabel();
        // if (_description != null)
        toStringIL.Emit(OpCodes.Ldarg_0);
        toStringIL.Emit(OpCodes.Ldfld, descriptionField);
        toStringIL.Emit(OpCodes.Brtrue, hasDescription);
        // return "Symbol()"
        toStringIL.Emit(OpCodes.Ldstr, "Symbol()");
        toStringIL.Emit(OpCodes.Br, doneToString);
        // return $"Symbol({_description})"
        toStringIL.MarkLabel(hasDescription);
        toStringIL.Emit(OpCodes.Ldstr, "Symbol(");
        toStringIL.Emit(OpCodes.Ldarg_0);
        toStringIL.Emit(OpCodes.Ldfld, descriptionField);
        toStringIL.Emit(OpCodes.Ldstr, ")");
        toStringIL.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String, _types.String])!);
        toStringIL.MarkLabel(doneToString);
        toStringIL.Emit(OpCodes.Ret);

        // valueOf method: public object valueOf() => this — Symbol.prototype.valueOf
        // (ECMA-262 §20.4.3.4). Dynamic dispatch resolves it via GetFieldsProperty's
        // reflection fallback (IgnoreCase method lookup), the same path that already
        // serves `description` and `toString` on symbol receivers.
        var valueOfBuilder = typeBuilder.DefineMethod(
            "valueOf",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes
        );
        var valueOfIL = valueOfBuilder.GetILGenerator();
        valueOfIL.Emit(OpCodes.Ldarg_0);
        valueOfIL.Emit(OpCodes.Ret);

        // ============================================================
        // Description property getter: public object get_description()
        // Returns the description string or $Undefined.Instance if null
        // ============================================================
        var descriptionGetter = typeBuilder.DefineMethod(
            "get_description",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.SymbolDescriptionGetter = descriptionGetter;

        var descriptionIL = descriptionGetter.GetILGenerator();
        var hasDesc = descriptionIL.DefineLabel();
        var doneDesc = descriptionIL.DefineLabel();

        // if (_description != null) return _description; else return $Undefined.Instance;
        descriptionIL.Emit(OpCodes.Ldarg_0);
        descriptionIL.Emit(OpCodes.Ldfld, descriptionField);
        descriptionIL.Emit(OpCodes.Dup);
        descriptionIL.Emit(OpCodes.Brtrue, hasDesc);

        // null case: pop null, return undefined
        descriptionIL.Emit(OpCodes.Pop);
        descriptionIL.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        descriptionIL.Emit(OpCodes.Br, doneDesc);

        // not null case: description is on stack
        descriptionIL.MarkLabel(hasDesc);
        descriptionIL.MarkLabel(doneDesc);
        descriptionIL.Emit(OpCodes.Ret);

        // Define the property
        var descriptionProp = typeBuilder.DefineProperty(
            "description",
            PropertyAttributes.None,
            _types.Object,
            null
        );
        descriptionProp.SetGetMethod(descriptionGetter);

        typeBuilder.CreateType();
    }
}
