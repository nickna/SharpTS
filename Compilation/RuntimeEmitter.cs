using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the runtime support types into the generated assembly.
/// This makes compiled DLLs standalone without requiring SharpTS.dll.
/// </summary>
public partial class RuntimeEmitter
{
    private readonly TypeProvider _types;

    /// <summary>
    /// Feature gating set — populated by <see cref="EmitAll(ModuleBuilder, RuntimeFeatureSet)"/>
    /// and consulted by individual <c>Emit*</c> methods to skip emission of helper types
    /// (and any <c>$Runtime</c> methods that depend on those helper types) the program
    /// doesn't need. Defaults to "emit everything" when an older overload is used.
    /// </summary>
    private RuntimeFeatureSet _features = RuntimeFeatureSet.EmitEverything();

    public RuntimeEmitter(TypeProvider types)
    {
        _types = types;
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
        var runtime = new EmittedRuntime();

        // Emit $Undefined singleton class first (other methods need this type)
        EmitUndefinedClass(moduleBuilder, runtime);

        // Forward-declare the $Runtime class plus a handful of helper signatures
        // (Stringify, CreateException) so types that emit BEFORE EmitRuntimeClass
        // — most importantly $RegExp, whose Symbol.* protocol helpers want to
        // call them — can refer to the MethodBuilders. Bodies fill in later
        // during EmitRuntimeClass / EmitStringify / EmitCreateException, which
        // re-use the pre-allocated TypeBuilder + MethodBuilders.
        DefineRuntimeClassPhase1(moduleBuilder, runtime);

        // Emit IUnionType marker interface first (union types need to implement this)
        EmitIUnionTypeInterface(moduleBuilder, runtime);

        // Emit a tiny dedicated type holding the thread-static `_currentArguments` slot
        // that $TSFunction.Invoke publishes so JS `arguments` capture can see caller
        // values beyond declared arity. Lives on its own type — adding it to
        // $TSFunction regressed Intl's formatRangeToParts test in opaque ways tied to
        // that type's field layout; isolating keeps $TSFunction's layout unchanged.
        EmitArgumentsContextClass(moduleBuilder, runtime);

        // Marker attribute for "this method's body reads JS `arguments`".
        // Must be defined+created before EmitTSFunctionClass so its ctor IL can
        // ldtoken the type for the IsDefined read.
        EmitCapturesArgumentsAttribute(moduleBuilder, runtime);

        // Marker attribute for "this is a user TS function; pad omitted args with the
        // `undefined` sentinel". Defined+created before EmitTSFunctionClass so the ctor IL
        // can ldtoken the type for the IsDefined read in AdjustArgs caching. (#640)
        EmitPadUndefinedAttribute(moduleBuilder, runtime);

        // Marker attribute for "this method's first parameter is the synthetic `__this` receiver".
        // Defined+created before EmitTSFunctionClass so the ctor IL can ldtoken the type for the
        // IsDefined read that backstops the (ref-asm-fragile) parameter-name check. (#738)
        EmitExpectsThisAttribute(moduleBuilder, runtime);

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

        // Emit $TSDate class for standalone Date support — gated on UsesDate.
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSDate
        if (features.UsesDate)
            EmitTSDateClass(moduleBuilder, runtime);

        // Emit $Error class hierarchy for standalone error support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSError and subclasses
        EmitTSErrorClasses(moduleBuilder, runtime);

        // Emit $Promise class for standalone Promise support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSPromise
        EmitTSPromiseClass(moduleBuilder, runtime);

        // Emit $ArrayHole singleton first — $Array methods reference
        // $ArrayHole.Instance for padding intermediate positions on sparse writes
        // and `a.length = N` extensions.
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.ArrayHole
        EmitArrayHoleClass(moduleBuilder, runtime);

        // Per-thread args[] pool used by method-call dispatch to skip
        // newarr per `obj.method(a, b)` invocation. Lives on a separate
        // class — historically to avoid the layout-sensitive .NET 10
        // tier-0 JIT bug behind issue #39 (since fixed upstream).
        EmitCallArgsPool(moduleBuilder, runtime);

        // Emit $Array class for standalone array support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSArray
        EmitTSArrayClass(moduleBuilder, runtime);

        // Emit $IHasFields interface for unified property access
        // Must come before $Object which implements it
        EmitHasFieldsInterface(moduleBuilder, runtime);

        // Emit $Object class for standalone object support
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSObject
        EmitTSObjectClass(moduleBuilder, runtime);

        // Emit $PropertyDescriptorStore and $CompiledPropertyDescriptor early
        // so types that build property descriptors during their own emission
        // ($RegExp.Exec attaches `index`/`input`/`groups` to its Array result
        // via PDS so the result remains a List<object?> for `instanceof Array`)
        // can reference CompiledPropertyDescriptorType / PDSDefineProperty.
        EmitPropertyDescriptorTypes(moduleBuilder, runtime);

        // Emit $RegExp class for standalone regex support — gated on UsesRegExp.
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSRegExp
        if (features.UsesRegExp)
            EmitTSRegExpClass(moduleBuilder, runtime);

        // AssertionError now lives in stdlib/node/assert.ts (embedded stdlib migration).
        // Emit $NodeError class for standalone fs module support
        // NOTE: Must stay in sync with NodeError in Runtime/BuiltIns/Modules/NodeError.cs
        EmitNodeErrorClass(moduleBuilder, runtime);

        // Emit $Buffer class for standalone buffer support — gated on UsesBuffer.
        // Implied by crypto/fs/zlib/http/fetch/dgram/net (their methods return
        // or consume Buffer values), so the gate only fires when ALL of those
        // are off too.
        // NOTE: Must come before $Hash and $Hmac since they return Buffer
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSBuffer
        if (features.UsesBuffer)
            EmitTSBufferClass(moduleBuilder, runtime);

        // Crypto helper types — gated on UsesCrypto. All references are confined
        // to crypto's own emit files; no central-dispatch fallout.
        if (features.UsesCrypto)
        {
            EmitTSHashClass(moduleBuilder, runtime);
            EmitTSHmacClass(moduleBuilder, runtime);
            EmitTSCipherClass(moduleBuilder, runtime);
            EmitTSDecipherClass(moduleBuilder, runtime);
            EmitTSSignTypeDefinition(moduleBuilder, runtime);
            EmitTSVerifyTypeDefinition(moduleBuilder, runtime);
            EmitTSKeyObjectClass(moduleBuilder, runtime);
            EmitTSECDHTypeDefinition(moduleBuilder, runtime);
            EmitBoundECDHMethodTypeDefinition(moduleBuilder, runtime);
            EmitTSDHTypeDefinition(moduleBuilder, runtime);
            EmitBoundDHMethodTypeDefinition(moduleBuilder, runtime);
        }

        // Emit $EventLoop singleton (must come before timer types and net/http types that call Ref/Unref/Schedule)
        EmitTSEventLoopClass(moduleBuilder, runtime);

        // Emit $VirtualTimer class for virtual timer support (single-threaded semantics)
        // Must come after TSFunction (uses TSFunctionType)
        // Must come BEFORE TSTimeoutClass (TSTimeout references VirtualTimer)
        EmitVirtualTimerClass(moduleBuilder, runtime);

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
        EmitBoundTSFunctionClass(moduleBuilder, runtime);

        // Emit $AsyncLocalStorage class for async context propagation
        // Must come after TSFunction (Run/Exit invoke callbacks via TSFunctionInvoke)
        if (features.UsesAsyncLocalStorage)
            EmitAsyncLocalStorageClass(moduleBuilder, runtime);

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

        // Emit cluster types for standalone cluster support
        // NOTE: Must come after EventEmitter ($ClusterWorker and $ClusterManager extend it)
        if (features.UsesCluster)
            EmitClusterTypes(moduleBuilder, runtime);

        // FS-only types — gated on UsesFs together with the FS module methods.
        if (features.UsesFs)
        {
            // Emit $FileDescriptorTable for standalone fs fd-based operations (Phase 21)
            // NOTE: Must come after $NodeError (uses NodeErrorCtor for EBADF errors)
            EmitFileDescriptorTableType(moduleBuilder, runtime);

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
            EmitSharedArrayBufferType(moduleBuilder, runtime);
            EmitDataViewType(moduleBuilder, runtime);
            EmitTypedArrayTypes(moduleBuilder, runtime);
            // $BoundTypedArrayMethod Phase 1 (#940): callable wrapper for typed-array bulk methods.
            // Needs $TypedArray defined (above); must precede EmitRuntimeClass, whose invocation
            // helpers and GetTypedArrayMember reference its type/ctor/Invoke.
            EmitBoundTypedArrayMethodTypeDefinition(moduleBuilder, runtime);
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
            EmitTSDuplexFinalize(runtime);                         // Phase 2: CreateType
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

        // util.promisify family — gated on UsesUtilPromisify. Matching dispatch
        // arms in EmitInvokeValue / EmitInvokeMethodValue / EmitTypeOf are gated
        // on the same flag.
        if (features.UsesUtilPromisify)
        {
            EmitTSDeprecatedFunctionClass(moduleBuilder, runtime);
            EmitTSCallbackifiedFunctionClass(moduleBuilder, runtime);
            EmitPromisifyCallbackClass(moduleBuilder, runtime);  // Must come before PromisifiedFunction
            EmitTSPromisifiedFunctionClass(moduleBuilder, runtime);
        }
        // TextEncoder/Decoder — gated on UsesTextEncoding. $TextDecoderDecodeMethod
        // is referenced from EmitInvokeValue's dispatch, gated on the same flag.
        if (features.UsesTextEncoding)
        {
            EmitTSTextEncoderClass(moduleBuilder, runtime);
            EmitTSTextDecoderClass(moduleBuilder, runtime);
            EmitTSTextDecoderDecodeMethodClass(moduleBuilder, runtime);
        }

        // $StringDecoder class removed — StringDecoder migrated to
        // stdlib/node/string_decoder.ts (pure-TS over the Buffer JS API).

        // Emit $Stats class for fs.stat() and related methods — gated on UsesFs.
        // Must come before fs module methods which use it. Conditional Isinst
        // in GetFieldsProperty's central dispatch (Properties.cs) is gated on
        // the same flag.
        if (features.UsesFs)
            EmitStatsClass(moduleBuilder, runtime);

        // Emit $CJSModule — backs the `module` local bound in every CJS module init.
        // Gated on UsesCjsRequire (the detector flips this whenever the program
        // mentions `require`, `module`, `exports`, or has a require('...') call).
        if (features.UsesCjsRequire)
            EmitCjsModuleClass(moduleBuilder, runtime);

        // Emit $Arguments : List<object> marker subclass. Must come before
        // any IL that constructs `arguments` (ILCompiler.Functions.cs uses
        // runtime.ArgumentsDefaultCtor / ArgumentsEnumerableCtor).
        EmitArgumentsTypeDefinition(moduleBuilder, runtime);

        // Emit $BoundArrayMethod type and constructor (Phase 1)
        // Must come before EmitRuntimeClass so GetListProperty can use the constructor
        EmitBoundArrayMethodTypeDefinition(moduleBuilder, runtime);

        // Emit $BoundMapMethod / $BoundSetMethod types and constructors (Phase 1)
        // Must come before EmitRuntimeClass so GetMapProperty/GetSetProperty can use them.
        // Gated alongside the rest of Map/Set emission.
        if (features.UsesMap)
            EmitBoundMapMethodTypeDefinition(moduleBuilder, runtime);
        if (features.UsesSet)
            EmitBoundSetMethodTypeDefinition(moduleBuilder, runtime);

        // Emit $BoundAnyFunction (the partial-apply wrapper for .bind on non-$TSFunction
        // callables) and the function bind/call/apply wrappers. All reference the
        // Bound*Method TypeBuilders above, so they MUST come after Phase 1 of those.
        // They come before EmitRuntimeClass so GetFunctionMethod (inside EmitRuntimeClass)
        // can use their constructors.
        EmitBoundAnyFunctionClass(moduleBuilder, runtime);
        EmitFunctionBindWrapperClass(moduleBuilder, runtime);
        EmitFunctionCallWrapperClass(moduleBuilder, runtime);
        EmitFunctionApplyWrapperClass(moduleBuilder, runtime);

        // Emit $MethodCallable type and constructor (Phase 1)
        // Must come before EmitRuntimeClass so GetFieldsProperty can wrap GetMember results
        EmitMethodCallableTypeDefinition(moduleBuilder, runtime);

        // Emit $TemplateStringsList class for tagged template literals
        // Must come before EmitRuntimeClass so InvokeTaggedTemplate can use the constructor
        EmitTemplateStringsListClass(moduleBuilder, runtime);

        // $PropertyDescriptorStore is now emitted earlier (just before $RegExp)
        // so types that need CompiledPropertyDescriptorType during their own
        // emission can reference it. This used to live here.

        // Net / Dgram types — gated on UsesNet / UsesDgram. UsesNet is implied
        // by UsesHttp and UsesTls (both extend $NetServer-style sockets).
        if (features.UsesNet)
        {
            EmitTSNetSocketPhase1(moduleBuilder, runtime);
            EmitTSNetServerPhase1(moduleBuilder, runtime);
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
            EmitReadlineInterfaceTypeDefinition(moduleBuilder, runtime);

        // Emit $FinRegEntry type (finalizer helper for FinalizationRegistry)
        // Must come before EmitRuntimeClass so Register can use the constructor
        if (features.UsesFinalizationRegistry)
            EmitFinRegEntryTypeDefinition(moduleBuilder, runtime);

        // FS stream/watcher types — gated on UsesFs. EmitFsModuleMethods is
        // also gated below in EmitRuntimeClass on the same flag, so dependent
        // runtime methods skip in tandem.
        if (features.UsesFs)
        {
            EmitFsStreamTypeDefinitions(moduleBuilder, runtime);
            EmitFsWatcherClass(moduleBuilder, runtime);
            EmitStatWatcherClass(moduleBuilder, runtime);
        }

        // Emit $Runtime class with all helper methods
        EmitRuntimeClass(moduleBuilder, runtime);

        // Emit $Runtime.NewOnFunction — the JS `new` protocol for runtime-valued
        // function callees. Depends on $Object, $TSFunction, $BoundTSFunction, and
        // the $Runtime type itself all being defined.
        EmitNewOnFunction(_runtimeTypeBuilder!, runtime);

        // Dynamic-callee `new x(...)` dispatch for state-machine emitters (#224).
        // Must follow EmitNewOnFunction — it calls through runtime.NewOnFunction.
        EmitConstructDynamicValue(_runtimeTypeBuilder!, runtime);

        // General NewPromiseCapability (#349): the $PromiseCapability holder type
        // and the body of the pre-declared NewPromiseCapabilityResult helper.
        // Must follow EmitConstructDynamicValue (it calls through that helper) and
        // EmitRuntimeClass (depends on InvokeValue / WrapException).
        EmitPromiseCapabilitySupport(moduleBuilder, runtime);

        // AbortSignal / Intl value-position singletons (#224). Must follow
        // EmitRuntimeClass — they wrap the AbortSignal*/CreateIntl* helpers
        // emitted there.
        EmitNamespaceSingletons(_runtimeTypeBuilder!, runtime);

        // Emit $BroadcastChannel — extends $EventEmitter, dispatches via $EventLoop,
        // and clones messages via $Runtime.StructuredClone (populated during EmitRuntimeClass
        // → EmitWorkerHelpers → EmitStructuredCloneHelper).
        // NOTE: Must come after EmitRuntimeClass so runtime.StructuredCloneClone is set.
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSBroadcastChannel
        if (features.UsesBroadcastChannel)
            EmitBroadcastChannelClass(moduleBuilder, runtime);

        // Emit $MessagePort/$MessageChannel — same constraints as
        // $BroadcastChannel ($EventEmitter base, $EventLoop dispatch,
        // $Runtime.StructuredClone for per-message cloning). Unconditional,
        // matching the previous CreateMessageChannel helper (#222).
        // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSMessagePort
        EmitMessageChannelTypes(moduleBuilder, runtime);

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
        if (features.UsesReflectMetadata)
            EmitReflectMetadataDecoratorClass(moduleBuilder, runtime);

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
        if (features.UsesMap)
            EmitBoundMapMethodFinalize(runtime);
        if (features.UsesSet)
            EmitBoundSetMethodFinalize(runtime);

        // Finalize $MethodCallable with Invoke method (Phase 2)
        EmitMethodCallableFinalize(runtime);

        // Net / Http / Tls / Dgram phase-1b/phase-2 finalize work — gated on
        // their own feature flags. UsesHttp ⇒ UsesNet, UsesTls ⇒ UsesNet.
        if (features.UsesNet)
        {
            EmitNetClosureTypes(moduleBuilder, runtime);
        }

        if (features.UsesHttp)
            EmitHttpServerAcceptWorkerBody(runtime);

        if (features.UsesNet)
        {
            EmitTSNetSocketPhase2(runtime);
            EmitTSNetServerPhase2(runtime);
        }

        if (features.UsesDgram)
        {
            EmitDgramMessageClosureClass(moduleBuilder, runtime);
            EmitDgramReceiveWorkerBody(runtime);
            EmitDatagramSocketFinalize(runtime);
        }

        if (features.UsesTls)
        {
            EmitTlsAcceptClosureClass(moduleBuilder, runtime);
            EmitTlsConnectClosureClass(moduleBuilder, runtime);
            EmitTlsConnectBody(runtime);
            // $TlsSocket Phase 2: emit method bodies + CreateType. Must come after the
            // connect closure (its Connect body sets $TlsSocket fields) and after
            // $NetSocket.CreateType (base, already finalized above).
            EmitTlsSocketFinalize(runtime);
        }

        EmitRuntimeClassFinalize();     // Finalize $Runtime after all method bodies

        if (features.UsesTls)
        {
            EmitTlsServerAcceptWorkerBody(runtime);
            EmitTlsServerFinalize();
        }

        // Finalize $ReadlineInterface class (Phase 2)
        // Must come after EmitRuntimeClass (Question uses InvokeValue)
        if (features.UsesReadline)
            EmitReadlineInterfaceFinalize(runtime);

        // Crypto Phase-2 finalize calls — gated on UsesCrypto with the type
        // emission above.
        if (features.UsesCrypto)
        {
            EmitTSSignFinalize(runtime);
            EmitTSVerifyFinalize(runtime);
            EmitTSECDHFinalize(runtime);
            EmitBoundECDHMethodFinalize(runtime);
            EmitTSDHFinalize(runtime);
            EmitBoundDHMethodFinalize(runtime);
        }

        return runtime;
    }
}
