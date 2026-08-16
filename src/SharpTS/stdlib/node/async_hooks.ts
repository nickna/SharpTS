// Node.js 'async_hooks' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/async_context.html.
//
// AsyncLocalStorage requires host-level async context propagation (via .NET's
// AsyncLocal<T>), so the backing instance lives in C#. The primitive exposes
// only a `create()` factory; the TS class below wraps the returned instance,
// forwarding every method dynamically so context flows through awaits and
// Promise chains unchanged.
//
// Note: Node's `run(store, callback, ...args)` and `exit(callback, ...args)`
// accept extra args to pass to the callback. SharpTS's existing C# backing
// supports these, but the TS wrapper drops them (no test coverage and the
// common case is zero extra args). Adding them requires the arity-dispatch
// workaround — see stdlib/node/process.ts for the pattern.

import { create as __create } from 'primitive:async_hooks';

/**
 * Per-request/per-operation store that propagates through asynchronous
 * operations. Context set via `run` / `enterWith` is visible via `getStore`
 * across await boundaries, Promise chains, and timer callbacks.
 */
export class AsyncLocalStorage {
    // The underlying host-backed AsyncLocal<object> wrapper. Typed `any` so
    // TS dispatches method calls dynamically against the runtime instance
    // (SharpTSAsyncLocalStorage in interpreter mode, $AsyncLocalStorage in
    // compiled mode).
    private _inner: any;

    constructor() {
        this._inner = __create();
    }

    /**
     * Runs `callback` with the given store. The store is visible via
     * `getStore()` inside the callback and any async work it spawns.
     * The previous store is restored after the callback completes.
     */
    run(store: any, callback: any): any {
        return this._inner.run(store, callback);
    }

    /** Returns the current store, or undefined if outside any run/enterWith scope. */
    getStore(): any {
        return this._inner.getStore();
    }

    /**
     * Sets the store for the current async context without running a
     * callback. The store will be visible in subsequent code in this
     * async flow.
     */
    enterWith(store: any): void {
        this._inner.enterWith(store);
    }

    /**
     * Runs `callback` with the store cleared. The previous store is
     * restored after the callback completes.
     */
    exit(callback: any): any {
        return this._inner.exit(callback);
    }

    /** Disables this instance. After disable(), getStore() always returns undefined. */
    disable(): void {
        this._inner.disable();
    }
}

export default { AsyncLocalStorage };
