using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Phase-1 forward declaration of the <c>$Runtime</c> class plus a small
    /// set of helper-method signatures. The full $Runtime class is emitted
    /// much later (<see cref="EmitRuntimeClass"/>), but other helper types
    /// — most importantly <c>$RegExp</c> whose Symbol.* protocol methods
    /// want to call <c>Stringify</c> and <c>CreateException</c> — emit
    /// before then. By pre-creating the TypeBuilder and reserving the two
    /// MethodBuilders here, those callers can emit a <c>Call</c> /
    /// <c>Newobj</c> against them right away; CLR finalises everything when
    /// each TypeBuilder's <c>CreateType</c> runs at the end of emission.
    /// </summary>
    private void DefineRuntimeClassPhase1(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public static class $Runtime
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$Runtime",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.RuntimeClass.Type = typeBuilder;

        // Reserve Stringify(object) → string. EmitStringify fills the body
        // later; it must skip its own DefineMethod call when this signature
        // is already present.
        runtime.StringCoercion.Stringify = typeBuilder.DefineMethod(
            "Stringify",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);

        // Reserve FormatNumber(double) → string. EmitFormatNumberMethod fills the
        // body later; Stringify's double case calls it.
        runtime.Numbers.Format = typeBuilder.DefineMethod(
            "FormatNumber",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Double]);

        // Reserve CreateException(object) → Exception. EmitCreateException
        // similarly fills the body later.
        runtime.Errors.CreateException = typeBuilder.DefineMethod(
            "CreateException",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Exception,
            [_types.Object]);

        // Reserve GetProperty(object, string) → object — generic property
        // reader used by $RegExp's Symbol.* protocol slow path to read
        // `exec`/`flags`/`lastIndex` etc. via the spec-aligned chain.
        DeclareObjectReadProperty(typeBuilder, runtime.ObjectRead);

        // Reserve SetProperty(object, string, object) → void.
        DeclareObjectWriteProperty(typeBuilder, runtime.ObjectWrite);

        // Proxy [[Set]] needs the receiver-aware OrdinarySet helper while
        // SetPropertyStrict is being emitted, before Reflect's public methods
        // receive their bodies later in EmitRuntimeClass.
        if (runtime.Reflect.Assignment is not null)
        {
            runtime.Reflect.RequireAssignment().Set = typeBuilder.DefineMethod(
                "ReflectSet",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Boolean,
                [_types.Object, _types.Object, _types.Object, _types.Object]);
        }

        // Reflect.set and trapless Proxy [[Set]] must use the boolean-returning
        // [[DefineOwnProperty]] operation rather than Object.defineProperty's
        // throwing wrapper. Reserve it here so ReflectSet can call it before
        // its body is emitted later in EmitRuntimeClass.
        if (runtime.Reflect.Assignment is not null)
        {
            runtime.Reflect.RequireAssignment().DefineProperty = typeBuilder.DefineMethod(
                "ReflectDefineProperty",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Boolean,
                [_types.Object, _types.Object, _types.Object]);
        }

        // Reserve GetProcessObject() → object — the live $Process singleton
        // (epic #1078). Body emitted by EmitProcessObjectInfrastructure; the
        // signature must exist earlier because GlobalThisGetProperty (emitted
        // before EmitProcessMethods) resolves `globalThis.process` through it.
        runtime.Process.GetObject = typeBuilder.DefineMethod(
            "GetProcessObject",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes);

        // Reserve GetWebCryptoObject() → object — the $WebCrypto singleton (#1063).
        // Body emitted by EmitWebCryptoTypes (crypto in use) or the stub; the
        // signature must exist earlier because GlobalThisGetProperty resolves
        // `globalThis.crypto` through it.
        DeclareGetWebCryptoObject(typeBuilder, runtime.WebCrypto);

        // Reserve GlobalThisGetProperty(string) → object and
        // GlobalThisSetProperty(string, object) → void. EmitGlobalThisMethods
        // (run much later) fills the bodies, but GetProperty/GetIndex/SetProperty/
        // SetIndex — emitted before it — route the value-position globalThis
        // sentinel through these, so the signatures must exist now (#271).
        runtime.GlobalObject.GetProperty = typeBuilder.DefineMethod(
            "GlobalThisGetProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String]);
        runtime.GlobalObject.SetProperty = typeBuilder.DefineMethod(
            "GlobalThisSetProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.String, _types.Object]);

        // Reserve the native-error Type-token adapter before Reflect.construct
        // is emitted. EmitErrorMethods fills its body later, after CreateError
        // and descriptor support are available.
        runtime.Errors.CreateErrorFromTypeOrNull = typeBuilder.DefineMethod(
            "CreateErrorFromTypeOrNull",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Type, _types.ObjectArray]);
        // Shared backing store for value-form global assignments. DeleteProperty
        // is emitted before the GlobalThis helper bodies, so the field must be
        // reserved in phase 1 alongside their method signatures.
        runtime.GlobalObject.Properties = typeBuilder.DefineField(
            "_globalThisProperties",
            _types.DictionaryStringObject,
            FieldAttributes.Private | FieldAttributes.Static);

        // Symbol-keyed class accessor registry (#266). GetIndex/SetIndex (emitted
        // during EmitRuntimeClass) call FindSymbol{Getter,Setter}For, and class
        // .cctors (emitted later still) call RegisterSymbolAccessor.
        DefineSymbolAccessorRegistry(typeBuilder, runtime.SymbolAccessors);

        // Reserve ToNumber(object) → double. Used by $RegExp's Symbol.split
        // to coerce `limit` per ECMA-262 §22.2.5.13 step 7 (and to throw
        // TypeError on Symbol limits). EmitToNumber later fills the body.
        runtime.NumericCoercion.ToNumber = typeBuilder.DefineMethod(
            "ToNumber",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]);

        // Reserve strict ToBigInt for DataView's BigInt setter adapters. The
        // adapters must be emitted before GetProperty can bind method values;
        // EmitStrictToBigInt fills this body later after observable
        // object-to-primitive helpers are available.
        if (_features.UsesBigInt)
        {
            runtime.BigInt.RequireImplementation().ToBigInt = typeBuilder.DefineMethod(
                "ToBigInt",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object]);
        }

        // Reserve ToJsString(object) → string. Stringify (which $RegExp's
        // Symbol.* helpers currently call) doesn't run @@toPrimitive on
        // objects; ToJsString does (ECMA-262 §7.1.1). Forward-declared so
        // Symbol.{replace,split,matchAll}'s flags coercion can route through
        // the spec-aligned ToString chain.
        runtime.StringCoercion.ToJsString = typeBuilder.DefineMethod(
            "ToJsString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);

        // Reserve the full RegExp.prototype[@@split] protocol. $RegExp is
        // emitted before the runtime helpers it needs (dynamic construction,
        // generic Get/Set, and RegExpExec), so its public symbol wrapper calls
        // this forward declaration; the body is filled after those helpers.
        if (runtime.RegExps.Implementation is not null)
        {
            runtime.RegExps.RequireImplementation().SymbolSplitProtocol = typeBuilder.DefineMethod(
                "RegExpSymbolSplitProtocol",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object, _types.Object, _types.Object]);
            runtime.RegExps.RequireImplementation().SymbolMatchAllProtocol = typeBuilder.DefineMethod(
                "RegExpSymbolMatchAllProtocol",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object, _types.Object]);
        }

        // Reserve StringifyCoerce(object) → string — Stringify plus the
        // ECMA-262 §7.1.17 Symbol guard (implicit ToString coercion throws
        // TypeError for Symbol values). Reserved here because $Runtime.Add's
        // string-concat arm (emitted before EmitToJsString) references it;
        // EmitStringifyCoerce fills the body later.
        runtime.StringCoercion.StringifyCoerce = typeBuilder.DefineMethod(
            "StringifyCoerce",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);

        // Reserve JsToInt32(object) → int. ECMA-262 §7.1.6 ToInt32 chain
        // (ToNumber → ToPrimitive → valueOf). Forward-declared so $RegExp's
        // Symbol.match empty-match advance can read `lastIndex` and propagate
        // `valueOf` throws, and so SetProperty's `r.lastIndex = obj` coerce
        // path can route through the spec-aligned ToInt32 chain. EmitJsToInt32
        // later fills the body.
        runtime.NumericCoercion.JsToInt32 = typeBuilder.DefineMethod(
            "JsToInt32",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32,
            [_types.Object]);

        // Reserve ToObject(object) → object. ECMA-262 §7.1.18 ToObject.
        // Forward-declared so Object.assign (emitted in EmitObjectMethods
        // before EmitToObject) can coerce its target arg via runtime.BoxedPrimitives.ToObject.
        // Body filled later by EmitToObject.
        runtime.BoxedPrimitives.ToObject = typeBuilder.DefineMethod(
            "ToObject",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);

        // Reserve IsTruthy(object) → bool. ECMA-262 §7.1.2 ToBoolean.
        // Forward-declared so $RegExp's Symbol.match can spec-align its
        // `global`/`unicode`/`sticky` reads via `ToBoolean(? Get(rx, name))`
        // — supports the coerce-global / coerce-sticky / coerce-unicode tests
        // where the user overrides those properties on a $RegExp instance.
        // EmitIsTruthy later fills the body.
        runtime.Booleans.IsTruthy = typeBuilder.DefineMethod(
            "IsTruthy",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);

        // Reserve the RegExp.prototype dictionary field. $RegExp emits
        // before EmitRuntimeClass and its accessor helpers
        // (TSRegExpProtoGet*) need to compare `__this` against this field
        // to detect the "called on RegExp.prototype itself" spec case.
        // Cctor in EmitRuntimeClass initializes it to a fresh Dictionary;
        // RegExpPrototypePopulate fills the per-process singleton lazily.
        runtime.RegExps.Prototype = typeBuilder.DefineField(
            "_regexpPrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);

        // $RegExp is emitted before the main $Runtime body. Its guarded
        // intrinsic protocol helpers inspect the current RegExp.prototype
        // descriptors, so reserve the populate method token here; the body is
        // still filled by EmitRegExpPrototypePopulate with the rest of the
        // runtime prototype machinery.
        if (runtime.RegExps.Implementation is not null)
        {
            runtime.RegExps.PopulatePrototype = typeBuilder.DefineMethod(
                "_RegExpPrototypePopulate",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Void,
                Type.EmptyTypes);
        }

        // Forward-declare the globalThis/global sentinel field (#271). $TSFunction's
        // InvokeWithThis (emitted in EmitTSFunctionClass, BEFORE EmitRuntimeClass)
        // coerces a null sloppy-this thisArg to this sentinel, so the field must
        // exist now. EmitRuntimeClass initialises it in the .cctor (new object());
        // EmitRuntimeClass MUST skip its own DefineField when this is already set.
        runtime.GlobalObject.SingletonField = typeBuilder.DefineField(
            "_globalThisSingleton",
            _types.Object,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.GlobalObject.CompleteDeclarations();
    }
}
