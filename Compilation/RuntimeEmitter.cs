using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the runtime support types into the generated assembly.
/// This makes compiled DLLs standalone without requiring SharpTS.dll.
/// </summary>
public partial class RuntimeEmitter
{
    private readonly TypeProvider _types;

    public RuntimeEmitter(TypeProvider types)
    {
        _types = types;
    }

    public EmittedRuntime EmitAll(ModuleBuilder moduleBuilder)
    {
        var runtime = new EmittedRuntime();

        // Emit $Undefined singleton class first (other methods need this type)
        EmitUndefinedClass(moduleBuilder, runtime);

        // Emit IUnionType marker interface first (union types need to implement this)
        EmitIUnionTypeInterface(moduleBuilder, runtime);

        // Emit TSFunction class first (other methods depend on it)
        EmitTSFunctionClass(moduleBuilder, runtime);

        // Emit TSNamespace class for namespace support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSNamespace
        EmitTSNamespaceClass(moduleBuilder, runtime);

        // Emit TSSymbol class for symbol support
        EmitTSSymbolClass(moduleBuilder, runtime);

        // Emit ReferenceEqualityComparer for Map/Set key equality
        EmitReferenceEqualityComparerClass(moduleBuilder, runtime);

        // Emit $IGenerator interface for generator return/throw support
        EmitGeneratorInterface(moduleBuilder, runtime);

        // Emit $IAsyncGenerator interface for async generator return/throw support
        EmitAsyncGeneratorInterface(moduleBuilder, runtime);

        // NOTE: $IteratorWrapper is emitted later, after iterator methods are defined

        // Emit $TSDate class for standalone Date support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSDate
        EmitTSDateClass(moduleBuilder, runtime);

        // Emit $Error class hierarchy for standalone error support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSError and subclasses
        EmitTSErrorClasses(moduleBuilder, runtime);

        // Emit $Promise class for standalone Promise support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSPromise
        EmitTSPromiseClass(moduleBuilder, runtime);

        // Emit $Array class for standalone array support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSArray
        EmitTSArrayClass(moduleBuilder, runtime);

        // Emit $Object class for standalone object support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSObject
        EmitTSObjectClass(moduleBuilder, runtime);

        // Emit $RegExp class for standalone regex support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSRegExp
        EmitTSRegExpClass(moduleBuilder, runtime);

        // Emit $AssertionError class for standalone assert module support
        // NOTE: Must stay in sync with AssertionError in AssertModuleInterpreter.cs
        EmitTSAssertionErrorClass(moduleBuilder, runtime);

        // Emit $Hash class for standalone crypto hash support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSHash
        EmitTSHashClass(moduleBuilder, runtime);

        // Emit $TSTimeout class for timer support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSTimeout
        EmitTSTimeoutClass(moduleBuilder, runtime);

        // Emit $TimeoutClosure class for setTimeout callback execution
        // Must come after TSFunction (uses TSFunctionType, TSFunctionInvoke)
        EmitTimeoutClosureClass(moduleBuilder, runtime);

        // Emit $Runtime class with all helper methods
        EmitRuntimeClass(moduleBuilder, runtime);

        return runtime;
    }

    /// <summary>
    /// Emits the $Undefined singleton class.
    /// This is used instead of referencing SharpTS.Runtime.Types.SharpTSUndefined
    /// so that compiled assemblies are standalone.
    /// </summary>
    private void EmitUndefinedClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $Undefined
        var typeBuilder = moduleBuilder.DefineType(
            "$Undefined",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.UndefinedType = typeBuilder;

        // Static field: public static readonly $Undefined Instance = new $Undefined();
        var instanceField = typeBuilder.DefineField(
            "Instance",
            typeBuilder,
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.UndefinedInstance = instanceField;

        // Private constructor to ensure singleton
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Private,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ret);

        // Static constructor to initialize Instance
        var cctor = typeBuilder.DefineTypeInitializer();
        var cctorIL = cctor.GetILGenerator();
        cctorIL.Emit(OpCodes.Newobj, ctor);
        cctorIL.Emit(OpCodes.Stsfld, instanceField);
        cctorIL.Emit(OpCodes.Ret);

        // Override ToString() to return "undefined"
        var toStringMethod = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var toStringIL = toStringMethod.GetILGenerator();
        toStringIL.Emit(OpCodes.Ldstr, "undefined");
        toStringIL.Emit(OpCodes.Ret);

        // Create the type immediately so other emitters can reference it
        var createdType = typeBuilder.CreateType()!;
        runtime.UndefinedType = createdType;
        runtime.UndefinedInstance = createdType.GetField("Instance")!;
    }

    /// <summary>
    /// Emits the $IUnionType marker interface for fast union type detection.
    /// All generated union types implement this interface.
    /// </summary>
    private void EmitIUnionTypeInterface(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define interface: public interface $IUnionType
        var typeBuilder = moduleBuilder.DefineType(
            "$IUnionType",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            null
        );

        // Define Value property getter: object? Value { get; }
        var valueGetter = typeBuilder.DefineMethod(
            "get_Value",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual |
            MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Object,
            Type.EmptyTypes
        );

        var valueProp = typeBuilder.DefineProperty(
            "Value",
            PropertyAttributes.None,
            _types.Object,
            null
        );
        valueProp.SetGetMethod(valueGetter);

        // Create and store the interface type
        runtime.IUnionTypeInterface = typeBuilder.CreateType()!;
        runtime.IUnionTypeValueGetter = runtime.IUnionTypeInterface.GetProperty("Value")!.GetGetMethod()!;
    }

    private void EmitTSFunctionClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $TSFunction
        var typeBuilder = moduleBuilder.DefineType(
            "$TSFunction",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSFunctionType = typeBuilder;

        // Fields
        var targetField = typeBuilder.DefineField("_target", _types.Object, FieldAttributes.Private);
        var methodField = typeBuilder.DefineField("_method", _types.MethodInfo, FieldAttributes.Private);

        // Static cache for "this" fields: ConcurrentDictionary<Type, FieldInfo>
        // used to avoid reflection overhead in BindThis
        var fieldCacheType = _types.MakeGenericType(_types.ConcurrentDictionaryOpen, _types.Type, _types.FieldInfo);
        var fieldCacheField = typeBuilder.DefineField("_thisFieldCache", fieldCacheType, FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly);

        // Static Constructor
        var cctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var cctorIL = cctorBuilder.GetILGenerator();
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(fieldCacheType));
        cctorIL.Emit(OpCodes.Stsfld, fieldCacheField);
        cctorIL.Emit(OpCodes.Ret);

        // Constructor: public $TSFunction(object target, MethodInfo method)
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.MethodInfo]
        );
        runtime.TSFunctionCtor = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        // Call base constructor
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        // this._target = target
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, targetField);
        // this._method = method
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, methodField);
        ctorIL.Emit(OpCodes.Ret);

        // Helper method: private static void ConvertArgsForUnionTypes(MethodInfo method, object[] args)
        var convertArgsMethod = EmitTSFunctionConvertArgsHelper(typeBuilder, runtime);

        // Invoke method: public object Invoke(object[] args)
        var invokeBuilder = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.TSFunctionInvoke = invokeBuilder;

        var invokeIL = invokeBuilder.GetILGenerator();

        // Local variables
        var paramCountLocal = invokeIL.DeclareLocal(_types.Int32);
        var effectiveArgsLocal = invokeIL.DeclareLocal(_types.ObjectArray);
        var invokeTargetLocal = invokeIL.DeclareLocal(_types.Object);

        // Get parameter count: int paramCount = _method.GetParameters().Length
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, methodField);
        invokeIL.Emit(OpCodes.Callvirt, _types.MethodInfo.GetMethod("GetParameters")!);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);
        invokeIL.Emit(OpCodes.Stloc, paramCountLocal);

        // Check if this is a static method with a bound target
        // if (_method.IsStatic && _target != null)
        var notStaticWithTarget = invokeIL.DefineLabel();
        var afterArgPrep = invokeIL.DefineLabel();

        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, methodField);
        invokeIL.Emit(OpCodes.Callvirt, _types.MethodInfo.GetProperty("IsStatic")!.GetGetMethod()!);
        invokeIL.Emit(OpCodes.Brfalse, notStaticWithTarget);

        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, targetField);
        invokeIL.Emit(OpCodes.Brfalse, notStaticWithTarget);

        // Static method with bound target: prepend target to args
        // effectiveArgs = new object[args.Length + 1];
        // effectiveArgs[0] = _target;
        // Array.Copy(args, 0, effectiveArgs, 1, args.Length);
        // invokeTarget = null;
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);
        invokeIL.Emit(OpCodes.Ldc_I4_1);
        invokeIL.Emit(OpCodes.Add);
        invokeIL.Emit(OpCodes.Newarr, _types.Object);
        invokeIL.Emit(OpCodes.Stloc, effectiveArgsLocal);

        invokeIL.Emit(OpCodes.Ldloc, effectiveArgsLocal);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, targetField);
        invokeIL.Emit(OpCodes.Stelem_Ref);

        invokeIL.Emit(OpCodes.Ldarg_1);  // source
        invokeIL.Emit(OpCodes.Ldc_I4_0); // sourceIndex
        invokeIL.Emit(OpCodes.Ldloc, effectiveArgsLocal); // dest
        invokeIL.Emit(OpCodes.Ldc_I4_1); // destIndex
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);  // length
        invokeIL.Emit(OpCodes.Call, _types.ArrayType.GetMethod("Copy", [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);

        invokeIL.Emit(OpCodes.Ldnull);
        invokeIL.Emit(OpCodes.Stloc, invokeTargetLocal);
        invokeIL.Emit(OpCodes.Br, afterArgPrep);

        // Not a static with target: use args directly, target is _target
        invokeIL.MarkLabel(notStaticWithTarget);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Stloc, effectiveArgsLocal);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, targetField);
        invokeIL.Emit(OpCodes.Stloc, invokeTargetLocal);

        invokeIL.MarkLabel(afterArgPrep);

        // Now handle padding/trimming based on paramCount
        var argsLengthLocal = invokeIL.DeclareLocal(_types.Int32);
        var adjustedArgsLocal = invokeIL.DeclareLocal(_types.ObjectArray);

        invokeIL.Emit(OpCodes.Ldloc, effectiveArgsLocal);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);
        invokeIL.Emit(OpCodes.Stloc, argsLengthLocal);

        var exactMatch = invokeIL.DefineLabel();
        var doInvoke = invokeIL.DefineLabel();

        // If effectiveArgs.Length == paramCount, use effectiveArgs directly
        invokeIL.Emit(OpCodes.Ldloc, argsLengthLocal);
        invokeIL.Emit(OpCodes.Ldloc, paramCountLocal);
        invokeIL.Emit(OpCodes.Beq, exactMatch);

        // If effectiveArgs.Length < paramCount, pad with nulls
        var tooManyArgs = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Ldloc, argsLengthLocal);
        invokeIL.Emit(OpCodes.Ldloc, paramCountLocal);
        invokeIL.Emit(OpCodes.Bge, tooManyArgs);

        // Pad with nulls
        invokeIL.Emit(OpCodes.Ldloc, paramCountLocal);
        invokeIL.Emit(OpCodes.Newarr, _types.Object);
        invokeIL.Emit(OpCodes.Stloc, adjustedArgsLocal);
        invokeIL.Emit(OpCodes.Ldloc, effectiveArgsLocal); // source
        invokeIL.Emit(OpCodes.Ldloc, adjustedArgsLocal); // dest
        invokeIL.Emit(OpCodes.Ldloc, argsLengthLocal); // length
        invokeIL.Emit(OpCodes.Call, _types.ArrayType.GetMethod("Copy", [_types.ArrayType, _types.ArrayType, _types.Int32])!);
        invokeIL.Emit(OpCodes.Br, doInvoke);

        // Too many args: trim
        invokeIL.MarkLabel(tooManyArgs);
        invokeIL.Emit(OpCodes.Ldloc, paramCountLocal);
        invokeIL.Emit(OpCodes.Newarr, _types.Object);
        invokeIL.Emit(OpCodes.Stloc, adjustedArgsLocal);
        invokeIL.Emit(OpCodes.Ldloc, effectiveArgsLocal); // source
        invokeIL.Emit(OpCodes.Ldloc, adjustedArgsLocal); // dest
        invokeIL.Emit(OpCodes.Ldloc, paramCountLocal); // length
        invokeIL.Emit(OpCodes.Call, _types.ArrayType.GetMethod("Copy", [_types.ArrayType, _types.ArrayType, _types.Int32])!);
        invokeIL.Emit(OpCodes.Br, doInvoke);

        // Exact match - use effectiveArgs directly
        invokeIL.MarkLabel(exactMatch);
        invokeIL.Emit(OpCodes.Ldloc, effectiveArgsLocal);
        invokeIL.Emit(OpCodes.Stloc, adjustedArgsLocal);

        // Do invoke
        invokeIL.MarkLabel(doInvoke);

        // Convert args for union types before invoking
        // ConvertArgsForUnionTypes(this._method, adjustedArgs)
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, methodField);
        invokeIL.Emit(OpCodes.Ldloc, adjustedArgsLocal);
        invokeIL.Emit(OpCodes.Call, convertArgsMethod);

        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, methodField);
        invokeIL.Emit(OpCodes.Ldloc, invokeTargetLocal);
        invokeIL.Emit(OpCodes.Ldloc, adjustedArgsLocal);
        invokeIL.Emit(OpCodes.Callvirt, _types.MethodInfo.GetMethod("Invoke", [_types.Object, _types.ObjectArray])!);
        invokeIL.Emit(OpCodes.Ret);

        // InvokeWithThis method: public object InvokeWithThis(object thisArg, object[] args)
        // This checks if the first parameter is named "__this" and prepends thisArg if so
        var invokeWithThisBuilder = typeBuilder.DefineMethod(
            "InvokeWithThis",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.ObjectArray]
        );
        runtime.TSFunctionInvokeWithThis = invokeWithThisBuilder;

        var iwt = invokeWithThisBuilder.GetILGenerator();

        // Local variables
        var paramsLocal = iwt.DeclareLocal(_types.MakeArrayType(_types.ParameterInfo));
        var paramCountLocalIWT = iwt.DeclareLocal(_types.Int32);
        var expectsThisLocal = iwt.DeclareLocal(_types.Boolean);
        var effectiveArgsIWT = iwt.DeclareLocal(_types.ObjectArray);

        // params = _method.GetParameters()
        iwt.Emit(OpCodes.Ldarg_0);
        iwt.Emit(OpCodes.Ldfld, methodField);
        iwt.Emit(OpCodes.Callvirt, _types.MethodInfo.GetMethod("GetParameters")!);
        iwt.Emit(OpCodes.Stloc, paramsLocal);

        // paramCount = params.Length
        iwt.Emit(OpCodes.Ldloc, paramsLocal);
        iwt.Emit(OpCodes.Ldlen);
        iwt.Emit(OpCodes.Conv_I4);
        iwt.Emit(OpCodes.Stloc, paramCountLocalIWT);

        // expectsThis = paramCount > 0 && params[0].Name == "__this"
        var checkDoneLabel = iwt.DefineLabel();

        iwt.Emit(OpCodes.Ldc_I4_0);
        iwt.Emit(OpCodes.Stloc, expectsThisLocal);

        iwt.Emit(OpCodes.Ldloc, paramCountLocalIWT);
        iwt.Emit(OpCodes.Ldc_I4_0);
        iwt.Emit(OpCodes.Ble, checkDoneLabel);

        iwt.Emit(OpCodes.Ldloc, paramsLocal);
        iwt.Emit(OpCodes.Ldc_I4_0);
        iwt.Emit(OpCodes.Ldelem_Ref);
        iwt.Emit(OpCodes.Callvirt, _types.ParameterInfo.GetProperty("Name")!.GetGetMethod()!);
        iwt.Emit(OpCodes.Ldstr, "__this");
        iwt.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        iwt.Emit(OpCodes.Stloc, expectsThisLocal);

        iwt.MarkLabel(checkDoneLabel);

        // if (!expectsThis) { return Invoke(args); }
        var expectsThisLabel = iwt.DefineLabel();
        iwt.Emit(OpCodes.Ldloc, expectsThisLocal);
        iwt.Emit(OpCodes.Brtrue, expectsThisLabel);

        // Call Invoke(args) and return
        iwt.Emit(OpCodes.Ldarg_0);
        iwt.Emit(OpCodes.Ldarg_2);
        iwt.Emit(OpCodes.Callvirt, invokeBuilder);
        iwt.Emit(OpCodes.Ret);

        // expectsThis is true - prepend thisArg to args
        iwt.MarkLabel(expectsThisLabel);

        // effectiveArgs = new object[args.Length + 1]
        iwt.Emit(OpCodes.Ldarg_2);
        iwt.Emit(OpCodes.Ldlen);
        iwt.Emit(OpCodes.Conv_I4);
        iwt.Emit(OpCodes.Ldc_I4_1);
        iwt.Emit(OpCodes.Add);
        iwt.Emit(OpCodes.Newarr, _types.Object);
        iwt.Emit(OpCodes.Stloc, effectiveArgsIWT);

        // effectiveArgs[0] = thisArg
        iwt.Emit(OpCodes.Ldloc, effectiveArgsIWT);
        iwt.Emit(OpCodes.Ldc_I4_0);
        iwt.Emit(OpCodes.Ldarg_1);
        iwt.Emit(OpCodes.Stelem_Ref);

        // Array.Copy(args, 0, effectiveArgs, 1, args.Length)
        iwt.Emit(OpCodes.Ldarg_2);  // source
        iwt.Emit(OpCodes.Ldc_I4_0); // sourceIndex
        iwt.Emit(OpCodes.Ldloc, effectiveArgsIWT); // dest
        iwt.Emit(OpCodes.Ldc_I4_1); // destIndex
        iwt.Emit(OpCodes.Ldarg_2);
        iwt.Emit(OpCodes.Ldlen);
        iwt.Emit(OpCodes.Conv_I4);  // length
        iwt.Emit(OpCodes.Call, _types.ArrayType.GetMethod("Copy", [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);

        // Call Invoke(effectiveArgs) and return
        iwt.Emit(OpCodes.Ldarg_0);
        iwt.Emit(OpCodes.Ldloc, effectiveArgsIWT);
        iwt.Emit(OpCodes.Callvirt, invokeBuilder);
        iwt.Emit(OpCodes.Ret);

        // ToString method
        var toStringBuilder = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var toStringIL = toStringBuilder.GetILGenerator();
        toStringIL.Emit(OpCodes.Ldstr, "[Function]");
        toStringIL.Emit(OpCodes.Ret);

        // BindThis method: public void BindThis(object thisValue)
        // Sets the 'this' field in the display class to the given value
        var bindThisBuilder = typeBuilder.DefineMethod(
            "BindThis",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]
        );
        runtime.TSFunctionBindThis = bindThisBuilder;

        var bindThisIL = bindThisBuilder.GetILGenerator();
        var noTargetLabel = bindThisIL.DefineLabel();
        var endLabel = bindThisIL.DefineLabel();
        var thisFieldLocal = bindThisIL.DeclareLocal(_types.FieldInfo);
        var targetTypeLocal = bindThisIL.DeclareLocal(_types.Type);

        // if (_target == null) return;
        bindThisIL.Emit(OpCodes.Ldarg_0);
        bindThisIL.Emit(OpCodes.Ldfld, targetField);
        bindThisIL.Emit(OpCodes.Brfalse, noTargetLabel);

        // targetType = _target.GetType();
        bindThisIL.Emit(OpCodes.Ldarg_0);
        bindThisIL.Emit(OpCodes.Ldfld, targetField);
        bindThisIL.Emit(OpCodes.Callvirt, _types.Object.GetMethod("GetType")!);
        bindThisIL.Emit(OpCodes.Stloc, targetTypeLocal);

        // Try get from cache
        // if (!_thisFieldCache.TryGetValue(targetType, out thisField))
        bindThisIL.Emit(OpCodes.Ldsfld, fieldCacheField);
        bindThisIL.Emit(OpCodes.Ldloc, targetTypeLocal);
        bindThisIL.Emit(OpCodes.Ldloca, thisFieldLocal);
        bindThisIL.Emit(OpCodes.Callvirt, fieldCacheType.GetMethod("TryGetValue", [_types.Type, _types.FieldInfo.MakeByRefType()])!);
        var cacheHitLabel = bindThisIL.DefineLabel();
        bindThisIL.Emit(OpCodes.Brtrue, cacheHitLabel);

        // Cache miss: lookup field
        // thisField = targetType.GetField("this", BindingFlags.Public | BindingFlags.Instance);
        bindThisIL.Emit(OpCodes.Ldloc, targetTypeLocal);
        bindThisIL.Emit(OpCodes.Ldstr, "this");
        bindThisIL.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Public | BindingFlags.Instance));
        bindThisIL.Emit(OpCodes.Callvirt, _types.Type.GetMethod("GetField", [_types.String, _types.BindingFlags])!);
        bindThisIL.Emit(OpCodes.Stloc, thisFieldLocal);

        // Store in cache (even if null, but ConcurrentDictionary doesn't allow null values if we used that, but here we can just skip if null)
        // Actually, if null, we shouldn't cache null if we use TryGetValue. 
        // Let's simplify: if field is found, cache it. If not found, we don't cache (or cache a dummy? no need to overcomplicate).
        
        var fieldNullLabel = bindThisIL.DefineLabel();
        bindThisIL.Emit(OpCodes.Ldloc, thisFieldLocal);
        bindThisIL.Emit(OpCodes.Brfalse, fieldNullLabel);

        // Cache it
        bindThisIL.Emit(OpCodes.Ldsfld, fieldCacheField);
        bindThisIL.Emit(OpCodes.Ldloc, targetTypeLocal);
        bindThisIL.Emit(OpCodes.Ldloc, thisFieldLocal);
        bindThisIL.Emit(OpCodes.Callvirt, fieldCacheType.GetMethod("TryAdd", [_types.Type, _types.FieldInfo])!);
        bindThisIL.Emit(OpCodes.Pop); // discard bool result

        bindThisIL.MarkLabel(fieldNullLabel);
        bindThisIL.MarkLabel(cacheHitLabel);

        // if (thisField == null) return;
        bindThisIL.Emit(OpCodes.Ldloc, thisFieldLocal);
        bindThisIL.Emit(OpCodes.Brfalse, noTargetLabel);

        // thisField.SetValue(_target, thisValue);
        bindThisIL.Emit(OpCodes.Ldloc, thisFieldLocal);
        bindThisIL.Emit(OpCodes.Ldarg_0);
        bindThisIL.Emit(OpCodes.Ldfld, targetField);
        bindThisIL.Emit(OpCodes.Ldarg_1);
        bindThisIL.Emit(OpCodes.Callvirt, _types.FieldInfo.GetMethod("SetValue", [_types.Object, _types.Object])!);
        bindThisIL.Emit(OpCodes.Br, endLabel);

        bindThisIL.MarkLabel(noTargetLabel);
        bindThisIL.MarkLabel(endLabel);
        bindThisIL.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits a private static helper method on $TSFunction to convert arguments for union type parameters.
    /// </summary>
    private MethodBuilder EmitTSFunctionConvertArgsHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // private static void ConvertArgsForUnionTypes(MethodInfo method, object[] args)
        var method = typeBuilder.DefineMethod(
            "ConvertArgsForUnionTypes",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.MethodInfo, _types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // var parameters = method.GetParameters();
        var paramsLocal = il.DeclareLocal(_types.MakeArrayType(_types.ParameterInfo));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.MethodInfo.GetMethod("GetParameters")!);
        il.Emit(OpCodes.Stloc, paramsLocal);

        // for (int i = 0; i < args.Length && i < parameters.Length; i++)
        var indexLocal = il.DeclareLocal(_types.Int32);
        var paramTypeLocal = il.DeclareLocal(_types.Type);
        var argLocal = il.DeclareLocal(_types.Object);
        var argTypeLocal = il.DeclareLocal(_types.Type);
        var implicitOpLocal = il.DeclareLocal(_types.MethodInfo);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var continueLoop = il.DefineLabel();

        // i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        // i < args.Length
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // i < parameters.Length
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // paramType = parameters[i].ParameterType
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.ParameterInfo.GetProperty("ParameterType")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, paramTypeLocal);

        // arg = args[i]
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, argLocal);

        // if (!typeof($IUnionType).IsAssignableFrom(paramType)) continue
        // Load the $IUnionType interface type
        il.Emit(OpCodes.Ldtoken, runtime.IUnionTypeInterface);
        il.Emit(OpCodes.Call, _types.Type.GetMethod("GetTypeFromHandle")!);
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.Type.GetMethod("IsAssignableFrom", [_types.Type])!);
        il.Emit(OpCodes.Brfalse, continueLoop);

        // if (arg == null) continue
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Brfalse, continueLoop);

        // argType = arg.GetType()
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, argTypeLocal);

        // if (argType == paramType) continue
        il.Emit(OpCodes.Ldloc, argTypeLocal);
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, continueLoop);

        // implicitOp = paramType.GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static, null, new Type[] { argType }, null)
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Ldstr, "op_Implicit");
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Public | BindingFlags.Static));
        il.Emit(OpCodes.Ldnull);  // binder
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Type);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, argTypeLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldnull);  // modifiers
        il.Emit(OpCodes.Callvirt, _types.Type.GetMethod("GetMethod", [_types.String, _types.BindingFlags, _types.Binder, _types.MakeArrayType(_types.Type), _types.MakeArrayType(_types.ParameterModifier)])!);
        il.Emit(OpCodes.Stloc, implicitOpLocal);

        // if (implicitOp == null) continue
        il.Emit(OpCodes.Ldloc, implicitOpLocal);
        il.Emit(OpCodes.Brfalse, continueLoop);

        // args[i] = implicitOp.Invoke(null, new object[] { arg })
        il.Emit(OpCodes.Ldarg_1);  // args
        il.Emit(OpCodes.Ldloc, indexLocal);  // i
        il.Emit(OpCodes.Ldloc, implicitOpLocal);  // implicitOp
        il.Emit(OpCodes.Ldnull);  // target (null for static)
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.MethodInfo.GetMethod("Invoke", [_types.Object, _types.ObjectArray])!);
        il.Emit(OpCodes.Stelem_Ref);

        il.MarkLabel(continueLoop);
        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private void EmitTSNamespaceClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $TSNamespace
        // Mirrors SharpTSNamespace but is emitted into the compiled assembly
        var typeBuilder = moduleBuilder.DefineType(
            "$TSNamespace",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSNamespaceType = typeBuilder;

        // Field: private readonly Dictionary<string, object?> _members
        var membersField = typeBuilder.DefineField("_members", _types.DictionaryStringObject, FieldAttributes.Private);

        // Field: public string Name
        var nameField = typeBuilder.DefineField("_name", _types.String, FieldAttributes.Private);

        // Constructor: public $TSNamespace(string name)
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        runtime.TSNamespaceCtor = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        // Call base constructor
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        // _name = name
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, nameField);
        // _members = new Dictionary<string, object?>()
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        ctorIL.Emit(OpCodes.Stfld, membersField);
        ctorIL.Emit(OpCodes.Ret);

        // Get method: public object? Get(string name) => _members.TryGetValue(name, out var value) ? value : null;
        var getBuilder = typeBuilder.DefineMethod(
            "Get",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        runtime.TSNamespaceGet = getBuilder;

        var getIL = getBuilder.GetILGenerator();
        var valueLocal = getIL.DeclareLocal(_types.Object);
        var foundLabel = getIL.DefineLabel();
        var notFoundLabel = getIL.DefineLabel();

        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, membersField);
        getIL.Emit(OpCodes.Ldarg_1);
        getIL.Emit(OpCodes.Ldloca, valueLocal);
        getIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        getIL.Emit(OpCodes.Brtrue, foundLabel);
        getIL.Emit(OpCodes.Ldnull);
        getIL.Emit(OpCodes.Ret);
        getIL.MarkLabel(foundLabel);
        getIL.Emit(OpCodes.Ldloc, valueLocal);
        getIL.Emit(OpCodes.Ret);

        // Set method: public void Set(string name, object? value) => _members[name] = value;
        var setBuilder = typeBuilder.DefineMethod(
            "Set",
            MethodAttributes.Public,
            _types.Void,
            [_types.String, _types.Object]
        );
        runtime.TSNamespaceSet = setBuilder;

        var setIL = setBuilder.GetILGenerator();
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldfld, membersField);
        setIL.Emit(OpCodes.Ldarg_1);
        setIL.Emit(OpCodes.Ldarg_2);
        setIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        setIL.Emit(OpCodes.Ret);

        // ToString method: public override string ToString() => $"[namespace {Name}]"
        var toStringBuilder = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var toStringIL = toStringBuilder.GetILGenerator();
        toStringIL.Emit(OpCodes.Ldstr, "[namespace ");
        toStringIL.Emit(OpCodes.Ldarg_0);
        toStringIL.Emit(OpCodes.Ldfld, nameField);
        toStringIL.Emit(OpCodes.Ldstr, "]");
        toStringIL.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        toStringIL.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

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

        // Static constructor to initialize well-known symbols
        var cctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var cctorIL = cctorBuilder.GetILGenerator();

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

        typeBuilder.CreateType();
    }

    private void EmitReferenceEqualityComparerClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $ReferenceEqualityComparer : IEqualityComparer<object>
        // This implements JavaScript-style equality for Map/Set keys:
        // - Primitives (string, double, bool): value equality
        // - Objects: reference equality
        var typeBuilder = moduleBuilder.DefineType(
            "$ReferenceEqualityComparer",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [_types.IEqualityComparerOfObject]
        );
        runtime.ReferenceEqualityComparerType = typeBuilder;

        // Static Instance field (singleton)
        var instanceField = typeBuilder.DefineField(
            "Instance",
            typeBuilder,
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.ReferenceEqualityComparerInstance = instanceField;

        // Private constructor
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Private,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var ctorIL = ctorBuilder.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ret);

        // Static constructor to initialize Instance
        var cctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var cctorIL = cctorBuilder.GetILGenerator();
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, instanceField);
        cctorIL.Emit(OpCodes.Ret);

        // Equals method: public bool Equals(object? x, object? y)
        EmitReferenceEqualityComparerEquals(typeBuilder, runtime);

        // GetHashCode method: public int GetHashCode(object obj)
        EmitReferenceEqualityComparerGetHashCode(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    private void EmitReferenceEqualityComparerEquals(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Equals",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
            _types.Boolean,
            [_types.Object, _types.Object]
        );

        var il = method.GetILGenerator();

        // Labels for control flow
        var returnTrueLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();
        var checkPrimitivesLabel = il.DefineLabel();
        var checkBigIntLabel = il.DefineLabel();
        var checkSymbolLabel = il.DefineLabel();
        var useReferenceEqualityLabel = il.DefineLabel();
        var useValueEqualityLabel = il.DefineLabel();

        // if (x is null && y is null) return true;
        il.Emit(OpCodes.Ldarg_1);  // x
        il.Emit(OpCodes.Brtrue, checkPrimitivesLabel);  // x not null, check primitives

        // x is null - check if y is also null
        il.Emit(OpCodes.Ldarg_2);  // y
        il.Emit(OpCodes.Brfalse, returnTrueLabel);  // both null, return true

        // x is null but y is not null
        il.Emit(OpCodes.Br, returnFalseLabel);

        // x is not null - check if y is null
        il.MarkLabel(checkPrimitivesLabel);
        il.Emit(OpCodes.Ldarg_2);  // y
        il.Emit(OpCodes.Brfalse, returnFalseLabel);  // x not null but y is null

        // Both are not null - check primitives
        // if (x is string || x is double || x is bool) return object.Equals(x, y);
        il.Emit(OpCodes.Ldarg_1);  // x
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, useValueEqualityLabel);

        il.Emit(OpCodes.Ldarg_1);  // x
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, useValueEqualityLabel);

        il.Emit(OpCodes.Ldarg_1);  // x
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, useValueEqualityLabel);

        // Check for BigInteger value equality
        il.MarkLabel(checkBigIntLabel);
        il.Emit(OpCodes.Ldarg_1);  // x
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, checkSymbolLabel);

        // x is BigInteger - check if y is also BigInteger
        il.Emit(OpCodes.Ldarg_2);  // y
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // Both are BigInteger - compare values
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        il.Emit(OpCodes.Call, _types.BigInteger.GetMethod("op_Equality", [_types.BigInteger, _types.BigInteger])!);
        il.Emit(OpCodes.Ret);

        // Check for Symbol - use reference equality
        il.MarkLabel(checkSymbolLabel);
        il.Emit(OpCodes.Ldarg_1);  // x
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, useReferenceEqualityLabel);

        il.Emit(OpCodes.Ldarg_2);  // y
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, useReferenceEqualityLabel);

        // Default: use reference equality for all other objects
        il.MarkLabel(useReferenceEqualityLabel);
        il.Emit(OpCodes.Ldarg_1);  // x
        il.Emit(OpCodes.Ldarg_2);  // y
        il.Emit(OpCodes.Ceq);  // Reference equality check
        il.Emit(OpCodes.Ret);

        // Use value equality (Object.Equals) - for primitives
        il.MarkLabel(useValueEqualityLabel);
        il.Emit(OpCodes.Ldarg_1);  // x
        il.Emit(OpCodes.Ldarg_2);  // y
        il.Emit(OpCodes.Call, _types.Object.GetMethod("Equals", [_types.Object, _types.Object])!);
        il.Emit(OpCodes.Ret);

        // Return true
        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // Return false
        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitReferenceEqualityComparerGetHashCode(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetHashCode",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
            _types.Int32,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        // Labels
        var notNullLabel = il.DefineLabel();
        var notStringLabel = il.DefineLabel();
        var notDoubleLabel = il.DefineLabel();
        var notBoolLabel = il.DefineLabel();
        var notBigIntLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // if (obj == null) return 0;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNullLabel);

        // if (obj is string s) return s.GetHashCode();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStringLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.String, "GetHashCode"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notStringLabel);

        // if (obj is double d) return d.GetHashCode();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        var doubleLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, doubleLocal);
        il.Emit(OpCodes.Ldloca, doubleLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Double, "GetHashCode"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notDoubleLabel);

        // if (obj is bool b) return b.GetHashCode();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notBoolLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        var boolLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Stloc, boolLocal);
        il.Emit(OpCodes.Ldloca, boolLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Boolean, "GetHashCode"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notBoolLabel);

        // if (obj is BigInteger bigInt) return bigInt.GetHashCode();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, notBigIntLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        var bigIntLocal = il.DeclareLocal(_types.BigInteger);
        il.Emit(OpCodes.Stloc, bigIntLocal);
        il.Emit(OpCodes.Ldloca, bigIntLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.BigInteger, "GetHashCode"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notBigIntLabel);

        // default: return RuntimeHelpers.GetHashCode(obj);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.RuntimeHelpers, "GetHashCode", [_types.Object]));
        il.Emit(OpCodes.Ret);
    }

    private void EmitRuntimeClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public static class $Runtime
        var typeBuilder = moduleBuilder.DefineType(
            "$Runtime",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.RuntimeType = typeBuilder;

        // Static field for Random
        var randomField = typeBuilder.DefineField("_random", _types.Random, FieldAttributes.Private | FieldAttributes.Static);

        // Static field for symbol storage: ConditionalWeakTable<object, Dictionary<object, object?>>
        var symbolDictType = _types.DictionaryObjectObject;
        var symbolStorageType = _types.MakeGenericType(_types.ConditionalWeakTableOpen, _types.Object, symbolDictType);
        var symbolStorageField = typeBuilder.DefineField(
            "_symbolStorage",
            symbolStorageType,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.SymbolStorageField = symbolStorageField;

        // Static fields for Object.freeze/seal tracking: ConditionalWeakTable<object, object>
        var frozenObjectsField = typeBuilder.DefineField(
            "_frozenObjects",
            _types.ConditionalWeakTable,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.FrozenObjectsField = frozenObjectsField;
        var sealedObjectsField = typeBuilder.DefineField(
            "_sealedObjects",
            _types.ConditionalWeakTable,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.SealedObjectsField = sealedObjectsField;

        // Static constructor to initialize Random and symbol storage
        var cctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var cctorIL = cctorBuilder.GetILGenerator();

        // Initialize _random = new Random()
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.Random));
        cctorIL.Emit(OpCodes.Stsfld, randomField);

        // Initialize _symbolStorage = new ConditionalWeakTable<object, Dictionary<object, object?>>()
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(symbolStorageType));
        cctorIL.Emit(OpCodes.Stsfld, symbolStorageField);

        // Initialize _frozenObjects = new ConditionalWeakTable<object, object>()
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ConditionalWeakTable));
        cctorIL.Emit(OpCodes.Stsfld, frozenObjectsField);

        // Initialize _sealedObjects = new ConditionalWeakTable<object, object>()
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ConditionalWeakTable));
        cctorIL.Emit(OpCodes.Stsfld, sealedObjectsField);

        cctorIL.Emit(OpCodes.Ret);

        // Emit all methods - these are now in partial class files
        // Core utilities
        EmitStringify(typeBuilder, runtime);
        EmitConsoleLog(typeBuilder, runtime);
        EmitConsoleLogMultiple(typeBuilder, runtime);
        EmitToNumber(typeBuilder, runtime);
        EmitIsTruthy(typeBuilder, runtime);
        EmitTypeOf(typeBuilder, runtime);
        EmitInstanceOf(typeBuilder, runtime);
        EmitAdd(typeBuilder, runtime);
        EmitEquals(typeBuilder, runtime);
        // Object methods - must come BEFORE iterator methods since GetProperty, InvokeMethodValue are needed
        EmitCreateObject(typeBuilder, runtime);
        EmitGetArrayMethod(typeBuilder, runtime);
        EmitToPascalCase(typeBuilder, runtime);  // Must be emitted before GetFieldsProperty/SetFieldsProperty
        EmitGetFieldsProperty(typeBuilder, runtime);
        EmitSetFieldsProperty(typeBuilder, runtime);
        EmitSetFieldsPropertyStrict(typeBuilder, runtime);
        EmitGetProperty(typeBuilder, runtime);
        EmitSetProperty(typeBuilder, runtime);
        EmitSetPropertyStrict(typeBuilder, runtime);
        EmitMergeIntoObject(typeBuilder, runtime);
        // Symbol support helpers - must come before iterator methods which depend on GetSymbolDict
        EmitGetSymbolDict(typeBuilder, runtime, symbolStorageField);
        EmitIsSymbol(typeBuilder, runtime);
        EmitGetIndex(typeBuilder, runtime);
        EmitSetIndex(typeBuilder, runtime);
        EmitSetIndexStrict(typeBuilder, runtime);
        EmitInvokeValue(typeBuilder, runtime);
        EmitInvokeMethodValue(typeBuilder, runtime);
        // Basic iterator protocol methods - must come AFTER object methods (need GetProperty, InvokeMethodValue)
        EmitIteratorMethodsBasic(typeBuilder, runtime);
        // Emit $IteratorWrapper AFTER basic iterator methods (needs InvokeIteratorNext etc.)
        // but BEFORE IterateToList (which needs IteratorWrapperCtor)
        EmitIteratorWrapperType(moduleBuilder, runtime);
        // Advanced iterator methods (IterateToList) - needs IteratorWrapperCtor
        EmitIteratorMethodsAdvanced(typeBuilder, runtime);
        // Arrays - must come AFTER iterator methods since ConcatArrays/ExpandCallArgs use IterateToList
        EmitCreateArray(typeBuilder, runtime);
        EmitGetLength(typeBuilder, runtime);
        EmitGetElement(typeBuilder, runtime);
        EmitGetKeys(typeBuilder, runtime);
        EmitGetValues(typeBuilder, runtime);
        EmitGetEntries(typeBuilder, runtime);
        EmitObjectFromEntries(typeBuilder, runtime);
        EmitObjectHasOwn(typeBuilder, runtime);
        EmitObjectAssign(typeBuilder, runtime);
        EmitObjectFreeze(typeBuilder, runtime, frozenObjectsField, sealedObjectsField);
        EmitObjectSeal(typeBuilder, runtime, sealedObjectsField);
        EmitObjectIsFrozen(typeBuilder, runtime, frozenObjectsField);
        EmitObjectIsSealed(typeBuilder, runtime, sealedObjectsField);
        EmitIsArray(typeBuilder, runtime);
        EmitSpreadArray(typeBuilder, runtime);
        EmitConcatArrays(typeBuilder, runtime);
        EmitExpandCallArgs(typeBuilder, runtime);
        EmitArrayPop(typeBuilder, runtime);
        EmitArrayShift(typeBuilder, runtime);
        EmitArrayUnshift(typeBuilder, runtime);
        EmitArraySlice(typeBuilder, runtime);
        // Array callback methods must come after InvokeValue and IsTruthy
        EmitArrayMap(typeBuilder, runtime);
        EmitArrayFilter(typeBuilder, runtime);
        EmitArrayForEach(typeBuilder, runtime);
        EmitArrayPush(typeBuilder, runtime);
        EmitArrayFind(typeBuilder, runtime);
        EmitArrayFindIndex(typeBuilder, runtime);
        EmitArraySome(typeBuilder, runtime);
        EmitArrayEvery(typeBuilder, runtime);
        EmitArrayReduce(typeBuilder, runtime);
        EmitArrayIncludes(typeBuilder, runtime);
        EmitArrayIndexOf(typeBuilder, runtime);
        EmitArrayJoin(typeBuilder, runtime);
        EmitArrayConcat(typeBuilder, runtime);
        EmitArrayReverse(typeBuilder, runtime);
        EmitArrayFrom(typeBuilder, runtime);
        EmitArrayOf(typeBuilder, runtime);
        // String methods
        EmitStringCharAt(typeBuilder, runtime);
        EmitStringSubstring(typeBuilder, runtime);
        EmitStringIndexOf(typeBuilder, runtime);
        EmitStringReplace(typeBuilder, runtime);
        EmitStringSplit(typeBuilder, runtime);
        EmitStringIncludes(typeBuilder, runtime);
        EmitStringStartsWith(typeBuilder, runtime);
        EmitStringEndsWith(typeBuilder, runtime);
        EmitStringSlice(typeBuilder, runtime);
        EmitStringRepeat(typeBuilder, runtime);
        EmitStringPadStart(typeBuilder, runtime);
        EmitStringPadEnd(typeBuilder, runtime);
        EmitStringCharCodeAt(typeBuilder, runtime);
        EmitStringConcat(typeBuilder, runtime);
        EmitStringLastIndexOf(typeBuilder, runtime);
        EmitStringReplaceAll(typeBuilder, runtime);
        EmitStringAt(typeBuilder, runtime);
        // Object utilities
        EmitGetSuperMethod(typeBuilder, runtime);
        EmitCreateException(typeBuilder, runtime);
        EmitWrapException(typeBuilder, runtime);
        EmitRandom(typeBuilder, runtime, randomField);
        EmitGetEnumMemberName(typeBuilder, runtime);
        EmitConcatTemplate(typeBuilder, runtime);
        EmitObjectRest(typeBuilder, runtime);
        // JSON methods
        EmitJsonParse(typeBuilder, runtime);
        EmitJsonParseWithReviver(typeBuilder, runtime);
        EmitJsonStringify(typeBuilder, runtime);
        EmitJsonStringifyFull(typeBuilder, runtime);
        // BigInt methods
        EmitCreateBigInt(typeBuilder, runtime);
        EmitBigIntArithmetic(typeBuilder, runtime);
        EmitBigIntComparison(typeBuilder, runtime);
        EmitBigIntBitwise(typeBuilder, runtime);
        // Promise methods
        EmitPromiseMethods(typeBuilder, runtime);
        // Number methods
        EmitNumberMethods(typeBuilder, runtime);
        // Date methods
        EmitDateMethods(typeBuilder, runtime);
        // RegExp methods
        EmitRegExpMethods(typeBuilder, runtime);
        // Error methods
        EmitErrorMethods(typeBuilder, runtime);
        // Map methods
        EmitMapMethods(typeBuilder, runtime);
        // Set methods
        EmitSetMethods(typeBuilder, runtime);
        // WeakMap methods
        EmitWeakMapMethods(typeBuilder, runtime);
        // WeakSet methods
        EmitWeakSetMethods(typeBuilder, runtime);
        // Dynamic import methods
        EmitDynamicImportMethods(typeBuilder, runtime);
        // Async generator await continuation helper
        EmitAsyncGeneratorAwaitContinueMethods(typeBuilder, moduleBuilder, runtime);
        // NodeError conversion helpers (must be before fs methods which use them)
        EmitNodeErrorHelpers(typeBuilder, runtime);
        // Built-in module methods (path, fs, os)
        EmitPathModuleMethods(typeBuilder, runtime);
        EmitFsModuleMethods(typeBuilder, runtime);
        EmitOsModuleMethods(typeBuilder, runtime);
        // Emit wrapper methods for named imports
        EmitFsModuleMethodWrappers(typeBuilder, runtime);
        EmitPathModulePropertyWrappers(typeBuilder, runtime);
        // Process global methods (env, argv)
        EmitProcessMethods(typeBuilder, runtime);
        // globalThis methods (ES2020)
        EmitGlobalThisMethods(typeBuilder, runtime);
        // Querystring module methods
        EmitQuerystringMethods(typeBuilder, runtime);
        // Assert module methods
        EmitAssertMethods(typeBuilder, runtime);
        // URL module methods
        EmitUrlMethods(typeBuilder, runtime);
        // Console extensions (error, warn, clear, time, timeEnd, timeLog)
        EmitConsoleExtensions(typeBuilder, runtime);
        // Crypto module methods
        EmitCryptoMethods(typeBuilder, runtime);
        // Util module methods
        EmitUtilMethods(typeBuilder, runtime);
        // Readline module methods
        EmitReadlineMethods(typeBuilder, runtime);
        // Child process module methods
        EmitChildProcessMethods(typeBuilder, runtime);
        // Timer methods (setTimeout, clearTimeout)
        EmitSetTimeoutMethod(typeBuilder, runtime);
        EmitClearTimeoutMethod(typeBuilder, runtime);

        typeBuilder.CreateType();
    }
}

