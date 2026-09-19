using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the runtime support types into the generated assembly.
/// This makes compiled DLLs standalone without requiring SharpTS.dll.
/// </summary>
public partial class RuntimeEmitter
{
    private readonly TypeProvider _types;
    private readonly bool _emitHosted;

    /// <summary>
    /// Feature gating set — populated by <see cref="EmitAll(ModuleBuilder, RuntimeFeatureSet)"/>
    /// and consulted by individual <c>Emit*</c> methods to skip emission of helper types
    /// (and any <c>$Runtime</c> methods that depend on those helper types) the program
    /// doesn't need. Defaults to "emit everything" when an older overload is used.
    /// </summary>
    private RuntimeFeatureSet _features = RuntimeFeatureSet.EmitEverything();

    public RuntimeEmitter(TypeProvider types, bool emitHosted = false)
    {
        _types = types;
        _emitHosted = emitHosted;
    }

    /// <summary>
    /// Backward-compatible overload: emit every helper type unconditionally.
    /// New callers should pass a <see cref="RuntimeFeatureSet"/> derived from
    /// <see cref="RuntimeFeatureDetector"/> so unused machinery can be skipped.
    /// </summary>
    public EmittedRuntime EmitAll(ModuleBuilder moduleBuilder)
        => EmitAll(moduleBuilder, RuntimeFeatureSet.EmitEverything());

    public EmittedRuntime EmitAll(ModuleBuilder moduleBuilder, RuntimeFeatureSet features)
    {
        _features = features;
        if (_emitHosted)
            _features.UsesPromise = true;
        var runtime = new EmittedRuntime();
        if (features.UsesRegExp)
            runtime.RegExps.BeginImplementationEmission();
        if (features.UsesDate)
            runtime.Dates.BeginImplementationEmission();
        if (features.UsesWeakMap)
            runtime.BeginWeakMapEmission();
        if (features.UsesWeakSet)
            runtime.BeginWeakSetEmission();
        if (features.UsesWeakRef)
            runtime.BeginWeakRefEmission();
        if (features.UsesFinalizationRegistry)
            runtime.FinalizationRegistry.BeginImplementationEmission();
        if (features.UsesMap)
            runtime.BeginMapEmission();
        if (features.UsesSet)
            runtime.BeginSetEmission();
        if (features.UsesJSON)
            runtime.Json.BeginImplementationEmission();
        if (features.UsesReflect || features.UsesProxy)
            runtime.Reflect.BeginAssignmentEmission();
        if (features.UsesReflect)
            runtime.Reflect.BeginNamespaceEmission();
        if (features.UsesReflectMetadata)
            runtime.Reflect.BeginMetadataEmission();
        if (features.UsesBigInt)
            runtime.BigInt.BeginImplementationEmission();
        if (features.UsesCjsRequire)
            runtime.Modules.BeginCommonJsEmission();
        if (features.UsesDynamicImport)
            runtime.Modules.BeginDynamicImportEmission();
        if (_emitHosted)
        {
            runtime.EventLoop.BeginHostedEmission();
            runtime.Process.BeginHostedEmission();
        }
        if (features.HasAnyTypedArray)
        {
            runtime.BeginArrayBufferEmission();
            runtime.BeginSharedArrayBufferEmission();
            runtime.BeginDataViewEmission();
            runtime.TypedArrays.BeginImplementationEmission();
            runtime.BeginAtomicsEmission();
        }
        if (features.UsesBuffer)
            runtime.BeginBufferEmission(features.HasAnyTypedArray);
        if (features.UsesCrypto)
        {
            runtime.BeginCryptoEmission();
            runtime.WebCrypto.BeginImplementationEmission();
        }
        if (features.UsesPromise)
        {
            runtime.BeginPromiseEmission();
            runtime.BeginTimerPromiseEmission();
        }
        if (features.UsesCluster)
            runtime.BeginClusterEmission();
        if (features.UsesBroadcastChannel)
            runtime.BeginBroadcastChannelEmission();
        if (features.UsesTty)
            runtime.BeginTtyEmission();
        if (features.UsesPerf)
            runtime.BeginPerformanceEmission();
        if (features.UsesIntl)
            runtime.BeginIntlEmission();
        if (features.UsesAsyncLocalStorage)
            runtime.BeginAsyncLocalStorageEmission();
        if (features.UsesAbortController)
            runtime.BeginAbortEmission();
        if (features.UsesSourceExecution)
            runtime.BeginSourceExecutionEmission();
        if (features.UsesVm)
            runtime.BeginVmEmission();
        if (features.UsesReadline)
            runtime.BeginReadlineEmission();
        if (features.UsesTextEncoding)
            runtime.BeginTextEncodingEmission();
        if (features.UsesOs)
            runtime.BeginOsEmission();
        if (features.UsesChildProcess)
            runtime.BeginChildProcessEmission();
        if (features.UsesFs)
        {
            runtime.BeginFileSystemEmission();
            runtime.BeginFileSystemAsyncEmission();
            runtime.BeginFileSystemStreamEmission();
            runtime.BeginFileSystemWatcherEmission();
        }
        if (features.UsesWebStreams)
            runtime.BeginWebStreamEmission();
        if (features.UsesNodeStreams)
        {
            runtime.BeginNodeStreamEmission(features.UsesAbortController);
            runtime.Process.BeginStreamsEmission();
        }
        if (features.UsesNet)
            runtime.BeginNetEmission();
        if (features.UsesHttp)
        {
            runtime.BeginHttpEmission();
            runtime.Fetch.BeginImplementationEmission();
        }
        if (features.UsesTls)
            runtime.BeginTlsEmission();
        if (features.UsesZlib)
            runtime.BeginZlibEmission();
        if (features.UsesDns)
            runtime.BeginDnsEmission();
        if (features.UsesDgram)
            runtime.BeginDgramEmission();

        // Emit $Undefined singleton class first (other methods need this type)
        EmitUndefinedClass(moduleBuilder, runtime.Sentinels);
        runtime.ArrayStorage.NumberQueue = EmitArrayQueue(moduleBuilder, runtime, ArrayElements.Double);
        runtime.ArrayStorage.BooleanQueue = EmitArrayQueue(moduleBuilder, runtime, ArrayElements.Bool);
        runtime.ArrayStorage.NumberQueueWithHoles = EmitArrayQueue(moduleBuilder, runtime, ArrayElements.Double, true);
        runtime.ArrayStorage.BooleanQueueWithHoles = EmitArrayQueue(moduleBuilder, runtime, ArrayElements.Bool, true);
        EmitLexicalUninitializedClass(moduleBuilder, runtime.Sentinels);
        runtime.Sentinels.CompleteEmission();

        // Marker used only to give compiler-generated prototype constructors a
        // signature that cannot collide with a user-declared constructor.
        EmitClassPrototypeMarkerInterface(moduleBuilder, runtime.ClassPrototypes);

        // Forward-declare the $Runtime class plus a handful of helper signatures
        // (Stringify, CreateException) so types that emit BEFORE EmitRuntimeClass
        // — most importantly $RegExp, whose Symbol.* protocol helpers want to
        // call them — can refer to the MethodBuilders. Bodies fill in later
        // during EmitRuntimeClass / EmitStringify / EmitCreateException, which
        // re-use the pre-allocated TypeBuilder + MethodBuilders.
        DefineRuntimeClassPhase1(moduleBuilder, runtime);

        // Guest throws use a dedicated exception carrying the original value.
        // Define it immediately after the phase-1 runtime signatures because its
        // lazy Message getter calls the forward-declared Stringify helper.
        EmitThrownValueExceptionType(moduleBuilder, runtime.Errors, runtime.StringCoercion);

        // Emit IUnionType marker interface first (union types need to implement this)
        EmitIUnionTypeInterface(moduleBuilder, runtime.UnionValues);
        runtime.UnionValues.CompleteEmission();

        // Emit a tiny dedicated type holding the thread-static `_currentArguments` slot
        // that $TSFunction.Invoke publishes so JS `arguments` capture can see caller
        // values beyond declared arity. Lives on its own type — adding it to
        // $TSFunction regressed Intl's formatRangeToParts test in opaque ways tied to
        // that type's field layout; isolating keeps $TSFunction's layout unchanged.
        EmitArgumentsContextClass(moduleBuilder, runtime.Arguments);

        // Marker attribute for "this method's body reads JS `arguments`".
        // Must be defined+created before EmitTSFunctionClass so its ctor IL can
        // ldtoken the type for the IsDefined read.
        EmitCapturesArgumentsAttribute(moduleBuilder, runtime.FunctionAttributes);

        // Marker attribute for "this is a user TS function; pad omitted args with the
        // `undefined` sentinel". Defined+created before EmitTSFunctionClass so the ctor IL
        // can ldtoken the type for the IsDefined read in AdjustArgs caching. (#640)
        EmitPadUndefinedAttribute(moduleBuilder, runtime.FunctionAttributes);

        // Carries the ECMAScript Function.length of emitted user methods. This must be
        // available before $TSFunction so its reflective constructor can cache the value.
        EmitFunctionLengthAttribute(moduleBuilder, runtime.FunctionAttributes);
        EmitFunctionNameAttribute(moduleBuilder, runtime.FunctionAttributes);
        EmitNumericRest4Attribute(moduleBuilder, runtime.FunctionAttributes);
        EmitNonConstructibleAttribute(moduleBuilder, runtime.FunctionAttributes);

        // Marker attribute for "this method's first parameter is the synthetic `__this` receiver".
        // Defined+created before EmitTSFunctionClass so the ctor IL can ldtoken the type for the
        // IsDefined read that backstops the (ref-asm-fragile) parameter-name check. (#738)
        EmitExpectsThisAttribute(moduleBuilder, runtime.FunctionAttributes);
        runtime.FunctionAttributes.CompleteEmission();

        if (features.CompactObjectRecordStableIteratorShapes.Count > 0)
        {
            runtime.BeginStableIteratorResultsEmission();
            EmitStableNumberIteratorResult(moduleBuilder, runtime.RequireStableIteratorResults());
            runtime.RequireStableIteratorResults().CompleteEmission();
        }

        // Emit TSFunction class first (other methods depend on it)
        EmitTSFunctionClass(
            moduleBuilder,
            runtime.FunctionValues,
            runtime.FunctionConstruction,
            new TSFunctionClassInputs(
                runtime.Arguments,
                runtime.FunctionAttributes,
                runtime.GlobalObject.SingletonField,
                runtime.Sentinels.UndefinedInstance,
                runtime.Sentinels.UndefinedType
            )
        );
        runtime.FunctionValues.CompleteEmission();
        runtime.FunctionConstruction.CompleteEmission();

        // Emit TSNamespace class for namespace support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSNamespace
        EmitTSNamespaceClass(moduleBuilder, runtime.Namespaces);
        runtime.Namespaces.CompleteEmission();

        // Emit TSSymbol class for symbol support
        EmitTSSymbolClass(moduleBuilder, runtime.Symbols, runtime.Sentinels.UndefinedInstance);

        // Emit ReferenceEqualityComparer for Map/Set key equality
        EmitReferenceEqualityComparerClass(moduleBuilder, runtime.CollectionKeys, runtime.Symbols.Type);

        // Emit $IGenerator interface for generator return/throw support
        EmitGeneratorInterface(moduleBuilder, runtime.Generators);
        runtime.Generators.CompleteEmission();

        // Emit $IAsyncGenerator only when async-generator or for-await support
        // can reference it.
        if (features.UsesAsyncGenerator || features.UsesForAwaitOf)
        {
            runtime.BeginAsyncGeneratorsEmission();
            EmitAsyncGeneratorInterface(moduleBuilder, runtime.RequireAsyncGenerators());
        }

        // NOTE: $IteratorWrapper is emitted later, after iterator methods are defined

        // Emit $TSDate class for standalone Date support — gated on UsesDate.
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSDate
        if (runtime.Dates.Implementation is not null)
            EmitTSDateClass(moduleBuilder, runtime.Dates.RequireImplementation(), runtime.FunctionAttributes.NonConstructibleCtor);

        // Emit $Error class hierarchy for standalone error support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSError and subclasses
        EmitTSErrorClasses(moduleBuilder, runtime.Errors);

        // Emit $DataCloneError exception type — thrown by StructuredCloneCore (#1255).
        // Unconditional: StructuredCloneCore itself is always emitted (EmitWorkerHelpers
        // runs unconditionally inside EmitRuntimeClass below).
        EmitTSDataCloneErrorType(moduleBuilder, runtime.StructuredClone);

        // Emit $Promise class for standalone Promise support.
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSPromise
        if (features.UsesPromise)
            EmitTSPromiseClass(moduleBuilder, runtime.RequirePromise());

        // Emit $ArrayHole singleton first — $Array methods reference
        // $ArrayHole.Instance for padding intermediate positions on sparse writes
        // and `a.length = N` extensions.
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.ArrayHole
        EmitArrayHoleClass(moduleBuilder, runtime.ArrayStorage);

        // Per-thread args[] pool used by method-call dispatch to skip
        // newarr per `obj.method(a, b)` invocation. Lives on a separate
        // class — historically to avoid the layout-sensitive .NET 10
        // tier-0 JIT bug behind issue #39 (since fixed upstream).
        EmitCallArgsPool(moduleBuilder, runtime.CallArguments);

        // Emit $PropertyDescriptorStore and $CompiledPropertyDescriptor before
        // $Array: array length truncation must remove indexed descriptors as
        // well as dense/sparse storage. The descriptor store itself depends
        // only on helper types emitted above.
        EmitPropertyDescriptorTypes(moduleBuilder, runtime.DescriptorStorage,
            new DescriptorKeyInputs(runtime.FunctionValues.Type, runtime.FunctionValues.GetMethodInfo),
            runtime.Sentinels.UndefinedType);

        // Emit $Array class for standalone array support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSArray
        EmitTSArrayClass(moduleBuilder, runtime);

        // Emit $IHasFields interface for unified property access
        // Must come before $Object which implements it
        EmitHasFieldsInterface(moduleBuilder, runtime.ObjectFields);
        runtime.ObjectFields.CompleteEmission();
        EmitCompactObjectRecordInterface(moduleBuilder, runtime.Records);

        // Emit $Object class for standalone object support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSObject
        EmitTSObjectClass(moduleBuilder, runtime.ObjectStorage,
            new ObjectStorageInputs(runtime.ObjectFields.Interface,
                new ObjectReadInputs(runtime.DescriptorStorage.DescriptorType, runtime.DescriptorStorage.TryGetGetter,
                    runtime.DescriptorStorage.GetPropertyDescriptor, runtime.DescriptorStorage.DescriptorSetter.GetGetMethod()!,
                    runtime.DescriptorStorage.DescriptorValue.GetGetMethod()!, runtime.FunctionValues.Type,
                    runtime.FunctionValues.InvokeWithThis, runtime.Sentinels.UndefinedInstance),
                new ObjectInvokeInputs(runtime.FunctionValues.Type, runtime.FunctionValues.InvokeWithThis),
                runtime.Errors.TypeErrorConstructor));

        if (features.UsesJSON || features.UsesCompactObjectRecords)
        {
            runtime.Records.BeginScalarEmission();
            var recordContract = new RecordStorageContractInputs(
                runtime.ObjectFields.Interface, runtime.ObjectFields.FieldsGetter,
                runtime.ObjectFields.GetProperty, runtime.ObjectFields.SetProperty, runtime.ObjectFields.HasProperty);
            EmitJsonScalarRecordClass(moduleBuilder, runtime.Records, recordContract, features.JsonScalarRecordShapes);
            EmitCompactObjectRecordClasses(moduleBuilder, runtime.Records, recordContract,
                runtime.Sentinels.UndefinedInstance, features.CompactObjectRecordShapes, features.CompactObjectRecordSelfFields);
        }

        if (runtime.Json.Implementation is not null)
        {
            EmitTSRawJsonClass(
                moduleBuilder,
                runtime.Json.RequireImplementation(),
                new TSRawJsonClassInputs(runtime.DescriptorStorage, runtime.ObjectStorage)
            );
        }

        // Emit $RegExp class for standalone regex support — gated on UsesRegExp.
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSRegExp
        if (runtime.RegExps.Implementation is not null)
            EmitTSRegExpClass(
                moduleBuilder,
                runtime.RegExps,
                new TSRegExpClassInputs(
                    runtime.ArrayStorage,
                    runtime.Booleans,
                    runtime.Errors.CreateException,
                    runtime.DescriptorStorage,
                    runtime.ObjectRead.Property,
                    runtime.NumericCoercion,
                    runtime.ObjectStorage,
                    runtime.FunctionAttributes.PadUndefinedCtor,
                    runtime.ObjectWrite.Property,
                    runtime.StringCoercion,
                    runtime.Symbols,
                    runtime.FunctionValues.GetMethodInfo,
                    runtime.FunctionValues.InvokeWithThis,
                    runtime.FunctionValues.Type,
                    runtime.Errors.SyntaxErrorConstructor,
                    runtime.Errors.TypeErrorConstructor,
                    runtime.Sentinels.UndefinedInstance,
                    runtime.Sentinels.UndefinedType
                )
            );

        // AssertionError now lives in stdlib/node/assert.ts (embedded stdlib migration).
        // Emit $NodeError class for standalone fs module support
        // NOTE: Must stay in sync with NodeError in Runtime/BuiltIns/Modules/NodeError.cs
        EmitNodeErrorClass(moduleBuilder, runtime.NodeErrors);

        // Emit $Buffer class for standalone buffer support — gated on UsesBuffer.
        // Implied by crypto/fs/zlib/http/fetch/dgram/net (their methods return
        // or consume Buffer values), so the gate only fires when ALL of those
        // are off too.
        // NOTE: Must come before $Hash and $Hmac since they return Buffer
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSBuffer
        if (features.UsesBuffer)
            EmitTSBufferClass(moduleBuilder, runtime.RequireBuffer());

        // Crypto helper types — gated on UsesCrypto. All references are confined
        // to crypto's own emit files; no central-dispatch fallout.
        if (features.UsesCrypto)
        {
            // Shared digest/encoding primitives — must precede the crypto value
            // types below, whose bodies call into it (#1054).
            EmitCryptoPrimitivesClass(moduleBuilder, runtime);
            EmitTSHashClass(moduleBuilder, runtime);
            EmitTSHmacClass(moduleBuilder, runtime);
            EmitTSCipherClass(moduleBuilder, runtime);
            EmitTSDecipherClass(moduleBuilder, runtime);
            EmitTSSignTypeDefinition(moduleBuilder, runtime.RequireCrypto());
            EmitTSVerifyTypeDefinition(moduleBuilder, runtime.RequireCrypto());
            EmitTSKeyObjectClass(moduleBuilder, runtime);
            EmitTSX509Class(moduleBuilder, runtime); // crypto.X509Certificate (#1064); needs $TSKeyObject
            EmitTSECDHTypeDefinition(moduleBuilder, runtime.RequireCrypto());
            EmitBoundECDHMethodTypeDefinition(moduleBuilder, runtime.RequireCrypto());
            EmitTSDHTypeDefinition(moduleBuilder, runtime);
            EmitBoundDHMethodTypeDefinition(moduleBuilder, runtime.RequireCrypto());
        }

        // Emit $EventLoop singleton (must come before timer types and net/http types that call Ref/Unref/Schedule)
        // Cancellation methods are declared later in EmitRuntimeClass; preserve the early loop dependency.
        EmitTSEventLoopClass(moduleBuilder, runtime.EventLoop, checkCancellation: null);

        // Emit $VirtualTimer class for virtual timer support (single-threaded semantics)
        // Must come after TSFunction (uses TSFunctionType)
        // Must come BEFORE TSTimeoutClass (TSTimeout references VirtualTimer)
        EmitVirtualTimerClass(moduleBuilder, runtime.Timers);

        // Emit $TSTimeout class for timer support
        // Must come after $EventLoop (Cancel/Ref/Unref call EventLoop.Ref/Unref)
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
        EmitBoundTSFunctionClass(moduleBuilder, runtime.FunctionBindings, runtime.FunctionValues);

        // Emit $AsyncLocalStorage class for async context propagation
        // Must come after TSFunction (Run/Exit invoke callbacks via TSFunctionInvoke)
        if (features.UsesAsyncLocalStorage)
            EmitAsyncLocalStorageClass(moduleBuilder, runtime.RequireAsyncLocalStorage(), runtime.FunctionValues.Type, runtime.FunctionValues.Invoke);

        // Emit $EventEmitter class for standalone event emitter support
        // NOTE: Must come after BoundTSFunction (uses TSFunctionType, BoundTSFunctionType)
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSEventEmitter
        EmitTSEventEmitterClass(moduleBuilder, runtime);

        // HTTP types — gated on UsesHttp. The detector arranges implications so
        // UsesFetch ⇒ UsesHttp ⇒ UsesNet, UsesTls ⇒ UsesNet.
        if (features.UsesHttp)
            EmitHttpTypes(moduleBuilder, runtime);
        // TLS type emission ($TlsSocket : $NetSocket, $TlsServer : $EventEmitter) is
        // deferred to just after $NetSocket Phase 1 below — $TlsSocket extends $NetSocket
        // (mirroring interp SharpTSTlsSocket : SharpTSSocket), so the base TypeBuilder must
        // exist first.

        // cluster emits no module-level types: the compiled module late-binds into
        // SharpTS.dll via $Runtime.ClusterFork/ClusterInvoke (RuntimeEmitter.RuntimeClass.cs).

        // FS-only types — gated on UsesFs together with the FS module methods.
        if (features.UsesFs)
        {
            // Emit $FileDescriptorTable for standalone fs fd-based operations (Phase 21)
            // NOTE: Must come after $NodeError (uses NodeErrorCtor for EBADF errors)
            EmitFileDescriptorTableType(moduleBuilder, runtime.RequireFileSystem(), runtime.NodeErrors);

            // Emit $Dirent and $Dir for standalone fs.opendirSync support (Phase 21)
            // NOTE: Must emit Dirent first since Dir's ReadSync creates Dirent instances
            EmitDirentType(moduleBuilder, runtime);
            EmitDirType(moduleBuilder, runtime);
        }

        // Emit $ArrayBuffer, $SharedArrayBuffer, $DataView, and the 11 typed-array
        // variants. Gated on TypedArrays != None — granular per-kind selection
        // (Int8 vs Float32 etc.) is a future refinement; today we emit them as
        // a single bag whenever any typed-array identifier was seen.
        if (features.HasAnyTypedArray)
        {
            EmitArrayBufferType(moduleBuilder, runtime);
            EmitSharedArrayBufferType(moduleBuilder, runtime.RequireSharedArrayBuffer());
            EmitDataViewType(moduleBuilder, runtime);
            EmitTypedArrayTypes(moduleBuilder, runtime);
            // $BoundTypedArrayMethod Phase 1 (#940): callable wrapper for typed-array bulk methods.
            // Needs $TypedArray defined (above); must precede EmitRuntimeClass, whose invocation
            // helpers and GetTypedArrayMember reference its type/ctor/Invoke.
            EmitBoundTypedArrayMethodTypeDefinition(moduleBuilder, runtime.TypedArrays.RequireImplementation());
        }

        // Emit stream classes for standalone stream support
        // NOTE: Must come after EventEmitter (stream types extend $EventEmitter)
        // Order matters due to inheritance and cross-references:
        // - Writable is standalone
        // - Readable's Pipe() method needs to reference Duplex (for piping to Duplex streams)
        // - Duplex extends Readable
        // - Transform extends Duplex
        // - PassThrough extends Transform
        //
        // Node-stream types ($Readable / $Writable / $Duplex / $Transform / etc.)
        // — gated on UsesNodeStreams. The detector implies UsesFs ⇒ UsesNodeStreams
        // (FsReadStream extends Readable) and UsesHttp ⇒ UsesNodeStreams.
        if (features.UsesNodeStreams)
        {
            EmitTSWritableClass(moduleBuilder, runtime);
            EmitTSReadableTypeDefinition(moduleBuilder, runtime);  // Phase 1: type, fields, most methods
            EmitTSDuplexTypeDefinition(moduleBuilder, runtime);    // Phase 1: type, fields, all methods
            EmitTSReadablePhaseTwoMethods(runtime);                  // Phase 2a: Push, Pipe (need Duplex)
            EmitTSDuplexFinalize(runtime.RequireNodeStreams());                         // Phase 2: CreateType
            EmitTSTransformClass(moduleBuilder, runtime);
            EmitMapFilterTransformCallbackClasses(moduleBuilder, runtime); // Helper classes for map/filter
            EmitTSReadableMapFilterMethods(runtime);               // Phase 2b: Map, Filter (need Transform) + CreateType
            EmitTSPassThroughClass(moduleBuilder, runtime);
            EmitTSStreamUtilsClass(moduleBuilder, runtime);
            // addAbortSignal listener closure (#1027) — needs $Readable/$Writable Destroy + $Error.
            EmitStreamAbortCallbackClass(moduleBuilder, runtime);
        }
        if (features.UsesZlib)
            EmitTSZlibTransformClass(moduleBuilder, runtime);

        // Function wrapper emission is deferred below until AFTER $BoundArrayMethod /
        // $BoundMapMethod / $BoundSetMethod Phase 1 so their Invoke MethodBuilders
        // are available to the wrapper bodies (for dispatching .call/.apply/.bind on
        // bound methods).

        // TextEncoder/Decoder — gated on UsesTextEncoding. $TextDecoderDecodeMethod
        // is referenced from EmitInvokeValue's dispatch, gated on the same flag.
        if (features.UsesTextEncoding)
        {
            EmitTSTextEncoderClass(moduleBuilder, runtime.RequireTextEncoding(), runtime.RequireBuffer());
            EmitTSTextDecoderClass(moduleBuilder, runtime.RequireTextEncoding(), runtime.RequireBuffer());
            EmitTSTextDecoderDecodeMethodClass(moduleBuilder, runtime.RequireTextEncoding(), runtime.RequireBuffer());
        }

        // $StringDecoder class removed — StringDecoder migrated to
        // stdlib/node/string_decoder.ts (pure-TS over the Buffer JS API).

        // Emit $Stats class for fs.stat() and related methods — gated on UsesFs.
        // Must come before fs module methods which use it. Conditional Isinst
        // in GetFieldsProperty's central dispatch (Properties.cs) is gated on
        // the same flag.
        if (features.UsesFs)
            EmitStatsClass(moduleBuilder, runtime.RequireFileSystem());

        // Emit $CJSModule — backs the `module` local bound in every CJS module init.
        // Gated on UsesCjsRequire (the detector flips this whenever the program
        // mentions `require`, `module`, `exports`, or has a require('...') call).
        if (features.UsesCjsRequire)
            EmitCjsModuleClass(moduleBuilder, runtime.Modules.RequireCommonJs());

        // Emit $Arguments : List<object> marker subclass. Must come before
        // any IL that constructs `arguments` (ILCompiler.Functions.cs uses
        // runtime.ArgumentsDefaultCtor / ArgumentsEnumerableCtor).
        EmitArgumentsTypeDefinition(moduleBuilder, runtime.Arguments);
        runtime.Arguments.CompleteEmission();

        // Emit $BoundArrayMethod type and constructor (Phase 1)
        // Must come before EmitRuntimeClass so GetListProperty can use the constructor
        EmitBoundArrayMethodTypeDefinition(moduleBuilder, runtime.ArrayOperations);

        // Emit $BoundMapMethod / $BoundSetMethod types and constructors (Phase 1)
        // Must come before EmitRuntimeClass so GetMapProperty/GetSetProperty can use them.
        // Gated alongside the rest of Map/Set emission.
        if (runtime.Map is not null)
            EmitBoundMapMethodTypeDefinition(moduleBuilder, runtime.RequireMap());
        if (runtime.Set is not null)
            EmitBoundSetMethodTypeDefinition(moduleBuilder, runtime.RequireSet());

        // Emit $BoundAnyFunction (the partial-apply wrapper for .bind on non-$TSFunction
        // callables) and the function bind/call/apply wrappers. All reference the
        // Bound*Method TypeBuilders above, so they MUST come after Phase 1 of those.
        // They come before EmitRuntimeClass so GetFunctionMethod (inside EmitRuntimeClass)
        // can use their constructors.
        EmitBoundAnyFunctionClass(
            moduleBuilder,
            runtime.FunctionBindings,
            new BoundAnyFunctionClassInputs(runtime.FunctionValues, runtime.ArrayOperations, runtime.Map, runtime.Set)
        );
        EmitFunctionBindWrapperClass(
            moduleBuilder,
            runtime.FunctionBindings,
            new FunctionBindWrapperClassInputs(
                runtime.FunctionValues,
                runtime.ArrayOperations,
                runtime.Map,
                runtime.Set,
                runtime.Sentinels.UndefinedInstance,
                runtime.Errors
            )
        );
        EmitFunctionCallWrapperClass(
            moduleBuilder,
            runtime.FunctionBindings,
            new FunctionCallWrapperClassInputs(
                runtime.FunctionValues,
                runtime.ArrayOperations,
                runtime.Map,
                runtime.Set
            )
        );
        EmitFunctionApplyWrapperClass(
            moduleBuilder,
            runtime.FunctionBindings,
            new FunctionApplyWrapperClassInputs(
                runtime.FunctionValues,
                runtime.ArrayOperations,
                runtime.Map,
                runtime.Set,
                runtime.ArrayStorage
            )
        );
        runtime.FunctionBindings.CompleteEmission();

        // Emit $MethodCallable type and constructor (Phase 1)
        // Must come before EmitRuntimeClass so GetFieldsProperty can wrap GetMember results
        EmitMethodCallableTypeDefinition(moduleBuilder, runtime.ReflectedMethods);

        // Emit $TemplateStringsList class for tagged template literals
        // Must come before EmitRuntimeClass so InvokeTaggedTemplate can use the constructor
        EmitTemplateStringsListClass(moduleBuilder, runtime.Templates);

        // $PropertyDescriptorStore is now emitted earlier (just before $RegExp)
        // so types that need CompiledPropertyDescriptorType during their own
        // emission can reference it. This used to live here.

        NetConstruction? netConstruction = null;

        // Net / Dgram types — gated on UsesNet / UsesDgram. UsesNet is implied
        // by UsesHttp and UsesTls (both extend $NetServer-style sockets).
        if (features.UsesNet)
        {
            // The opaque $BlockList handle is self-contained (pure BCL) and must
            // exist before $NetServer and the primitive net factory reference it.
            EmitTSNetBlockListTypes(moduleBuilder, runtime.RequireNet());
            var socketConstruction = EmitTSNetSocketPhase1(moduleBuilder, runtime);
            var serverConstruction = EmitTSNetServerPhase1(moduleBuilder, runtime);
            netConstruction = new(socketConstruction, serverConstruction);
        }
        // TLS types — Phase 1 (type + fields + method stubs, no CreateType). Must come
        // after $NetSocket Phase 1 ($TlsSocket : $NetSocket) and before EmitRuntimeClass
        // (the tls module methods reference TlsSocketCtor/TlsServerCtor). UsesTls ⇒ UsesNet.
        if (features.UsesTls)
            EmitTlsTypesPhase1(moduleBuilder, runtime);
        if (features.UsesDgram)
            EmitDatagramSocketTypeDefinition(moduleBuilder, runtime);

        // Emit $ReadlineInterface type definition (Phase 1)
        // Must come before EmitRuntimeClass so ReadlineCreateInterface can use the constructor
        if (features.UsesReadline)
            EmitReadlineInterfaceTypeDefinition(moduleBuilder, runtime.RequireReadline(), runtime.EventEmitter);

        // Emit $FinRegEntry type (finalizer helper for FinalizationRegistry)
        // Must come before EmitRuntimeClass so Register can use the constructor
        if (runtime.FinalizationRegistry.Implementation is { } finalizationRegistry)
            EmitFinRegEntryTypeDefinition(moduleBuilder, finalizationRegistry);

        // FS stream/watcher types — gated on UsesFs. EmitFsModuleMethods is
        // also gated below in EmitRuntimeClass on the same flag, so dependent
        // runtime methods skip in tandem.
        if (features.UsesFs)
        {
            EmitFsStreamTypeDefinitions(moduleBuilder, runtime);
            EmitFsWatcherClass(moduleBuilder, runtime.RequireFileSystemWatchers(), runtime.EventEmitter, runtime.EventLoop);
            EmitStatWatcherClass(moduleBuilder, runtime.RequireFileSystemWatchers(), runtime.EventEmitter, runtime.EventLoop, runtime.RequireFileSystem());
        }

        // Reflect.construct and Proxy [[Construct]] need this token while the
        // main $Runtime body is emitted. Its body is filled after $Runtime.
        runtime.DynamicConstruction.Function = runtime.RuntimeClass.Type.DefineMethod(
            "NewOnFunction",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.ObjectArray]);

        // Emit $Runtime class with all helper methods
        EmitRuntimeClass(moduleBuilder, runtime, netConstruction);

        // Emit $Runtime.NewOnFunction — the JS `new` protocol for runtime-valued
        // function callees. Depends on $Object, $TSFunction, $BoundTSFunction, and
        // the $Runtime type itself all being defined.
        EmitNewOnFunction(
            runtime.RuntimeClass.Type,
            runtime.DynamicConstruction,
            new NewOnFunctionInputs(
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.FunctionBindings,
                runtime.FunctionIntrospection,
                runtime.FunctionValues,
                runtime.ObjectRead,
                runtime.ObjectStorage,
                runtime.ReflectedMethods,
                runtime.Sentinels.UndefinedInstance
            )
        );

        // Dynamic-callee `new x(...)` dispatch for state-machine emitters (#224).
        // Must follow EmitNewOnFunction — it calls through runtime.DynamicConstruction.Function.
        EmitConstructDynamicValue(
            runtime.RuntimeClass.Type,
            runtime.DynamicConstruction,
            new ConstructDynamicValueInputs(
                runtime.BoxedPrimitives,
                runtime.Errors,
                runtime.FunctionConstruction,
                runtime.FunctionValues,
                runtime.RegExps,
                runtime.StringCoercion,
                runtime.Sentinels.UndefinedInstance,
                runtime.Sentinels.UndefinedType
            )
        );
        runtime.DynamicConstruction.CompleteEmission();

        // RegExp @@split needs ConstructDynamicValue for SpeciesConstructor;
        // its signature was reserved before $RegExp emitted its public wrapper.
        if (runtime.RegExps.Implementation is not null)
        {
            EmitRegExpSymbolSplitProtocol(
                runtime.RegExps.RequireImplementation(),
                new RegExpSymbolSplitProtocolInputs(
                    runtime.ArrayStorage,
                    runtime.DynamicConstruction.Value,
                    runtime.Errors.CreateException,
                    runtime.ObjectRead.Index,
                    runtime.ObjectRead.Property,
                    runtime.NumericCoercion,
                    runtime.ObjectWrite.Property,
                    runtime.StringCoercion,
                    runtime.Symbols,
                    runtime.Errors.TypeErrorConstructor,
                    runtime.Sentinels.UndefinedType
                )
            );
            EmitRegExpSymbolMatchAllProtocol(
                runtime.RegExps.RequireImplementation(),
                new RegExpSymbolMatchAllProtocolInputs(
                    runtime.DynamicConstruction.Value,
                    runtime.Errors.CreateException,
                    runtime.ObjectRead.Index,
                    runtime.ObjectRead.Property,
                    runtime.NumericCoercion,
                    runtime.ObjectWrite.Property,
                    runtime.StringCoercion,
                    runtime.Symbols,
                    runtime.Errors.TypeErrorConstructor,
                    runtime.Sentinels.UndefinedType
                )
            );
        }

        // General NewPromiseCapability (#349): the $PromiseCapability holder type
        // and the body of the pre-declared NewPromiseCapabilityResult helper.
        // Must follow EmitConstructDynamicValue (it calls through that helper) and
        // EmitRuntimeClass (depends on InvokeValue / WrapException).
        if (features.UsesPromise)
            EmitPromiseCapabilitySupport(moduleBuilder, runtime);

        // The async-from-sync adapter depends on iterator helpers plus
        // CoerceAwaitableToTask, all of which are now defined. It is gated on
        // for-await syntax because most programs never reference this type.
        if (features.UsesForAwaitOf)
        {
            runtime.RequireAsyncGenerators().BeginFromSyncEmission();
            EmitAsyncFromSyncIteratorSupport(
                moduleBuilder,
                runtime.RuntimeClass.Type,
                runtime.RequireAsyncGenerators(),
                runtime.RequireAsyncGenerators().RequireFromSync(),
                new AsyncFromSyncIteratorSupportInputs(
                    runtime.Generators,
                    runtime.IteratorProtocol.Done,
                    runtime.IteratorProtocol.Function,
                    runtime.IteratorRecords.NextMethod,
                    runtime.IteratorProtocol.Value,
                    runtime.Invocation,
                    runtime.IteratorRecords.InvokeNext,
                    runtime.IteratorCollection.ToList,
                    runtime.IteratorHelpers.NormalizeToEnumerator,
                    runtime.ObjectRead,
                    runtime.RequirePromise(),
                    runtime.RuntimeClass.Type,
                    runtime.Symbols,
                    runtime.Sentinels.UndefinedInstance,
                    runtime.Sentinels.UndefinedType
                )
            );
            runtime.RequireAsyncGenerators().RequireFromSync().CompleteEmission();
        }
        runtime.AsyncGenerators?.CompleteEmission();

        // AbortSignal / Intl value-position singletons (#224). Must follow
        // EmitRuntimeClass — they wrap the AbortSignal*/CreateIntl* helpers
        // emitted there.
        EmitNamespaceSingletons(runtime.RuntimeClass.Type, runtime.Abort, runtime.Intl, runtime.FunctionConstruction.GetOrCreate);

        // Emit $BroadcastChannel — extends $EventEmitter, dispatches via $EventLoop,
        // and clones messages via $Runtime.StructuredClone (populated during EmitRuntimeClass
        // → EmitWorkerHelpers → EmitStructuredCloneHelper).
        // NOTE: Must come after EmitRuntimeClass so runtime.StructuredClone.Clone is set.
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSBroadcastChannel
        if (features.UsesBroadcastChannel)
            EmitBroadcastChannelClass(moduleBuilder, runtime.RequireBroadcastChannel(), runtime.EventEmitter,
                runtime.EventLoop, runtime.FunctionValues.Type, runtime.FunctionValues.Invoke,
                runtime.StructuredClone.Clone, runtime.StructuredClone.ErrorType);

        // Emit $MessagePort/$MessageChannel — same constraints as
        // $BroadcastChannel ($EventEmitter base, $EventLoop dispatch,
        // $Runtime.StructuredClone for per-message cloning). Unconditional,
        // matching the previous CreateMessageChannel helper (#222).
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSMessagePort
        EmitMessageChannelTypes(moduleBuilder, runtime.MessageChannels, runtime.RuntimeClass.Type,
            runtime.EventEmitter, runtime.EventLoop, runtime.StructuredClone.Clone, runtime.StructuredClone.ErrorType);

        // receiveMessageOnPort's body reads $MessagePort's _pending/_closed/_cloneError, so it
        // must be filled now that EmitMessageChannelTypes has created the type (#1077). Still
        // before EmitRuntimeClassFinalize, which closes the $Runtime type this method lives on.
        EmitWorkerThreadsReceiveMessageOnPortBody(runtime.Workers, runtime.MessageChannels.Port, runtime.Sentinels.UndefinedInstance);

        // Web Streams — gated on UsesWebStreams. The only external references are
        // user-code `new ReadableStream(...)`/`new WritableStream(...)`/`new TransformStream(...)`
        // in ExpressionEmitterBase.Constructors.cs, which only fire when the
        // detector has already flipped the flag.
        if (features.UsesWebStreams)
        {
            EmitQueuingStrategyClasses(moduleBuilder, runtime);
            EmitWritableStreamClasses(moduleBuilder, runtime);
            EmitReadableStreamClasses(moduleBuilder, runtime);
            EmitTransformStreamClasses(moduleBuilder, runtime);
        }

        // Emit $ReflectMetadataDecorator closure class
        // Must come after EmitRuntimeClass (calls ReflectDefineMetadata)
        // External usage in ReflectStaticEmitter has a null-check fallback, so
        // skipping this is safe even if some path slips past the detector.
        if (runtime.Reflect.Metadata is not null)
            EmitReflectMetadataDecoratorClass(moduleBuilder, runtime.Reflect.RequireMetadata());

        // Finalize $BoundArrayMethod with Invoke method (Phase 2)
        // Must come after EmitRuntimeClass (needs array methods defined)
        EmitBoundArrayMethodFinalize(runtime);

        // Finalize $BoundTypedArrayMethod (#940) Phase 2 — Invoke dispatches to the base
        // typed-array bulk methods and uses GetElement/TSArrayLengthGetter (defined in EmitRuntimeClass).
        if (features.HasAnyTypedArray)
            EmitBoundTypedArrayMethodFinalize(runtime);

        // Finalize $BoundMapMethod / $BoundSetMethod with Invoke method (Phase 2)
        // Must come after EmitRuntimeClass (needs Map*/Set* runtime methods defined).
        // Gated alongside the rest of Map/Set emission.
        if (runtime.Map is not null)
            EmitBoundMapMethodFinalize(runtime.RequireMap(), runtime.Sentinels.UndefinedInstance);
        if (runtime.Set is not null)
            EmitBoundSetMethodFinalize(runtime.RequireSet(), runtime.Sentinels.UndefinedInstance);

        // Finalize $MethodCallable with Invoke method (Phase 2)
        EmitMethodCallableFinalize(runtime.ReflectedMethods);
        runtime.ReflectedMethods.CompleteEmission();

        NetClosureConstruction? netClosures = null;

        // Net / Http / Tls / Dgram phase-1b/phase-2 finalize work — gated on
        // their own feature flags. UsesHttp ⇒ UsesNet, UsesTls ⇒ UsesNet.
        if (features.UsesNet)
        {
            var net = RequireNetConstruction(netConstruction);
            netClosures = EmitNetClosureTypes(moduleBuilder, runtime, net.Socket.Fields, net.Socket.Methods, net.Server.Fields);
        }

        if (features.UsesHttp)
            EmitHttpServerAcceptWorkerBody(runtime);

        if (features.UsesNet)
        {
            var net = RequireNetConstruction(netConstruction);
            var closures = netClosures ?? throw new InvalidOperationException("Net closures must be emitted before socket/server bodies.");
            EmitTSNetSocketPhase2(runtime, net.Socket.Fields, net.Socket.Methods, closures.Socket);
            EmitTSNetServerPhase2(runtime, net.Server.Fields, net.Server.Methods, closures.Server);
        }

        if (features.UsesDgram)
        {
            EmitDgramMessageClosureClass(moduleBuilder, runtime);
            EmitDgramReceiveWorkerBody(runtime);
            EmitDatagramSocketFinalize(runtime.RequireDgram());
        }

        if (features.UsesTls)
        {
            var socketFields = RequireNetConstruction(netConstruction).Socket.Fields;
            EmitTlsAcceptClosureClass(moduleBuilder, runtime);
            EmitTlsAcceptErrorClosureClass(moduleBuilder, runtime);
            EmitTlsConnectClosureClass(moduleBuilder, runtime, socketFields.Client, socketFields.Stream);
            EmitTlsConnectBody(runtime);
            // $TlsSocket Phase 2: emit method bodies + CreateType. Must come after the
            // connect closure (its Connect body sets $TlsSocket fields) and after
            // $NetSocket.CreateType (base, already finalized above).
            EmitTlsSocketFinalize(runtime);
        }

        EmitRuntimeClassFinalize(runtime.RuntimeClass);     // Finalize $Runtime after all method bodies

        if (features.UsesTls)
        {
            var socketFields = RequireNetConstruction(netConstruction).Socket.Fields;
            EmitTlsServerAcceptWorkerBody(runtime, socketFields.Client, socketFields.Stream);
            EmitTlsServerFinalize();
        }

        // Finalize $ReadlineInterface class (Phase 2)
        // Must come after EmitRuntimeClass (Question uses InvokeValue)
        if (features.UsesReadline)
            EmitReadlineInterfaceFinalize(runtime.RequireReadline(), runtime.Invocation.Value);

        // Crypto Phase-2 finalize calls — gated on UsesCrypto with the type
        // emission above.
        if (features.UsesCrypto)
        {
            EmitTSSignFinalize(runtime);
            EmitTSVerifyFinalize(runtime);
            EmitTSECDHFinalize(runtime.RequireCrypto());
            EmitBoundECDHMethodFinalize(runtime.RequireCrypto());
            EmitTSDHFinalize(runtime);
            EmitBoundDHMethodFinalize(runtime.RequireCrypto());
        }

        runtime.ArrayBuffer?.CompleteEmission();
        runtime.SharedArrayBuffer?.CompleteEmission();
        runtime.DataView?.CompleteEmission();
        runtime.TypedArrays.CompleteEmission();
        runtime.Atomics?.CompleteEmission();
        runtime.Cluster?.CompleteEmission();
        runtime.MessageChannels.CompleteEmission();
        runtime.Workers.CompleteEmission();
        runtime.StructuredClone.CompleteEmission();
        runtime.Strings.CompleteEmission();
        runtime.Templates.CompleteEmission();
        runtime.StringCoercion.CompleteEmission();
        runtime.BoxedPrimitives.CompleteEmission();
        runtime.Numbers.CompleteEmission();
        runtime.Math.CompleteEmission();
        runtime.BigInt.CompleteEmission();
        runtime.Booleans.CompleteEmission();
        runtime.NumericCoercion.CompleteEmission();
        runtime.ObjectStorage.CompleteEmission();
        runtime.DescriptorStorage.CompleteEmission();
        runtime.ObjectState.CompleteEmission();
        runtime.Errors.CompleteEmission();
        runtime.ObjectDescriptors.CompleteEmission();
        runtime.ObjectPrototypes.CompleteEmission();
        runtime.ClassPrototypes.CompleteEmission();
        runtime.ObjectKeys.CompleteEmission();
        runtime.ObjectOwnProperties.CompleteEmission();
        runtime.ObjectOperations.CompleteEmission();
        runtime.ObjectConstruction.CompleteEmission();
        runtime.ObjectDeletion.CompleteEmission();
        runtime.ObjectRead.CompleteEmission();
        runtime.ObjectWrite.CompleteEmission();
        runtime.Operators.CompleteEmission();
        runtime.Reflect.CompleteEmission();
        runtime.Json.CompleteEmission();
        runtime.Records.CompleteEmission();
        runtime.CollectionKeys.CompleteEmission();
        runtime.Map?.CompleteEmission();
        runtime.Set?.CompleteEmission();
        runtime.WeakMap?.CompleteEmission();
        runtime.WeakSet?.CompleteEmission();
        runtime.WeakRef?.CompleteEmission();
        runtime.FinalizationRegistry.CompleteEmission();
        runtime.Symbols.CompleteEmission();
        runtime.SymbolAccessors.CompleteEmission();
        runtime.Dates.CompleteEmission();
        runtime.RegExps.CompleteEmission();
        runtime.BroadcastChannel?.CompleteEmission();
        runtime.EventEmitter.CompleteEmission();
        runtime.NodeStreams?.CompleteEmission();
        runtime.WebStreams?.CompleteEmission();
        runtime.FileSystem?.CompleteEmission();
        runtime.FileSystemAsync?.CompleteEmission();
        runtime.FileSystemStreams?.CompleteEmission();
        runtime.FileSystemWatchers?.CompleteEmission();
        runtime.Timers.CompleteEmission();
        runtime.Microtasks.CompleteEmission();
        runtime.TimerPromises?.CompleteEmission();
        runtime.Modules.CompleteEmission();
        runtime.NodeErrors.CompleteEmission();
        runtime.Tty?.CompleteEmission();
        runtime.Performance?.CompleteEmission();
        runtime.Intl?.CompleteEmission();
        runtime.AsyncLocalStorage?.CompleteEmission();
        runtime.Abort?.CompleteEmission();
        runtime.SourceExecution?.CompleteEmission();
        runtime.Vm?.CompleteEmission();
        runtime.Readline?.CompleteEmission();
        runtime.TextEncoding?.CompleteEmission();
        runtime.Inspection.CompleteEmission();
        runtime.Console.CompleteEmission();
        runtime.Os?.CompleteEmission();
        runtime.ChildProcess?.CompleteEmission();
        runtime.Process.CompleteEmission();
        runtime.EventLoop.CompleteEmission();
        runtime.Buffer?.CompleteEmission();
        runtime.ArrayStorage.CompleteEmission();
        runtime.ArrayOperations.CompleteEmission();
        runtime.Crypto?.CompleteEmission();
        runtime.WebCrypto.CompleteEmission();
        runtime.Dns?.CompleteEmission();
        runtime.Zlib?.CompleteEmission();
        runtime.Tls?.CompleteEmission();
        runtime.Dgram?.CompleteEmission();
        runtime.Net?.CompleteEmission();
        runtime.Http?.CompleteEmission();
        runtime.Fetch.CompleteEmission();
        runtime.Promise?.CompleteEmission();
        return runtime;
    }
}
