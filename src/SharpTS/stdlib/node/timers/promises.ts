// Node.js 'timers/promises' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/timers.html#timers-promises-api.
//
// Promise-based timer operations. No rest args here (Node's promise API takes
// positional delay/value/options), so a direct re-export is safe — no
// arity-dispatch needed.

import {
    setTimeout as __setTimeout,
    setImmediate as __setImmediate,
    setInterval as __setInterval,
} from 'primitive:timers/promises';

// `value` is a plain optional param: an omitted optional argument is padded with the `undefined`
// sentinel (not CLR null) through the $TSFunction.Invoke boundary in compiled mode, so an unset
// `value` resolves the promise with `undefined`, matching Node and the interpreter. This used to
// require an explicit `= undefined` default to dodge null padding (#640/#600); #925 made the
// boundary ABI-faithful, so the plain `?:` form is correct.

/** Resolves with `value` after `delay` ms. Supports `options.signal` for AbortSignal. */
export function setTimeout(delay?: number, value?: any, options?: any): Promise<any> {
    return __setTimeout(delay as any, value, options);
}

/** Resolves with `value` on the next event-loop tick. Supports `options.signal`. */
export function setImmediate(value?: any, options?: any): Promise<any> {
    return __setImmediate(value, options);
}

/** Returns an AsyncIterable that yields `value` every `delay` ms. */
export function setInterval(delay?: number, value?: any, options?: any): AsyncIterable<any> {
    // NOTE: if the primitive throws synchronously (pre-aborted AbortSignal), the
    // interpreter currently converts the error to a string at the TS-function
    // boundary, losing Error identity. One test pins this behavior
    // (TimersPromises_SetInterval_AbortSignal_PreAborted in interpreter mode).
    // See the plan's Phase 3n notes. No workaround at this layer — a proper fix
    // belongs in the interpreter's function-boundary exception handling.
    return __setInterval(delay as any, value, options);
}

export default { setTimeout, setImmediate, setInterval };
