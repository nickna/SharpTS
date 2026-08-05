using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Walks the parsed AST and produces a <see cref="RuntimeFeatureSet"/> recording
/// which categories of runtime helper types the program actually needs.
/// Used by <see cref="ILCompiler"/> to skip emitting unused machinery.
///
/// <para>
/// <b>Bias toward over-emitting.</b> Each feature flag starts <c>false</c> and
/// is flipped to <c>true</c> on any plausible AST trigger. We don't try to
/// prove the trigger is reachable or even type-correct — a literal mention of
/// <c>BroadcastChannel</c> as an identifier flips <see cref="RuntimeFeatureSet.UsesBroadcastChannel"/>
/// even if the surrounding context turns out to be dead code. False positives
/// just inflate the DLL slightly; false negatives produce <c>TypeLoadException</c>
/// at runtime, which is much worse.
/// </para>
///
/// <para>
/// <b>What's NOT detected.</b> Tier B features (<c>$Promise</c> + state machines,
/// <c>$RegExp</c>, <c>$TSDate</c>, <c>$Map</c>/<c>$Set</c>) aren't in the feature
/// set yet — those get emitted unconditionally for now and will be gated in
/// Phase 2. Likewise <c>$Runtime</c> itself is always emitted; per-method
/// shaking inside it is Phase 3.
/// </para>
/// </summary>
public sealed class RuntimeFeatureDetector
{
    private readonly RuntimeFeatureSet _set;

    public RuntimeFeatureDetector()
    {
        // Start with everything off; the walk flips flags on as triggers are seen.
        _set = new RuntimeFeatureSet
        {
            UsesNet = false,
            UsesHttp = false,
            UsesTls = false,
            UsesDgram = false,
            UsesDns = false,
            UsesFetch = false,
            UsesFs = false,
            UsesCrypto = false,
            UsesZlib = false,
            UsesNodeStreams = false,
            UsesWebStreams = false,
            UsesCluster = false,
            UsesBroadcastChannel = false,
            UsesAsyncLocalStorage = false,
            UsesReadline = false,
            UsesTextEncoding = false,
            UsesFinalizationRegistry = false,
            UsesReflectMetadata = false,
            UsesCjsRequire = false,
            UsesJSON = false,
            UsesIntl = false,
            UsesReflect = false,
            UsesIteratorHelpers = false,
            UsesDate = false,
            UsesRegExp = false,
            UsesBuffer = false,
            UsesBigInt = false,
            UsesOs = false,
            UsesChildProcess = false,
            UsesVm = false,
            UsesSourceExecution = false,
            UsesTty = false,
            UsesPerf = false,
            UsesAbortController = false,
            UsesAbortSignalAny = false,
            UsesProxy = false,
            UsesDynamicImport = false,
            UsesAsyncGenerator = false,
            UsesForAwaitOf = false,
            UsesWeakRef = false,
            UsesWeakMap = false,
            UsesWeakSet = false,
            UsesMap = false,
            UsesSet = false,
            TypedArrays = RuntimeFeatureSet.TypedArrayKinds.None,
        };
    }

    public RuntimeFeatureSet Detect(List<Stmt> statements)
    {
        foreach (var stmt in statements)
            VisitStmt(stmt);

        // Implications between feature flags (one feature pulls in another's
        // emit machinery). Applied after the walk so flags-set-by-trigger
        // can cascade once.
        if (_set.UsesFetch)
        {
            // fetch(), Headers, Request, Response all emit through the HTTP
            // module's EmitHttpModuleMethods + EmitHeadersClass.
            _set.UsesHttp = true;
        }
        if (_set.UsesHttp)
        {
            // $HttpServer extends $NetServer, so HTTP types must come with net.
            _set.UsesNet = true;
            // $Runtime's HTTP module methods call JsonParse/JsonStringify for
            // request/response body handling.
            _set.UsesJSON = true;
        }
        if (_set.UsesTls)
        {
            // $TlsSocket etc. extend $NetSocket-ish plumbing.
            _set.UsesNet = true;
        }
        if (_set.UsesFs || _set.UsesHttp || _set.UsesZlib)
        {
            // $FsReadStream / $FsWriteStream extend $Readable / $Writable.
            // $HttpServer's responder writes to a $Writable (chunked encoding).
            // $TSZlibTransform extends $Transform.
            _set.UsesNodeStreams = true;
        }
        // $Buffer is the return type of crypto's hash.digest, fs.readFileSync,
        // zlib.gzipSync, fetch's response.arrayBuffer, dgram messages, etc.
        // TextEncoder.encode also returns a $Buffer.
        // Imply UsesBuffer from any feature that produces or consumes Buffer.
        if (_set.UsesCrypto || _set.UsesFs || _set.UsesZlib || _set.UsesHttp
            || _set.UsesFetch || _set.UsesDgram || _set.UsesNet
            || _set.UsesTextEncoding)
        {
            _set.UsesBuffer = true;
        }
        // WebCrypto (#1063): subtle results are $ArrayBuffer and getRandomValues
        // consumes $TypedArray views, so crypto implies both base types.
        if (_set.UsesCrypto)
        {
            _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.ArrayBuffer
                              | RuntimeFeatureSet.TypedArrayKinds.TypedArrayBase;
        }
        // Anything that needs a typed-array kind also needs $TypedArray + $ArrayBuffer.
        if (_set.TypedArrays != RuntimeFeatureSet.TypedArrayKinds.None)
        {
            _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.ArrayBuffer
                              | RuntimeFeatureSet.TypedArrayKinds.TypedArrayBase;
        }
        // BigInt64Array / BigUint64Array store BigInteger values. Even without
        // a `123n` literal in the source, arithmetic on their elements lowers
        // to BigInt helpers in the type-checker. Imply UsesBigInt to keep them
        // emitted.
        if ((_set.TypedArrays & (RuntimeFeatureSet.TypedArrayKinds.BigInt64
                              | RuntimeFeatureSet.TypedArrayKinds.BigUint64)) != 0)
        {
            _set.UsesBigInt = true;
        }
        // ReadableStream's pipeTo / pipeThrough check signal abort state, and
        // fetch's request handler honours AbortSignal cancellation. Imply
        // UsesAbortController so AbortSignalGetAborted/GetReason exist.
        if (_set.UsesWebStreams || _set.UsesFetch || _set.UsesHttp)
        {
            _set.UsesAbortController = true;
        }
        // AbortSignal.any is emitted as part of the AbortController method cluster
        // and registered on the AbortSignal namespace singleton, so it needs the
        // base AbortController machinery present.
        if (_set.UsesAbortSignalAny)
        {
            _set.UsesAbortController = true;
        }

        return _set;
    }

    // ── Module-name → feature mapping ─────────────────────────────────────

    private void HandleModulePath(string path)
    {
        // Strip "node:" prefix that Node.js permits on builtins.
        var p = path.StartsWith("node:") ? path[5..] : path;
        switch (p)
        {
            case "net":
                _set.UsesNet = true; break;
            case "http":
            case "https":
                _set.UsesHttp = true; _set.UsesNet = true; break;  // http server inherits net
            case "tls":
                _set.UsesTls = true; _set.UsesNet = true; break;
            case "dgram":
                _set.UsesDgram = true; break;
            case "dns":
            case "dns/promises":
                _set.UsesDns = true; break;
            case "fs":
            case "fs/promises":
                _set.UsesFs = true; break;
            case "crypto":
            case "crypto/promises":
                _set.UsesCrypto = true; break;
            case "zlib":
            // The stdlib/node/zlib.ts facade's own primitive import — detected when
            // the bundled facade AST is scanned, so zlib IL helpers are emitted even
            // if the user-facing 'zlib' specifier escaped detection (e.g. dynamic require).
            case "primitive:zlib":
                _set.UsesZlib = true; break;
            case "buffer":
                // `import { atob, isUtf8, ... } from 'buffer'` may not reference the
                // Buffer class directly; ensure $Buffer + the module helpers are emitted.
                _set.UsesBuffer = true; break;
            case "os":
                _set.UsesOs = true; break;
            case "child_process":
                // spawn()/fork() expose stdout/stderr as $Readable and stdin as
                // $Writable; exec/execFile with encoding:'buffer' produce a $Buffer.
                _set.UsesChildProcess = true;
                _set.UsesNodeStreams = true;
                _set.UsesBuffer = true;
                break;
            case "vm":
                _set.UsesVm = true; break;
            case "sharpts:execution":
                _set.UsesSourceExecution = true; break;
            case "tty":
                _set.UsesTty = true; break;
            case "perf_hooks":
                _set.UsesPerf = true; break;
            case "stream":
            case "stream/promises":
            case "stream/web":
                _set.UsesNodeStreams = true;
                if (p == "stream/web") _set.UsesWebStreams = true;
                break;
            case "cluster":
                _set.UsesCluster = true; break;
            case "readline":
            case "readline/promises":
                _set.UsesReadline = true; break;
            // `import 'util'` needs no runtime feature flags: the util module is
            // pure stdlib TS compiled with the program (only console.dir's
            // inspect helpers remain emitted, and those are unconditional).
            case "worker_threads":
                _set.UsesBroadcastChannel = true;
                _set.UsesAsyncLocalStorage = true;
                break;
            case "async_hooks":
                _set.UsesAsyncLocalStorage = true; break;
        }
    }

    // ── Bare-identifier triggers ──────────────────────────────────────────

    private void HandleIdentifier(string name)
    {
        switch (name)
        {
            // Fetch family
            case "fetch":
            case "Headers":
            case "Request":
            case "Response":
                _set.UsesFetch = true; break;

            // WebCrypto global (#1063): bare `crypto` / `globalThis.crypto` without
            // an import still needs $WebCrypto + the crypto helpers emitted. A local
            // variable named crypto over-emits harmlessly.
            case "crypto":
                _set.UsesCrypto = true; break;

            // Workers / channels
            case "BroadcastChannel":
                _set.UsesBroadcastChannel = true; break;
            case "AsyncLocalStorage":
                _set.UsesAsyncLocalStorage = true; break;

            // Encoding
            case "TextEncoder":
            case "TextDecoder":
                _set.UsesTextEncoding = true; break;

            // GC observers
            case "FinalizationRegistry":
                _set.UsesFinalizationRegistry = true; break;

            // Web Streams (also detected via `new` below; bare identifier covers
            // patterns like `globalThis.ReadableStream`).
            case "ReadableStream":
            case "WritableStream":
            case "TransformStream":
                _set.UsesWebStreams = true; break;

            // Atomics — operates on typed arrays. Pull in $TypedArray + $ArrayBuffer +
            // $SharedArrayBuffer so the Atomics helpers (gated on HasAnyTypedArray) get
            // emitted along with their dependencies.
            case "Atomics":
                _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.ArrayBuffer
                                  | RuntimeFeatureSet.TypedArrayKinds.SharedArrayBuffer
                                  | RuntimeFeatureSet.TypedArrayKinds.TypedArrayBase;
                break;

            // Typed arrays — bare identifier and `new X(...)` paths land here.
            case "ArrayBuffer":
                _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.ArrayBuffer; break;
            case "SharedArrayBuffer":
                _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.SharedArrayBuffer; break;
            case "DataView":
                _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.DataView
                                  | RuntimeFeatureSet.TypedArrayKinds.ArrayBuffer;
                break;
            case "Int8Array":
                _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.Int8
                                  | RuntimeFeatureSet.TypedArrayKinds.ArrayBuffer
                                  | RuntimeFeatureSet.TypedArrayKinds.TypedArrayBase;
                break;
            case "Uint8Array":
                _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.Uint8
                                  | RuntimeFeatureSet.TypedArrayKinds.ArrayBuffer
                                  | RuntimeFeatureSet.TypedArrayKinds.TypedArrayBase;
                break;
            case "Uint8ClampedArray":
                _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.Uint8Clamped
                                  | RuntimeFeatureSet.TypedArrayKinds.ArrayBuffer
                                  | RuntimeFeatureSet.TypedArrayKinds.TypedArrayBase;
                break;
            case "Int16Array":
                _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.Int16
                                  | RuntimeFeatureSet.TypedArrayKinds.ArrayBuffer
                                  | RuntimeFeatureSet.TypedArrayKinds.TypedArrayBase;
                break;
            case "Uint16Array":
                _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.Uint16
                                  | RuntimeFeatureSet.TypedArrayKinds.ArrayBuffer
                                  | RuntimeFeatureSet.TypedArrayKinds.TypedArrayBase;
                break;
            case "Int32Array":
                _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.Int32
                                  | RuntimeFeatureSet.TypedArrayKinds.ArrayBuffer
                                  | RuntimeFeatureSet.TypedArrayKinds.TypedArrayBase;
                break;
            case "Uint32Array":
                _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.Uint32
                                  | RuntimeFeatureSet.TypedArrayKinds.ArrayBuffer
                                  | RuntimeFeatureSet.TypedArrayKinds.TypedArrayBase;
                break;
            case "Float32Array":
                _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.Float32
                                  | RuntimeFeatureSet.TypedArrayKinds.ArrayBuffer
                                  | RuntimeFeatureSet.TypedArrayKinds.TypedArrayBase;
                break;
            case "Float64Array":
                _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.Float64
                                  | RuntimeFeatureSet.TypedArrayKinds.ArrayBuffer
                                  | RuntimeFeatureSet.TypedArrayKinds.TypedArrayBase;
                break;
            case "BigInt64Array":
                _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.BigInt64
                                  | RuntimeFeatureSet.TypedArrayKinds.ArrayBuffer
                                  | RuntimeFeatureSet.TypedArrayKinds.TypedArrayBase;
                break;
            case "BigUint64Array":
                _set.TypedArrays |= RuntimeFeatureSet.TypedArrayKinds.BigUint64
                                  | RuntimeFeatureSet.TypedArrayKinds.ArrayBuffer
                                  | RuntimeFeatureSet.TypedArrayKinds.TypedArrayBase;
                break;

            // CJS plumbing
            case "require":
            case "module":
            case "exports":
                _set.UsesCjsRequire = true; break;

            // Bare JSON identifier — covers `globalThis.JSON`, `const j = JSON`,
            // and the more common `JSON.parse(...)` (which also passes through
            // HandleMemberAccess). Conservative — flag on any reference.
            case "JSON":
                _set.UsesJSON = true; break;

            // Intl namespace — `Intl.NumberFormat`, `Intl.DateTimeFormat`,
            // `Intl.Collator`. Bare identifier covers `globalThis.Intl` and
            // any value-form access.
            case "Intl":
                _set.UsesIntl = true; break;

            // Reflect — used both for ES2015 Reflect.get/set/has/etc. and for
            // Reflect.metadata (which has its own UsesReflectMetadata flag).
            // We flag Reflect generally; the metadata-specific arms are still
            // flagged separately via HandleMemberAccess for finer granularity.
            case "Reflect":
                _set.UsesReflect = true; break;

            // Date — bare identifier covers `new Date()`, `Date.now()`,
            // `Date.parse(...)`, value-form `globalThis.Date`, etc.
            case "Date":
                _set.UsesDate = true; break;

            // RegExp — bare identifier covers `new RegExp(...)` and
            // `RegExp.X` constructors. Regex literals (/pattern/) get a
            // separate trigger via VisitExpr's RegexLiteral case.
            case "RegExp":
                _set.UsesRegExp = true; break;

            // Node stream class names — both bare references and `new X()` end
            // up here. Conservative — flag if mentioned, even when shadowed.
            case "Readable":
            case "Writable":
            case "Duplex":
            case "Transform":
            case "PassThrough":
                _set.UsesNodeStreams = true; break;

            // Node Buffer — covers `Buffer.from(...)`, `Buffer.alloc(...)`,
            // `new Buffer(...)` (deprecated but still supported), and bare
            // value-form access.
            case "Buffer":
            // Global atob/btoa compile to $Runtime.BufferAtob/BufferBtoa, which compose
            // the $Buffer helpers — ensure $Buffer + the buffer module methods are emitted.
            case "atob":
            case "btoa":
                _set.UsesBuffer = true; break;

            // BigInt — `BigInt(123)` constructor calls, `BigInt.asIntN(...)`,
            // and bare value-form access. The `123n` literal path is detected
            // separately via Expr.Literal in VisitExpr (BigInteger value).
            case "BigInt":
                _set.UsesBigInt = true; break;

            // Performance — `performance.now()`, `performance.timeOrigin`. The
            // perf_hooks module path also flips UsesPerf via HandleModulePath.
            case "performance":
                _set.UsesPerf = true; break;

            // AbortController / AbortSignal — `new AbortController()`,
            // bare value-form access, also implicitly used by fetch/timer cancel.
            case "AbortController":
            case "AbortSignal":
                _set.UsesAbortController = true; break;

            // Proxy — `new Proxy(target, handler)` or bare value-form access.
            case "Proxy":
                _set.UsesProxy = true; break;

            // Weak collections — `new WeakRef(t)`, `new WeakMap()`, `new WeakSet()`.
            // Each has an isolated emitter; methods are not consumed by other
            // emit paths, so the gate is safe.
            case "WeakRef":
                _set.UsesWeakRef = true; break;
            case "WeakMap":
                _set.UsesWeakMap = true; break;
            case "WeakSet":
                _set.UsesWeakSet = true; break;

            // Map / Set collections — bare or `new`. Their dispatch is
            // cross-cutting: GetFieldsProperty and InvokeMethodValue route
            // Dictionary<object,object> / HashSet<object> receivers through
            // GetMapProperty/GetSetProperty. The dispatch arms fold up under
            // the same flag (see RuntimeEmitter.Objects.Properties).
            case "Map":
                _set.UsesMap = true; break;
            case "Set":
                _set.UsesSet = true; break;

            // globalThis / global escape hatch — many runtime identifiers can be
            // resolved through these. Set the broad-net features as a safety
            // valve; the fine-grained ones still get set by their own triggers.
            case "globalThis":
            case "global":
                _set.UsesFetch = true;
                _set.UsesTextEncoding = true;
                _set.UsesWebStreams = true;
                _set.UsesBroadcastChannel = true;
                _set.UsesFinalizationRegistry = true;
                _set.UsesJSON = true;
                _set.UsesIntl = true;
                _set.UsesReflect = true;
                _set.TypedArrays = RuntimeFeatureSet.TypedArrayKinds.All;
                break;
        }
    }

    // ── Member-access triggers ───────────────────────────────────────────

    private void HandleMemberAccess(string objectName, string memberName)
    {
        if (objectName == "Reflect")
        {
            if (memberName == "metadata" || memberName == "defineMetadata" || memberName == "getMetadata")
                _set.UsesReflectMetadata = true;
            // Any other Reflect.X access flips the broader flag (Reflect.set/get/etc.)
            _set.UsesReflect = true;
        }
        // process.stdout / process.stderr / process.stdin are $Writable / $Readable
        // singletons that only exist when Node streams are emitted.
        if (objectName == "process" && (memberName == "stdout" || memberName == "stderr" || memberName == "stdin"))
        {
            _set.UsesNodeStreams = true;
        }
        // JSON.parse / JSON.stringify — accept both lowercase / capitalized forms
        // and uppercase identifier `JSON` (the standard one). Conservative: any
        // member access on `JSON` flips the flag.
        if (objectName == "JSON")
        {
            _set.UsesJSON = true;
        }
        // `os` namespace (`import * as os`) member access — `os.platform()`,
        // `os.cpus()`, etc. The module-path trigger covers the import; this
        // catches the rare case where `os` is bound by other means
        // (e.g. `globalThis.os` or a local with the same name and any-typed).
        if (objectName == "os")
        {
            _set.UsesOs = true;
        }
        // `.isTTY` access on any receiver — typically `process.stdout.isTTY`.
        // Conservative; false positives just over-emit a tiny isatty helper.
        if (memberName == "isTTY")
        {
            _set.UsesTty = true;
        }
        // `AbortSignal.any([...])` late-binds to RuntimeTypes.AbortSignalAnyCompiled via
        // reflection on its normal path, so a program that actually calls it needs SharpTS.dll
        // co-located at runtime. This precise flag drives RequireSharpTSRuntime("AbortSignal.any")
        // without penalising the common, pure-IL AbortController + fetch case (#116).
        if (objectName == "AbortSignal" && memberName == "any")
        {
            _set.UsesAbortSignalAny = true;
        }
    }

    // ── Statement walk ────────────────────────────────────────────────────

    private void VisitStmt(Stmt? stmt)
    {
        if (stmt is null) return;
        switch (stmt)
        {
            case Stmt.Import imp:
                HandleModulePath(imp.ModulePath);
                break;
            case Stmt.ImportRequire req:
                HandleModulePath(req.ModulePath);
                _set.UsesCjsRequire = true;
                break;
            case Stmt.Export exp:
                if (exp.Declaration is not null) VisitStmt(exp.Declaration);
                if (exp.DefaultExpr is not null) VisitExpr(exp.DefaultExpr);
                if (exp.ExportAssignment is not null) VisitExpr(exp.ExportAssignment);
                break;

            case Stmt.Block block:
                foreach (var s in block.Statements) VisitStmt(s);
                break;

            case Stmt.Expression es:
                VisitExpr(es.Expr);
                break;

            case Stmt.Var var:
                if (var.Initializer is not null) VisitExpr(var.Initializer);
                break;

            case Stmt.Const cst:
                VisitExpr(cst.Initializer);
                break;

            case Stmt.AutoAccessor aa:
                if (aa.Initializer is not null) VisitExpr(aa.Initializer);
                break;

            case Stmt.StaticBlock sb:
                foreach (var s in sb.Body) VisitStmt(s);
                break;

            case Stmt.Sequence seq:
                foreach (var s in seq.Statements) VisitStmt(s);
                break;

            case Stmt.Using usg:
                // `using` statement walks an iterable; visit the initializer expression.
                // (AST shape: Using(declarations, etc.) — be defensive about field access.)
                break;

            case Stmt.Function fn:
                if (fn.IsAsync && fn.IsGenerator)
                    _set.UsesAsyncGenerator = true;
                foreach (var p in fn.Parameters)
                    if (p.DefaultValue is not null) VisitExpr(p.DefaultValue);
                if (fn.Body is not null)
                    foreach (var s in fn.Body) VisitStmt(s);
                break;

            case Stmt.Class cls:
                if (cls.SuperclassExpr is not null) VisitExpr(cls.SuperclassExpr);
                foreach (var m in cls.Methods)
                {
                    // Class methods can be `async *foo()` — async generators.
                    // The Stmt.Function visit happens via ClassMembersBuild,
                    // not the top-level Stmt switch, so re-check the flags here.
                    if (m.IsAsync && m.IsGenerator)
                        _set.UsesAsyncGenerator = true;
                    foreach (var p in m.Parameters)
                        if (p.DefaultValue is not null) VisitExpr(p.DefaultValue);
                    if (m.Body is not null)
                        foreach (var s in m.Body) VisitStmt(s);
                }
                foreach (var f in cls.Fields)
                    if (f.Initializer is not null) VisitExpr(f.Initializer);
                if (cls.Accessors is not null)
                    foreach (var a in cls.Accessors)
                        foreach (var s in a.Body) VisitStmt(s);
                if (cls.StaticInitializers is not null)
                    foreach (var s in cls.StaticInitializers) VisitStmt(s);
                break;

            case Stmt.Field field:
                if (field.Initializer is not null) VisitExpr(field.Initializer);
                break;

            case Stmt.Accessor acc:
                foreach (var s in acc.Body) VisitStmt(s);
                break;

            case Stmt.Namespace ns:
                foreach (var s in ns.Members) VisitStmt(s);
                break;

            case Stmt.If ifs:
                VisitExpr(ifs.Condition);
                VisitStmt(ifs.ThenBranch);
                if (ifs.ElseBranch is not null) VisitStmt(ifs.ElseBranch);
                break;
            case Stmt.While w:
                VisitExpr(w.Condition);
                VisitStmt(w.Body);
                break;
            case Stmt.DoWhile dw:
                VisitStmt(dw.Body);
                VisitExpr(dw.Condition);
                break;
            case Stmt.For f:
                if (f.Initializer is not null) VisitStmt(f.Initializer);
                if (f.Condition is not null) VisitExpr(f.Condition);
                if (f.Increment is not null) VisitExpr(f.Increment);
                VisitStmt(f.Body);
                break;
            case Stmt.ForOf fo:
                if (fo.IsAsync)
                    _set.UsesForAwaitOf = true;
                VisitExpr(fo.Iterable);
                VisitStmt(fo.Body);
                break;
            case Stmt.ForIn fi:
                VisitExpr(fi.Object);
                VisitStmt(fi.Body);
                break;
            case Stmt.Return r:
                if (r.Value is not null) VisitExpr(r.Value);
                break;
            case Stmt.Throw t:
                VisitExpr(t.Value);
                break;
            case Stmt.TryCatch ts:
                foreach (var s in ts.TryBlock) VisitStmt(s);
                if (ts.CatchBlock is not null)
                    foreach (var s in ts.CatchBlock) VisitStmt(s);
                if (ts.FinallyBlock is not null)
                    foreach (var s in ts.FinallyBlock) VisitStmt(s);
                break;
            case Stmt.Switch sw:
                VisitExpr(sw.Subject);
                foreach (var c in sw.Cases)
                {
                    VisitExpr(c.Value);
                    foreach (var s in c.Body) VisitStmt(s);
                }
                if (sw.DefaultBody is not null)
                    foreach (var s in sw.DefaultBody) VisitStmt(s);
                break;
            case Stmt.LabeledStatement lab:
                VisitStmt(lab.Statement);
                break;

            // Statements that carry no expressions worth walking.
            default:
                break;
        }
    }

    // ── Expression walk ──────────────────────────────────────────────────

    private void VisitExpr(Expr? expr)
    {
        if (expr is null) return;
        switch (expr)
        {
            case Expr.Variable v:
                HandleIdentifier(v.Name.Lexeme);
                break;

            case Expr.Literal lit when lit.Value is System.Numerics.BigInteger:
                // `123n` literal — BigInt arithmetic helpers are needed even
                // without a bare `BigInt` identifier.
                _set.UsesBigInt = true;
                break;

            case Expr.Get g:
                if (g.Object is Expr.Variable ov)
                    HandleMemberAccess(ov.Name.Lexeme, g.Name.Lexeme);
                // String methods that StringEmitter routes through RegExp
                // helpers (split/replace/replaceAll/match/matchAll/search) need
                // $RegExp helpers emitted even when no /literal/ or `new RegExp`
                // appears. Conservative — flag UsesRegExp on any access of these
                // names regardless of receiver type. False positives just over-emit.
                switch (g.Name.Lexeme)
                {
                    case "split":
                    case "replace":
                    case "replaceAll":
                    case "match":
                    case "matchAll":
                    case "search":
                        _set.UsesRegExp = true;
                        break;

                    // Duck-typed Map/Set method access on `any` receivers
                    // (cross-module patterns: `import { makeSet } from './lib';
                    // s.has(x)`) routes through ILEmitter.Calls.MapMethods which
                    // unconditionally emits runtime.MapHas/Get/Set/etc. Flag
                    // both Map and Set conservatively — false positives just
                    // over-emit the small dispatch wrappers.
                    case "has":
                    case "get":
                    case "set":
                    case "delete":
                    case "entries":
                    case "keys":
                    case "values":
                    case "forEach":
                    case "size":
                    case "clear":
                        _set.UsesMap = true;
                        _set.UsesSet = true;
                        break;
                }
                VisitExpr(g.Object);
                break;

            case Expr.Set s:
                if (s.Object is Expr.Variable osv)
                    HandleMemberAccess(osv.Name.Lexeme, s.Name.Lexeme);
                VisitExpr(s.Object);
                VisitExpr(s.Value);
                break;

            case Expr.GetIndex gi:
                VisitExpr(gi.Object);
                VisitExpr(gi.Index);
                break;
            case Expr.SetIndex si:
                VisitExpr(si.Object);
                VisitExpr(si.Index);
                VisitExpr(si.Value);
                break;

            case Expr.Call c:
                if (c.Callee is Expr.Variable cv && cv.Name.Lexeme == "require")
                {
                    _set.UsesCjsRequire = true;
                    if (c.Arguments.Count >= 1 && c.Arguments[0] is Expr.Literal lit
                        && lit.Value is string modPath)
                    {
                        HandleModulePath(modPath);
                    }
                }
                VisitExpr(c.Callee);
                foreach (var a in c.Arguments) VisitExpr(a);
                break;

            case Expr.New n:
                VisitExpr(n.Callee);
                foreach (var a in n.Arguments) VisitExpr(a);
                break;

            case Expr.Assign asg:
                VisitExpr(asg.Value);
                break;
            case Expr.CompoundAssign ca:
                VisitExpr(ca.Value);
                break;
            case Expr.CompoundSet cs:
                VisitExpr(cs.Object);
                VisitExpr(cs.Value);
                break;
            case Expr.CompoundSetIndex csi:
                VisitExpr(csi.Object);
                VisitExpr(csi.Index);
                VisitExpr(csi.Value);
                break;
            case Expr.LogicalAssign la:
                VisitExpr(la.Value);
                break;
            case Expr.LogicalSet ls:
                VisitExpr(ls.Object);
                VisitExpr(ls.Value);
                break;
            case Expr.LogicalSetIndex lsi:
                VisitExpr(lsi.Object);
                VisitExpr(lsi.Index);
                VisitExpr(lsi.Value);
                break;

            case Expr.Binary b:
                VisitExpr(b.Left);
                VisitExpr(b.Right);
                break;
            case Expr.Logical lg:
                VisitExpr(lg.Left);
                VisitExpr(lg.Right);
                break;
            case Expr.NullishCoalescing nc:
                VisitExpr(nc.Left);
                VisitExpr(nc.Right);
                break;
            case Expr.Ternary t:
                VisitExpr(t.Condition);
                VisitExpr(t.ThenBranch);
                VisitExpr(t.ElseBranch);
                break;
            case Expr.Comma cm:
                VisitExpr(cm.Left);
                VisitExpr(cm.Right);
                break;
            case Expr.DestructuringAssign da:
                // Walk the lowered assignment statements so a feature inside the rhs/targets
                // (e.g. eval/Proxy in the source expression) is still detected (#754).
                foreach (var s in da.Assignments)
                    VisitStmt(s);
                VisitExpr(da.ResultValue);
                break;
            case Expr.Grouping gr:
                VisitExpr(gr.Expression);
                break;
            case Expr.Unary u:
                VisitExpr(u.Right);
                break;
            case Expr.Delete d:
                VisitExpr(d.Operand);
                break;
            case Expr.PrefixIncrement pi:
                VisitExpr(pi.Operand);
                break;
            case Expr.PostfixIncrement po:
                VisitExpr(po.Operand);
                break;
            case Expr.GetPrivate gp:
                VisitExpr(gp.Object);
                break;
            case Expr.SetPrivate sp:
                VisitExpr(sp.Object);
                VisitExpr(sp.Value);
                break;
            case Expr.CallPrivate cp:
                VisitExpr(cp.Object);
                foreach (var a in cp.Arguments) VisitExpr(a);
                break;

            case Expr.ArrayLiteral al:
                foreach (var e in al.Elements) VisitExpr(e);
                break;
            case Expr.ObjectLiteral ol:
                foreach (var prop in ol.Properties) VisitExpr(prop.Value);
                break;

            case Expr.ArrowFunction af:
                if (af.IsAsync && af.IsGenerator)
                    _set.UsesAsyncGenerator = true;
                foreach (var ap in af.Parameters)
                    if (ap.DefaultValue is not null) VisitExpr(ap.DefaultValue);
                if (af.ExpressionBody is not null) VisitExpr(af.ExpressionBody);
                if (af.BlockBody is not null)
                    foreach (var s in af.BlockBody) VisitStmt(s);
                break;

            case Expr.TemplateLiteral tl:
                foreach (var e in tl.Expressions) VisitExpr(e);
                break;
            case Expr.TaggedTemplateLiteral ttl:
                VisitExpr(ttl.Tag);
                foreach (var e in ttl.Expressions) VisitExpr(e);
                break;
            case Expr.Spread sp2:
                VisitExpr(sp2.Expression);
                break;
            case Expr.TypeAssertion ta:
                VisitExpr(ta.Expression);
                break;
            case Expr.Satisfies sat:
                VisitExpr(sat.Expression);
                break;
            case Expr.Await aw:
                VisitExpr(aw.Expression);
                break;
            case Expr.DynamicImport dimp:
                _set.UsesDynamicImport = true;
                VisitExpr(dimp.PathExpression);
                if (dimp.PathExpression is Expr.Literal lit2 && lit2.Value is string p)
                    HandleModulePath(p);
                break;
            case Expr.Yield y:
                if (y.Value is not null) VisitExpr(y.Value);
                break;
            case Expr.NonNullAssertion nn:
                VisitExpr(nn.Expression);
                break;
            case Expr.ClassExpr ce:
                if (ce.SuperclassExpr is not null) VisitExpr(ce.SuperclassExpr);
                foreach (var m in ce.Methods)
                {
                    if (m.IsAsync && m.IsGenerator)
                        _set.UsesAsyncGenerator = true;
                    foreach (var mp in m.Parameters)
                        if (mp.DefaultValue is not null) VisitExpr(mp.DefaultValue);
                    if (m.Body is not null)
                        foreach (var s in m.Body) VisitStmt(s);
                }
                foreach (var f in ce.Fields)
                    if (f.Initializer is not null) VisitExpr(f.Initializer);
                if (ce.Accessors is not null)
                    foreach (var a in ce.Accessors)
                        foreach (var s in a.Body) VisitStmt(s);
                break;

            case Expr.RegexLiteral:
                // Regex literal /pattern/flags — needs $TSRegExp emission.
                _set.UsesRegExp = true;
                break;

            // Leaves with no nested expressions worth walking.
            default:
                break;
        }
    }
}
