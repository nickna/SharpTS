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

        // Emit $Buffer class for standalone buffer support
        // NOTE: Must come before $Hash and $Hmac since they return Buffer
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSBuffer
        EmitTSBufferClass(moduleBuilder, runtime);

        // Emit $Hash class for standalone crypto hash support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSHash
        EmitTSHashClass(moduleBuilder, runtime);

        // Emit $Hmac class for standalone crypto HMAC support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSHmac
        EmitTSHmacClass(moduleBuilder, runtime);

        // Emit $Cipher class for standalone crypto cipher support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSCipher
        EmitTSCipherClass(moduleBuilder, runtime);

        // Emit $Decipher class for standalone crypto decipher support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSDecipher
        EmitTSDecipherClass(moduleBuilder, runtime);

        // Emit $Sign class for standalone crypto signing support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSSign
        EmitTSSignClass(moduleBuilder, runtime);

        // Emit $Verify class for standalone crypto verification support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSVerify
        EmitTSVerifyClass(moduleBuilder, runtime);

        // Emit $VirtualTimer class for virtual timer support (single-threaded semantics)
        // Must come after TSFunction (uses TSFunctionType)
        // Must come BEFORE TSTimeoutClass (TSTimeout references VirtualTimer)
        EmitVirtualTimerClass(moduleBuilder, runtime);

        // Emit $TSTimeout class for timer support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSTimeout
        EmitTSTimeoutClass(moduleBuilder, runtime);

        // Emit $TimeoutClosure class for setTimeout callback execution
        // Must come after TSFunction (uses TSFunctionType, TSFunctionInvoke)
        EmitTimeoutClosureClass(moduleBuilder, runtime);

        // Emit $IntervalClosure class for setInterval callback execution
        // Must come after TSFunction (uses TSFunctionType, TSFunctionInvoke)
        EmitIntervalClosureClass(moduleBuilder, runtime);

        // Emit $BoundTSFunction class for bound functions
        // Must come after TSFunction (uses TSFunctionType, TSFunctionInvokeWithThis)
        EmitBoundTSFunctionClass(moduleBuilder, runtime);

        // Emit $EventEmitter class for standalone event emitter support
        // NOTE: Must come after BoundTSFunction (uses TSFunctionType, BoundTSFunctionType)
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSEventEmitter
        EmitTSEventEmitterClass(moduleBuilder, runtime);

        // Emit stream classes for standalone stream support
        // NOTE: Must come after EventEmitter (stream types extend $EventEmitter)
        // Order matters due to inheritance and cross-references:
        // - Writable is standalone
        // - Readable's Pipe() method needs to reference Duplex (for piping to Duplex streams)
        // - Duplex extends Readable
        // - Transform extends Duplex
        // - PassThrough extends Transform
        //
        // Two-phase approach to resolve circular reference:
        // Phase 1: Define types, fields, and most methods (no CreateType)
        // Phase 2: Add methods that need cross-references, then CreateType
        EmitTSWritableClass(moduleBuilder, runtime);
        EmitTSReadableTypeDefinition(moduleBuilder, runtime);  // Phase 1: type, fields, most methods
        EmitTSDuplexTypeDefinition(moduleBuilder, runtime);    // Phase 1: type, fields, all methods
        EmitTSReadableMethods(runtime);                        // Phase 2: Pipe method + CreateType
        EmitTSDuplexFinalize(runtime);                         // Phase 2: CreateType
        EmitTSTransformClass(moduleBuilder, runtime);
        EmitTSPassThroughClass(moduleBuilder, runtime);

        // Emit function method wrapper classes for bind/call/apply
        // Must come after TSFunction and BoundTSFunction
        EmitFunctionBindWrapperClass(moduleBuilder, runtime);
        EmitFunctionCallWrapperClass(moduleBuilder, runtime);
        EmitFunctionApplyWrapperClass(moduleBuilder, runtime);

        // Emit util module types for standalone execution
        // Must come after $Buffer (TextEncoder returns $Buffer)
        EmitTSDeprecatedFunctionClass(moduleBuilder, runtime);
        EmitTSPromisifiedFunctionClass(moduleBuilder, runtime);
        EmitTSTextEncoderClass(moduleBuilder, runtime);
        EmitTSTextDecoderClass(moduleBuilder, runtime);
        EmitTSTextDecoderDecodeMethodClass(moduleBuilder, runtime);

        // Emit $StringDecoder class for string_decoder module
        // Must come after $Buffer (StringDecoder works with Buffer)
        EmitTSStringDecoderClass(moduleBuilder, runtime);

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
        // Cached name and length for functions where reflection doesn't work (e.g., MethodBuilder tokens)
        var cachedNameField = typeBuilder.DefineField("_cachedName", _types.String, FieldAttributes.Private);
        var cachedLengthField = typeBuilder.DefineField("_cachedLength", _types.Int32, FieldAttributes.Private);

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
        // this._cachedLength = -1 (sentinel for "not cached")
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldc_I4_M1);
        ctorIL.Emit(OpCodes.Stfld, cachedLengthField);
        // this._cachedName = null (will use reflection)
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldnull);
        ctorIL.Emit(OpCodes.Stfld, cachedNameField);
        ctorIL.Emit(OpCodes.Ret);

        // Alternative constructor with cached name/length: public $TSFunction(object target, MethodInfo method, string name, int length)
        // Use this constructor when the MethodInfo might not support GetParameters() (e.g., MethodBuilder tokens in persisted assemblies)
        var ctorWithCacheBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.MethodInfo, _types.String, _types.Int32]
        );
        runtime.TSFunctionCtorWithCache = ctorWithCacheBuilder;

        var ctorCacheIL = ctorWithCacheBuilder.GetILGenerator();
        // Call base constructor
        ctorCacheIL.Emit(OpCodes.Ldarg_0);
        ctorCacheIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        // this._target = target
        ctorCacheIL.Emit(OpCodes.Ldarg_0);
        ctorCacheIL.Emit(OpCodes.Ldarg_1);
        ctorCacheIL.Emit(OpCodes.Stfld, targetField);
        // this._method = method
        ctorCacheIL.Emit(OpCodes.Ldarg_0);
        ctorCacheIL.Emit(OpCodes.Ldarg_2);
        ctorCacheIL.Emit(OpCodes.Stfld, methodField);
        // this._cachedName = name
        ctorCacheIL.Emit(OpCodes.Ldarg_0);
        ctorCacheIL.Emit(OpCodes.Ldarg_3);
        ctorCacheIL.Emit(OpCodes.Stfld, cachedNameField);
        // this._cachedLength = length
        ctorCacheIL.Emit(OpCodes.Ldarg_0);
        ctorCacheIL.Emit(OpCodes.Ldarg, 4);  // 4th argument (0-indexed: 0=this, 1=target, 2=method, 3=name, 4=length)
        ctorCacheIL.Emit(OpCodes.Stfld, cachedLengthField);
        ctorCacheIL.Emit(OpCodes.Ret);

        // Helper method: private static object[] AdjustArgs(MethodInfo method, object[] args)
        var adjustArgsMethod = EmitTSFunctionAdjustArgsHelper(typeBuilder, runtime);

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
        var effectiveArgsLocal = invokeIL.DeclareLocal(_types.ObjectArray);
        var invokeTargetLocal = invokeIL.DeclareLocal(_types.Object);

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

        // Adjust args for rest parameters and padding/trimming
        // adjustedArgs = AdjustArgs(_method, effectiveArgs)
        var adjustedArgsLocal = invokeIL.DeclareLocal(_types.ObjectArray);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, methodField);
        invokeIL.Emit(OpCodes.Ldloc, effectiveArgsLocal);
        invokeIL.Emit(OpCodes.Call, adjustArgsMethod);
        invokeIL.Emit(OpCodes.Stloc, adjustedArgsLocal);

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

        // Length property: returns the number of required parameters (excluding rest, optional, and those with defaults)
        // public int get_Length()
        var lengthGetterBuilder = typeBuilder.DefineMethod(
            "get_Length",
            MethodAttributes.Public,
            _types.Int32,
            Type.EmptyTypes
        );
        runtime.TSFunctionLengthGetter = lengthGetterBuilder;

        var lengthIL = lengthGetterBuilder.GetILGenerator();
        var paramsLocalLength = lengthIL.DeclareLocal(_types.MakeArrayType(_types.ParameterInfo));
        var countLocal = lengthIL.DeclareLocal(_types.Int32);
        var indexLocalLength = lengthIL.DeclareLocal(_types.Int32);
        var paramLocalLength = lengthIL.DeclareLocal(_types.ParameterInfo);
        var lengthLoopStart = lengthIL.DefineLabel();
        var lengthLoopEnd = lengthIL.DefineLabel();
        var incrementCount = lengthIL.DefineLabel();
        var skipParam = lengthIL.DefineLabel();
        var returnZero = lengthIL.DefineLabel();
        var useCachedLength = lengthIL.DefineLabel();
        var computeLength = lengthIL.DefineLabel();

        // Check if _cachedLength >= 0 (cached value available)
        lengthIL.Emit(OpCodes.Ldarg_0);
        lengthIL.Emit(OpCodes.Ldfld, cachedLengthField);
        lengthIL.Emit(OpCodes.Ldc_I4_0);
        lengthIL.Emit(OpCodes.Bge, useCachedLength);
        lengthIL.Emit(OpCodes.Br, computeLength);

        // Return cached length
        lengthIL.MarkLabel(useCachedLength);
        lengthIL.Emit(OpCodes.Ldarg_0);
        lengthIL.Emit(OpCodes.Ldfld, cachedLengthField);
        lengthIL.Emit(OpCodes.Ret);

        // Compute length via reflection
        lengthIL.MarkLabel(computeLength);

        // Check if _method is null - if so, return 0
        lengthIL.Emit(OpCodes.Ldarg_0);
        lengthIL.Emit(OpCodes.Ldfld, methodField);
        lengthIL.Emit(OpCodes.Brfalse, returnZero);

        // params = _method.GetParameters()
        lengthIL.Emit(OpCodes.Ldarg_0);
        lengthIL.Emit(OpCodes.Ldfld, methodField);
        lengthIL.Emit(OpCodes.Callvirt, _types.MethodInfo.GetMethod("GetParameters")!);
        lengthIL.Emit(OpCodes.Stloc, paramsLocalLength);

        // Check if params is null - if so, return 0
        lengthIL.Emit(OpCodes.Ldloc, paramsLocalLength);
        lengthIL.Emit(OpCodes.Brfalse, returnZero);

        // count = 0
        lengthIL.Emit(OpCodes.Ldc_I4_0);
        lengthIL.Emit(OpCodes.Stloc, countLocal);

        // index = 0
        lengthIL.Emit(OpCodes.Ldc_I4_0);
        lengthIL.Emit(OpCodes.Stloc, indexLocalLength);

        // Loop through parameters
        lengthIL.MarkLabel(lengthLoopStart);
        // if (index >= params.Length) goto end
        lengthIL.Emit(OpCodes.Ldloc, indexLocalLength);
        lengthIL.Emit(OpCodes.Ldloc, paramsLocalLength);
        lengthIL.Emit(OpCodes.Ldlen);
        lengthIL.Emit(OpCodes.Conv_I4);
        lengthIL.Emit(OpCodes.Bge, lengthLoopEnd);

        // param = params[index]
        lengthIL.Emit(OpCodes.Ldloc, paramsLocalLength);
        lengthIL.Emit(OpCodes.Ldloc, indexLocalLength);
        lengthIL.Emit(OpCodes.Ldelem_Ref);
        lengthIL.Emit(OpCodes.Stloc, paramLocalLength);

        // Skip if param.IsOptional (has default value or is optional)
        lengthIL.Emit(OpCodes.Ldloc, paramLocalLength);
        lengthIL.Emit(OpCodes.Callvirt, _types.ParameterInfo.GetProperty("IsOptional")!.GetGetMethod()!);
        lengthIL.Emit(OpCodes.Brtrue, skipParam);

        // Skip if param type is List<object> (rest parameter)
        lengthIL.Emit(OpCodes.Ldloc, paramLocalLength);
        lengthIL.Emit(OpCodes.Callvirt, _types.ParameterInfo.GetProperty("ParameterType")!.GetGetMethod()!);
        lengthIL.Emit(OpCodes.Ldtoken, _types.ListOfObject);
        lengthIL.Emit(OpCodes.Call, _types.Type.GetMethod("GetTypeFromHandle")!);
        lengthIL.Emit(OpCodes.Call, _types.Type.GetMethod("op_Equality", [_types.Type, _types.Type])!);
        lengthIL.Emit(OpCodes.Brtrue, skipParam);

        // Skip if param name starts with "__" (internal parameters like __this)
        lengthIL.Emit(OpCodes.Ldloc, paramLocalLength);
        lengthIL.Emit(OpCodes.Callvirt, _types.ParameterInfo.GetProperty("Name")!.GetGetMethod()!);
        lengthIL.Emit(OpCodes.Ldstr, "__");
        lengthIL.Emit(OpCodes.Callvirt, _types.String.GetMethod("StartsWith", [_types.String])!);
        lengthIL.Emit(OpCodes.Brtrue, skipParam);

        // count++
        lengthIL.Emit(OpCodes.Ldloc, countLocal);
        lengthIL.Emit(OpCodes.Ldc_I4_1);
        lengthIL.Emit(OpCodes.Add);
        lengthIL.Emit(OpCodes.Stloc, countLocal);

        lengthIL.MarkLabel(skipParam);
        // index++
        lengthIL.Emit(OpCodes.Ldloc, indexLocalLength);
        lengthIL.Emit(OpCodes.Ldc_I4_1);
        lengthIL.Emit(OpCodes.Add);
        lengthIL.Emit(OpCodes.Stloc, indexLocalLength);
        lengthIL.Emit(OpCodes.Br, lengthLoopStart);

        lengthIL.MarkLabel(lengthLoopEnd);
        lengthIL.Emit(OpCodes.Ldloc, countLocal);
        lengthIL.Emit(OpCodes.Ret);

        // Return 0 if _method or params was null
        lengthIL.MarkLabel(returnZero);
        lengthIL.Emit(OpCodes.Ldc_I4_0);
        lengthIL.Emit(OpCodes.Ret);

        // Define Length property
        var lengthProperty = typeBuilder.DefineProperty(
            "Length",
            PropertyAttributes.None,
            _types.Int32,
            Type.EmptyTypes
        );
        lengthProperty.SetGetMethod(lengthGetterBuilder);

        // Name property: returns the method name
        // public string get_Name()
        var nameGetterBuilder = typeBuilder.DefineMethod(
            "get_Name",
            MethodAttributes.Public,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSFunctionNameGetter = nameGetterBuilder;

        var nameIL = nameGetterBuilder.GetILGenerator();
        var nameReturnEmpty = nameIL.DefineLabel();
        var useCachedName = nameIL.DefineLabel();
        var computeName = nameIL.DefineLabel();

        // Check if _cachedName is not null (cached value available)
        nameIL.Emit(OpCodes.Ldarg_0);
        nameIL.Emit(OpCodes.Ldfld, cachedNameField);
        nameIL.Emit(OpCodes.Brtrue, useCachedName);
        nameIL.Emit(OpCodes.Br, computeName);

        // Return cached name
        nameIL.MarkLabel(useCachedName);
        nameIL.Emit(OpCodes.Ldarg_0);
        nameIL.Emit(OpCodes.Ldfld, cachedNameField);
        nameIL.Emit(OpCodes.Ret);

        // Compute name via reflection
        nameIL.MarkLabel(computeName);

        // Check if _method is null - if so, return ""
        nameIL.Emit(OpCodes.Ldarg_0);
        nameIL.Emit(OpCodes.Ldfld, methodField);
        nameIL.Emit(OpCodes.Brfalse, nameReturnEmpty);

        // return _method.Name
        nameIL.Emit(OpCodes.Ldarg_0);
        nameIL.Emit(OpCodes.Ldfld, methodField);
        nameIL.Emit(OpCodes.Callvirt, _types.MethodInfo.GetProperty("Name")!.GetGetMethod()!);
        nameIL.Emit(OpCodes.Ret);

        // Return "" if _method was null
        nameIL.MarkLabel(nameReturnEmpty);
        nameIL.Emit(OpCodes.Ldstr, "");
        nameIL.Emit(OpCodes.Ret);

        // Define Name property
        var nameProperty = typeBuilder.DefineProperty(
            "Name",
            PropertyAttributes.None,
            _types.String,
            Type.EmptyTypes
        );
        nameProperty.SetGetMethod(nameGetterBuilder);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits a private static helper method on $TSFunction to adjust arguments for rest parameters and padding/trimming.
    /// </summary>
    private MethodBuilder EmitTSFunctionAdjustArgsHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // private static object[] AdjustArgs(MethodInfo method, object[] args)
        var method = typeBuilder.DefineMethod(
            "AdjustArgs",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.ObjectArray,
            [_types.MethodInfo, _types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // Local variables
        var paramsLocal = il.DeclareLocal(_types.MakeArrayType(_types.ParameterInfo));
        var paramCountLocal = il.DeclareLocal(_types.Int32);
        var argsLengthLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.ObjectArray);
        var lastParamTypeLocal = il.DeclareLocal(_types.Type);
        var restListLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var copyCountLocal = il.DeclareLocal(_types.Int32);

        // params = method.GetParameters()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.MethodInfo.GetMethod("GetParameters")!);
        il.Emit(OpCodes.Stloc, paramsLocal);

        // paramCount = params.Length
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, paramCountLocal);

        // argsLength = args.Length
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, argsLengthLocal);

        // Labels
        var notRestParam = il.DefineLabel();
        var doReturn = il.DefineLabel();
        var exactMatch = il.DefineLabel();
        var needsPadding = il.DefineLabel();
        var needsTrimming = il.DefineLabel();
        var restLoopStart = il.DefineLabel();
        var restLoopEnd = il.DefineLabel();

        // Check if paramCount > 0
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, notRestParam);

        // lastParamType = params[paramCount - 1].ParameterType
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.ParameterInfo.GetProperty("ParameterType")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lastParamTypeLocal);

        // if (lastParamType != typeof(List<object>)) goto notRestParam
        il.Emit(OpCodes.Ldloc, lastParamTypeLocal);
        il.Emit(OpCodes.Ldtoken, _types.ListOfObject);
        il.Emit(OpCodes.Call, _types.Type.GetMethod("GetTypeFromHandle")!);
        il.Emit(OpCodes.Call, _types.Type.GetMethod("op_Inequality", [_types.Type, _types.Type])!);
        il.Emit(OpCodes.Brtrue, notRestParam);

        // === REST PARAMETER HANDLING ===
        // result = new object[paramCount]
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, resultLocal);

        // regularParamCount = paramCount - 1
        // copyCount = min(argsLength, regularParamCount)
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _types.Math.GetMethod("Min", [_types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Stloc, copyCountLocal);

        // if (copyCount > 0) Array.Copy(args, result, copyCount)
        var skipCopy = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, copyCountLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, skipCopy);
        il.Emit(OpCodes.Ldarg_1); // source
        il.Emit(OpCodes.Ldloc, resultLocal); // dest
        il.Emit(OpCodes.Ldloc, copyCountLocal); // length
        il.Emit(OpCodes.Call, _types.ArrayType.GetMethod("Copy", [_types.ArrayType, _types.ArrayType, _types.Int32])!);
        il.MarkLabel(skipCopy);

        // restList = new List<object>()
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stloc, restListLocal);

        // for (i = paramCount - 1; i < argsLength; i++) restList.Add(args[i])
        // i = paramCount - 1 (start of rest args)
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(restLoopStart);
        // if (i >= argsLength) goto restLoopEnd
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        il.Emit(OpCodes.Bge, restLoopEnd);

        // restList.Add(args[i])
        il.Emit(OpCodes.Ldloc, restListLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add")!);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, restLoopStart);

        il.MarkLabel(restLoopEnd);

        // result[paramCount - 1] = restList
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldloc, restListLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // === NOT A REST PARAMETER - STANDARD PADDING/TRIMMING ===
        il.MarkLabel(notRestParam);

        // if (argsLength == paramCount) return args
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Bne_Un, needsPadding);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(needsPadding);
        // if (argsLength >= paramCount) goto needsTrimming
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Bge, needsTrimming);

        // Pad with nulls: result = new object[paramCount]; Array.Copy(args, result, argsLength)
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        il.Emit(OpCodes.Call, _types.ArrayType.GetMethod("Copy", [_types.ArrayType, _types.ArrayType, _types.Int32])!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // Trim: result = new object[paramCount]; Array.Copy(args, result, paramCount)
        il.MarkLabel(needsTrimming);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Call, _types.ArrayType.GetMethod("Copy", [_types.ArrayType, _types.ArrayType, _types.Int32])!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        return method;
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

        // Global registry fields for Symbol.for() / Symbol.keyFor()
        var registryLockField = typeBuilder.DefineField(
            "_registryLock",
            _types.Object,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        var globalRegistryType = typeof(Dictionary<,>).MakeGenericType(_types.String, typeBuilder);
        var globalRegistryField = typeBuilder.DefineField(
            "_globalRegistry",
            globalRegistryType,
            FieldAttributes.Private | FieldAttributes.Static
        );
        var reverseRegistryType = typeof(Dictionary<,>).MakeGenericType(typeBuilder, _types.String);
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
        var tryGetValueMethod = TypeBuilder.GetMethod(globalRegistryType, typeof(Dictionary<,>).GetMethod("TryGetValue")!);
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
        var setItemMethod = TypeBuilder.GetMethod(globalRegistryType, typeof(Dictionary<,>).GetMethod("set_Item")!);
        forIL.Emit(OpCodes.Callvirt, setItemMethod);

        // _reverseRegistry[symbol] = key;
        forIL.Emit(OpCodes.Ldsfld, reverseRegistryField);
        forIL.Emit(OpCodes.Ldloc_0);  // symbol
        forIL.Emit(OpCodes.Ldarg_0);  // key
        var reverseSetItemMethod = TypeBuilder.GetMethod(reverseRegistryType, typeof(Dictionary<,>).GetMethod("set_Item")!);
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
        var reverseTryGetValueMethod = TypeBuilder.GetMethod(reverseRegistryType, typeof(Dictionary<,>).GetMethod("TryGetValue")!);
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
        var globalRegistryCtor = TypeBuilder.GetConstructor(globalRegistryType, typeof(Dictionary<,>).GetConstructor(Type.EmptyTypes)!);
        cctorIL.Emit(OpCodes.Newobj, globalRegistryCtor);
        cctorIL.Emit(OpCodes.Stsfld, globalRegistryField);

        // Initialize reverse registry: _reverseRegistry = new Dictionary<$TSSymbol, string>()
        var reverseRegistryCtor = TypeBuilder.GetConstructor(reverseRegistryType, typeof(Dictionary<,>).GetConstructor(Type.EmptyTypes)!);
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

        // Static field for console group indentation level (needed early for ConsoleLog)
        var consoleGroupLevelField = typeBuilder.DefineField(
            "_consoleGroupLevel",
            _types.Int32,
            FieldAttributes.Private | FieldAttributes.Static
        );
        runtime.ConsoleGroupLevelField = consoleGroupLevelField;

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

        // Initialize perf_hooks timing fields (must be called after fields are defined)
        // Note: Fields will be defined by EmitPerfHooksMethods, so we defer this initialization
        // The initialization is done inline in EmitPerfHooksMethods instead

        cctorIL.Emit(OpCodes.Ret);

        // Emit all methods - these are now in partial class files
        // Core utilities
        EmitStringify(typeBuilder, runtime);
        // Format specifier helpers (must be emitted before ConsoleLog/ConsoleLogMultiple which call them)
        EmitHasFormatSpecifiers(typeBuilder, runtime);
        EmitFormatSingleArg(typeBuilder, runtime);
        EmitFormatAsInteger(typeBuilder, runtime);
        EmitFormatAsFloat(typeBuilder, runtime);
        EmitFormatAsJson(typeBuilder, runtime);
        EmitFormatConsoleArgs(typeBuilder, runtime);
        // GetConsoleIndent must be emitted before ConsoleLog/ConsoleLogMultiple which call it
        EmitGetConsoleIndent(typeBuilder, runtime);
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
        EmitGetFunctionMethod(typeBuilder, runtime);  // For bind/call/apply on functions
        EmitToPascalCase(typeBuilder, runtime);  // Must be emitted before GetFieldsProperty/SetFieldsProperty
        EmitGetFieldsProperty(typeBuilder, runtime);
        EmitGetListProperty(typeBuilder, runtime);
        EmitSetFieldsProperty(typeBuilder, runtime);
        EmitSetFieldsPropertyStrict(typeBuilder, runtime);
        // InvokeValue/InvokeMethodValue must come before Promise methods (needed by InvokeCallback)
        EmitInvokeValue(typeBuilder, runtime);
        EmitInvokeMethodValue(typeBuilder, runtime);
        // Promise methods must come before GetProperty (which needs PromiseThen for typeof p.then)
        EmitPromiseMethods(typeBuilder, runtime);
        EmitGetProperty(typeBuilder, runtime);
        EmitSetProperty(typeBuilder, runtime);
        EmitSetPropertyStrict(typeBuilder, runtime);
        EmitDeleteProperty(typeBuilder, runtime);
        EmitDeletePropertyStrict(typeBuilder, runtime);
        EmitMergeIntoObject(typeBuilder, runtime);
        EmitMergeIntoTSObject(typeBuilder, runtime);
        // Symbol support helpers - must come before iterator methods which depend on GetSymbolDict
        EmitGetSymbolDict(typeBuilder, runtime, symbolStorageField);
        EmitIsSymbol(typeBuilder, runtime);
        // HasIn operator depends on IsSymbol and GetSymbolDict
        EmitHasIn(typeBuilder, runtime);
        // TypedArray helpers must come BEFORE GetIndex/SetIndex since they use these helpers
        EmitTypedArrayDetectionHelpers(typeBuilder, runtime);
        EmitGetIndex(typeBuilder, runtime);
        EmitSetIndex(typeBuilder, runtime);
        EmitSetIndexStrict(typeBuilder, runtime);
        EmitDeleteIndex(typeBuilder, runtime);
        EmitDeleteIndexStrict(typeBuilder, runtime);
        EmitStrictModeHelpers(typeBuilder, runtime);
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
        EmitArrayFindLast(typeBuilder, runtime);
        EmitArrayFindLastIndex(typeBuilder, runtime);
        EmitArraySome(typeBuilder, runtime);
        EmitArrayEvery(typeBuilder, runtime);
        EmitArrayReduce(typeBuilder, runtime);
        EmitArrayIncludes(typeBuilder, runtime);
        EmitArrayIndexOf(typeBuilder, runtime);
        EmitArrayJoin(typeBuilder, runtime);
        EmitArrayConcat(typeBuilder, runtime);
        EmitArrayReverse(typeBuilder, runtime);
        EmitArrayFlatHelper(typeBuilder, runtime); // Must be before EmitArrayFlat
        EmitArrayFlat(typeBuilder, runtime);
        EmitArrayFlatMap(typeBuilder, runtime);
        EmitArrayFrom(typeBuilder, runtime);
        EmitArrayOf(typeBuilder, runtime);
        EmitArraySort(typeBuilder, runtime);
        EmitArrayToSorted(typeBuilder, runtime);
        EmitToIntegerOrInfinityHelper(typeBuilder, runtime); // Must be before EmitArraySplice/EmitArrayWith
        EmitArraySplice(typeBuilder, runtime);
        EmitArrayToSpliced(typeBuilder, runtime);
        EmitArrayToReversed(typeBuilder, runtime);
        EmitArrayWith(typeBuilder, runtime);
        EmitArrayAt(typeBuilder, runtime);
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
        EmitThrowUndefinedVariable(typeBuilder, runtime);
        EmitRandom(typeBuilder, runtime, randomField);
        EmitGetEnumMemberName(typeBuilder, runtime);
        EmitConcatTemplate(typeBuilder, runtime);
        EmitInvokeTaggedTemplate(typeBuilder, runtime);
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
        // Promise methods moved earlier (before GetProperty, which needs PromiseThen for typeof p.then)
        // Number methods
        EmitNumberMethods(typeBuilder, runtime);
        // Virtual timer infrastructure (must come before DateMethods which calls ProcessPendingTimers)
        EmitTimerQueueInfrastructure(typeBuilder, runtime);
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
        // Built-in module methods (path, fs, os, dns)
        EmitPathModuleMethods(typeBuilder, runtime);
        EmitFsModuleMethods(typeBuilder, runtime);
        EmitOsModuleMethods(typeBuilder, runtime);
        EmitDnsModuleMethods(typeBuilder, runtime);
        // Emit wrapper methods for named imports
        EmitFsModuleMethodWrappers(typeBuilder, runtime);
        EmitPathModulePropertyWrappers(typeBuilder, runtime);
        // Querystring module methods
        EmitQuerystringMethods(typeBuilder, runtime);
        // Assert module methods
        EmitAssertMethods(typeBuilder, runtime);
        // URL module methods
        EmitUrlMethods(typeBuilder, runtime);
        // HTTP module methods (fetch, http.createServer, etc.) - must be before globalThis
        EmitHttpModuleMethods(typeBuilder, runtime);
        // globalThis methods (ES2020) - must be after HTTP for fetch reference
        EmitGlobalThisMethods(typeBuilder, runtime);
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
        // Timer methods (setTimeout, clearTimeout, setInterval, clearInterval)
        EmitSetTimeoutMethod(typeBuilder, runtime);
        EmitClearTimeoutMethod(typeBuilder, runtime);
        EmitSetIntervalMethod(typeBuilder, runtime);
        EmitClearIntervalMethod(typeBuilder, runtime);
        // Timer module wrappers for namespace imports (import * as timers from 'timers')
        EmitTimerModuleWrappers(typeBuilder, runtime);
        // Process global methods (env, argv, nextTick) - must be after timer methods for nextTick
        EmitProcessMethods(typeBuilder, runtime);
        // Zlib module methods
        EmitZlibMethods(typeBuilder, runtime);
        // DNS module methods
        EmitDnsModuleMethods(typeBuilder, runtime);
        // perf_hooks module methods
        EmitPerfHooksMethods(typeBuilder, runtime);
        // string_decoder module constructor helper
        EmitStringDecoderGetConstructor(typeBuilder, runtime);

        // Worker Threads support (SharedArrayBuffer, TypedArrays, Atomics, MessagePort, Worker)
        EmitWorkerHelpers(typeBuilder, runtime);

        typeBuilder.CreateType();
    }
}

