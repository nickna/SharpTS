// Node.js 'events' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/events.html.
//
// Self-contained TS EventEmitter class. Mirrors Node's observable semantics:
// listener-addition order, once-unwrap on emit, method chaining, prepend
// variants, per-instance and default max listeners.
//
// Note: SharpTS's internal runtime types (SharpTSProcess, SharpTSReadable,
// etc.) still inherit from a C#-side SharpTSEventEmitter. That's separate
// from this class. User code that imports EventEmitter from 'events' gets
// *this* class and builds on it; the runtime classes remain independent.

type Listener = Function;

// Event names may be strings or symbols (e.g. EventEmitter.errorMonitor).
type EventName = string | symbol;

interface ListenerWrapper {
    listener: Listener;
    once: boolean;
}

// Well-known symbols Node attaches to the EventEmitter constructor.
//   captureRejectionSymbol — the documented Symbol.for('nodejs.rejection');
//     emitters define this method to intercept async-listener rejections.
//   errorMonitor — a unique (unregistered) symbol whose listeners observe
//     'error' events without satisfying the throw-on-unhandled-error contract.
// Exported inline (not via a trailing `export { }` re-export) because the
// embedded-facade compiler mishandles re-exported symbol bindings in compiled
// mode (they surface as `undefined`/`object`); an inline `export const` round-
// trips correctly in both modes.
export const captureRejectionSymbol: symbol = Symbol.for('nodejs.rejection');
// Registered (Symbol.for) rather than Node's unregistered symbol so the
// identity/stringification is stable across the module boundary in both modes
// and recoverable by the C#/IL EventEmitter runtime via the fixed sentinel key
// it stringifies to: "Symbol(nodejs.events.errorMonitor)".
export const errorMonitor: symbol = Symbol.for('nodejs.events.errorMonitor');

/** Node.js-compatible EventEmitter implementation. */
export class EventEmitter {
    /**
     * Default maximum listeners before a warning is emitted. Node defaults to 10.
     * Overridable globally; per-instance values take precedence via setMaxListeners.
     */
    static defaultMaxListeners: number = 10;

    /** The `errorMonitor` symbol (see note above). Exposed as a static getter
     *  because non-literal static field initializers don't round-trip in
     *  compiled mode. */
    static get errorMonitor(): symbol { return errorMonitor; }

    /** The `captureRejectionSymbol` (Symbol.for('nodejs.rejection')). */
    static get captureRejectionSymbol(): symbol { return captureRejectionSymbol; }

    private _events: any;
    private _maxListeners: number;

    // Note: the captureRejections / errorMonitor / throw-on-unhandled instance
    // behaviors live in the C#/IL EventEmitter runtime type that backs
    // `new EventEmitter()` in both modes (SharpTSEventEmitter / $EventEmitter),
    // not here — a direct `new EventEmitter()` is never an instance of this
    // facade class. This class supplies the module statics/helpers and serves
    // as the base for EventEmitterAsyncResource.
    constructor() {
        this._events = {};
        this._maxListeners = 0; // 0 → use defaultMaxListeners
    }

    private _getListeners(eventName: EventName, create: boolean): ListenerWrapper[] | null {
        let arr = this._events[eventName];
        if (arr == null) {
            if (!create) return null;
            arr = [];
            this._events[eventName] = arr;
        }
        return arr;
    }

    private _addListener(eventName: EventName, listener: Listener, once: boolean, prepend: boolean): EventEmitter {
        if (typeof listener !== 'function') {
            throw new TypeError('Listener must be a function');
        }
        const arr = this._getListeners(eventName, true)!;
        const wrapper: ListenerWrapper = { listener, once };
        if (prepend) arr.unshift(wrapper);
        else arr.push(wrapper);
        // Node emits a 'warning' event when the count exceeds the threshold; for now
        // we silently permit it — exceeds-threshold warnings are observability, not
        // correctness, and no current tests assert on them.
        return this;
    }

    /** Register a listener. Returns `this` for chaining. */
    on(eventName: EventName, listener: Listener): EventEmitter {
        return this._addListener(eventName, listener, false, false);
    }

    /** Alias for {@link on}. */
    addListener(eventName: EventName, listener: Listener): EventEmitter {
        return this._addListener(eventName, listener, false, false);
    }

    /** Register a one-shot listener that removes itself after firing once. */
    once(eventName: EventName, listener: Listener): EventEmitter {
        return this._addListener(eventName, listener, true, false);
    }

    /** Register a listener at the head of the chain. */
    prependListener(eventName: EventName, listener: Listener): EventEmitter {
        return this._addListener(eventName, listener, false, true);
    }

    /** Register a one-shot listener at the head of the chain. */
    prependOnceListener(eventName: EventName, listener: Listener): EventEmitter {
        return this._addListener(eventName, listener, true, true);
    }

    /**
     * Remove a single listener by reference.
     * Matches Node's semantics: only the first occurrence is removed even when
     * the same function is registered multiple times.
     */
    off(eventName: EventName, listener: Listener): EventEmitter {
        const arr = this._getListeners(eventName, false);
        if (arr == null) return this;
        for (let i = 0; i < arr.length; i++) {
            if (arr[i].listener === listener) {
                arr.splice(i, 1);
                if (arr.length === 0) delete this._events[eventName];
                break;
            }
        }
        return this;
    }

    /** Alias for {@link off}. */
    removeListener(eventName: EventName, listener: Listener): EventEmitter {
        return this.off(eventName, listener);
    }

    /** Remove every listener for an event, or every listener across all events when called without an argument. */
    removeAllListeners(eventName?: EventName): EventEmitter {
        if (eventName == null) {
            this._events = {};
        } else {
            delete this._events[eventName];
        }
        return this;
    }

    /**
     * Fire all registered listeners for `eventName` with the supplied arguments.
     * A snapshot of the listener array is taken before dispatch so listeners
     * added or removed during emission don't disturb the in-flight iteration.
     * Returns true if the event had listeners, false otherwise.
     */
    emit(eventName: EventName, ...args: any[]): boolean {
        const arr = this._getListeners(eventName, false);
        if (arr == null || arr.length === 0) return false;

        // Snapshot before dispatch — listeners may modify the array.
        const snapshot: ListenerWrapper[] = [];
        for (let i = 0; i < arr.length; i++) snapshot.push(arr[i]);

        // Pre-remove once wrappers from the live array before calling, so that
        // a listener that inspects listenerCount mid-emit sees the post-fire state.
        for (let i = 0; i < snapshot.length; i++) {
            const w = snapshot[i];
            if (w.once) {
                const live = this._events[eventName];
                if (live != null) {
                    for (let j = 0; j < live.length; j++) {
                        if (live[j] === w) {
                            live.splice(j, 1);
                            if (live.length === 0) delete this._events[eventName];
                            break;
                        }
                    }
                }
            }
        }

        // Dispatch.
        for (let i = 0; i < snapshot.length; i++) {
            snapshot[i].listener.apply(this, args);
        }
        return true;
    }

    /** Return the listener functions for `eventName`. Unwrapped — once wrappers' originals. */
    listeners(eventName: EventName): Listener[] {
        const arr = this._getListeners(eventName, false);
        if (arr == null) return [];
        const out: Listener[] = [];
        for (let i = 0; i < arr.length; i++) out.push(arr[i].listener);
        return out;
    }

    /** Same as {@link listeners} in this implementation; kept for API parity. */
    rawListeners(eventName: EventName): Listener[] {
        return this.listeners(eventName);
    }

    /** Number of listeners for `eventName`. */
    listenerCount(eventName: EventName): number {
        const arr = this._getListeners(eventName, false);
        return arr == null ? 0 : arr.length;
    }

    /** Names of events that currently have at least one listener. */
    eventNames(): string[] {
        const out: string[] = [];
        const keys = Object.keys(this._events);
        for (const k of keys) {
            if (this._events[k].length > 0) out.push(k);
        }
        return out;
    }

    /** Per-instance max listener override. Zero (default) falls back to the class default. */
    setMaxListeners(n: number): EventEmitter {
        if (typeof n !== 'number') throw new TypeError('setMaxListeners argument must be a number');
        this._maxListeners = n;
        return this;
    }

    /** Effective max listener count for this instance. */
    getMaxListeners(): number {
        return this._maxListeners > 0 ? this._maxListeners : EventEmitter.defaultMaxListeners;
    }

    // ─── Static / module-level helpers ──────────────────────────────────
    //
    // Node exposes these both as statics on the EventEmitter constructor
    // (`EventEmitter.once`) and as named module exports (`import { once }`).
    // The implementations live in module-scope functions below; the statics
    // delegate so both spellings reach identical behavior. (Assigning the
    // functions onto the class object after definition is unreliable in
    // compiled mode — class objects are System.Type references that silently
    // drop plain property writes — so the statics are declared in the class
    // body and forward to the module functions.)

    /**
     * Resolve once `name` fires on `emitter`, with the event arguments as an
     * array. Rejects if the emitter emits `'error'` first (unless waiting for
     * `'error'`), or if `options.signal` aborts.
     */
    static once(emitter: any, name: any, options?: any): Promise<any[]> {
        return once(emitter, name, options);
    }

    /**
     * Async iterator over every `name` event from `emitter`. Each `for await`
     * step yields the event-argument array. Honors `options.signal`.
     */
    static on(emitter: any, name: any, options?: any): any {
        return on(emitter, name, options);
    }

    /** Listeners registered for `name` on `emitterOrTarget`. */
    static getEventListeners(emitterOrTarget: any, name: any): any[] {
        return getEventListeners(emitterOrTarget, name);
    }

    /**
     * Static form. With no emitters, sets {@link defaultMaxListeners}; with
     * emitters, sets each one's per-instance max.
     */
    static setMaxListeners(n: number, ...emitters: any[]): void {
        setMaxListeners(n, ...emitters);
    }

    /** Static form: effective max-listener count for `emitter`. */
    static getMaxListeners(emitter: any): number {
        return getMaxListeners(emitter);
    }

    /** Registers `listener` for `signal`'s abort; returns a disposable remover. */
    static addAbortListener(signal: any, listener: any): any {
        return addAbortListener(signal, listener);
    }

    /** Deprecated static form of listener counting: `EventEmitter.listenerCount(ee, name)`. */
    static listenerCount(emitter: any, name: any): number {
        return listenerCount(emitter, name);
    }
}

// ─── Module-level statics / helpers ─────────────────────────────────────
//
// These mirror Node's `events.*` module functions. They operate on any
// emitter exposing the EventEmitter surface (on/off/once/listeners) and fall
// back to the EventTarget surface (addEventListener/removeEventListener) for
// objects like AbortSignal.

/** True if `e` looks like an EventEmitter (has an `on` method). */
function _isEmitter(e: any): boolean {
    return e != null && typeof e.on === 'function';
}

/** True if `e` looks like an EventTarget (has addEventListener, no `on`). */
function _isTarget(e: any): boolean {
    return e != null && typeof e.addEventListener === 'function' && typeof e.on !== 'function';
}

function _add(emitter: any, name: any, handler: any): void {
    if (_isEmitter(emitter)) emitter.on(name, handler);
    else if (_isTarget(emitter)) emitter.addEventListener(name, handler);
    else throw new TypeError('The "emitter" argument must be an EventEmitter or EventTarget');
}

function _remove(emitter: any, name: any, handler: any): void {
    if (emitter != null && typeof emitter.off === 'function') emitter.off(name, handler);
    else if (emitter != null && typeof emitter.removeEventListener === 'function') emitter.removeEventListener(name, handler);
}

/**
 * Builds the rejection value for an aborted signal. Node rejects with
 * `signal.reason`, which defaults to a DOMException named 'AbortError'. SharpTS
 * represents the default reason as a plain string, so when the reason isn't a
 * real Error we synthesize one carrying name='AbortError'/code='ABORT_ERR'
 * (a user-supplied Error reason is passed through unchanged).
 */
function _abortError(signal: any): any {
    const reason = signal != null ? signal.reason : undefined;
    if (reason instanceof Error) return reason;
    const err: any = new Error('The operation was aborted');
    err.name = 'AbortError';
    err.code = 'ABORT_ERR';
    return err;
}

/**
 * Returns a Promise that resolves with the argument array of the first `name`
 * event on `emitter`. Rejects on a prior `'error'` (unless awaiting `'error'`)
 * or on abort via `options.signal`.
 */
export function once(emitter: any, name: any, options?: any): Promise<any[]> {
    const signal = options != null ? options.signal : undefined;
    return new Promise((resolve: any, reject: any) => {
        if (signal != null && signal.aborted) {
            reject(_abortError(signal));
            return;
        }

        let settled = false;
        const eventHandler = (...args: any[]): void => {
            cleanup();
            resolve(args);
        };
        const errorHandler = (err: any): void => {
            cleanup();
            reject(err);
        };
        const abortHandler = (): void => {
            cleanup();
            reject(_abortError(signal));
        };
        const cleanup = (): void => {
            if (settled) return;
            settled = true;
            _remove(emitter, name, eventHandler);
            if (name !== 'error') _remove(emitter, 'error', errorHandler);
            if (signal != null) { try { signal.removeEventListener('abort', abortHandler); } catch (e) { } }
        };

        _add(emitter, name, eventHandler);
        if (name !== 'error') _add(emitter, 'error', errorHandler);
        if (signal != null) { try { signal.addEventListener('abort', abortHandler); } catch (e) { } }
    });
}

/**
 * Returns an async iterator yielding the argument array of each `name` event
 * from `emitter`. Honors `options.signal` (aborting ends iteration with an
 * AbortError). Mirrors Node's `events.on`.
 */
export function on(emitter: any, name: any, options?: any): any {
    const signal = options != null ? options.signal : undefined;

    const unconsumed: any[] = []; // queued event-arg arrays awaiting a puller
    const pending: any[] = [];    // parked { resolve, reject } awaiting an event
    let finished = false;
    let errored: any = null;

    const cleanup = (): void => {
        _remove(emitter, name, eventHandler);
        _remove(emitter, 'error', errorHandler);
        if (signal != null) { try { signal.removeEventListener('abort', abortHandler); } catch (e) { } }
    };

    const eventHandler = (...args: any[]): void => {
        if (pending.length > 0) pending.shift().resolve({ value: args, done: false });
        else unconsumed.push(args);
    };
    const errorHandler = (err: any): void => {
        finished = true;
        cleanup();
        if (pending.length > 0) pending.shift().reject(err);
        else errored = err;
    };
    const abortHandler = (): void => {
        finished = true;
        cleanup();
        const e = _abortError(signal);
        if (pending.length > 0) { while (pending.length > 0) pending.shift().reject(e); }
        else errored = e;
    };

    _add(emitter, name, eventHandler);
    _add(emitter, 'error', errorHandler);
    if (signal != null) {
        if (signal.aborted) { finished = true; errored = _abortError(signal); cleanup(); }
        else { try { signal.addEventListener('abort', abortHandler); } catch (e) { } }
    }

    return {
        next(): Promise<any> {
            if (unconsumed.length > 0) return Promise.resolve({ value: unconsumed.shift(), done: false });
            if (errored != null) { const e = errored; errored = null; return Promise.reject(e); }
            if (finished) return Promise.resolve({ value: undefined, done: true });
            return new Promise((resolve: any, reject: any) => { pending.push({ resolve, reject }); });
        },
        return(): Promise<any> {
            finished = true;
            cleanup();
            while (pending.length > 0) pending.shift().resolve({ value: undefined, done: true });
            return Promise.resolve({ value: undefined, done: true });
        },
        throw(err: any): Promise<any> {
            finished = true;
            cleanup();
            return Promise.reject(err);
        },
        [Symbol.asyncIterator](): any { return this; }
    };
}

/** Listeners registered for `name` on an EventEmitter (empty for bare EventTargets). */
export function getEventListeners(emitterOrTarget: any, name: any): any[] {
    if (emitterOrTarget != null && typeof emitterOrTarget.listeners === 'function') {
        return emitterOrTarget.listeners(name);
    }
    return [];
}

/**
 * With no emitters, sets the global default max-listener count; with emitters,
 * sets each one's per-instance max. Mirrors Node's `events.setMaxListeners`.
 */
export function setMaxListeners(n: number, ...emitters: any[]): void {
    if (typeof n !== 'number' || n < 0 || isNaN(n)) {
        throw new RangeError('The value of "n" is out of range. It must be a non-negative number.');
    }
    if (emitters.length === 0) {
        EventEmitter.defaultMaxListeners = n;
        return;
    }
    for (let i = 0; i < emitters.length; i++) {
        const e = emitters[i];
        if (e != null && typeof e.setMaxListeners === 'function') e.setMaxListeners(n);
    }
}

/** Effective max-listener count for `emitter`, or the default when it has none. */
export function getMaxListeners(emitter: any): number {
    if (emitter != null && typeof emitter.getMaxListeners === 'function') return emitter.getMaxListeners();
    return EventEmitter.defaultMaxListeners;
}

/** Deprecated counterpart to the instance `listenerCount`. */
export function listenerCount(emitter: any, name: any): number {
    if (emitter != null && typeof emitter.listenerCount === 'function') return emitter.listenerCount(name);
    return 0;
}

/**
 * Registers `listener` to run when `signal` aborts and returns a disposable
 * whose `[Symbol.dispose]` removes it. If `signal` is already aborted, the
 * listener fires on the next microtask. Mirrors Node's `events.addAbortListener`.
 */
export function addAbortListener(signal: any, listener: any): any {
    if (signal == null || typeof signal.addEventListener !== 'function') {
        throw new TypeError('The "signal" argument must be an AbortSignal');
    }
    if (typeof listener !== 'function') {
        throw new TypeError('The "listener" argument must be a function');
    }
    let removed = false;
    const handler = (e: any): void => { listener(e); };
    const remove = (): void => {
        if (removed) return;
        removed = true;
        try { signal.removeEventListener('abort', handler); } catch (e) { }
    };
    if (signal.aborted) {
        // Already aborted: fire on a later tick rather than reentrantly inside
        // this call (Node defers to a microtask; SharpTS drains microtasks
        // eagerly, so a 0ms timer is used to guarantee non-synchronous delivery).
        setTimeout(() => { if (!removed) listener(); }, 0);
    } else {
        try { signal.addEventListener('abort', handler); } catch (e) { }
    }
    return { [Symbol.dispose]: remove };
}

// ─── EventEmitterAsyncResource ──────────────────────────────────────────
//
// Node's EventEmitterAsyncResource runs each listener within an AsyncResource
// scope so async-context (async_hooks) propagates across emit. SharpTS has no
// host async-id/hooks backend (the async_hooks facade exposes only
// AsyncLocalStorage, not AsyncResource — see #1097 ceilings), so the scope is
// a no-op and listeners run with the ambient context. The class still provides
// the full documented surface: `asyncResource`, `asyncId`, `triggerAsyncId`,
// and `emitDestroy()`.

let _asyncIdCounter = 1;
function _nextAsyncId(): number { _asyncIdCounter = _asyncIdCounter + 1; return _asyncIdCounter; }

/**
 * Minimal stand-in for async_hooks.AsyncResource carrying the documented
 * surface. `runInAsyncScope` invokes the callback directly (no real context
 * switch — see #1097 ceilings).
 */
class _MinimalAsyncResource {
    private _type: any;
    private _asyncId: number;
    private _triggerAsyncId: number;
    private _destroyed: boolean;
    eventEmitter: any;

    constructor(type: any, options?: any) {
        this._type = type;
        this._asyncId = _nextAsyncId();
        this._triggerAsyncId =
            (options != null && typeof options.triggerAsyncId === 'number') ? options.triggerAsyncId : 0;
        this._destroyed = false;
    }

    runInAsyncScope(fn: any, thisArg?: any, ...args: any[]): any {
        return fn.apply(thisArg, args);
    }

    emitDestroy(): _MinimalAsyncResource { this._destroyed = true; return this; }
    asyncId(): number { return this._asyncId; }
    triggerAsyncId(): number { return this._triggerAsyncId; }
}

/**
 * EventEmitter associated with an AsyncResource. Listeners conceptually run
 * inside the resource's async scope; SharpTS lacks a host async-context backend
 * so the scope is a no-op (#1097 ceiling). `emit` is inherited unchanged — the
 * no-op scope makes a wrapping override observably identical, and a
 * super-spread override is unreliable in compiled mode.
 */
export class EventEmitterAsyncResource extends EventEmitter {
    private _asyncResource: any;

    constructor(options?: any) {
        super();
        const name =
            (options != null && typeof options.name === 'string') ? options.name : 'EventEmitterAsyncResource';
        const resource = new _MinimalAsyncResource(name, options);
        resource.eventEmitter = this;
        this._asyncResource = resource;
    }

    /** The underlying AsyncResource. */
    get asyncResource(): any { return this._asyncResource; }

    /** Unique async id of the underlying resource. */
    get asyncId(): number { return this._asyncResource.asyncId(); }

    /** Trigger async id of the underlying resource. */
    get triggerAsyncId(): number { return this._asyncResource.triggerAsyncId(); }

    /** Destroys the underlying resource. Returns `this`. */
    emitDestroy(): EventEmitterAsyncResource {
        this._asyncResource.emitDestroy();
        return this;
    }
}

// Node's `events` default export is the EventEmitter class itself, not a
// namespace object: `const EE = require('events')` gives you the class.
export default EventEmitter;
